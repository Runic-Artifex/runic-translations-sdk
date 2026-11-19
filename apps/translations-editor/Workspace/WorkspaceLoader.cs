using System.Text;
using System.Text.Json;
using Runic.Translations.Tooling;
using Runic.Translations.Authoring;
using Runic.Translations.Compiler;
using static Runic.Translations.Editor.WorkspaceFiles;
using static Runic.Translations.Editor.WorkspaceStateReader;

namespace Runic.Translations.Editor;

/// <summary>
/// The workspace snapshot the frontend receives: the catalog, documents with their entries, diagnostics, review data,
/// status report and library catalogs; or the pending recovery or the catalog choice when there is no catalog to load.
/// </summary>
internal sealed class WorkspaceLoader
{
    private readonly WorkspaceGate _gate;
    private readonly WorkspaceFiles _files;
    private readonly WorkspaceStateReader _reader;
    private readonly DiagnosticProjector _diagnostics;
    private readonly EntryProjector _entries;
    private readonly ReviewService _review;
    private readonly string _root;
    private (string Config, string Stamp, TranslationDependencyResolution Resolution)? _dependencies;

    internal WorkspaceLoader(WorkspaceGate gate, WorkspaceFiles files, WorkspaceStateReader reader, DiagnosticProjector diagnostics,
        EntryProjector entries, ReviewService review)
    {
        _gate = gate;
        _files = files;
        _reader = reader;
        _diagnostics = diagnostics;
        _entries = entries;
        _review = review;
        _root = files.Root;
    }

    public async Task<WorkspaceSnapshot> LoadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _gate.ThrowIfDisposed();
            return await LoadCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    // Callers that already hold _gate (commit paths) reload through this core.
    internal async Task<WorkspaceSnapshot> LoadCoreAsync(CancellationToken cancellationToken)
    {
        _files.InvalidateDiscoveredProjects();
        TranslationPendingTransaction? pending = TranslationWorkspaceTransaction.GetPending(_root);
        if (pending is not null)
        {
            return new WorkspaceSnapshot(
                _root,
                null,
                [],
                [],
                [new EditorDiagnostic("RECOVERY", "error", string.Empty, string.Empty, 1, 1, 1, 1, EditorNotice.Create("ui_backend_recovery_required"))],
                false,
                new EditorPendingTransaction(pending.CatalogId, pending.Paths),
                null,
                null);
        }
        if (_files.FindMf2ProjectConfig() is null && _files.DiscoverProjects() is { Count: > 0 } projects)
        {
            // Several catalogs and none chosen: the welcome screen lists them (L5-4).
            EditorCatalogSummary[] summaries = projects.Select(project => _reader.SummarizeProject(project, cancellationToken)).ToArray();
            return new WorkspaceSnapshot(_root, null, summaries, [], [], true, null, null, null);
        }
        WorkspaceState state = await _reader.ReadStateAsync(null, null, cancellationToken, resetBaseline: true).ConfigureAwait(false);
        return CreateSnapshot(state);
    }

