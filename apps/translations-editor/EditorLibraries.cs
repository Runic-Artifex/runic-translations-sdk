using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ReactiveUI;
using ReactiveUI.Primitives;
using ReactiveUI.Primitives.Concurrency;
using Runic.Application.Views;
using Runic.Translations.Authoring;
using Runic.Translations.Compiler;
using Runic.Translations.Tooling;

namespace Runic.Translations.Editor;

// Library catalogs in the editor (T5d: L5-5, L5-15, L5-16). A library catalog that an installed package packs is shown in
// two layers: layer 0 is the library itself, read-only, from the packed sources and contract; layer 1 is the application's
// override sources (translations/overrides/<catalog>/<locale>.rmf2). Everything is resolved, compiled and planned by Tooling
// (TranslationDependencyCatalogs, TranslationLibraryCatalog, TranslationOverrideChanges), as the build, the language server
// and runic-translations overrides do; the editor resolves no NuGet package or node_modules itself.

/// <summary>A library catalog the open project can override, in the workspace snapshot.</summary>
/// <param name="Overridden">Whether runic.json <c>overrides</c> lists it.</param>
/// <param name="Problem">Why the build cannot use it (RTR0104), or null.</param>
/// <param name="OverrideSources">The number of override sources the application has for it.</param>
internal sealed record EditorLibrarySummary(string Catalog, string? Package, string? Version, string? Origin, bool Overridden, string? Problem, int OverrideSources);

/// <summary>A locale of a library catalog: the library's (<c>library</c>) or one the application adds (<c>application</c>).</summary>
internal sealed record EditorLibraryLocale(string Tag, string? Fallback, string Origin, int Messages, int LibraryTranslated, int Overridden, int Covered, bool Pending);

/// <summary>An override of a message in one locale (layer 1).</summary>
/// <param name="Path">The override source, relative to the workspace root.</param>
/// <param name="Revision">The override source's revision, for a conflict-checked save.</param>
/// <param name="Changed">Whether the library's base text changed since the override was written; null when unknown.</param>
internal sealed record EditorLibraryOverride(string Locale, string Text, string Path, string Revision, bool? Changed, bool Known);

/// <summary>A library message: its contract and the library's text per locale (layer 0), and the application's overrides (layer 1).</summary>
internal sealed record EditorLibraryMessage(string Key, string Path, IReadOnlyList<string> Inputs, string Content, bool Structured, string BaseText, string? Description,
    IReadOnlyDictionary<string, string> Texts, IReadOnlyList<EditorLibraryOverride> Overrides);

/// <summary>A library catalog with the application's overrides.</summary>
/// <param name="Layers">Layer 0 <c>library</c> (read-only), layer 1 <c>override</c>.</param>
/// <param name="Diagnostics">The build's diagnostics of the override sources, with RTR0090 for overrides whose library text changed.</param>
internal sealed record EditorLibraryView(string Catalog, string? Package, string? Version, string? Origin, bool Listed, string? Problem, string BaseLocale,
    IReadOnlyList<EditorLayer> Layers, IReadOnlyList<EditorLibraryLocale> Locales, IReadOnlyList<EditorLibraryMessage> Messages, IReadOnlyList<EditorDiagnostic> Diagnostics,
    string? LibraryError);

/// <param name="ProjectChanged">Whether runic.json changed (the catalog was added to <c>overrides</c>), so the workspace should reload.</param>
internal sealed record EditorLibraryResult(bool Ok, EditorNotice? Message, EditorLibraryView? Library, bool ProjectChanged = false);

internal sealed record EditorLibraryRequest(string Catalog);

/// <param name="Text">The override's text: for <c>override</c> the initial text (null: the library's text), for <c>save</c> the new text.</param>
/// <param name="ExpectedRevision">For <c>save</c>, the override source's revision the text was edited from.</param>
internal sealed record EditorLibraryOverrideRequest(string Catalog, string Key, string Locale, string? Text = null, string? ExpectedRevision = null);

internal sealed record EditorLibraryLocaleRequest(string Catalog, string Locale);

internal sealed partial class EditorSession
{
    private static readonly UTF8Encoding LibraryUtf8 = new(false, true);

    /// <summary>The library catalog with the application's overrides.</summary>
    public Task<EditorLibraryResult> LoadLibraryAsync(EditorLibraryRequest request, CancellationToken cancellationToken = default) =>
        WithLibraryAsync(request.Catalog, (context, _) => Task.FromResult(context.Result()), cancellationToken);

