using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using TUnit.Core;

namespace Runic.Translations.Build.Tests;

// #264: the languages of runic.json become RunicApplicationLocale items during evaluation, as `dotnet runic package` reads
// them with -getProperty and -getItem, unless the project sets RunicApplicationLocales.
internal sealed class ApplicationLocalesTests
{
    private const string Catalog = """
        {
          "$schema": "https://runic-artifex.eu/schemas/translations/project-v1.schema.json",
          // A comment with "locales": ["xx"] is not read.
          "schemaVersion": 1,
          "catalog": "notes",
          "code": { "namespace": "Notes", "className": "NotesText" },
          "baseLocale": "en",
          "locales": ["en", { "validation": { "placeholders": ["a"] }, "tag": "pt-BR", "fallback": "pt" }, "de" /* c */, "zh-Hant"],
          "pseudoLocales": ["en-XA", "ar-XB"]
        }
        """;

    [Test, DisplayName("a project without RunicApplicationLocales gets the runic.json languages, base first, before its own items")]
    public void FillsFromRunicJson()
    {
        using TemporaryDirectory temporary = new();
        Write(temporary.Resolve("Frontend", "translations", "runic.json"), Catalog);
        Write(temporary.Resolve("App.proj"), Project("""
            <ItemGroup>
              <RunicTranslationProject Include="Frontend/translations/runic.json" />
              <RunicApplicationLocale Include="de" Name="Notizen" />
              <RunicApplicationLocale Include="ja" Name="ノート" />
            </ItemGroup>
            """, BuildTargets()));

        (string Property, string[] Items) locales = Evaluate(temporary.Path, "App.proj");

        Assert.Equal("", locales.Property, "the property stays the project's");
        Assert.Equal("en|pt-BR|de|zh-Hant|de:Notizen|ja:ノート", string.Join("|", locales.Items), "items");
    }

    [Test, DisplayName("a project that sets RunicApplicationLocales keeps its value and its items")]
    public void KeepsTheProjectsValue()
    {
        using TemporaryDirectory temporary = new();
        Write(temporary.Resolve("translations", "runic.json"), Catalog);
        Write(temporary.Resolve("App.proj"), Project("""
            <PropertyGroup><RunicApplicationLocales>fr;de</RunicApplicationLocales></PropertyGroup>
            <ItemGroup><RunicApplicationLocale Include="de" Name="Notizen" /></ItemGroup>
            """, BuildTargets()));

        (string Property, string[] Items) locales = Evaluate(temporary.Path, "App.proj");

        Assert.Equal("fr;de", locales.Property, "property");
        Assert.Equal("de:Notizen", string.Join("|", locales.Items), "items");
        Assert.Equal("de:Notizen", string.Join("|", Evaluate(temporary.Path, "App.proj", "-p:RunicApplicationLocales=", "-p:RunicTranslationsFillApplicationLocales=false").Items),
            "RunicTranslationsFillApplicationLocales=false adds nothing");
    }

    [Test, DisplayName("the languages come from the default translations/runic.json, also in a multi-targeted project's outer evaluation")]
    public void ReadsTheDefaultProject()
    {
        using TemporaryDirectory temporary = new();
        Write(temporary.Resolve("translations", "runic.json"), Catalog);
        Write(temporary.Resolve("Inner.proj"), Project("", BuildTargets()));
        Write(temporary.Resolve("Outer.proj"), Project("", Targets("buildMultiTargeting", "Runic.Translations.Build.targets")));

        Assert.Equal("en|pt-BR|de|zh-Hant", string.Join("|", Evaluate(temporary.Path, "Inner.proj").Items), "inner");
        Assert.Equal("en|pt-BR|de|zh-Hant", string.Join("|", Evaluate(temporary.Path, "Outer.proj").Items), "outer");
    }

    [Test, DisplayName("without locales only the base locale counts; several runic.json add theirs in order; a missing or invalid one adds nothing")]
    public void HandlesOtherDeclarations()
    {
        using TemporaryDirectory temporary = new();
        Write(temporary.Resolve("a", "runic.json"), """{ "catalog": "a", "baseLocale": "de-CH" }""");
        Write(temporary.Resolve("b", "runic.json"), Catalog);
        Write(temporary.Resolve("c", "runic.json"), "not JSON");
        foreach ((string include, string expected) in new[]
        {
            ("a/runic.json", "de-CH"),
            ("a/runic.json;b/runic.json", "de-CH|en|pt-BR|de|zh-Hant"),
            ("missing/runic.json", ""),
            ("c/runic.json", ""),
        })
        {
            Write(temporary.Resolve("App.proj"), Project($"""<ItemGroup><RunicTranslationProject Include="{include}" /></ItemGroup>""", BuildTargets()));
            Assert.Equal(expected, string.Join("|", Evaluate(temporary.Path, "App.proj").Items), include);
        }
    }

    private static string BuildTargets() => Targets("build", "Runic.Translations.Build.targets");

    private static string Targets(string folder, string file) =>
        RepositoryPaths.Resolve("packages", "dotnet", "Runic.Translations.Build", folder, file)
            .Replace("&", "&amp;", System.StringComparison.Ordinal).Replace("\"", "&quot;", System.StringComparison.Ordinal);

    private static string Project(string body, string targets) => $$"""
        <Project>
          <PropertyGroup><Configuration>Debug</Configuration></PropertyGroup>
          {{body}}
          <Import Project="{{targets}}" />
        </Project>
        """;

    private static void Write(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text, new UTF8Encoding(false));
    }

    // As dotnet runic package reads the project: the property, and each item as its tag with ":Name" when it has one.
    private static (string Property, string[] Items) Evaluate(string directory, string project, params string[] properties)
    {
        ProcessResult result = Processes.DotNet(directory, ["msbuild", project, "-nologo",
            "-getProperty:RunicApplicationLocales", "-getItem:RunicApplicationLocale", .. properties]);
        Assert.Equal(0, result.ExitCode, result.Combined);
        using JsonDocument json = JsonDocument.Parse(result.StandardOutput);
        string property = json.RootElement.GetProperty("Properties").GetProperty("RunicApplicationLocales").GetString()!;
        string[] items = json.RootElement.GetProperty("Items").TryGetProperty("RunicApplicationLocale", out JsonElement list)
            ? list.EnumerateArray().Select(item =>
                item.GetProperty("Identity").GetString()
                + (item.TryGetProperty("Name", out JsonElement name) && name.GetString() is { Length: > 0 } text ? ":" + text : "")).ToArray()
            : [];
        return (property, items);
    }
}
