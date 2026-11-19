using System.Security.Cryptography;
using System.Text;
using Runic.Translations.Internal;
using Runic.Translations.Tooling;
using Runic.Translations.Authoring;
using Runic.Translations.Compiler;
using static Runic.Translations.Editor.WorkspaceFiles;

namespace Runic.Translations.Editor;

/// <summary>
/// Reads the selected project's state (runic.json and its message sources, optionally with one unsaved document) and
/// compiles it, reusing the last compilation and its pseudo-locale compilations while the sources are unchanged. Owns the
/// open catalog's ID, the review compilation of a state, and the summaries of the workspace's other projects.
/// </summary>
internal sealed class WorkspaceStateReader
{
    private readonly WorkspaceFiles _files;
    private readonly WorkspaceChangeTracker _changes;
    private readonly string _root;
    private string? _catalogId;
    // The last compilation by the revision of its sources, so requests on unchanged sources (the preview and the
    // pseudo-locale preview of one edit, a samples change) reuse it instead of compiling again.
    private (string Revision, Rmf2ProjectCompilation Compilation)? _lastCompilation;
    // The pseudo-locale compilations of _lastCompilation's sources, by pseudo-locale.
    private readonly Dictionary<string, (string Revision, Rmf2ProjectCompilation Compilation)> _pseudoCompilations = new(StringComparer.Ordinal);
    // Guards the two caches: PlanRmf2Mutation reads the state outside _gate.
    private readonly Lock _compilationCache = new();

    internal WorkspaceStateReader(WorkspaceFiles files, WorkspaceChangeTracker changes)
    {
        _files = files;
        _changes = changes;
        _root = files.Root;
    }

    internal string? CatalogId => _catalogId;

    // The state's sources compiled with the pseudo-locale generated, as --pseudo-locales builds compile them, also when
    // runic.json does not configure it. runic.json is read by the compiler as in every other compile (a BOM is skipped,
    // a duplicate member is its diagnostic), never re-serialized. One compilation per sources revision and pseudo-locale.
    internal Rmf2ProjectCompilation PseudoLocaleCompilation(WorkspaceState state, string locale, CancellationToken cancellationToken)
    {
        lock (_compilationCache)
            if (_pseudoCompilations.TryGetValue(locale, out var cached) && string.Equals(cached.Revision, state.SourcesRevision, StringComparison.Ordinal))
                return cached.Compilation;
        WorkspaceFile manifest = state.Files.Single(static file => file.Kind == DocumentKind.Manifest);
        Rmf2ProjectCompilation compilation = TranslationCompiler.CompileRmf2ProjectV5(Source(manifest.Path, manifest.Content),
            state.Files.Where(static file => file.Kind == DocumentKind.Resource).Select(static file => Source(file.Path, file.Content)).ToArray(),
            null, [locale], cancellationToken);
        lock (_compilationCache)
            // Only for the sources the cache holds; an older state's pseudo compilation is not kept.
            if (state.SourcesRevision is { } revision && string.Equals(_lastCompilation?.Revision, revision, StringComparison.Ordinal))
                _pseudoCompilations[locale] = (revision, compilation);
        return compilation;
    }

    // The compilation review data is computed from: the state's own when it succeeded, else the same sources compiled
    // without validation policies, else the base locale alone (TranslationReviewFile.ForReview). Cached per state.
    internal static Rmf2ProjectCompilation ReviewCompilation(WorkspaceState state, CancellationToken cancellationToken = default)
    {
        if (state.ReviewCompilation is { } cached) return cached;
        WorkspaceFile? manifest = state.Files.Find(static file => file.Kind == DocumentKind.Manifest);
        Rmf2ProjectCompilation review = manifest is null ? state.Compilation : TranslationReviewFile.ForReview(state.Compilation, Source(manifest.Path, manifest.Content),
            state.Files.Where(static file => file.Kind == DocumentKind.Resource).Select(static file => Source(file.Path, file.Content)).ToArray(), null, cancellationToken);
        state.ReviewCompilation = review;
        return review;
    }