    /// <summary>Override: adds the message to the locale's override source, seeded with the library's text, and records the library text.</summary>
    public Task<EditorLibraryResult> OverrideLibraryMessageAsync(EditorLibraryOverrideRequest request, CancellationToken cancellationToken = default) =>
        WithLibraryAsync(request.Catalog, (context, token) =>
        {
            bool declared = context.Declare();
            context.Apply(TranslationOverrideChanges.Override(context.Catalog, [request.Key], request.Locale, request.Text));
            return Task.FromResult(context.Reload(token).Result() with { ProjectChanged = declared });
        }, cancellationToken);

    /// <summary>Saves the text of an override, refusing when its source changed since <c>ExpectedRevision</c>.</summary>
    public Task<EditorLibraryResult> SaveLibraryOverrideAsync(EditorLibraryOverrideRequest request, CancellationToken cancellationToken = default) =>
        WithLibraryAsync(request.Catalog, (context, token) =>
        {
            TranslationLibraryOverride item = context.Catalog.Override(request.Key, request.Locale)
                ?? throw new EditorUserException(EditorNotice.Create("ui_backend_library_not_overridden", ("key", request.Key), ("locale", request.Locale)));
            TranslationSource source = context.Catalog.OverrideSources.Single(candidate => candidate.Path == item.Path);
            if (request.ExpectedRevision is not null && request.ExpectedRevision != LibraryRevision(source.GetUtf8Bytes()))
                throw new EditorUserException(EditorNotice.Create("ui_backend_document_conflict", ("path", context.Relative(item.Path))));
            string text = (request.Text ?? throw new ArgumentException("Text is required.")).Replace("\r\n", "\n", StringComparison.Ordinal);
            byte[] bytes = Rmf2ResourceWriter.SetMessage(source, request.Key, text);
            Rmf2ResourceDocument check = Rmf2ResourceReader.Analyze(new TranslationSource(item.Path, bytes), null, token);
            if (!check.Success) throw new EditorUserException(EditorNotice.Create("ui_backend_library_invalid_text", ("detail", check.Diagnostics[0].Message)));
            context.Apply(new TranslationOverridePlan(item.Path, bytes, null));
            return Task.FromResult(context.Reload(token).Result());
        }, cancellationToken);

    /// <summary>Revert to library: removes the override and its review entry.</summary>
    public Task<EditorLibraryResult> RevertLibraryOverrideAsync(EditorLibraryOverrideRequest request, CancellationToken cancellationToken = default) =>
        WithLibraryAsync(request.Catalog, (context, token) =>
        {
            context.Apply(TranslationOverrideChanges.Revert(context.Catalog, request.Key, request.Locale));
            return Task.FromResult(context.Reload(token).Result());
        }, cancellationToken);

    /// <summary>Keep override: records that the override was checked against the library's current base text.</summary>
    public Task<EditorLibraryResult> KeepLibraryOverrideAsync(EditorLibraryOverrideRequest request, CancellationToken cancellationToken = default) =>
        WithLibraryAsync(request.Catalog, (context, token) =>
        {
            context.ApplyReview(TranslationOverrideChanges.Keep(context.Catalog, [request.Key], request.Locale));
            return Task.FromResult(context.Reload(token).Result());
        }, cancellationToken);

    /// <summary>
    /// Adds a locale to a library catalog that the library lacks (L5-16): its override source with commented templates. It
    /// falls back by the one-hop rule of the compiler and both runtimes, and the build adds it with its first override.
    /// </summary>
    public Task<EditorLibraryResult> AddLibraryLocaleAsync(EditorLibraryLocaleRequest request, CancellationToken cancellationToken = default) =>
        WithLibraryAsync(request.Catalog, (context, token) =>
        {
            if (context.Catalog.Locales.Any(locale => string.Equals(locale.Tag, request.Locale, StringComparison.OrdinalIgnoreCase)))
                throw new EditorUserException(EditorNotice.Create("ui_backend_library_locale_exists", ("locale", request.Locale)));
            bool declared = context.Declare();
            context.Apply(TranslationOverrideChanges.Scaffold(context.Catalog, request.Locale));
            return Task.FromResult(context.Reload(token).Result() with { ProjectChanged = declared });
        }, cancellationToken);

