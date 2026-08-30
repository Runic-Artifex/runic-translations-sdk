using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Diagnostics;
using System.Text.Json;

namespace Runic.Translations.Editor;

internal static class EditorDiagnostics
{
    private const string BundleSchema = "runic.translations.editor-diagnostics/1";
    private const int MaximumDiagnosticGroups = 256;
    private const long MaximumLegalNoticeBytes = 1_048_576;
    private const long MaximumBundleBytes = 2_097_152;

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
            string directory = BundleDirectory();
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory,
                $"runic-translations-editor-diagnostics-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.zip");
            EditorDiagnosticGroup[] diagnostics = snapshot.Diagnostics
                .GroupBy(static item => (item.Id, item.Severity))
                .OrderBy(static group => group.Key.Id, StringComparer.Ordinal)
                .ThenBy(static group => group.Key.Severity, StringComparer.Ordinal)
                .Take(MaximumDiagnosticGroups)
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
            if (new FileInfo(path).Length > MaximumBundleBytes)
            {
                File.Delete(path);
                return new EditorDiagnosticBundleResult(false, null, "The diagnostic bundle exceeded its size limit.");
            }
            return new EditorDiagnosticBundleResult(true, path, null);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return new EditorDiagnosticBundleResult(false, null, "The diagnostic bundle could not be created.");
        }
    }

    public static EditorDiagnosticBundleActionResult RevealBundle(string path)
    {
        string? bundle = OwnedBundle(path);
        if (bundle is null)
            return new EditorDiagnosticBundleActionResult(false, "That diagnostic bundle is no longer available in this user profile.");

        try
        {
            ProcessStartInfo? startInfo = RevealStartInfo(bundle);
            if (startInfo is null)
                return new EditorDiagnosticBundleActionResult(false, "Revealing diagnostic bundles is not supported on this platform.");
            using var process = Process.Start(startInfo);
            return process is null
                ? new EditorDiagnosticBundleActionResult(false, "The diagnostic bundle location could not be opened.")
                : new EditorDiagnosticBundleActionResult(true, null);
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return new EditorDiagnosticBundleActionResult(false, "The diagnostic bundle location could not be opened.");
        }
    }

    public static EditorDiagnosticBundleActionResult DeleteBundle(string path)
    {
        string? bundle = OwnedBundle(path);
        if (bundle is null)
            return new EditorDiagnosticBundleActionResult(false, "That diagnostic bundle is no longer available in this user profile.");

        try
        {
            File.Delete(bundle);
            return new EditorDiagnosticBundleActionResult(true, null);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new EditorDiagnosticBundleActionResult(false, "The diagnostic bundle could not be deleted.");
        }
    }

    private static void AddFile(ZipArchive archive, string name, string path)
    {
        if (!File.Exists(path)) return;
        var file = new FileInfo(path);
        if (file.Length > MaximumLegalNoticeBytes)
            throw new InvalidOperationException($"Diagnostic legal notice '{name}' exceeds the bundle bound.");
        archive.CreateEntryFromFile(path, name, CompressionLevel.SmallestSize);
    }

    private static string BundleDirectory()
    {
        string root;
        if (OperatingSystem.IsWindows()) root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        else if (OperatingSystem.IsMacOS()) root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support");
        else root = Environment.GetEnvironmentVariable("XDG_STATE_HOME")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "state");
        return Path.Combine(root, "RunicArtifex", "Runic.Translations.Editor", "Diagnostics");
    }

    private static string? OwnedBundle(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path)) return null;
        string directory = Path.GetFullPath(BundleDirectory());
        string candidate = Path.GetFullPath(path);
        if (!candidate.StartsWith(directory + Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
            !candidate.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ||
            !Path.GetFileName(candidate).StartsWith("runic-translations-editor-diagnostics-", StringComparison.Ordinal) ||
            !File.Exists(candidate)) return null;
        return candidate;
    }

    private static ProcessStartInfo? RevealStartInfo(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            var windows = new ProcessStartInfo("explorer.exe") { UseShellExecute = false };
            windows.ArgumentList.Add($"/select,{path}");
            return windows;
        }
        if (OperatingSystem.IsMacOS())
        {
            var mac = new ProcessStartInfo("/usr/bin/open") { UseShellExecute = false };
            mac.ArgumentList.Add("-R");
            mac.ArgumentList.Add(path);
            return mac;
        }
        var linux = new ProcessStartInfo("xdg-open") { UseShellExecute = false };
        linux.ArgumentList.Add(BundleDirectory());
        return linux;
    }

    private static string? CommitFromVersion(string version)
    {
        int separator = version.LastIndexOf('+');
        return separator < 0 || separator == version.Length - 1 ? null : version[(separator + 1)..];
    }
}