    private WorkspaceSnapshot CreateSnapshot(WorkspaceState state)
    {
        EditorCatalog? catalog = null;
        WorkspaceFile? manifest = _reader.CatalogId is null
            ? null
            : state.Files.Find(file => file.Kind == DocumentKind.Manifest && string.Equals(file.CatalogId, _reader.CatalogId, StringComparison.Ordinal));
        if (manifest is not null)
            catalog = ReadCatalog(manifest.Content);
        if (catalog is not null && state.Compilation.Project is { } rmf2)
        {
            catalog = catalog with
            {
                DefaultLocale = rmf2.DefaultLocale,
                Locales = rmf2.Locales.Select(static locale => new EditorLocale(locale.Tag, locale.FallbackTag)).ToArray(),
            };
        }

        var documents = new List<EditorDocument>(state.Files.Count);
        IReadOnlyDictionary<string, Rmf2TranslationV5> baseResources = BaseResources(state);
        foreach (WorkspaceFile file in state.Files)
        {
            bool malformed = file.Kind == DocumentKind.Resource && state.Compilation.Diagnostics.Any(diagnostic =>
                diagnostic.Severity == TranslationDiagnosticSeverity.Error &&
                string.Equals(diagnostic.Location.Path, file.Path, StringComparison.Ordinal));
            documents.Add(new EditorDocument(
                file.Path,
                file.Content,
                file.Revision,
                file.Kind == DocumentKind.Manifest,
                malformed,
                file.Locale,
                file.Layer,
                _entries.ReadEntries(file.Path, file.Content, state.Compilation, baseResources)));
        }
        EditorReviewSnapshot? review = null;
        if (_reader.CatalogId is not null)
        {
            EditorReviewStore store = _review.ReviewStore(_reader.CatalogId);
            Rmf2ProjectCompilation reviewCompilation = ReviewCompilation(state);
            try { review = _review.KnownReview(store.Load(reviewCompilation, ReviewService.BaseValues(state, reviewCompilation))); }
            catch (Exception exception) when (exception is TranslationEditorStateException or TranslationInterchangeException or IOException or UnauthorizedAccessException)
            {
                review = new EditorReviewSnapshot(store.DisplayPath, null, exception.Message, [], []);
            }
        }
        return new WorkspaceSnapshot(_root, catalog, state.Catalogs, documents, _diagnostics.Diagnostics(state), state.Compilation.Success, null, review, null, Status(state)) { Libraries = Libraries() };
    }

    // The status report of runic-translations status, from the same compilation (relaxed while translations are
    // incomplete) and review file. The editor has no --min-coverage thresholds.
    private TranslationsStatusProject? Status(WorkspaceState state)
    {
        WorkspaceFile? manifest = state.Files.Find(static file => file.Kind == DocumentKind.Manifest);
        if (manifest is null || _reader.CatalogId is null) return null;
        Rmf2ProjectCompilation? counted = TranslationStatus.ForStatus(state.Compilation, Source(manifest.Path, manifest.Content),
            state.Files.Where(static file => file.Kind == DocumentKind.Resource).Select(static file => Source(file.Path, file.Content)).ToArray());
        if (counted is null) return null;
        string reviewPath = TranslationReviewFile.PathFor(_files.ContainedPath(manifest.Path), _reader.CatalogId);
        TranslationInterchangeReview? review = null;
        try { review = TranslationReviewFile.Read(reviewPath, _reader.CatalogId); }
        catch (Exception exception) when (exception is TranslationInterchangeException or IOException or UnauthorizedAccessException) { /* The review snapshot reports it. */ }
        return TranslationStatus.Report(manifest.Path, counted, review, review is null ? null : _files.ReportedOutputPath(reviewPath), new Dictionary<string, double>());
    }