    // A read of the files on disk (no replacement) becomes the baseline of the external-change check only when
    // resetBaseline is true: a workspace load (WorkspaceLoader), which every applied change ends with. Previews, exports
    // and drafts read without acknowledging anything, so an outside edit made just before them is still reported.
    internal Task<WorkspaceState> ReadStateAsync(
        string? replacementPath,
        string? replacementContent,
        CancellationToken cancellationToken,
        bool resetBaseline = false)
    {
        string? projectConfig = _files.FindMf2ProjectConfig();
        if (projectConfig is null)
            throw new EditorUserException(EditorNotice.Create("ui_backend_project_required"));
        return ReadMf2StateAsync(projectConfig, replacementPath, replacementContent, cancellationToken, resetBaseline);
    }

    private Task<WorkspaceState> ReadMf2StateAsync(
        string configPath,
        string? replacementPath,
        string? replacementContent,
        CancellationToken cancellationToken,
        bool resetBaseline = false)
    {
        string projectRoot = Path.GetDirectoryName(configPath)!;
        string configRelativePath = NormalizeRelativePath(Path.GetRelativePath(_root, configPath));
        var paths = new List<string> { configPath };
        // A malformed or half-saved manifest contributes no source roots. The
        // compiler then reports the located configuration diagnostic, and the
        // next load after the manifest is fixed discovers the sources again.
        TranslationManifestLayout layout = TranslationManifestReader.Read(
            replacementPath == configRelativePath && replacementContent is not null ? StrictUtf8.GetBytes(replacementContent) : File.ReadAllBytes(configPath),
            projectRoot);
        var discoveryErrors = new List<string>();
        IReadOnlyList<string> sourceRoots = _files.UsableSourceRoots(layout.SourceRoots, discoveryErrors);
        foreach (string sourceRoot in sourceRoots)
        {
            // runic.json "overrides": translations/overrides holds overrides of library catalogs, not this catalog's sources.
            paths.AddRange(EnumerateSourceFiles(sourceRoot, ".mf2").Where(path => !layout.IsOverrideSource(path)).Order(StringComparer.Ordinal));
            paths.AddRange(EnumerateSourceFiles(sourceRoot, ".rmf2").Where(path => !layout.IsOverrideSource(path)).Order(StringComparer.Ordinal));
        }
        var files = new List<WorkspaceFile>(paths.Count);
        TranslationSource? projectSource = null;
        var messageSources = new List<TranslationSource>();
        string? catalogId = null;
        foreach (string fullPath in paths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string relativePath = NormalizeRelativePath(Path.GetRelativePath(_root, fullPath));
            byte[] bytes = ReadSourceBytes(_files.ContainedPath(relativePath));
            string content = StrictUtf8.GetString(bytes);
            if (string.Equals(relativePath, replacementPath, StringComparison.Ordinal))
                content = replacementContent ?? string.Empty;
            bool isProject = string.Equals(relativePath, configRelativePath, StringComparison.Ordinal);
            if (isProject)
            {
                catalogId = ReadCatalogId(content);
                projectSource = Source(relativePath, content);
                files.Add(new WorkspaceFile(relativePath, content, Revision(bytes), DocumentKind.Manifest, catalogId, null, null));
            }
            else
            {
                string? locale = SourceLocale(fullPath, sourceRoots);
                messageSources.Add(Source(relativePath, content));
                files.Add(new WorkspaceFile(relativePath, content, Revision(bytes), DocumentKind.Resource, catalogId, locale, "base"));
            }
        }
        if (projectSource is null) throw new EditorUserException(EditorNotice.Create("ui_backend_project_config_missing"));
        if (replacementPath is not null && !files.Exists(file => string.Equals(file.Path, replacementPath, StringComparison.Ordinal)))
        {
            string replacementFullPath = _files.ContainedPath(replacementPath);
            if (!sourceRoots.Any(root => IsWithinSourceRoot(root, replacementFullPath)) ||
                !IsSourcePath(replacementPath))
                throw new EditorUserException(EditorNotice.Create("ui_backend_source_outside_project", ("path", replacementPath)));
            string? locale = SourceLocale(replacementFullPath, sourceRoots);
            string content = replacementContent ?? string.Empty;
            messageSources.Add(Source(replacementPath, content));
            files.Add(new WorkspaceFile(replacementPath, content, EditorWorkspace.NewMf2DocumentRevision, DocumentKind.Resource, catalogId, locale, "base"));
            files.Sort(static (left, right) => StringComparer.Ordinal.Compare(left.Path, right.Path));
        }

        string sourcesRevision = SourcesRevision(projectSource, messageSources);
        Rmf2ProjectCompilation? compiled = null;
        lock (_compilationCache)
            if (_lastCompilation is { } last && string.Equals(last.Revision, sourcesRevision, StringComparison.Ordinal)) compiled = last.Compilation;
        if (compiled is null)
        {
            compiled = TranslationCompiler.CompileRmf2ProjectV5(projectSource, messageSources, null, cancellationToken);
            lock (_compilationCache)
            {
                _lastCompilation = (sourcesRevision, compiled);
                _pseudoCompilations.Clear();
            }
        }
        Rmf2ProjectCompilation compilation = TranslationManifestReader.WithManifestErrors(compiled, projectSource.Path, layout.Error, discoveryErrors);
        Rmf2ProjectV5? rmf2 = compilation.Project;
        (string Tag, string? Fallback)[] compiledLocales = rmf2?.Locales.Select(static locale => (locale.Tag, locale.FallbackTag)).ToArray() ?? [];
        if (compiledLocales.Length != 0)
        {
            for (int index = 0; index < files.Count; index++)
            {
                WorkspaceFile file = files[index];
                string? canonicalLocale = compiledLocales.FirstOrDefault(locale =>
                    string.Equals(locale.Tag, file.Locale, StringComparison.OrdinalIgnoreCase)).Tag;
                if (canonicalLocale is not null) files[index] = file with { Locale = canonicalLocale };
            }
        }
        _catalogId = rmf2?.Id ?? catalogId;
        if (replacementPath is null && resetBaseline)
        {
            _changes.ResetBaseline(files);
        }
        int errors = compilation.Diagnostics.Count(item => item.Severity == TranslationDiagnosticSeverity.Error);
        var summaries = new List<EditorCatalogSummary>
        {
            new EditorCatalogSummary(
                _catalogId ?? string.Empty,
                new[] { configRelativePath },
                messageSources.Count,
                compiledLocales.Length,
                rmf2?.CanonicalMessages.Count ?? 0,
                errors,
                compilation.Diagnostics.Count - errors,
                compilation.Success),
        };
        // The workspace's other catalogs, for switching (L5-4); only on full loads, not per keystroke.
        if (replacementPath is null && resetBaseline)
            foreach (string other in _files.DiscoverProjects().Where(path => !string.Equals(path, configPath, StringComparison.Ordinal)))
                summaries.Add(SummarizeProject(other, cancellationToken));
        return Task.FromResult(new WorkspaceState(files, compilation, summaries) { SourcesRevision = sourcesRevision });
    }