    private async Task<EditorLibraryResult> WithLibraryAsync(string catalog, Func<LibraryContext, CancellationToken, Task<EditorLibraryResult>> operation, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            string config = _workspace.ProjectConfigPath ?? throw new EditorUserException(EditorNotice.Create("ui_backend_project_required"));
            TranslationDependencyResolution resolution = TranslationDependencyCatalogs.Resolve(config, null, cancellationToken);
            TranslationOverrideTarget target = resolution.TargetFor(catalog)
                ?? throw new EditorUserException(EditorNotice.Create("ui_backend_library_missing", ("catalog", catalog)));
            if (target.Dependency is null)
                return new EditorLibraryResult(false, EditorNotice.Create("ui_backend_library_unavailable", ("catalog", catalog), ("detail", target.Problem ?? "")), null);
            var context = new LibraryContext(_workspace, config, target, TranslationLibraryCatalog.Load(target, null, cancellationToken));
            return await operation(context, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is EditorUserException or ArgumentException or InvalidOperationException or IOException or UnauthorizedAccessException or TranslationAuthoringException or TranslationInterchangeException)
        {
            return new EditorLibraryResult(false, EditorNotice.FromException(exception), null);
        }
        finally
        {
            _gate.Release();
        }
    }

    private static string LibraryRevision(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    private sealed class LibraryContext(EditorWorkspace workspace, string config, TranslationOverrideTarget target, TranslationLibraryCatalog catalog)
    {
        internal TranslationOverrideTarget Target { get; private set; } = target;
        internal TranslationLibraryCatalog Catalog { get; private set; } = catalog;

        internal string Relative(string path) => Path.GetRelativePath(workspace.Root, Path.GetFullPath(path)).Replace('\\', '/');

        // Lists the catalog in runic.json "overrides" when it is not yet; returns whether runic.json changed.
        internal bool Declare()
        {
            if (Target.Listed) return false;
            byte[]? bytes = TranslationOverrideChanges.Declare(new TranslationSource(config, File.ReadAllBytes(config)), Target.Catalog, Target.Dependency!.Package);
            if (bytes is null) return false;
            workspace.EnsureWritable(config);
            string temporary = config + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try { File.WriteAllBytes(temporary, bytes); File.Move(temporary, config, overwrite: true); }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
            Target = Target with { Listed = true, Problem = null };
            return true;
        }

        internal void Apply(TranslationOverridePlan plan)
        {
            // Override sources and their review file are written only inside the workspace.
            workspace.EnsureWritable(plan.Path);
            workspace.EnsureWritable(Target.ReviewPath);
            TranslationOverrideChanges.Apply(plan, Target);
        }

        internal void ApplyReview(TranslationInterchangeReview review)
        {
            workspace.EnsureWritable(Target.ReviewPath);
            TranslationReviewFile.Write(Target.ReviewPath, review);
        }

        internal LibraryContext Reload(CancellationToken cancellationToken)
        {
            Catalog = TranslationLibraryCatalog.Load(Target, null, cancellationToken);
            return this;
        }

        internal EditorLibraryResult Result()
        {
            TranslationLibraryCatalog catalog = Catalog;
            var revisions = catalog.OverrideSources.ToDictionary(static source => source.Path, source => LibraryRevision(source.GetUtf8Bytes()), StringComparer.Ordinal);
            EditorLibraryMessage[] messages = catalog.Messages.Select(message => new EditorLibraryMessage(message.Key, string.Join(".", message.LogicalPath), message.Inputs,
                message.Content, message.Structured, message.BaseText, message.Description, message.Texts,
                catalog.Overrides.Where(item => item.Key == message.Key).Select(item => new EditorLibraryOverride(item.Locale, item.Text, Relative(item.Path),
                    revisions.GetValueOrDefault(item.Path, ""), item.Changed, item.Known)).ToArray())).ToArray();
            EditorLibraryLocale[] locales = catalog.Locales.Select(static locale => new EditorLibraryLocale(locale.Tag, locale.Fallback,
                locale.Origin == TranslationLibraryLocaleOrigin.Library ? "library" : "application", locale.Messages, locale.LibraryTranslated, locale.Overridden, locale.Covered, locale.Pending)).ToArray();
            EditorDiagnostic[] diagnostics = catalog.Diagnostics.Select(diagnostic => new EditorDiagnostic(diagnostic.Id,
                diagnostic.Severity switch { TranslationDiagnosticSeverity.Error => "error", TranslationDiagnosticSeverity.Warning => "warning", _ => "info" },
                diagnostic.Message, Relative(diagnostic.Location.Path), diagnostic.Location.Line, diagnostic.Location.Column, diagnostic.Location.EndLine, diagnostic.Location.EndColumn,
                Key: diagnostic.MessageKey, Locale: diagnostic.Locale)).ToArray();
            TranslationDependencyCatalog dependency = catalog.Dependency;
            var view = new EditorLibraryView(catalog.Catalog, dependency.Package, dependency.Version, dependency.Origin == TranslationDependencyOrigin.NuGet ? "nuget" : "npm",
                Target.Listed, Target.Problem, catalog.BaseLocale, [new EditorLayer("library", 0), new EditorLayer("override", 1)], locales, messages, diagnostics,
                catalog.Library.Project is null ? catalog.LibraryError?.Message : null);
            return new EditorLibraryResult(true, null, view);
        }
    }
}

/// <summary>Library catalogs and overrides as individual generated View commands (T5d).</summary>
public sealed class EditorLibrariesViewModel : EditorFeatureViewModel
{
    private string _loadLibraryResultJson = "null";
    private string _overrideMessageResultJson = "null";
    private string _saveOverrideResultJson = "null";
    private string _revertOverrideResultJson = "null";
    private string _keepOverrideResultJson = "null";
    private string _addLocaleResultJson = "null";

