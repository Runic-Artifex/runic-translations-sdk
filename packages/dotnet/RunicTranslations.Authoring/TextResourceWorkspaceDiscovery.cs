using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using RunicTranslations.Compiler;

namespace RunicTranslations.Authoring;

public static class TextResourceWorkspaceDiscovery
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static TextResourceWorkspaceDiscoveryResult Discover(
        string root,
        TextResourceWorkspaceDiscoveryOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        options ??= new TextResourceWorkspaceDiscoveryOptions();
        string fullRoot = Path.GetFullPath(root);
        if (!Directory.Exists(fullRoot))
            throw new TextResourceAuthoringException($"Workspace '{fullRoot}' does not exist.");
        if (IsReparsePoint(fullRoot))
            throw new TextResourceAuthoringException($"Workspace root '{fullRoot}' is a symbolic link or reparse point.");

        var files = new List<TextResourceWorkspaceFile>();
        var diagnostics = new List<TextResourceAuthoringDiagnostic>();
        var pending = new Stack<(string Path, int Depth)>();
        pending.Push((fullRoot, 0));
        int entryCount = 0;
        long totalBytes = 0;

        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            (string directory, int depth) = pending.Pop();
            string[] entries;
            try
            {
                entries = Directory.GetFileSystemEntries(directory);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                diagnostics.Add(Diagnostic("RTRA0004", TextResourceAuthoringDiagnosticSeverity.Error,
                    $"Directory could not be read: {exception.Message}", Relative(fullRoot, directory)));
                continue;
            }

            Array.Sort(entries, StringComparer.Ordinal);
            for (int index = entries.Length - 1; index >= 0; index--)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string entry = entries[index];
                entryCount++;
                if (entryCount > options.MaximumEntries)
                    throw new TextResourceAuthoringException($"Workspace exceeds the {options.MaximumEntries}-entry discovery limit.");

                FileAttributes attributes;
                try
                {
                    attributes = File.GetAttributes(entry);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    diagnostics.Add(Diagnostic("RTRA0004", TextResourceAuthoringDiagnosticSeverity.Error,
                        $"Entry could not be inspected: {exception.Message}", Relative(fullRoot, entry)));
                    continue;
                }

                string relativePath = Relative(fullRoot, entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    diagnostics.Add(Diagnostic("RTRA0003", TextResourceAuthoringDiagnosticSeverity.Error,
                        "Symbolic links and reparse points are not traversed.", relativePath));
                    continue;
                }

                if ((attributes & FileAttributes.Directory) != 0)
                {
                    if (IsIgnoredDirectory(Path.GetFileName(entry))) continue;
                    if (depth == options.MaximumDepth)
                    {
                        diagnostics.Add(Diagnostic("RTRA0002", TextResourceAuthoringDiagnosticSeverity.Warning,
                            $"Directory depth exceeds the configured limit of {options.MaximumDepth}.", relativePath));
                        continue;
                    }

                    pending.Push((entry, depth + 1));
                    continue;
                }

                if (!entry.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) continue;
                if (files.Count == options.MaximumJsonFiles)
                    throw new TextResourceAuthoringException($"Workspace exceeds the {options.MaximumJsonFiles}-JSON-file discovery limit.");

                long length = new FileInfo(entry).Length;
                if (length > options.MaximumFileBytes)
                {
                    diagnostics.Add(Diagnostic("RTRA0001", TextResourceAuthoringDiagnosticSeverity.Error,
                        $"JSON file exceeds the {options.MaximumFileBytes}-byte limit.", relativePath));
                    continue;
                }
                if (totalBytes + length > options.MaximumTotalBytes)
                    throw new TextResourceAuthoringException($"Workspace JSON exceeds the {options.MaximumTotalBytes}-byte total limit.");

                byte[] bytes;
                try
                {
                    bytes = File.ReadAllBytes(entry);
                    _ = StrictUtf8.GetString(bytes);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or DecoderFallbackException)
                {
                    diagnostics.Add(Diagnostic("RTRA0005", TextResourceAuthoringDiagnosticSeverity.Error,
                        $"JSON file is not readable strict UTF-8: {exception.Message}", relativePath));
                    continue;
                }

                totalBytes += bytes.Length;
                files.Add(Classify(relativePath, bytes, diagnostics));
            }
        }

        files.Sort(static (left, right) => StringComparer.Ordinal.Compare(left.RelativePath, right.RelativePath));
        diagnostics.Sort(CompareDiagnostics);
        TextResourceDiscoveredCatalog[] catalogs = CompileCatalogs(files);
        return new TextResourceWorkspaceDiscoveryResult(fullRoot, files.ToArray(), catalogs, diagnostics.ToArray());
    }

    private static TextResourceWorkspaceFile Classify(
        string path,
        byte[] bytes,
        List<TextResourceAuthoringDiagnostic> diagnostics)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 64 });
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return new TextResourceWorkspaceFile(path, TextResourceWorkspaceFileKind.OtherJson, null, null, null, bytes);

            string? catalog = StringProperty(root, "catalog");
            if (root.TryGetProperty("defaultLocale", out _) && root.TryGetProperty("locales", out _) && root.TryGetProperty("layers", out _))
                return new TextResourceWorkspaceFile(path, TextResourceWorkspaceFileKind.CatalogManifest, catalog, null, null, bytes);
            if (root.TryGetProperty("locale", out _) && root.TryGetProperty("layer", out _) && root.TryGetProperty("resources", out _))
                return new TextResourceWorkspaceFile(
                    path,
                    TextResourceWorkspaceFileKind.ResourceDocument,
                    catalog,
                    StringProperty(root, "locale"),
                    StringProperty(root, "layer"),
                    bytes);
            return new TextResourceWorkspaceFile(path, TextResourceWorkspaceFileKind.OtherJson, catalog, null, null, bytes);
        }
        catch (JsonException exception)
        {
            diagnostics.Add(Diagnostic("RTRA0006", TextResourceAuthoringDiagnosticSeverity.Error,
                $"Malformed JSON: {exception.Message}", path));
            return new TextResourceWorkspaceFile(path, TextResourceWorkspaceFileKind.MalformedJson, null, null, null, bytes);
        }
    }

    private static TextResourceDiscoveredCatalog[] CompileCatalogs(List<TextResourceWorkspaceFile> files)
    {
        var manifests = new Dictionary<string, List<TextResourceWorkspaceFile>>(StringComparer.Ordinal);
        var documents = new Dictionary<string, List<TextResourceWorkspaceFile>>(StringComparer.Ordinal);
        for (int index = 0; index < files.Count; index++)
        {
            TextResourceWorkspaceFile file = files[index];
            if (file.CatalogId is null) continue;
            Dictionary<string, List<TextResourceWorkspaceFile>> target = file.Kind switch
            {
                TextResourceWorkspaceFileKind.CatalogManifest => manifests,
                TextResourceWorkspaceFileKind.ResourceDocument => documents,
                _ => null!,
            };
            if (target is null) continue;
            if (!target.TryGetValue(file.CatalogId, out List<TextResourceWorkspaceFile>? group))
            {
                group = [];
                target.Add(file.CatalogId, group);
            }
            group.Add(file);
        }

        var result = new List<TextResourceDiscoveredCatalog>(manifests.Count);
        foreach (KeyValuePair<string, List<TextResourceWorkspaceFile>> pair in manifests)
        {
            documents.TryGetValue(pair.Key, out List<TextResourceWorkspaceFile>? catalogDocuments);
            catalogDocuments ??= [];
            TextResourceCompilation compilation = TextResourceCompiler.Compile(
                Sources(pair.Value),
                Sources(catalogDocuments));
            result.Add(new TextResourceDiscoveredCatalog(
                pair.Key,
                Paths(pair.Value),
                Paths(catalogDocuments),
                compilation));
        }
        result.Sort(static (left, right) => StringComparer.Ordinal.Compare(left.Id, right.Id));
        return result.ToArray();
    }

    private static TextResourceSource[] Sources(List<TextResourceWorkspaceFile> files)
    {
        var result = new TextResourceSource[files.Count];
        for (int index = 0; index < files.Count; index++)
            result[index] = new TextResourceSource(files[index].RelativePath, files[index].Bytes);
        return result;
    }

    private static string[] Paths(List<TextResourceWorkspaceFile> files)
    {
        var result = new string[files.Count];
        for (int index = 0; index < files.Count; index++) result[index] = files[index].RelativePath;
        Array.Sort(result, StringComparer.Ordinal);
        return result;
    }

    private static string? StringProperty(JsonElement root, string name) =>
        root.TryGetProperty(name, out JsonElement property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static bool IsIgnoredDirectory(string name) => name is ".git" or ".hg" or ".svn" or "bin" or "obj" or "node_modules" or "artifacts";
    private static bool IsReparsePoint(string path) => (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
    private static string Relative(string root, string path) => Path.GetRelativePath(root, path).Replace('\\', '/');
    private static TextResourceAuthoringDiagnostic Diagnostic(
        string id,
        TextResourceAuthoringDiagnosticSeverity severity,
        string message,
        string path) => new(id, severity, message, path);
    private static int CompareDiagnostics(TextResourceAuthoringDiagnostic left, TextResourceAuthoringDiagnostic right)
    {
        int path = StringComparer.Ordinal.Compare(left.RelativePath, right.RelativePath);
        return path != 0 ? path : StringComparer.Ordinal.Compare(left.Id, right.Id);
    }
}