    private static EditorCatalog? ReadCatalog(string content)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(WithoutBom(content));
            JsonElement root = document.RootElement;
            string id = root.GetProperty("catalog").GetString() ?? string.Empty;
            int schemaVersion = root.GetProperty("schemaVersion").GetInt32();
            string defaultLocale = root.GetProperty("baseLocale").GetString() ?? string.Empty;
            var locales = new List<EditorLocale>();
            if (root.TryGetProperty("locales", out JsonElement localeValues))
            {
                foreach (JsonElement locale in localeValues.EnumerateArray())
                {
                    if (locale.ValueKind == JsonValueKind.String)
                    {
                        string tag = locale.GetString() ?? string.Empty;
                        locales.Add(new EditorLocale(tag, string.Equals(tag, defaultLocale, StringComparison.OrdinalIgnoreCase) ? null : defaultLocale));
                    }
                    else
                    {
                        locales.Add(new EditorLocale(
                            locale.GetProperty("tag").GetString() ?? string.Empty,
                            locale.TryGetProperty("fallback", out JsonElement fallback) ? fallback.GetString() : null));
                    }
                }
            }
            if (locales.Count == 0 && defaultLocale.Length != 0) locales.Add(new EditorLocale(defaultLocale, null));
            string[] pseudoLocales = root.TryGetProperty("pseudoLocales", out JsonElement pseudo) && pseudo.ValueKind == JsonValueKind.Array
                ? Rmf2PseudoLocalesV5.Supported.Where(tag => pseudo.EnumerateArray().Any(item => item.ValueKind == JsonValueKind.String &&
                    string.Equals(item.GetString(), tag, StringComparison.OrdinalIgnoreCase))).ToArray()
                : [];
            return new EditorCatalog(id, schemaVersion, defaultLocale, locales, [new EditorLayer("base", 0)], pseudoLocales.Length == 0 ? null : pseudoLocales);
        }
        // A manifest with a catalog but missing or mistyped members is shown
        // without catalog metadata; the compiler diagnostics explain why.
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            return null;
        }
    }

    // The dependency resolution of the selected project, reused while runic.json and the restore and install inputs
    // (project.assets.json, node_modules state; their size and time) are unchanged, so a full load does not parse a large
    // project.assets.json again.
    private TranslationDependencyResolution ResolveDependencies(string config)
    {
        byte[] bytes = File.ReadAllBytes(config);
        if (_dependencies is { } cached && cached.Config == config && cached.Stamp == DependencyStamp(bytes, cached.Resolution.Inputs)) return cached.Resolution;
        TranslationDependencyResolution resolution = TranslationDependencyCatalogs.Resolve(config, bytes);
        _dependencies = (config, DependencyStamp(bytes, resolution.Inputs), resolution);
        return resolution;

        static string DependencyStamp(byte[] manifest, IReadOnlyList<string> inputs)
        {
            var stamp = new StringBuilder(Revision(manifest));
            foreach (string input in inputs)
            {
                var file = new FileInfo(input);
                if (file.Exists) stamp.Append('|').Append(file.Length).Append(':').Append(file.LastWriteTimeUtc.Ticks);
                else if (Directory.Exists(input)) stamp.Append("|d").Append(Directory.GetLastWriteTimeUtc(input).Ticks);
                else stamp.Append("|-");
            }
            return stamp.ToString();
        }
    }

    // The library catalogs the selected project can override (T5d): installed packages that pack a catalog, and the
    // catalogs its runic.json "overrides" lists, resolved through Tooling as the build resolves them.
    private List<EditorLibrarySummary> Libraries()
    {
        if (_files.FindMf2ProjectConfig() is not { } config) return new List<EditorLibrarySummary>();
        try
        {
            TranslationDependencyResolution resolution = ResolveDependencies(config);
            var result = new List<EditorLibrarySummary>();
            foreach (string id in resolution.Catalogs.Select(static item => item.Catalog).Concat(resolution.Overrides.Select(static item => item.Catalog)).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
            {
                TranslationOverrideTarget? listed = resolution.Override(id);
                TranslationDependencyCatalog? dependency = listed?.Dependency ?? resolution.Catalogs.FirstOrDefault(item => item.Catalog == id);
                int overrides = listed is null || !Directory.Exists(listed.Directory) ? 0
                    : Directory.EnumerateFiles(listed.Directory, "*.rmf2", SearchOption.AllDirectories).Count();
                result.Add(new EditorLibrarySummary(id, dependency?.Package, dependency?.Version,
                    dependency is null ? null : dependency.Origin == TranslationDependencyOrigin.NuGet ? "nuget" : "npm",
                    listed is { Listed: true }, listed?.Problem, overrides));
            }
            return result;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return new List<EditorLibrarySummary>();
        }
    }

    /// <summary>The compiled catalog's locales, default locale and unsupportedLocale policy; null when it does not compile.</summary>
    internal async Task<EditorLocaleContext?> LocaleContextAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _gate.ThrowIfDisposed();
            // No baseline reset: reading the state for a description must not hide an external change.
            WorkspaceState state = await _reader.ReadStateAsync(null, null, cancellationToken, resetBaseline: false).ConfigureAwait(false);
            if (state.Compilation.Project is not { } project) return null;
            UnsupportedLocalePolicy policy = project.UnsupportedLocale switch
            {
                TranslationUnsupportedLocalePolicy.Exact => UnsupportedLocalePolicy.Exact,
                TranslationUnsupportedLocalePolicy.Default => UnsupportedLocalePolicy.Default,
                _ => UnsupportedLocalePolicy.ParentsThenDefault,
            };
            return new EditorLocaleContext(project.Locales.Select(static locale => locale.Tag).ToArray(), project.DefaultLocale, policy, project.Id);
        }
        finally
        {
            _gate.Release();
        }
    }
}