    // The revision of everything a compile reads: runic.json and every message source, by path and content.
    private static string SourcesRevision(TranslationSource project, IEnumerable<TranslationSource> messages)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> length = stackalloc byte[8];
        foreach (TranslationSource source in messages.Prepend(project))
            foreach (byte[] part in (byte[][])[StrictUtf8.GetBytes(source.Path), source.GetUtf8Bytes()])
            {
                System.Buffers.Binary.BinaryPrimitives.WriteInt64LittleEndian(length, part.Length);
                hash.AppendData(length);
                hash.AppendData(part);
            }
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    // A catalog summary of a project that is not open: its ID and counts from a compilation of its sources on disk.
    internal EditorCatalogSummary SummarizeProject(string configPath, CancellationToken cancellationToken)
    {
        string relative = NormalizeRelativePath(Path.GetRelativePath(_root, configPath));
        try
        {
            byte[] bytes = ReadSourceBytes(configPath);
            string? id = ReadCatalogId(StrictUtf8.GetString(bytes));
            TranslationManifestLayout layout = TranslationManifestReader.Read(bytes, Path.GetDirectoryName(configPath)!);
            var sources = new List<TranslationSource>();
            foreach (string sourceRoot in _files.UsableSourceRoots(layout.SourceRoots, null))
                foreach (string path in EnumerateSourceFiles(sourceRoot, ".mf2").Concat(EnumerateSourceFiles(sourceRoot, ".rmf2")).Where(path => !layout.IsOverrideSource(path)).Order(StringComparer.Ordinal))
                    sources.Add(new TranslationSource(NormalizeRelativePath(Path.GetRelativePath(_root, path)), ReadSourceBytes(path)));
            // Full loads summarize every other project; one is compiled again only when its runic.json or a source changed.
            var hash = new SummaryHash();
            hash.Append(bytes);
            foreach (TranslationSource source in sources) { hash.Append(Encoding.UTF8.GetBytes("\0" + source.Path + "\0")); hash.Append(source.GetUtf8Bytes()); }
            string revision = hash.Finish();
            lock (_summaries)
                if (_summaries.TryGetValue(relative, out var cached) && cached.Revision == revision) return cached.Summary;
            Rmf2ProjectCompilation compilation = TranslationCompiler.CompileRmf2ProjectV5(new TranslationSource(relative, bytes), sources, null, cancellationToken);
            int errors = compilation.Diagnostics.Count(item => item.Severity == TranslationDiagnosticSeverity.Error);
            var summary = new EditorCatalogSummary(compilation.Project?.Id ?? id ?? relative, [relative], sources.Count, compilation.Project?.Locales.Count ?? 0,
                compilation.Project?.CanonicalMessages.Count ?? 0, errors, compilation.Diagnostics.Count - errors, compilation.Success);
            lock (_summaries) _summaries[relative] = (revision, summary);
            return summary;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or DecoderFallbackException or EditorUserException or TranslationAuthoringException)
        {
            return new EditorCatalogSummary(relative, [relative], 0, 0, 0, 1, 0, false);
        }
    }

