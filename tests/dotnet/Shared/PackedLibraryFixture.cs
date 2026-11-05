using System;
using System.IO;
using System.Text.Json.Nodes;

namespace Runic.Translations.Tests.Shared;

/// <summary>
/// An application that overrides a packed library catalog, laid out as a restore or an npm install leaves it: the
/// <c>acme.table</c> library of the build-time override tests (#154), packed under <c>runic/acme.table/</c> of a NuGet
/// package folder (found through <c>obj/project.assets.json</c>) or of <c>node_modules/@acme/table</c>
/// (<c>package.json#runicTranslations</c>). Shared by the Tooling, CLI and language-server tests.
/// </summary>
internal sealed class PackedLibraryFixture : IDisposable
{
    internal const string Catalog = "acme.table";
    internal const string LibraryProject = """{"schemaVersion":1,"catalog":"acme.table","code":{"namespace":"Acme.Table","className":"TableText","visibility":"public"},"baseLocale":"en","locales":["en","de"]}""";
    internal const string LibraryEnglish = "empty = No rows\nrows =\n  .input {$count :integer}\n  .match $count\n  one {{One row}}\n  * {{{$count} rows}}\nheader {\n  sort = Sort by {$column}\n}\n";
    internal const string LibraryGerman = "empty = Keine Zeilen\nrows =\n  .input {$count :integer}\n  .match $count\n  one {{Eine Zeile}}\n  * {{{$count} Zeilen}}\nheader {\n  sort = Sortieren nach {$column}\n}\n";

    private PackedLibraryFixture(string root, bool npm)
    {
        Root = root;
        Npm = npm;
        ProjectDirectory = Path.Combine(root, "app", "translations");
        Directory.CreateDirectory(ProjectDirectory);
        LibraryRoot = npm
            ? Path.Combine(root, "app", "node_modules", "@acme", "table", "runic", Catalog)
            : Path.Combine(root, "packages", "acme.table", "1.0.0", "runic", Catalog);
        Directory.CreateDirectory(LibraryRoot);
        File.WriteAllText(Path.Combine(LibraryRoot, "runic.json"), LibraryProject);
        File.WriteAllText(Path.Combine(LibraryRoot, "en.rmf2"), LibraryEnglish);
        File.WriteAllText(Path.Combine(LibraryRoot, "de.rmf2"), LibraryGerman);
        string from = npm ? "@acme/table" : "Acme.Table";
        File.WriteAllText(ProjectPath, $$"""
            {
              "schemaVersion": 1,
              "catalog": "shop",
              "code": { "namespace": "Shop", "className": "ShopText" },
              "baseLocale": "en",
              "overrides": [{ "catalog": "acme.table", "from": "{{from}}" }]
            }
            """);
        File.WriteAllText(Path.Combine(ProjectDirectory, "en.rmf2"), "title = Shop\n");
        string app = Path.Combine(root, "app");
        if (npm)
        {
            File.WriteAllText(Path.Combine(app, "package.json"), """{"name":"shop","private":true,"dependencies":{"@acme/table":"1.0.0"}}""");
            File.WriteAllText(Path.Combine(app, "node_modules", "@acme", "table", "package.json"),
                """{"name":"@acme/table","version":"1.0.0","exports":{"./runic-translations":"./translations/acme.table.esm-v5/web-module-manifest-v3.json"},"runicTranslations":{"catalog":"acme.table","project":"./runic/acme.table/runic.json"}}""");
            string manifest = Path.Combine(app, "node_modules", "@acme", "table", "translations", "acme.table.esm-v5");
            Directory.CreateDirectory(manifest);
            File.WriteAllText(Path.Combine(manifest, "web-module-manifest-v3.json"), """{"catalog":"acme.table","contractFingerprint":"sha256:0000000000000000000000000000000000000000000000000000000000000000"}""");
        }
        else
        {
            File.WriteAllText(Path.Combine(app, "Shop.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\" />\n");
            Directory.CreateDirectory(Path.Combine(app, "obj"));
            var assets = new JsonObject
            {
                ["version"] = 3,
                ["libraries"] = new JsonObject
                {
                    ["Acme.Table/1.0.0"] = new JsonObject
                    {
                        ["type"] = "package", ["path"] = "acme.table/1.0.0",
                        ["files"] = new JsonArray("acme.table.1.0.0.nupkg.sha512", "buildTransitive/Acme.Table.props", "runic/acme.table/catalog.props",
                            "runic/acme.table/contract.json", "runic/acme.table/de.rmf2", "runic/acme.table/en.rmf2", "runic/acme.table/runic.json"),
                    },
                    ["Unrelated/2.0.0"] = new JsonObject { ["type"] = "package", ["path"] = "unrelated/2.0.0", ["files"] = new JsonArray("lib/net10.0/Unrelated.dll") },
                },
                ["packageFolders"] = new JsonObject { [Path.Combine(root, "packages") + Path.DirectorySeparatorChar] = new JsonObject() },
            };
            File.WriteAllText(Path.Combine(app, "obj", "project.assets.json"), assets.ToJsonString());
        }
    }

    internal static PackedLibraryFixture Create(bool npm = false) =>
        new(Path.Combine(Path.GetTempPath(), "runic-library-" + Guid.NewGuid().ToString("N")), npm);

    internal string Root { get; }
    internal bool Npm { get; }
    /// <summary>The application's translations directory.</summary>
    internal string ProjectDirectory { get; }
    internal string ProjectPath => Path.Combine(ProjectDirectory, "runic.json");
    internal string OverridesDirectory => Path.Combine(ProjectDirectory, "overrides", Catalog);
    /// <summary>The installed library's <c>runic/acme.table/</c>.</summary>
    internal string LibraryRoot { get; }

    internal string OverridePath(string locale) => Path.Combine(OverridesDirectory, locale + ".rmf2");

    internal string WriteOverride(string locale, string text)
    {
        Directory.CreateDirectory(OverridesDirectory);
        File.WriteAllText(OverridePath(locale), text);
        return OverridePath(locale);
    }

    /// <summary>Simulates a library update that changes the base text of <c>empty</c>.</summary>
    internal void ChangeLibraryEmptyText(string text) =>
        File.WriteAllText(Path.Combine(LibraryRoot, "en.rmf2"), LibraryEnglish.Replace("empty = No rows", "empty = " + text, StringComparison.Ordinal));

    public void Dispose()
    {
        try { Directory.Delete(Root, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