    internal EditorLibrariesViewModel(EditorSession session, EditorViewModel owner, ISequencer scheduler) : base(session, owner, scheduler)
    {
        LoadLibraryCommand = CreateCommand(async argument => await Session.LoadLibraryAsync(Parse<EditorLibraryRequest>(argument)), (_, value) => LoadLibraryResultJson = value);
        OverrideMessageCommand = CreateCommand(async argument => await Session.OverrideLibraryMessageAsync(Parse<EditorLibraryOverrideRequest>(argument)), (_, value) => OverrideMessageResultJson = value);
        SaveOverrideCommand = CreateCommand(async argument => await Session.SaveLibraryOverrideAsync(Parse<EditorLibraryOverrideRequest>(argument)), (_, value) => SaveOverrideResultJson = value);
        RevertOverrideCommand = CreateCommand(async argument => await Session.RevertLibraryOverrideAsync(Parse<EditorLibraryOverrideRequest>(argument)), (_, value) => RevertOverrideResultJson = value);
        KeepOverrideCommand = CreateCommand(async argument => await Session.KeepLibraryOverrideAsync(Parse<EditorLibraryOverrideRequest>(argument)), (_, value) => KeepOverrideResultJson = value);
        AddLocaleCommand = CreateCommand(async argument => await Session.AddLibraryLocaleAsync(Parse<EditorLibraryLocaleRequest>(argument)), (_, value) => AddLocaleResultJson = value);
    }

    public string LoadLibraryResultJson { get => _loadLibraryResultJson; private set => this.RaiseAndSetIfChanged(ref _loadLibraryResultJson, value); }
    public string OverrideMessageResultJson { get => _overrideMessageResultJson; private set => this.RaiseAndSetIfChanged(ref _overrideMessageResultJson, value); }
    public string SaveOverrideResultJson { get => _saveOverrideResultJson; private set => this.RaiseAndSetIfChanged(ref _saveOverrideResultJson, value); }
    public string RevertOverrideResultJson { get => _revertOverrideResultJson; private set => this.RaiseAndSetIfChanged(ref _revertOverrideResultJson, value); }
    public string KeepOverrideResultJson { get => _keepOverrideResultJson; private set => this.RaiseAndSetIfChanged(ref _keepOverrideResultJson, value); }
    public string AddLocaleResultJson { get => _addLocaleResultJson; private set => this.RaiseAndSetIfChanged(ref _addLocaleResultJson, value); }

    public ReactiveCommand<string, RxVoid> LoadLibraryCommand { get; }
    public ReactiveCommand<string, RxVoid> OverrideMessageCommand { get; }
    public ReactiveCommand<string, RxVoid> SaveOverrideCommand { get; }
    public ReactiveCommand<string, RxVoid> RevertOverrideCommand { get; }
    public ReactiveCommand<string, RxVoid> KeepOverrideCommand { get; }
    public ReactiveCommand<string, RxVoid> AddLocaleCommand { get; }
}
