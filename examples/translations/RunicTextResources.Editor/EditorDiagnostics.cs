using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace RunicTextResources.Editor;

internal static class EditorDiagnostics
{
    private const string BundleSchema = "runic.textresources.editor-diagnostics/1";

    public static EditorAbout About()
    {
        Assembly assembly = typeof(EditorDiagnostics).Assembly;
        string version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString()
            ?? "unknown";
        Dictionary<string, string> metadata = assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .ToDictionary(static value => value.Key, static value => value.Value ?? string.Empty, StringComparer.Ordinal);
        metadata.TryGetValue("RunicUpdateChannel", out string? channel);
        metadata.TryGetValue("RepositoryCommit", out string? commit);
        if (string.IsNullOrWhiteSpace(commit)) commit = CommitFromVersion(version);
        return new EditorAbout(
            "Runic Translations Editor",
            version,
            string.IsNullOrWhiteSpace(channel) ? "preview" : channel,
            string.IsNullOrWhiteSpace(commit) ? null : commit,
            RuntimeInformation.FrameworkDescription,
            RuntimeInformation.RuntimeIdentifier,
            RuntimeInformation.OSDescription,
            RuntimeInformation.ProcessArchitecture.ToString());
    }

    public static EditorDiagnosticBundleResult CreateBundle(WorkspaceSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        try
        {
            string directory = Path.Combine(Path.GetTempPath(), "RunicTextResources", "Diagnostics");
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory,
                $"runic-text-resources-diagnostics-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.zip");
            EditorDiagnosticGroup[] diagnostics = snapshot.Diagnostics
                .GroupBy(static item => (item.Id, item.Severity))
                .OrderBy(static group => group.Key.Id, StringComparer.Ordinal)
                .ThenBy(static group => group.Key.Severity, StringComparer.Ordinal)
                .Select(static group => new EditorDiagnosticGroup(group.Key.Id, group.Key.Severity, group.Count()))
                .ToArray();
            EditorCatalogSummary? catalog = snapshot.Catalogs.FirstOrDefault(candidate =>
                string.Equals(candidate.Id, snapshot.Catalog?.Id, StringComparison.Ordinal));
            var workspace = new EditorDiagnosticWorkspace(
                snapshot.Catalog?.Id,
                snapshot.Catalog?.SchemaVersion,
                snapshot.Catalog?.Locales.Count ?? 0,
                snapshot.Documents.Count,
                catalog?.MessageCount ?? 0,
                snapshot.Success,
                snapshot.Review?.Error is null,
                snapshot.PendingTransaction is not null,
                snapshot.PendingTransaction?.Paths.Count ?? 0,
                diagnostics);
            var bundle = new EditorDiagnosticBundle(BundleSchema, DateTimeOffset.UtcNow, About(), workspace);

            using (ZipArchive archive = ZipFile.Open(path, ZipArchiveMode.Create))
            {
                ZipArchiveEntry diagnosticsEntry = archive.CreateEntry("diagnostics.json", CompressionLevel.SmallestSize);
                using (Stream stream = diagnosticsEntry.Open())
                    JsonSerializer.Serialize(stream, bundle, EditorJsonContext.Default.EditorDiagnosticBundle);
                AddFile(archive, "LICENSE.txt", Path.Combine(AppContext.BaseDirectory, "LICENSE.txt"));
                AddFile(archive, "THIRD-PARTY-NOTICES.md", Path.Combine(AppContext.BaseDirectory, "THIRD-PARTY-NOTICES.md"));
            }
            return new EditorDiagnosticBundleResult(true, path, null);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return new EditorDiagnosticBundleResult(false, null, exception.Message);
        }
    }

    private static void AddFile(ZipArchive archive, string name, string path)
    {
        if (File.Exists(path)) archive.CreateEntryFromFile(path, name, CompressionLevel.SmallestSize);
    }

    private static string? CommitFromVersion(string version)
    {
        int separator = version.LastIndexOf('+');
        return separator < 0 || separator == version.Length - 1 ? null : version[(separator + 1)..];
    }
}
