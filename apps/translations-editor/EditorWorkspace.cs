using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using RunicTextResources.Authoring;
using RunicTextResources.Compiler;

namespace RunicTextResources.Editor;

internal sealed class EditorWorkspace : IDisposable
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _root;
    private readonly FileSystemWatcher _watcher;
    private readonly ConcurrentDictionary<string, byte> _pendingChanges = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _knownRevisions = new(StringComparer.Ordinal);
    private string? _catalogId;
    private int _watcherOverflowed;
    private bool _disposed;

    public EditorWorkspace(string root, string? catalogId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        _root = Path.GetFullPath(root);
        _catalogId = catalogId;
        if (!Directory.Exists(_root))
            throw new DirectoryNotFoundException($"The text-resource workspace '{_root}' does not exist.");
        _watcher = new FileSystemWatcher(_root)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size,
        };
        _watcher.Changed += OnWatcherChanged;
        _watcher.Created += OnWatcherChanged;
        _watcher.Deleted += OnWatcherChanged;
        _watcher.Renamed += OnWatcherRenamed;
        _watcher.Error += OnWatcherError;
        _watcher.EnableRaisingEvents = true;
    }

    public string Root => _root;
    public string? CatalogId => _catalogId;

    public async Task<EditorExternalChanges> CheckExternalChangesAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            bool overflowed = Interlocked.Exchange(ref _watcherOverflowed, 0) != 0;
            if (!overflowed && _pendingChanges.IsEmpty)
                return new EditorExternalChanges(false, [], []);

            TextResourceWorkspaceDiscoveryResult discovery = TextResourceWorkspaceDiscovery.Discover(_root, cancellationToken: cancellationToken);
            Dictionary<string, string> current = Fingerprints(discovery);
            var candidates = new HashSet<string>(_pendingChanges.Keys, StringComparer.Ordinal);
            _pendingChanges.Clear();
            if (overflowed)
            {
                candidates.UnionWith(_knownRevisions.Keys);
                candidates.UnionWith(current.Keys);
            }

            string[] changed = candidates
                .Where(path => !_knownRevisions.TryGetValue(path, out string? known) ||
                    !current.TryGetValue(path, out string? revision) ||
                    !string.Equals(known, revision, StringComparison.Ordinal))
                .Order(StringComparer.Ordinal)
                .ToArray();
            var discoveredByPath = discovery.Files.ToDictionary(file => file.RelativePath, StringComparer.Ordinal);
            var changes = new EditorExternalFileChange[changed.Length];
            for (int index = 0; index < changed.Length; index++)
            {
                string path = changed[index];
                if (!discoveredByPath.TryGetValue(path, out TextResourceWorkspaceFile? file))
                {
                    changes[index] = new EditorExternalFileChange(path, false, null, null);
                    continue;
                }
                byte[] bytes = file.GetUtf8Bytes();
                changes[index] = new EditorExternalFileChange(path, true, StrictUtf8.GetString(bytes), Revision(bytes));
            }
            return new EditorExternalChanges(overflowed, changed, changes);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<WorkspaceSnapshot> LoadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            TextResourcePendingTransaction? pending = TextResourceWorkspaceTransaction.GetPending(_root);
            if (pending is not null)
            {
                return new WorkspaceSnapshot(
                    _root,
                    null,
                    [],
                    [],
                    [new EditorDiagnostic("RECOVERY", "error", "An interrupted workspace transaction requires recovery.", string.Empty, 1, 1, 1, 1)],
                    false,
                    new EditorPendingTransaction(pending.CatalogId, pending.Paths));
            }
            WorkspaceState state = await ReadStateAsync(null, null, cancellationToken).ConfigureAwait(false);
            return CreateSnapshot(state);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<ValidationResult> ValidateAsync(
        string relativePath,
        string content,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            string path = NormalizeKnownPath(relativePath);
            WorkspaceState state = await ReadStateAsync(path, content, cancellationToken).ConfigureAwait(false);
            if (state.Files.Exists(file => string.Equals(file.Path, path, StringComparison.Ordinal) && file.Kind == DocumentKind.Malformed))
                return MalformedValidation(path);
            return new ValidationResult(state.Compilation.Success, Diagnostics(state.Compilation));
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<EditorOperationResult> SaveAsync(
        string relativePath,
        string content,
        string expectedRevision,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            string path = NormalizeKnownPath(relativePath);
            string fullPath = ContainedPath(path);
            if (!File.Exists(fullPath))
                return Failure("not-found", $"'{path}' no longer exists.");

            byte[] currentBytes = await File.ReadAllBytesAsync(fullPath, cancellationToken).ConfigureAwait(false);
            string currentRevision = Revision(currentBytes);
            if (!string.Equals(currentRevision, expectedRevision, StringComparison.Ordinal))
                return Failure("conflict", $"'{path}' changed on disk. Reload before saving your draft.");

            WorkspaceState state = await ReadStateAsync(path, content, cancellationToken).ConfigureAwait(false);
            if (state.Files.Exists(file => string.Equals(file.Path, path, StringComparison.Ordinal) && file.Kind == DocumentKind.Malformed))
            {
                return new EditorOperationResult(
                    false,
                    "validation",
                    "The draft is not valid JSON.",
                    null,
                    MalformedValidation(path));
            }
            if (!state.Compilation.Success)
            {
                return new EditorOperationResult(
                    false,
                    "validation",
                    "The draft contains validation errors.",
                    null,
                    new ValidationResult(false, Diagnostics(state.Compilation)));
            }

            byte[] bytes;
            try
            {
                bytes = StrictUtf8.GetBytes(content);
            }
            catch (EncoderFallbackException)
            {
                return Failure("encoding", "The document contains text that cannot be encoded as UTF-8.");
            }

            string temporaryPath = Path.Combine(
                Path.GetDirectoryName(fullPath)!,
                $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
            try
            {
                await File.WriteAllBytesAsync(temporaryPath, bytes, cancellationToken).ConfigureAwait(false);
                File.Move(temporaryPath, fullPath, true);
            }
            finally
            {
                if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
            }

            WorkspaceState saved = await ReadStateAsync(null, null, cancellationToken).ConfigureAwait(false);
            return new EditorOperationResult(true, "saved", null, CreateSnapshot(saved), null);
        }
        catch (ArgumentException exception)
        {
            return Failure("invalid-request", exception.Message);
        }
        catch (IOException exception)
        {
            return Failure("io", exception.Message);
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _watcher.EnableRaisingEvents = false;
        _watcher.Dispose();
        _gate.Dispose();
    }

    private Task<WorkspaceState> ReadStateAsync(
        string? replacementPath,
        string? replacementContent,
        CancellationToken cancellationToken)
    {
        TextResourceWorkspaceDiscoveryResult discovery = TextResourceWorkspaceDiscovery.Discover(_root, cancellationToken: cancellationToken);
        if (replacementPath is null)
        {
            ReplaceKnownRevisions(Fingerprints(discovery));
            _pendingChanges.Clear();
            Interlocked.Exchange(ref _watcherOverflowed, 0);
        }
        if (_catalogId is null && discovery.Catalogs.Count == 1)
            _catalogId = discovery.Catalogs[0].Id;
        if (_catalogId is not null && !discovery.Catalogs.Any(catalog => string.Equals(catalog.Id, _catalogId, StringComparison.Ordinal)))
            throw new ArgumentException($"Catalog '{_catalogId}' was not found in this workspace.");

        var files = new List<WorkspaceFile>(discovery.Files.Count);
        foreach (TextResourceWorkspaceFile discoveredFile in discovery.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string relativePath = discoveredFile.RelativePath;
            byte[] bytes = discoveredFile.GetUtf8Bytes();
            string content = StrictUtf8.GetString(bytes);
            if (string.Equals(relativePath, replacementPath, StringComparison.Ordinal))
                content = replacementContent ?? string.Empty;
            DocumentKind kind = string.Equals(relativePath, replacementPath, StringComparison.Ordinal)
                ? DetectKind(content)
                : ToDocumentKind(discoveredFile.Kind);
            if (kind == DocumentKind.Unrelated) continue;
            string? catalogId = discoveredFile.CatalogId;
            string? locale = discoveredFile.Locale;
            string? layer = discoveredFile.Layer;
            if (string.Equals(relativePath, replacementPath, StringComparison.Ordinal))
                ReadFileIdentity(content, out catalogId, out locale, out layer);
            if (kind != DocumentKind.Malformed && _catalogId is not null && !string.Equals(catalogId, _catalogId, StringComparison.Ordinal))
                continue;
            files.Add(new WorkspaceFile(relativePath, content, Revision(bytes), kind, catalogId, locale, layer));
        }

        files.Sort(static (left, right) => StringComparer.Ordinal.Compare(left.Path, right.Path));
        if (replacementPath is not null && !files.Exists(file => string.Equals(file.Path, replacementPath, StringComparison.Ordinal)))
            throw new ArgumentException($"'{replacementPath}' is not a text-resource file in this workspace.", nameof(replacementPath));

        TextResourceSource[] manifests = (_catalogId is null ? Enumerable.Empty<WorkspaceFile>() : files)
            .Where(static file => file.Kind == DocumentKind.Manifest)
            .Select(static file => Source(file.Path, file.Content))
            .ToArray();
        TextResourceSource[] documents = (_catalogId is null ? Enumerable.Empty<WorkspaceFile>() : files)
            .Where(static file => file.Kind == DocumentKind.Resource)
            .Select(static file => Source(file.Path, file.Content))
            .ToArray();
        TextResourceCompilation compilation = TextResourceCompiler.Compile(manifests, documents, cancellationToken);
        return Task.FromResult(new WorkspaceState(files, compilation, CatalogSummaries(discovery)));
    }

    private WorkspaceSnapshot CreateSnapshot(WorkspaceState state)
    {
        EditorCatalog? catalog = null;
        WorkspaceFile? manifest = _catalogId is null
            ? null
            : state.Files.Find(file => file.Kind == DocumentKind.Manifest && string.Equals(file.CatalogId, _catalogId, StringComparison.Ordinal));
        if (manifest is not null)
            catalog = ReadCatalog(manifest.Content);

        var documents = new List<EditorDocument>(state.Files.Count);
        foreach (WorkspaceFile file in state.Files)
        {
            documents.Add(new EditorDocument(
                file.Path,
                file.Content,
                file.Revision,
                file.Kind == DocumentKind.Manifest,
                file.Kind == DocumentKind.Malformed,
                file.Locale,
                file.Layer));
        }
        return new WorkspaceSnapshot(_root, catalog, state.Catalogs, documents, Diagnostics(state.Compilation), state.Compilation.Success, null);
    }

    private static EditorDiagnostic[] Diagnostics(TextResourceCompilation compilation)
    {
        var result = new EditorDiagnostic[compilation.Diagnostics.Count];
        for (int index = 0; index < result.Length; index++)
        {
            TextResourceDiagnostic diagnostic = compilation.Diagnostics[index];
            TextSourceLocation location = diagnostic.Location;
            result[index] = new EditorDiagnostic(
                diagnostic.Id,
                diagnostic.Severity == TextResourceDiagnosticSeverity.Error ? "error" : "warning",
                diagnostic.Message,
                location.Path,
                location.Line,
                location.Column,
                location.EndLine,
                location.EndColumn);
        }
        return result;
    }

    private static ValidationResult MalformedValidation(string path) => new(
        false,
        [new EditorDiagnostic("JSON", "error", "The document is not valid JSON.", path, 1, 1, 1, 1)]);

    private static EditorCatalog? ReadCatalog(string content)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(content);
            JsonElement root = document.RootElement;
            string id = root.GetProperty("catalog").GetString() ?? string.Empty;
            int schemaVersion = root.GetProperty("schemaVersion").GetInt32();
            string defaultLocale = root.GetProperty("defaultLocale").GetString() ?? string.Empty;
            var locales = new List<EditorLocale>();
            foreach (JsonElement locale in root.GetProperty("locales").EnumerateArray())
            {
                locales.Add(new EditorLocale(
                    locale.GetProperty("tag").GetString() ?? string.Empty,
                    locale.TryGetProperty("fallback", out JsonElement fallback) ? fallback.GetString() : null));
            }
            var layers = new List<EditorLayer>();
            foreach (JsonElement layer in root.GetProperty("layers").EnumerateArray())
                layers.Add(new EditorLayer(layer.GetProperty("name").GetString() ?? string.Empty, layer.GetProperty("priority").GetInt32()));
            layers.Sort(static (left, right) => right.Priority.CompareTo(left.Priority));
            return new EditorCatalog(id, schemaVersion, defaultLocale, locales, layers);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static void ReadFileIdentity(string content, out string? catalog, out string? locale, out string? layer)
    {
        catalog = null;
        locale = null;
        layer = null;
        try
        {
            using JsonDocument document = JsonDocument.Parse(content);
            JsonElement root = document.RootElement;
            catalog = StringProperty(root, "catalog");
            locale = StringProperty(root, "locale");
            layer = StringProperty(root, "layer");
        }
        catch (JsonException)
        {
        }
    }

    private static string? StringProperty(JsonElement root, string name) =>
        root.ValueKind == JsonValueKind.Object &&
        root.TryGetProperty(name, out JsonElement property) &&
        property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static DocumentKind DetectKind(string content)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(content);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return DocumentKind.Unrelated;
            if (root.TryGetProperty("defaultLocale", out _) && root.TryGetProperty("locales", out _) && root.TryGetProperty("layers", out _))
                return DocumentKind.Manifest;
            if (root.TryGetProperty("locale", out _) && root.TryGetProperty("layer", out _) && root.TryGetProperty("resources", out _))
                return DocumentKind.Resource;
            return DocumentKind.Unrelated;
        }
        catch (JsonException)
        {
            return DocumentKind.Malformed;
        }
    }

    private static DocumentKind ToDocumentKind(TextResourceWorkspaceFileKind kind) => kind switch
    {
        TextResourceWorkspaceFileKind.CatalogManifest => DocumentKind.Manifest,
        TextResourceWorkspaceFileKind.ResourceDocument => DocumentKind.Resource,
        TextResourceWorkspaceFileKind.MalformedJson => DocumentKind.Malformed,
        _ => DocumentKind.Unrelated,
    };

    private static EditorCatalogSummary[] CatalogSummaries(TextResourceWorkspaceDiscoveryResult discovery)
    {
        var summaries = new EditorCatalogSummary[discovery.Catalogs.Count];
        for (int index = 0; index < summaries.Length; index++)
        {
            TextResourceDiscoveredCatalog catalog = discovery.Catalogs[index];
            int errors = catalog.Compilation.Diagnostics.Count(diagnostic => diagnostic.Severity == TextResourceDiagnosticSeverity.Error);
            int warnings = catalog.Compilation.Diagnostics.Count - errors;
            CompiledTextCatalog? compiled = catalog.Compilation.Catalogs.Count == 0 ? null : catalog.Compilation.Catalogs[0];
            summaries[index] = new EditorCatalogSummary(
                catalog.Id,
                catalog.ManifestPaths,
                catalog.DocumentPaths.Count,
                compiled?.Locales.Count ?? 0,
                compiled?.CanonicalResources.Count ?? 0,
                errors,
                warnings,
                catalog.Compilation.Success);
        }
        return summaries;
    }

    private string NormalizeKnownPath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string normalized = NormalizeRelativePath(path);
        _ = ContainedPath(normalized);
        return normalized;
    }

    private string ContainedPath(string relativePath)
    {
        if (Path.IsPathRooted(relativePath)) throw new ArgumentException("Workspace paths must be relative.", nameof(relativePath));
        string fullPath = Path.GetFullPath(relativePath.Replace('/', Path.DirectorySeparatorChar), _root);
        string boundary = _root.EndsWith(Path.DirectorySeparatorChar) ? _root : _root + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(boundary, StringComparison.Ordinal))
            throw new ArgumentException("The requested path escapes the workspace.", nameof(relativePath));
        return fullPath;
    }

    private static string NormalizeRelativePath(string path) => path.Replace('\\', '/').TrimStart('/');

    private static TextResourceSource Source(string path, string content) => new(path, StrictUtf8.GetBytes(content));

    private static string Revision(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    private static Dictionary<string, string> Fingerprints(TextResourceWorkspaceDiscoveryResult discovery)
    {
        var result = new Dictionary<string, string>(discovery.Files.Count, StringComparer.Ordinal);
        foreach (TextResourceWorkspaceFile file in discovery.Files)
            result[file.RelativePath] = Revision(file.GetUtf8Bytes());
        return result;
    }

    private void ReplaceKnownRevisions(Dictionary<string, string> revisions)
    {
        _knownRevisions.Clear();
        foreach (KeyValuePair<string, string> revision in revisions)
            _knownRevisions.Add(revision.Key, revision.Value);
    }

    private void OnWatcherChanged(object sender, FileSystemEventArgs eventArgs) => QueueChange(eventArgs.FullPath);

    private void OnWatcherRenamed(object sender, RenamedEventArgs eventArgs)
    {
        QueueChange(eventArgs.OldFullPath);
        QueueChange(eventArgs.FullPath);
    }

    private void OnWatcherError(object sender, ErrorEventArgs eventArgs) =>
        Interlocked.Exchange(ref _watcherOverflowed, 1);

    private void QueueChange(string fullPath)
    {
        if (_disposed) return;
        if (!fullPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) return;
        string relativePath = NormalizeRelativePath(Path.GetRelativePath(_root, fullPath));
        if (relativePath == ".." || relativePath.StartsWith("../", StringComparison.Ordinal)) return;
        _pendingChanges.TryAdd(relativePath, 0);
    }

    private static EditorOperationResult Failure(string kind, string message) => new(false, kind, message, null, null);

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    private enum DocumentKind
    {
        Unrelated,
        Manifest,
        Resource,
        Malformed,
    }

    private sealed record WorkspaceFile(
        string Path,
        string Content,
        string Revision,
        DocumentKind Kind,
        string? CatalogId,
        string? Locale,
        string? Layer);

    private sealed record WorkspaceState(
        List<WorkspaceFile> Files,
        TextResourceCompilation Compilation,
        IReadOnlyList<EditorCatalogSummary> Catalogs);
}
