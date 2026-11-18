using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Runic.Translations.Tooling;
using Runic.Translations.Authoring;
using Runic.Translations.Compiler;

namespace Runic.Translations.Editor;

/// <summary>
/// The workspace's files and paths: the root and its containment and link checks, the selected project's runic.json and
/// the projects below the root, source enumeration, and the byte, revision and path helpers every service shares.
/// </summary>
internal sealed class WorkspaceFiles
{
    internal WorkspaceFiles(string root, string? requestedCatalog)
    {
        _root = root;
        _requestedCatalog = requestedCatalog;
    }

    private readonly string _root;
    // The catalog the workspace was opened for (L5-4): a catalog ID or the runic.json path relative to the root, or null for
    // the conventional project (root/runic.json, root/translations/runic.json) or the workspace's only project.
    private readonly string? _requestedCatalog;
    private string? _selectedConfig;

    internal string Root => _root;

    internal void InvalidateDiscoveredProjects() => Volatile.Write(ref _discoveredProjects, null);

    internal static readonly UTF8Encoding StrictUtf8 = new(false, true);

    // Exported files inside the workspace are reported relative to it, as
    // before; a rooted command-line output outside it is reported as given.
    internal string ReportedOutputPath(string fullPath) =>
        IsWithinSourceRoot(_root, fullPath) ? Path.GetRelativePath(_root, fullPath).Replace('\\', '/') : fullPath;

