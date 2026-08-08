using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using RunicTextResources.Compiler;

namespace RunicTextResources.Editor;

internal sealed class EditorWorkspace : IDisposable
{
    private const int MaximumFiles = 512;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _root;
    private bool _disposed;

    public EditorWorkspace(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        _root = Path.GetFullPath(root);
        if (!Directory.Exists(_root))
            throw new DirectoryNotFoundException($"The text-resource workspace '{_root}' does not exist.");
    }

    public async Task<WorkspaceSnapshot> LoadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
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
        _gate.Dispose();
    }

    private async Task<WorkspaceState> ReadStateAsync(
        string? replacementPath,
        string? replacementContent,
        CancellationToken cancellationToken)
    {
        var files = new List<WorkspaceFile>();
        foreach (string fullPath in Directory.EnumerateFiles(_root, "*.json", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string relativePath = NormalizeRelativePath(Path.GetRelativePath(_root, fullPath));
            if (IsIgnored(relativePath)) continue;
            if (files.Count == MaximumFiles)
                throw new InvalidOperationException($"The workspace exceeds the {MaximumFiles}-file editor limit.");

            byte[] bytes = await File.ReadAllBytesAsync(fullPath, cancellationToken).ConfigureAwait(false);
            string content = StrictUtf8.GetString(bytes);
            DocumentKind kind = DetectKind(content);
            if (kind == DocumentKind.Unrelated) continue;
            if (string.Equals(relativePath, replacementPath, StringComparison.Ordinal))
            {
                content = replacementContent ?? string.Empty;
                bytes = StrictUtf8.GetBytes(content);
            }
            files.Add(new WorkspaceFile(relativePath, content, Revision(bytes), kind));
        }

        files.Sort(static (left, right) => StringComparer.Ordinal.Compare(left.Path, right.Path));
        if (replacementPath is not null && !files.Exists(file => string.Equals(file.Path, replacementPath, StringComparison.Ordinal)))
            throw new ArgumentException($"'{replacementPath}' is not a text-resource file in this workspace.", nameof(replacementPath));

        TextResourceSource[] manifests = files
            .Where(static file => file.Kind == DocumentKind.Manifest)
            .Select(static file => Source(file.Path, file.Content))
            .ToArray();
        TextResourceSource[] documents = files
            .Where(static file => file.Kind == DocumentKind.Resource)
            .Select(static file => Source(file.Path, file.Content))
            .ToArray();
        TextResourceCompilation compilation = TextResourceCompiler.Compile(manifests, documents, cancellationToken);
        return new WorkspaceState(files, compilation);
    }

    private WorkspaceSnapshot CreateSnapshot(WorkspaceState state)
    {
        EditorCatalog? catalog = null;
        WorkspaceFile? manifest = state.Files.Find(static file => file.Kind == DocumentKind.Manifest);
        if (manifest is not null)
            catalog = ReadCatalog(manifest.Content);

        var documents = new List<EditorDocument>(state.Files.Count);
        foreach (WorkspaceFile file in state.Files)
        {
            string? locale = null;
            string? layer = null;
            if (file.Kind == DocumentKind.Resource)
                ReadDocumentIdentity(file.Content, out locale, out layer);
            documents.Add(new EditorDocument(
                file.Path,
                file.Content,
                file.Revision,
                file.Kind == DocumentKind.Manifest,
                locale,
                layer));
        }
        return new WorkspaceSnapshot(_root, catalog, documents, Diagnostics(state.Compilation), state.Compilation.Success);
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

    private static void ReadDocumentIdentity(string content, out string? locale, out string? layer)
    {
        locale = null;
        layer = null;
        try
        {
            using JsonDocument document = JsonDocument.Parse(content);
            locale = document.RootElement.GetProperty("locale").GetString();
            layer = document.RootElement.GetProperty("layer").GetString();
        }
        catch (JsonException)
        {
        }
    }

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
            return DocumentKind.Unrelated;
        }
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

    private static bool IsIgnored(string path) =>
        path.StartsWith(".git/", StringComparison.Ordinal) ||
        path.Contains("/node_modules/", StringComparison.Ordinal) ||
        path.Contains("/bin/", StringComparison.Ordinal) ||
        path.Contains("/obj/", StringComparison.Ordinal);

    private static TextResourceSource Source(string path, string content) => new(path, StrictUtf8.GetBytes(content));

    private static string Revision(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    private static EditorOperationResult Failure(string kind, string message) => new(false, kind, message, null, null);

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    private enum DocumentKind
    {
        Unrelated,
        Manifest,
        Resource,
    }

    private sealed record WorkspaceFile(string Path, string Content, string Revision, DocumentKind Kind);

    private sealed record WorkspaceState(List<WorkspaceFile> Files, TextResourceCompilation Compilation);
}