    private readonly Dictionary<string, (string Revision, EditorCatalogSummary Summary)> _summaries = new(StringComparer.Ordinal);

    private sealed class SummaryHash
    {
        private readonly System.Security.Cryptography.IncrementalHash _hash = System.Security.Cryptography.IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        internal void Append(byte[] bytes) => _hash.AppendData(bytes);
        internal string Finish() { using (_hash) return Convert.ToHexStringLower(_hash.GetHashAndReset()); }
    }

    // The base locale's messages by canonical key, from the compiled catalog, else from the review compilation, which
    // compiles while translations are incomplete; empty when neither compiles.
    internal static Dictionary<string, Rmf2TranslationV5> BaseResources(WorkspaceState state, CancellationToken cancellationToken = default)
    {
        Rmf2ProjectV5? project = state.Compilation.Project ?? ReviewCompilation(state, cancellationToken).Project;
        var resources = new Dictionary<string, Rmf2TranslationV5>(StringComparer.Ordinal);
        foreach (Rmf2TranslationV5 resource in project?.Locales.FirstOrDefault(locale => locale.Tag == project.DefaultLocale)?.DirectResources ?? [])
            resources.TryAdd(resource.Key, resource);
        return resources;
    }

    internal enum DocumentKind
    {
        Manifest,
        Resource,
    }

    internal sealed record WorkspaceFile(
        string Path,
        string Content,
        string Revision,
        DocumentKind Kind,
        string? CatalogId,
        string? Locale,
        string? Layer);

    internal sealed record WorkspaceState(
        List<WorkspaceFile> Files,
        Rmf2ProjectCompilation Compilation,
        IReadOnlyList<EditorCatalogSummary> Catalogs)
    {
        internal Rmf2ProjectCompilation? ReviewCompilation { get; set; }
        /// <summary>The revision of the compiled sources (runic.json and the message sources), which keys cached compilations.</summary>
        internal string? SourcesRevision { get; init; }
    }
}