    internal static async Task WriteAtomicallyAsync(string fullPath, byte[] bytes, CancellationToken cancellationToken)
    {
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
            try
            {
                if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // Best-effort cleanup must not mask the committed write.
            }
        }
    }

    private static string? StringProperty(JsonElement root, string name) =>
        root.ValueKind == JsonValueKind.Object &&
        root.TryGetProperty(name, out JsonElement property) &&
        property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    internal static string? ReadCatalogId(string content)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(WithoutBom(content));
            return StringProperty(document.RootElement, "catalog");
        }
        catch (JsonException)
        {
            return null;
        }
    }

    internal string NormalizeKnownPath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string normalized = NormalizeRelativePath(path);
        _ = ContainedPath(normalized);
        return normalized;
    }

    // Session-level history must use exactly the same canonical path as the
    // workspace write boundary; otherwise an accepted alias can commit without
    // recording its inverse or invalidating redo.
    internal string NormalizeDocumentPath(string path) => NormalizeKnownPath(path);

    /// <summary>Checks that an absolute path is inside the workspace and crosses no link, as every editor write is (library overrides).</summary>
    internal void EnsureWritable(string fullPath) => ContainedPath(NormalizeRelativePath(Path.GetRelativePath(_root, Path.GetFullPath(fullPath))));

    /// <summary>A path relative to the workspace root, with forward slashes.</summary>
    internal string RelativePath(string fullPath) => NormalizeRelativePath(Path.GetRelativePath(_root, Path.GetFullPath(fullPath)));

    internal string ContainedPath(string relativePath)
    {
        if (Path.IsPathRooted(relativePath)) throw new EditorUserException(EditorNotice.Create("ui_backend_path_relative"));
        string fullPath = Path.GetFullPath(relativePath.Replace('/', Path.DirectorySeparatorChar), _root);
        string boundary = _root.EndsWith(Path.DirectorySeparatorChar) ? _root : _root + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(boundary, StringComparison.Ordinal))
            throw new EditorUserException(EditorNotice.Create("ui_backend_path_escape"));
        for (string? current = fullPath; current is not null && current.StartsWith(boundary, StringComparison.Ordinal);
            current = Path.GetDirectoryName(current))
        {
            if ((File.Exists(current) || Directory.Exists(current)) &&
                (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new EditorUserException(EditorNotice.Create("ui_backend_path_link"));
        }
        return fullPath;
    }

    internal static string NormalizeRelativePath(string path) => path.Replace('\\', '/').TrimStart('/');

    internal static TranslationSource Source(string path, string content) => new(path, StrictUtf8.GetBytes(content));

    // runic.json as the compiler reads it: a leading UTF-8 BOM is not part of the JSON.
    internal static string WithoutBom(string content) => content.StartsWith('\uFEFF') ? content[1..] : content;

    internal static string Revision(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    internal string? FindMf2ProjectConfig()
    {
        if (_selectedConfig is not null && File.Exists(_selectedConfig)) return _selectedConfig;
        if (_requestedCatalog is null)
        {
            string direct = Path.Combine(_root, "runic.json");
            if (File.Exists(direct)) return direct;
            string conventional = Path.Combine(_root, "translations", "runic.json");
            if (File.Exists(conventional)) return conventional;
            // A workspace with exactly one project elsewhere opens it; with several, the user chooses.
            IReadOnlyList<string> projects = DiscoverProjects();
            return projects.Count == 1 ? _selectedConfig = projects[0] : null;
        }
        // The requested catalog by its runic.json path, then by its catalog ID (the first in path order).
        foreach (string project in DiscoverProjects())
        {
            string relative = NormalizeRelativePath(Path.GetRelativePath(_root, project));
            if (relative == _requestedCatalog || NormalizeRelativePath(Path.GetDirectoryName(relative) ?? "") == _requestedCatalog) return _selectedConfig = project;
        }
        foreach (string project in DiscoverProjects())
        {
            try
            {
                if (ReadCatalogId(StrictUtf8.GetString(ReadSourceBytes(project))) == _requestedCatalog) return _selectedConfig = project;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or DecoderFallbackException or EditorUserException) { }
        }
        return null;
    }

    // Every runic.json below the root, by the language server's project-index rules (Tooling). The walk is reused until a
    // full load or a watcher event on a runic.json, so several projects with none chosen do not re-walk the tree per call.
    internal IReadOnlyList<string> DiscoverProjects()
    {
        if (Volatile.Read(ref _discoveredProjects) is { } cached) return cached;
        IReadOnlyList<string> projects;
        try { projects = TranslationProjectDiscovery.Discover(_root); }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or UnauthorizedAccessException) { projects = []; }
        Volatile.Write(ref _discoveredProjects, projects);
        return projects;
    }

    private IReadOnlyList<string>? _discoveredProjects;

    internal static bool IsSourcePath(string path) =>
        path.EndsWith(".mf2", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(".rmf2", StringComparison.OrdinalIgnoreCase);

    // Source roots the Editor can enumerate. A root outside the opened
    // workspace folder cannot be edited or watched here, and a missing root is
    // usually a path still being typed; both are skipped and, when errors is
    // supplied, reported as manifest diagnostics instead of failing the load.
    internal List<string> UsableSourceRoots(IReadOnlyList<string> sourceRoots, List<string>? errors)
    {
        var usable = new List<string>(sourceRoots.Count);
        foreach (string sourceRoot in sourceRoots)
        {
            string display = NormalizeRelativePath(Path.GetRelativePath(_root, sourceRoot));
            if (!IsWithinSourceRoot(_root, sourceRoot) &&
                !string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(sourceRoot)), Path.TrimEndingDirectorySeparator(_root), StringComparison.Ordinal))
                errors?.Add(SourceRootOutsideWorkspace(display));
            else if (!Directory.Exists(sourceRoot))
                errors?.Add(SourceRootMissing(display));
            else usable.Add(sourceRoot);
        }
        return usable;
    }

    internal static string SourceRootOutsideWorkspace(string root) => $"Source root '{root}' is outside the workspace folder opened in the Translations Editor.";
    internal static string SourceRootMissing(string root) => $"Source root '{root}' is not an existing directory.";

    internal static IEnumerable<string> EnumerateSourceFiles(string root, string extension)
    {
        ValidateNoReparseAncestors(root);
        foreach (string path in Directory.EnumerateFileSystemEntries(root).Order(StringComparer.Ordinal))
        {
            FileAttributes attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
                throw new EditorUserException(EditorNotice.Create("ui_backend_path_link"));
            if ((attributes & FileAttributes.Directory) != 0)
            {
                if (Path.GetFileName(path) == ".runic-translations") continue;
                foreach (string child in EnumerateSourceFiles(path, extension)) yield return child;
            }
            else if (string.Equals(Path.GetExtension(path), extension, StringComparison.OrdinalIgnoreCase)) yield return path;
        }
    }

    internal static void ValidateNoReparseAncestors(string path)
    {
        string? current = Path.GetFullPath(path);
        while (current is not null)
        {
            if ((File.Exists(current) || Directory.Exists(current)) &&
                (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new EditorUserException(EditorNotice.Create("ui_backend_path_link"));
            current = Path.GetDirectoryName(current);
        }
    }

    internal static byte[] ReadSourceBytes(string path)
    {
        if (new FileInfo(path).Length > new TranslationCompilerOptions().MaximumDocumentBytes)
            throw new EditorUserException(EditorNotice.Create("ui_backend_document_size"));
        return File.ReadAllBytes(path);
    }

    internal static string? SourceLocale(string path) => path.EndsWith(".rmf2", StringComparison.OrdinalIgnoreCase)
        ? Path.GetFileNameWithoutExtension(path)
        : path.Contains('/', StringComparison.Ordinal) ? path[..path.IndexOf('/')] : null;

    internal static string? SourceLocale(string fullPath, IReadOnlyList<string> sourceRoots)
    {
        string[] matches = sourceRoots.Where(root => IsWithinSourceRoot(root, fullPath)).ToArray();
        return matches.Length != 1
            ? null
            : SourceLocale(NormalizeRelativePath(Path.GetRelativePath(matches[0], fullPath)));
    }

    internal static bool IsWithinSourceRoot(string root, string path)
    {
        string boundary = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar;
        return Path.GetFullPath(path).StartsWith(boundary, StringComparison.Ordinal);
    }

    internal Rmf2Workspace Rmf2Catalog()
    {
        string config = FindMf2ProjectConfig() ?? throw new TranslationAuthoringException("No project configuration found.");
        return new Rmf2Workspace(_root, new TranslationSource(config, File.ReadAllBytes(config)), []);
    }

    internal static EditorOperationResult Failure(string kind, EditorNotice message) => new(false, kind, message, null, null);
}
