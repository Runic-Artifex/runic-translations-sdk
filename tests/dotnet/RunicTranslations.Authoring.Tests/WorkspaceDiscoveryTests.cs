using System;
using System.IO;
using System.Linq;
using System.Text;
using RunicTranslations.Authoring;

namespace RunicTranslations.Authoring.Tests;

internal static class WorkspaceDiscoveryTests
{
    public static void Register(TestRunner runner)
    {
        runner.Add("Discovery groups multiple catalogs deterministically", MultipleCatalogs);
        runner.Add("Discovery preserves malformed JSON as a repairable entry", MalformedJson);
        runner.Add("Discovery ignores generated and dependency directories", IgnoredDirectories);
        runner.Add("Discovery enforces file and entry limits", Limits);
        runner.Add("Discovery does not traverse symbolic links", SymbolicLinkEscape);
    }

    private static void MultipleCatalogs()
    {
        using TemporaryWorkspace workspace = new();
        WriteProject(workspace.Path, "nested/second", "second", "de", "SecondText");
        WriteProject(workspace.Path, "first", "first", "en", "FirstText");
        TranslationWorkspaceDiscoveryResult result = TranslationWorkspaceDiscovery.Discover(workspace.Path);
        Assert.Equal("first|second", string.Join('|', result.Catalogs.Select(catalog => catalog.Id)));
        Assert.True(result.Catalogs.All(catalog => catalog.Compilation.Success), "A discovered catalog did not compile.");
        Assert.Equal(4, result.Files.Count);
        Assert.Equal(0, result.Diagnostics.Count);
    }

    private static void MalformedJson()
    {
        using TemporaryWorkspace workspace = new();
        WriteProject(workspace.Path, "valid", "valid", "en", "ValidText");
        Write(workspace.Path, "broken/catalog.catalog.json", "{ \"schemaVersion\": 2,");
        TranslationWorkspaceDiscoveryResult result = TranslationWorkspaceDiscovery.Discover(workspace.Path);
        TranslationWorkspaceFile malformed = result.Files.Single(file => file.RelativePath == "broken/catalog.catalog.json");
        Assert.Equal(TranslationWorkspaceFileKind.MalformedJson, malformed.Kind);
        Assert.True(result.Diagnostics.Any(diagnostic => diagnostic.Id == "RTRA0006"), "Malformed JSON diagnostic is missing.");
        Assert.Equal("valid", AssertSingle(result.Catalogs).Id);
    }

    private static void IgnoredDirectories()
    {
        using TemporaryWorkspace workspace = new();
        WriteProject(workspace.Path, "Resources", "product", "en", "ProductText");
        Write(workspace.Path, "node_modules/hostile.catalog.json", "{");
        Write(workspace.Path, "obj/hostile.json", "{");
        TranslationWorkspaceDiscoveryResult result = TranslationWorkspaceDiscovery.Discover(workspace.Path);
        Assert.Equal(2, result.Files.Count);
        Assert.Equal(0, result.Diagnostics.Count);
    }

    private static void Limits()
    {
        using TemporaryWorkspace workspace = new();
        Write(workspace.Path, "one.json", "{}");
        Write(workspace.Path, "two.json", "{}");
        Assert.Throws<TranslationAuthoringException>(
            () => TranslationWorkspaceDiscovery.Discover(
                workspace.Path,
                new TranslationWorkspaceDiscoveryOptions(maximumJsonFiles: 1)),
            "JSON-file discovery limit");
        Assert.Throws<TranslationAuthoringException>(
            () => TranslationWorkspaceDiscovery.Discover(
                workspace.Path,
                new TranslationWorkspaceDiscoveryOptions(maximumEntries: 1)),
            "entry discovery limit");
    }

    private static void SymbolicLinkEscape()
    {
        if (OperatingSystem.IsWindows()) return;
        using TemporaryWorkspace workspace = new();
        using TemporaryWorkspace outside = new();
        WriteProject(outside.Path, "", "outside", "en", "OutsideText");
        Directory.CreateSymbolicLink(Path.Combine(workspace.Path, "linked"), outside.Path);
        TranslationWorkspaceDiscoveryResult result = TranslationWorkspaceDiscovery.Discover(workspace.Path);
        Assert.Equal(0, result.Catalogs.Count);
        Assert.True(result.Diagnostics.Any(diagnostic => diagnostic.Id == "RTRA0003"), "Link rejection diagnostic is missing.");
    }

    private static void WriteProject(string root, string directory, string catalog, string locale, string className)
    {
        string prefix = directory.Length == 0 ? string.Empty : directory + "/";
        Write(root, prefix + catalog + ".catalog.json",
            "{ \"schemaVersion\":2, \"catalog\":\"" + catalog + "\", \"code\":{\"namespace\":\"Tests\",\"className\":\"" + className + "\"}," +
            " \"defaultLocale\":\"" + locale + "\", \"locales\":[{\"tag\":\"" + locale + "\"}], \"layers\":[{\"name\":\"base\",\"priority\":0}] }");
        Write(root, prefix + catalog + "." + locale + ".json",
            "{ \"schemaVersion\":2, \"catalog\":\"" + catalog + "\", \"locale\":\"" + locale + "\", \"layer\":\"base\"," +
            " \"resources\":{\"Application\":{\"Name\":\"" + className + "\"}} }");
    }

    private static void Write(string root, string relativePath, string content)
    {
        string path = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content, new UTF8Encoding(false));
    }

    private static T AssertSingle<T>(System.Collections.Generic.IReadOnlyList<T> items)
    {
        Assert.Equal(1, items.Count);
        return items[0];
    }

    private sealed class TemporaryWorkspace : IDisposable
    {
        public TemporaryWorkspace()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"runic-discovery-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
