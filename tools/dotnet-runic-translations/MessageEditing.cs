using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Runic.CommandLine;
using Runic.Translations.Authoring;
using Runic.Translations.Compiler;
using Runic.Translations.Tooling;

namespace Runic.Translations.Tool;

/// <summary>
/// The <c>message</c> and <c>set-message</c> requests of <c>runic-translations serve</c>: in-page translation editing in a
/// development server reads one message in every locale and writes one locale's text back to its source.
/// </summary>
/// <remarks>
/// <para>A message of the application's catalog is written to the <c>.rmf2</c> or <c>.mf2</c> source that defines it in that
/// locale, through <see cref="Rmf2ResourceWriter"/> (the Translations Editor's source-preserving writer): only the entry's
/// own text changes, and the file keeps its formatting, comments and order. A locale without the message gets it inserted
/// into its source beside the base locale's (<see cref="Rmf2ResourceWriter.InsertMessage"/>).</para>
/// <para>A message of a library catalog (<c>catalog</c> names a catalog runic.json overrides or an installed package ships)
/// is written to the application's override source, <c>overrides/&lt;catalog&gt;/&lt;locale&gt;.rmf2</c>, by the rules of
/// <c>runic-translations overrides add</c> (<see cref="TranslationOverrideChanges"/>): a new override records the library
/// text it was written for in the review file, and a catalog runic.json does not list yet is added to its
/// <c>overrides</c>. The library's own packed sources are never written.</para>
/// <para>Every write is compiled first, with the rest of the catalog, and refused with its diagnostics when the edited
/// source has an error (or the catalog compiled before and does not after). It is also refused when the file's SHA-256
/// differs from <c>expectedHash</c>, so an edit made since the message was read (an editor's save) is never overwritten;
/// the response then carries the current message.</para>
/// </remarks>
internal static class MessageEditing
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    /// <summary>Why a request failed, as the response's <c>result.error</c>.</summary>
    internal static class Errors
    {
        /// <summary>The catalog, key or locale does not exist (exit code 2).</summary>
        internal const string NotFound = "not-found";
        /// <summary>The new text does not compile; nothing was written (exit code 1, with the compiler's diagnostics).</summary>
        internal const string Invalid = "invalid";
        /// <summary>The file changed since it was read (<c>expectedHash</c> differs); nothing was written (exit code 1).</summary>
        internal const string Conflict = "conflict";
        /// <summary>The message cannot be written there, such as a locale without a source file (exit code 1).</summary>
        internal const string Unsupported = "unsupported";
    }

    /// <summary>A failed request: its error, exit code, message and the current message when it is known.</summary>
    private sealed class EditException(string error, int exitCode, string message, IReadOnlyList<TranslationDiagnostic>? diagnostics = null) : Exception(message)
    {
        internal string Error { get; } = error;
        internal int ExitCode { get; } = exitCode;
        internal IReadOnlyList<TranslationDiagnostic> Diagnostics { get; } = diagnostics ?? [];
        internal MessageView? Current { get; init; }
    }

    /// <summary>One request's outcome: the protocol result and the <c>result</c> member of the response.</summary>
    internal sealed record Reply(ToolOperationResult Result, Action<Utf8JsonWriter>? Body);

    internal static Reply Read(string project, string? catalog, string key, SourceUnitCache units) =>
        Run(() =>
        {
            MessageView view = Load(project, catalog, key, units);
            return writer => { writer.WritePropertyName("message"); view.Write(writer); };
        });

    internal static Reply Write(string project, string? catalog, string key, string locale, string text, string? expectedHash, SourceUnitCache units) =>
        Run(() =>
        {
            MessageView before = Load(project, catalog, key, units);
            LocaleView target = before.Locale(locale) ?? throw new EditException(Errors.NotFound, 2,
                $"Catalog '{before.Catalog}' has no locale '{locale}'. Its locales: {string.Join(", ", before.Locales.Select(static item => item.Tag))}.");
            if (target.File is null) throw new EditException(Errors.Unsupported, 1, target.Reason ?? $"'{before.LogicalKey}' cannot be written in '{target.Tag}'.") { Current = before };
            string current = Hash(File.Exists(target.File) ? File.ReadAllBytes(target.File) : null);
            if (!string.Equals(expectedHash ?? Missing, current, StringComparison.Ordinal))
                throw new EditException(Errors.Conflict, 1,
                    $"{Display(target.File)} changed since the message was read (expected {expectedHash ?? Missing}, found {current}); nothing was written. Read the message again and reapply the edit.")
                { Current = before };
            IReadOnlyList<string> written = before.Library is null
                ? WriteCatalog(project, before, target, text, units)
                : WriteOverride(project, before, target, text);
            MessageView after = Load(project, catalog, key, units);
            return writer =>
            {
                writer.WriteStartArray("written");
                foreach (string path in written) writer.WriteStringValue(path);
                writer.WriteEndArray();
                writer.WritePropertyName("message");
                after.Write(writer);
            };
        });

    private static Reply Run(Func<Action<Utf8JsonWriter>> body)
    {
        var result = new ToolOperationResult();
        try
        {
            Action<Utf8JsonWriter> write = body();
            return new Reply(result, writer => { writer.WriteStartObject("result"); write(writer); writer.WriteEndObject(); });
        }
        catch (EditException exception)
        {
            result.ExitCode = exception.ExitCode;
            result.ExitCategory = exception.ExitCode == 2 ? CommandExitCategory.Usage : CommandExitCategory.CommandFailure;
            Program.WriteDiagnostics(exception.Diagnostics, result);
            result.AddDiagnostic(exception.ExitCode == 2 ? ToolCodes.Usage : ToolCodes.Authoring, "message-edit-" + exception.Error, exception.Message, CommandDiagnosticSeverity.Error);
            return new Reply(result, writer =>
            {
                writer.WriteStartObject("result");
                writer.WriteString("error", exception.Error);
                if (exception.Current is { } current) { writer.WritePropertyName("message"); current.Write(writer); }
                writer.WriteEndObject();
            });
        }
        catch (Exception exception) when (exception is ToolUsageException or ToolDiagnosticException or IOException or UnauthorizedAccessException
            or TranslationAuthoringException or InvalidOperationException or ArgumentException)
        {
            bool usage = exception is ToolUsageException;
            result.ExitCode = usage ? 2 : 1;
            result.ExitCategory = usage ? CommandExitCategory.Usage : CommandExitCategory.CommandFailure;
            result.AddDiagnostic(usage ? ToolCodes.Usage : ToolCodes.Authoring, usage ? "tool-usage" : "message-edit-failed", exception.Message, CommandDiagnosticSeverity.Error);
            return new Reply(result, writer =>
            {
                writer.WriteStartObject("result");
                writer.WriteString("error", usage ? Errors.NotFound : Errors.Unsupported);
                writer.WriteEndObject();
            });
        }
    }

    // ---- Reading --------------------------------------------------------------------------------------------------

    private static MessageView Load(string project, string? catalog, string key, SourceUnitCache units)
    {
        if (string.IsNullOrEmpty(key)) throw new ToolUsageException("key must be a non-empty message key.");
        CompilerInputs inputs = InputFiles.ReadProject(project);
        if (catalog is not null && !string.Equals(catalog, CatalogOf(inputs.Project), StringComparison.Ordinal))
            return LoadLibrary(Path.GetFullPath(inputs.Project.Path), catalog, key);
        return LoadCatalog(inputs, TranslationCompiler.CompileRmf2ProjectV5(inputs.Project, units.Resolve(inputs.Messages), units.Options), key);
    }

    private static string? CatalogOf(TranslationSource project)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(project.GetUtf8Bytes());
            return document.RootElement.ValueKind == JsonValueKind.Object && document.RootElement.TryGetProperty("catalog", out JsonElement value) &&
                value.ValueKind == JsonValueKind.String ? value.GetString() : null;
        }
        catch (JsonException) { return null; }
    }

    private static MessageView LoadCatalog(CompilerInputs inputs, Rmf2ProjectCompilation compilation, string key)
    {
        if (compilation.Project is not { } project)
            throw new EditException(Errors.Invalid, 1, "The catalog does not compile; fix its errors first.",
                compilation.Diagnostics.Where(static diagnostic => diagnostic.Severity == TranslationDiagnosticSeverity.Error).ToArray());
        Rmf2MessageContractV5 contract = Contract(project, key) ?? throw new EditException(Errors.NotFound, 2, $"Catalog '{project.Id}' has no message '{key}'.");
        var sources = inputs.Messages.ToDictionary(static source => source.Path, StringComparer.Ordinal);
        bool direct = sources.Keys.Any(static path => path.EndsWith(".mf2", StringComparison.OrdinalIgnoreCase));
        var workspace = new Rmf2Workspace(Environment.CurrentDirectory, inputs.Project, inputs.Messages);
        Rmf2TranslationV5? baseResource = project.Locales.FirstOrDefault(locale => locale.Tag == project.DefaultLocale)?.DirectResources.FirstOrDefault(resource => resource.Key == contract.Key);
        var locales = new List<LocaleView>();
        foreach (Rmf2LocaleV5 locale in Ordered(project))
        {
            Rmf2TranslationV5? resource = locale.DirectResources.FirstOrDefault(item => item.Key == contract.Key);
            if (resource is not null && sources.TryGetValue(resource.SourceLocation.Path, out TranslationSource? source))
            {
                Rmf2ResourceNode? node = Node(source, resource.SourceLocation);
                locales.Add(new LocaleView(locale.Tag, locale.FallbackTag, "catalog", node is null ? resource.SourceText : TextOf(source, node), Full(source.Path),
                    resource.SourceLocation.Line, resource.SourceLocation.Column, Full(source.Path), null));
                continue;
            }
            // A locale without its own text (translationCompleteness allows it) gets the message in the source of its locale
            // beside the base locale's: the same file name in a direct layout, the locale's .rmf2 in the same directory.
            string? file = null, reason = null;
            if (baseResource is null) reason = $"'{string.Join(".", contract.Path)}' has no base-locale source to place a '{locale.Tag}' translation beside.";
            else
            {
                string basePath = baseResource.SourceLocation.Path;
                string? directory = Path.GetDirectoryName(basePath);
                string? candidate = direct
                    ? sources.Keys.Where(path => Path.GetDirectoryName(Path.GetDirectoryName(path)) == Path.GetDirectoryName(directory) &&
                        string.Equals(Rmf2Workspace.Locale(path, true), locale.Tag, StringComparison.OrdinalIgnoreCase))
                        .Select(path => Path.Combine(Path.GetDirectoryName(path)!, Path.GetFileName(basePath)).Replace('\\', '/')).FirstOrDefault()
                    : sources.Keys.FirstOrDefault(path => Path.GetDirectoryName(path) == directory &&
                        string.Equals(Rmf2Workspace.Locale(path, false), locale.Tag, StringComparison.OrdinalIgnoreCase));
                if (candidate is null)
                    reason = $"'{locale.Tag}' has no source file beside {Display(Full(basePath))} to add '{string.Join(".", contract.Path)}' to; create it first.";
                else file = Full(candidate);
            }
            locales.Add(new LocaleView(locale.Tag, locale.FallbackTag, "missing", null, null, 0, 0, file, reason));
        }
        return new MessageView(project.Id, contract, project.DefaultLocale, locales, null, workspace);
    }

    private static MessageView LoadLibrary(string manifest, string catalogId, string key)
    {
        TranslationDependencyResolution resolution = TranslationDependencyCatalogs.Resolve(manifest);
        TranslationOverrideTarget target = resolution.TargetFor(catalogId) ?? throw new EditException(Errors.NotFound, 2,
            $"Neither the application's catalog nor an installed library catalog is '{catalogId}'." + (resolution.Catalogs.Count == 0 ? "" :
                $" Installed library catalogs: {string.Join(", ", resolution.Catalogs.Select(static item => item.Catalog))}."));
        if (target.Dependency is null) throw new EditException(Errors.NotFound, 2, target.Problem ?? $"Catalog '{catalogId}' is not installed.");
        TranslationLibraryCatalog library = TranslationLibraryCatalog.Load(target);
        if (library.Library.Project is not { } project)
            throw new EditException(Errors.Unsupported, 1, $"The library catalog '{catalogId}' ({target.Dependency.Display}) does not compile with this tool: {library.LibraryError?.Id} {library.LibraryError?.Message}");
        Rmf2MessageContractV5 contract = Contract(project, key) ?? throw new EditException(Errors.NotFound, 2, $"Catalog '{catalogId}' has no message '{key}'.");
        TranslationLibraryMessage message = library.Message(contract.Key)!;
        var locales = new List<LocaleView>();
        foreach (TranslationLibraryLocale locale in library.Locales.Where(static item => !item.Pending))
        {
            string file = Path.GetFullPath(target.SourcePath(locale.Tag));
            if (library.Override(contract.Key, locale.Tag) is { } item)
            {
                locales.Add(new LocaleView(locale.Tag, locale.Fallback, "override", item.Text, Full(item.Path), item.Location.Line, item.Location.Column, Full(item.Path), null));
                continue;
            }
            string? tag = message.Texts.Keys.FirstOrDefault(candidate => string.Equals(candidate, locale.Tag, StringComparison.OrdinalIgnoreCase));
            if (tag is not null)
            {
                TextSourceLocation location = message.Locations[tag];
                locales.Add(new LocaleView(locale.Tag, locale.Fallback, "library", message.Texts[tag], Full(location.Path), location.Line, location.Column, file, null));
            }
            else locales.Add(new LocaleView(locale.Tag, locale.Fallback, "missing", null, null, 0, 0, file, null));
        }
        return new MessageView(catalogId, contract, project.DefaultLocale, locales, library, null) { Package = target.Dependency.Display };
    }

    private static Rmf2MessageContractV5? Contract(Rmf2ProjectV5 project, string key) =>
        project.CanonicalMessages.Concat(project.ExtraMessages).FirstOrDefault(contract => contract.Key == key) ??
        project.CanonicalMessages.Concat(project.ExtraMessages).FirstOrDefault(contract => string.Join(".", contract.Path) == key);

    // The base locale first, then the others in ordinal order; pseudo-locales are generated, never authored.
    private static IEnumerable<Rmf2LocaleV5> Ordered(Rmf2ProjectV5 project) =>
        project.Locales.OrderBy(locale => locale.Tag != project.DefaultLocale).ThenBy(static locale => locale.Tag, StringComparer.Ordinal);

    // The entry a compiled resource came from.
    private static Rmf2ResourceNode? Node(TranslationSource source, TextSourceLocation location)
    {
        Rmf2ResourceDocument document = IsDirect(source.Path) ? Rmf2ResourceReader.ReadDirect(source) : Rmf2ResourceReader.Read(source);
        IEnumerable<Rmf2ResourceNode> entries = document.Nodes.Where(static node => !node.IsGroup);
        return IsDirect(source.Path) ? entries.FirstOrDefault() : entries.FirstOrDefault(node => node.NameLocation.StartByte == location.StartByte) ??
            entries.FirstOrDefault(node => node.NameLocation.Line == location.Line);
    }

    // The message text as authored: an RMF2 entry's message, or a direct file without the one line ending that frames it.
    private static string TextOf(TranslationSource source, Rmf2ResourceNode node)
    {
        if (!IsDirect(source.Path)) return node.Message ?? "";
        string text = StrictUtf8.GetString(source.GetUtf8Bytes());
        return text.EndsWith("\r\n", StringComparison.Ordinal) ? text[..^2] : text.EndsWith('\n') ? text[..^1] : text;
    }

    // ---- Writing --------------------------------------------------------------------------------------------------

    private static IReadOnlyList<string> WriteCatalog(string project, MessageView view, LocaleView locale, string text, SourceUnitCache units)
    {
        CompilerInputs inputs = InputFiles.ReadProject(project);
        string relative = InputFiles.DisplayPath(locale.File!, Path.GetFullPath(Environment.CurrentDirectory));
        TranslationSource? existing = inputs.Messages.FirstOrDefault(source => source.Path == relative);
        byte[] expected = existing?.GetUtf8Bytes() ?? (File.Exists(locale.File) ? File.ReadAllBytes(locale.File!) : []);
        var source = new TranslationSource(relative, expected);
        byte[] bytes;
        try
        {
            if (IsDirect(relative))
            {
                // One terminal line ending frames a direct file's message, as the Translations Editor writes it.
                string framed = text.EndsWith('\n') ? text : text + "\n";
                bytes = existing is null ? StrictUtf8.GetBytes(framed) : Rmf2ResourceWriter.SetMessage(source, Rmf2ResourceReader.ReadDirect(source).Nodes.First(static node => !node.IsGroup).Key, framed);
            }
            else if (locale.Origin == "catalog")
            {
                Rmf2ResourceNode node = Rmf2ResourceReader.Read(source).Nodes.FirstOrDefault(node => !node.IsGroup && node.NameLocation.Line == locale.Line)
                    ?? throw new EditException(Errors.Conflict, 1, $"{Display(locale.File!)} no longer has '{view.LogicalKey}' where it was read; read the message again.") { Current = view };
                bytes = Rmf2ResourceWriter.SetMessage(source, node.Key, text);
            }
            else bytes = Rmf2ResourceWriter.InsertMessage(source, view.Workspace!.LocalPath(relative, view.Contract.Path), text);
        }
        catch (TranslationAuthoringException exception)
        {
            throw new EditException(Errors.Invalid, 1, $"The new text of '{view.LogicalKey}' in '{locale.Tag}' cannot be written: {exception.Message}");
        }

        // The whole catalog is compiled with the edit before anything is written.
        var edited = inputs.Messages.Where(item => item.Path != relative).Append(new TranslationSource(relative, bytes)).OrderBy(static item => item.Path, StringComparer.Ordinal).ToArray();
        Rmf2ProjectCompilation before = TranslationCompiler.CompileRmf2ProjectV5(inputs.Project, units.Resolve(inputs.Messages), units.Options);
        Rmf2ProjectCompilation after = TranslationCompiler.CompileRmf2ProjectV5(inputs.Project, units.Resolve(edited), units.Options);
        Reject(view, locale, relative, before.Diagnostics, after.Diagnostics);
        ReplaceIfUnchanged(locale.File!, bytes, existing is null && !File.Exists(locale.File) ? null : expected, view);
        return [locale.File!];
    }

    private static List<string> WriteOverride(string project, MessageView view, LocaleView locale, string text)
    {
        TranslationLibraryCatalog library = view.Library!;
        string manifest = Path.GetFullPath(InputFiles.ReadProject(project).Project.Path);
        TranslationOverridePlan plan;
        try
        {
            if (library.Override(view.Contract.Key, locale.Tag) is { } item)
            {
                TranslationSource source = library.OverrideSources.Single(candidate => candidate.Path == item.Path);
                Rmf2ResourceNode node = Rmf2ResourceReader.Analyze(source).Nodes.First(node => !node.IsGroup && node.Key == view.Contract.Key);
                plan = new TranslationOverridePlan(Path.GetFullPath(item.Path), Rmf2ResourceWriter.SetMessage(source, node.Key, text), source.GetUtf8Bytes());
            }
            else
            {
                // A new override, by the rules of `runic-translations overrides add --key`: seeded comments, a review entry, and
                // the catalog listed in runic.json "overrides" when it is not yet.
                plan = TranslationOverrideChanges.Override(library, [view.Contract.Key], locale.Tag, text);
                plan = TranslationOverrideChanges.Declaring(plan, manifest, library.Target);
            }
        }
        catch (TranslationAuthoringException exception)
        {
            throw new EditException(Errors.Invalid, 1, $"The new text of '{view.LogicalKey}' in '{locale.Tag}' cannot be written: {exception.Message}");
        }
        catch (ArgumentException exception) { throw new EditException(Errors.Unsupported, 1, exception.Message) { Current = view }; }

        // The library compiled with the edited override source, by the build's rules, before anything is written.
        TranslationLibraryCatalog edited = TranslationLibraryCatalog.Load(library.Target with { Listed = true, Problem = null },
            new Dictionary<string, byte[]>(StringComparer.Ordinal) { [plan.Path] = plan.Bytes! });
        Reject(view, locale, new TranslationSource(plan.Path, []).Path, library.Overlay.Diagnostics, edited.Overlay.Diagnostics);
        try { TranslationOverrideChanges.Apply(plan, library.Target with { Listed = true, Problem = null }); }
        catch (TranslationOverrideConflictException exception) { throw new EditException(Errors.Conflict, 1, exception.Message) { Current = view }; }
        catch (TranslationInterchangeException exception) { throw new EditException(Errors.Unsupported, 1, $"The review file cannot be written ({exception.Code}): {exception.Message}"); }
        var written = new List<string> { plan.Path };
        if (plan.ProjectPath is not null) written.Add(plan.ProjectPath);
        if (plan.Review is not null) written.Add(Path.GetFullPath(library.Target.ReviewPath));
        return written;
    }

    // Refuses an edit whose source has an error, or that breaks a catalog that compiled before. Errors elsewhere that were
    // there before do not block an edit, so one broken file does not stop editing the others.
    private static void Reject(MessageView view, LocaleView locale, string path, IReadOnlyList<TranslationDiagnostic> before, IReadOnlyList<TranslationDiagnostic> after)
    {
        TranslationDiagnostic[] errors = after.Where(static diagnostic => diagnostic.Severity == TranslationDiagnosticSeverity.Error).ToArray();
        if (errors.Length == 0) return;
        bool brokeIt = !before.Any(static diagnostic => diagnostic.Severity == TranslationDiagnosticSeverity.Error);
        TranslationDiagnostic[] own = errors.Where(diagnostic => SamePath(diagnostic.Location.Path, path)).ToArray();
        if (own.Length == 0 && !brokeIt) return;
        throw new EditException(Errors.Invalid, 1,
            $"The new text of '{view.LogicalKey}' in '{locale.Tag}' does not compile; nothing was written.", own.Length != 0 ? own : errors);
    }

    private static bool SamePath(string left, string right) =>
        string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    // Replaces a file atomically while it still holds expected (null: does not exist), as the editor's save does.
    private static void ReplaceIfUnchanged(string path, byte[] bytes, byte[]? expected, MessageView view)
    {
        string directory = Path.GetDirectoryName(path)!;
        string temporary = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllBytes(temporary, bytes);
            if (File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new IOException($"{Display(path)} is a symbolic link or reparse point; it is not written.");
            byte[]? now = File.Exists(path) ? File.ReadAllBytes(path) : null;
            if (now is null ? expected is not null : expected is null || !now.AsSpan().SequenceEqual(expected))
                throw new EditException(Errors.Conflict, 1, $"{Display(path)} changed while the edit was compiled; nothing was written. Read the message again.") { Current = view };
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    // ---- Shared ---------------------------------------------------------------------------------------------------

    private const string Missing = "none";

    /// <summary>The <c>sha256:&lt;hex&gt;</c> hash of a file's bytes, or <c>none</c> for a file that does not exist.</summary>
    internal static string Hash(byte[]? bytes) => bytes is null ? Missing : "sha256:" + Convert.ToHexStringLower(SHA256.HashData(bytes));

    private static bool IsDirect(string path) => path.EndsWith(".mf2", StringComparison.OrdinalIgnoreCase);

    private static string Full(string path) => Path.GetFullPath(path).Replace('\\', '/');

    private static string Display(string path) => InputFiles.DisplayPath(path, Path.GetFullPath(Environment.CurrentDirectory));

    /// <param name="Origin"><c>catalog</c>, <c>override</c>, <c>library</c> or <c>missing</c>.</param>
    /// <param name="Source">Where <paramref name="Text"/> is defined.</param>
    /// <param name="File">The file a write of this locale changes (absolute), or null when it cannot be written (<paramref name="Reason"/>).</param>
    private sealed record LocaleView(string Tag, string? Fallback, string Origin, string? Text, string? Source, int Line, int Column, string? File, string? Reason);

    private sealed record MessageView(string Catalog, Rmf2MessageContractV5 Contract, string BaseLocale, IReadOnlyList<LocaleView> Locales,
        TranslationLibraryCatalog? Library, Rmf2Workspace? Workspace)
    {
        internal string? Package { get; init; }
        internal string LogicalKey => string.Join(".", Contract.Path);

        internal LocaleView? Locale(string tag) =>
            Locales.FirstOrDefault(item => string.Equals(item.Tag, tag, StringComparison.OrdinalIgnoreCase));

        internal void Write(Utf8JsonWriter writer)
        {
            writer.WriteStartObject();
            writer.WriteString("catalog", Catalog);
            writer.WriteString("kind", Library is null ? "catalog" : "library");
            if (Package is not null) writer.WriteString("package", Package);
            writer.WriteString("key", Contract.Key);
            writer.WriteString("logicalKey", LogicalKey);
            writer.WriteString("baseLocale", BaseLocale);
            writer.WriteString("content", Contract.Content);
            writer.WriteBoolean("structured", Contract.Structured);
            writer.WriteStartArray("inputs");
            foreach (Rmf2InputV5 input in Contract.Inputs)
            {
                writer.WriteStartObject();
                writer.WriteString("name", input.Name);
                writer.WriteString("type", input.Type);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteStartArray("markup");
            foreach (string name in Contract.MarkupNames) writer.WriteStringValue(name);
            writer.WriteEndArray();
            var hashes = new Dictionary<string, string>(StringComparer.Ordinal);
            string HashOf(string path) => hashes.TryGetValue(path, out string? hash) ? hash
                : hashes[path] = MessageEditing.Hash(System.IO.File.Exists(path) ? System.IO.File.ReadAllBytes(path) : null);
            writer.WriteStartArray("locales");
            foreach (LocaleView locale in Locales)
            {
                writer.WriteStartObject();
                writer.WriteString("locale", locale.Tag);
                if (locale.Fallback is null) writer.WriteNull("fallback"); else writer.WriteString("fallback", locale.Fallback);
                writer.WriteString("origin", locale.Origin);
                if (locale.Text is null) writer.WriteNull("text"); else writer.WriteString("text", locale.Text);
                if (locale.Source is null) writer.WriteNull("source");
                else
                {
                    writer.WriteStartObject("source");
                    writer.WriteString("path", locale.Source);
                    writer.WriteNumber("line", locale.Line);
                    writer.WriteNumber("column", locale.Column);
                    writer.WriteEndObject();
                }
                if (locale.File is null) writer.WriteNull("file");
                else
                {
                    writer.WriteStartObject("file");
                    writer.WriteString("path", locale.File);
                    writer.WriteBoolean("exists", System.IO.File.Exists(locale.File));
                    writer.WriteString("hash", HashOf(locale.File));
                    writer.WriteEndObject();
                }
                writer.WriteBoolean("editable", locale.File is not null);
                if (locale.Reason is not null) writer.WriteString("reason", locale.Reason);
                WriteStructure(writer, locale);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        // The message's inputs, selectors and variants in this locale, from the Translations Editor's projection.
        private static void WriteStructure(Utf8JsonWriter writer, LocaleView locale)
        {
            if (locale.Text is null) { writer.WriteNull("structure"); return; }
            Rmf2AuthoringProjection projection;
            try { projection = Rmf2AuthoringService.Project(new TranslationSource("message.mf2", StrictUtf8.GetBytes(locale.Text)), locale.Tag); }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or FormatException or TranslationAuthoringException)
            {
                writer.WriteStartObject("structure");
                writer.WriteBoolean("supported", false);
                writer.WriteString("reason", exception.Message);
                writer.WriteEndObject();
                return;
            }
            writer.WriteStartObject("structure");
            writer.WriteBoolean("supported", projection.Supported);
            if (projection.Reason is not null) writer.WriteString("reason", projection.Reason);
            writer.WriteStartArray("inputs");
            foreach (Rmf2AuthoringInput input in projection.Inputs)
            {
                writer.WriteStartObject();
                writer.WriteString("name", input.Name);
                writer.WriteString("type", input.Type);
                if (input.Function is null) writer.WriteNull("function"); else writer.WriteString("function", input.Function);
                writer.WriteBoolean("declared", input.Declared);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteStartArray("selectors");
            foreach (string selector in projection.Selectors) writer.WriteStringValue(selector);
            writer.WriteEndArray();
            writer.WriteStartArray("selectorFunctions");
            foreach (string function in projection.SelectorFunctions) writer.WriteStringValue(function);
            writer.WriteEndArray();
            writer.WriteStartArray("variants");
            foreach (Rmf2AuthoringVariant variant in projection.Variants)
            {
                writer.WriteStartObject();
                writer.WriteString("id", variant.Id);
                writer.WriteStartArray("keys");
                foreach (string key in variant.Keys) writer.WriteStringValue(key);
                writer.WriteEndArray();
                writer.WriteString("pattern", variant.Pattern);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteStartArray("pluralCategories");
            foreach (string category in projection.PluralCategories) writer.WriteStringValue(category);
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
    }
}
