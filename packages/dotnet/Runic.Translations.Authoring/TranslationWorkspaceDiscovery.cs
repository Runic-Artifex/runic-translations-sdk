using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using Runic.Translations.Compiler;

namespace Runic.Translations.Authoring;

public static class TranslationWorkspaceDiscovery
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static TranslationWorkspaceDiscoveryResult Discover(
        string root,
        TranslationWorkspaceDiscoveryOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        options ??= new TranslationWorkspaceDiscoveryOptions();
        string fullRoot = Path.GetFullPath(root);
        if (!Directory.Exists(fullRoot))
            throw new TranslationAuthoringException($"Workspace '{fullRoot}' does not exist.");
        if (IsReparsePoint(fullRoot))
            throw new TranslationAuthoringException($"Workspace root '{fullRoot}' is a symbolic link or reparse point.");

        var files = new List<TranslationWorkspaceFile>();
        var diagnostics = new List<TranslationAuthoringDiagnostic>();
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
                diagnostics.Add(Diagnostic("RTRA0004", TranslationAuthoringDiagnosticSeverity.Error,
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
                    throw new TranslationAuthoringException($"Workspace exceeds the {options.MaximumEntries}-entry discovery limit.");

                FileAttributes attributes;
                try
                {
                    attributes = File.GetAttributes(entry);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    diagnostics.Add(Diagnostic("RTRA0004", TranslationAuthoringDiagnosticSeverity.Error,
                        $"Entry could not be inspected: {exception.Message}", Relative(fullRoot, entry)));
                    continue;
                }

                string relativePath = Relative(fullRoot, entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    diagnostics.Add(Diagnostic("RTRA0003", TranslationAuthoringDiagnosticSeverity.Error,
                        "Symbolic links and reparse points are not traversed.", relativePath));
                    continue;
                }

                if ((attributes & FileAttributes.Directory) != 0)
                {
                    if (IsIgnoredDirectory(Path.GetFileName(entry))) continue;
                    if (depth == options.MaximumDepth)
                    {
                        diagnostics.Add(Diagnostic("RTRA0002", TranslationAuthoringDiagnosticSeverity.Warning,
                            $"Directory depth exceeds the configured limit of {options.MaximumDepth}.", relativePath));
                        continue;
                    }

                    pending.Push((entry, depth + 1));
                    continue;
                }

                if (!entry.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) continue;
                if (files.Count == options.MaximumJsonFiles)
                    throw new TranslationAuthoringException($"Workspace exceeds the {options.MaximumJsonFiles}-JSON-file discovery limit.");

                long length = new FileInfo(entry).Length;
                if (length > options.MaximumFileBytes)
                {
                    diagnostics.Add(Diagnostic("RTRA0001", TranslationAuthoringDiagnosticSeverity.Error,
                        $"JSON file exceeds the {options.MaximumFileBytes}-byte limit.", relativePath));
                    continue;
                }
                if (totalBytes + length > options.MaximumTotalBytes)
                    throw new TranslationAuthoringException($"Workspace JSON exceeds the {options.MaximumTotalBytes}-byte total limit.");

                byte[] bytes;
                try
                {
                    bytes = File.ReadAllBytes(entry);
                    _ = StrictUtf8.GetString(bytes);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or DecoderFallbackException)
                {
                    diagnostics.Add(Diagnostic("RTRA0005", TranslationAuthoringDiagnosticSeverity.Error,
                        $"JSON file is not readable strict UTF-8: {exception.Message}", relativePath));
                    continue;
                }

                totalBytes += bytes.Length;
                files.Add(Classify(relativePath, bytes, diagnostics));
            }
        }

        files.Sort(static (left, right) => StringComparer.Ordinal.Compare(left.RelativePath, right.RelativePath));
        diagnostics.Sort(CompareDiagnostics);
        TranslationDiscoveredCatalog[] catalogs = CompileCatalogs(files);
        return new TranslationWorkspaceDiscoveryResult(fullRoot, files.ToArray(), catalogs, diagnostics.ToArray());
    }

    private static TranslationWorkspaceFile Classify(
        string path,
        byte[] bytes,
        List<TranslationAuthoringDiagnostic> diagnostics)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 64 });
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return new TranslationWorkspaceFile(path, TranslationWorkspaceFileKind.OtherJson, null, null, null, bytes);

            string? catalog = StringProperty(root, "catalog");
            if (root.TryGetProperty("defaultLocale", out _) && root.TryGetProperty("locales", out _) && root.TryGetProperty("layers", out _))
                return new TranslationWorkspaceFile(path, TranslationWorkspaceFileKind.CatalogManifest, catalog, null, null, bytes);
            if (root.TryGetProperty("locale", out _) && root.TryGetProperty("layer", out _) && root.TryGetProperty("resources", out _))
                return new TranslationWorkspaceFile(
                    path,
                    TranslationWorkspaceFileKind.ResourceDocument,
                    catalog,
                    StringProperty(root, "locale"),
                    StringProperty(root, "layer"),
                    bytes);
            return new TranslationWorkspaceFile(path, TranslationWorkspaceFileKind.OtherJson, catalog, null, null, bytes);
        }
        catch (JsonException exception)
        {
            diagnostics.Add(Diagnostic("RTRA0006", TranslationAuthoringDiagnosticSeverity.Error,
                $"Malformed JSON: {exception.Message}", path));
            return new TranslationWorkspaceFile(path, TranslationWorkspaceFileKind.MalformedJson, null, null, null, bytes);
        }
    }

    private static TranslationDiscoveredCatalog[] CompileCatalogs(List<TranslationWorkspaceFile> files)
    {
        var manifests = new Dictionary<string, List<TranslationWorkspaceFile>>(StringComparer.Ordinal);
        var documents = new Dictionary<string, List<TranslationWorkspaceFile>>(StringComparer.Ordinal);
        for (int index = 0; index < files.Count; index++)
        {
            TranslationWorkspaceFile file = files[index];
            if (file.CatalogId is null) continue;
            Dictionary<string, List<TranslationWorkspaceFile>> target = file.Kind switch
            {
                TranslationWorkspaceFileKind.CatalogManifest => manifests,
                TranslationWorkspaceFileKind.ResourceDocument => documents,
                _ => null!,
            };
            if (target is null) continue;
            if (!target.TryGetValue(file.CatalogId, out List<TranslationWorkspaceFile>? group))
            {
                group = [];
                target.Add(file.CatalogId, group);
            }
            group.Add(file);
        }

        var result = new List<TranslationDiscoveredCatalog>(manifests.Count);
        foreach (KeyValuePair<string, List<TranslationWorkspaceFile>> pair in manifests)
        {
            documents.TryGetValue(pair.Key, out List<TranslationWorkspaceFile>? catalogDocuments);
            catalogDocuments ??= [];
            TranslationCompilation compilation = TranslationCompiler.Compile(
                Sources(pair.Value),
                Sources(catalogDocuments));
            result.Add(new TranslationDiscoveredCatalog(
                pair.Key,
                Paths(pair.Value),
                Paths(catalogDocuments),
                compilation));
        }
        result.Sort(static (left, right) => StringComparer.Ordinal.Compare(left.Id, right.Id));
        return result.ToArray();
    }

    private static TranslationSource[] Sources(List<TranslationWorkspaceFile> files)
    {
        var result = new TranslationSource[files.Count];
        for (int index = 0; index < files.Count; index++)
            result[index] = new TranslationSource(files[index].RelativePath, files[index].Bytes);
        return result;
    }

    private static string[] Paths(List<TranslationWorkspaceFile> files)
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
    private static TranslationAuthoringDiagnostic Diagnostic(
        string id,
        TranslationAuthoringDiagnosticSeverity severity,
        string message,
        string path) => new(id, severity, message, path);
    private static int CompareDiagnostics(TranslationAuthoringDiagnostic left, TranslationAuthoringDiagnostic right)
    {
        int path = StringComparer.Ordinal.Compare(left.RelativePath, right.RelativePath);
        return path != 0 ? path : StringComparer.Ordinal.Compare(left.Id, right.Id);
    }
}
