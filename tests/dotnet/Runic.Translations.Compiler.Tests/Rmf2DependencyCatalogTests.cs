using System;
using System.IO;
using System.Linq;
using System.Text;
using Runic.Translations.Compiler.Generation;
using TUnit.Core;

namespace Runic.Translations.Compiler.Tests;

// Vite dependencies: "auto" (T1e): an npm library ships its catalog (package.json#runicTranslations, the packed runic.json
// and exports["./runic-translations"]); a Vite build of an application regenerates it with the application's overrides,
// filtered to its locales, over one shared runtime, and redirects the library's own imports to it.
[Category("rmf2-semantic-v5"), NotInParallel]
internal sealed class Rmf2DependencyCatalogTests
{
    private const string LibraryProject = """
        { "schemaVersion": 1, "catalog": "acme.grid", "code": { "namespace": "Acme.Grid", "className": "GridText" }, "baseLocale": "en", "locales": ["en", "de", "it"] }
        """;
    private const string ChartProject = """
        { "schemaVersion": 1, "catalog": "acme.chart", "code": { "namespace": "Acme.Chart", "className": "ChartText" }, "baseLocale": "en", "locales": ["en", "de"] }
        """;

    [Test, DisplayName("a Vite build regenerates dependency catalogs with overrides, the app's locales and one shared runtime")]
    public void RegeneratesDependencies()
    {
        string root = Path.Combine(Path.GetTempPath(), "runic-vite-dependencies-" + Guid.NewGuid().ToString("N"));
        try
        {
            Library(root, "@acme/grid", "acme.grid", LibraryProject,
                ("en", "empty = No rows\nrows =\n  .input {$count :integer}\n  .match $count\n  one {{One row}}\n  * {{{$count} rows}}\n"),
                ("de", "empty = Keine Zeilen\nrows =\n  .input {$count :integer}\n  .match $count\n  one {{Eine Zeile}}\n  * {{{$count} Zeilen}}\n"),
                ("it", "empty = Nessuna riga\nrows =\n  .input {$count :integer}\n  .match $count\n  one {{Una riga}}\n  * {{{$count} righe}}\n"));
            File.WriteAllText(Path.Combine(root, "node_modules", "@acme", "grid", "index.js"),
                "import { m } from \"./translations/acme.grid.esm-v5/messages.js\";\nexport const empty = () => m.empty();\nexport const rows = count => m.rows({ count });\n");
            Library(root, "acme-chart", "acme.chart", ChartProject, ("en", "axis = Axis\n"), ("de", "axis = Achse\n"));
            File.WriteAllText(Path.Combine(root, "node_modules", "acme-chart", "index.js"),
                "import { m } from \"./translations/acme.chart.esm-v5/messages.js\";\nexport const axis = () => m.axis();\n");

            Write(root, "package.json", """{ "name": "shop", "type": "module", "dependencies": { "@acme/grid": "1.0.0", "acme-chart": "1.0.0" } }""");
            Write(root, "translations/runic.json", """
                { "schemaVersion": 1, "catalog": "shop", "code": { "namespace": "Shop", "className": "ShopText" }, "baseLocale": "en", "locales": ["en", "fr"],
                  "overrides": [{ "catalog": "acme.grid", "from": "@acme/grid" }] }
                """);
            Write(root, "translations/en.rmf2", "title = Shop\n");
            Write(root, "translations/fr.rmf2", "title = Boutique\n");
            Write(root, "translations/overrides/acme.grid/en.rmf2", "empty = Nothing to show\n");
            Write(root, "translations/overrides/acme.grid/fr.rmf2", "empty = Aucune ligne\nrows =\n  .input {$count :integer}\n  .match $count\n  one {{Une ligne}}\n  many {{{$count} de lignes}}\n  * {{{$count} lignes}}\n");
            Write(root, "app.js", """
                import { empty, rows } from "@acme/grid";
                import { axis } from "acme-chart";
                import { m } from "virtual:runic-translations/shop";
                import { m as grid } from "virtual:runic-translations/acme.grid";
                import { configureSharedLocaleResolver, locales } from "virtual:runic-translations/acme.grid/runtime";
                let locale = "en";
                configureSharedLocaleResolver(() => locale);
                export const english = [m.title(), empty(), rows(3), axis(), grid.empty()].join("|");
                locale = "fr";
                export const french = [m.title(), empty(), rows(3), axis()].join("|");
                locale = "de";
                export const german = [empty(), axis()].join("|");
                export const gridLocales = locales.join(",");
                """);

            string configuration = new DirectoryInfo(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar)).Parent!.Name;
            string tool = RepositoryPaths.Resolve("tools", "dotnet-runic-translations", "bin", configuration, "net10.0", "dotnet-runic-translations.dll");
            Assert.True(File.Exists(tool), "the tool is built: " + tool);
            string plugin = RepositoryPaths.Resolve("packages", "web", "vite-plugin-runic-translations", "dist", "index.js");
            Write(root, "build.mjs", "import { createRequire } from \"node:module\";\nimport { readFile, readdir } from \"node:fs/promises\";\nimport { join } from \"node:path\";\n" +
                "const plugin = " + Json(plugin) + ", tool = " + Json(tool) + ";\n" + """
                const { runicTranslations } = await import(plugin);
                const { build } = await import(createRequire(plugin).resolve("vite"));
                const root = import.meta.dirname, outDir = join(root, "dist");
                await build({ configFile: false, root, logLevel: "silent",
                  plugins: [runicTranslations({ project: join(root, "translations"), cwd: root, dependencies: "auto", command: "dotnet", commandArguments: [tool], persistentCompiler: false })],
                  build: { outDir, minify: false, lib: { entry: join(root, "app.js"), formats: ["es"], fileName: () => "bundle.js" } } });
                const bundle = await readFile(join(outDir, "bundle.js"), "utf8");
                const result = await import(join(outDir, "bundle.js"));
                const equal = (actual, expected, label) => { if (actual !== expected) throw new Error(`${label}: ${JSON.stringify(actual)} !== ${JSON.stringify(expected)}`); };
                equal(result.english, "Shop|Nothing to show|3 rows|Axis|Nothing to show", "English: the override, through the library's import and the virtual ID");
                equal(result.french, "Boutique|Aucune ligne|3 lignes|Axis", "French: added by the application; the chart falls back to English");
                equal(result.german, "Nothing to show|Axis", "German is not an application locale, so the library's German is left out");
                equal(result.gridLocales, "en,fr", "locales");
                for (const text of ["Keine Zeilen", "Nessuna riga", "Achse"]) if (bundle.includes(text)) throw new Error("the bundle contains " + text);
                equal(bundle.split("function createCatalogRuntime(").length - 1, 1, "one shared runtime for both dependency catalogs");
                const types = await readFile(join(root, ".runic", "translations", "dependencies", "acme.grid.d.ts"), "utf8");
                if (!types.includes('declare module "virtual:runic-translations/acme.grid"')) throw new Error("no declarations for the dependency catalog");
                const declarations = await readFile(join(root, ".runic", "translations", "virtual.d.ts"), "utf8");
                if (!declarations.includes('/// <reference path="dependencies/acme.grid.d.ts" />')) throw new Error("the application's declarations do not reference the dependency's: " + declarations);
                const library = await readdir(join(root, "node_modules", "@acme", "grid"), { recursive: true });
                if (library.some(file => file.endsWith(".d.ts") && !file.includes("esm-v5"))) throw new Error("declarations were written into node_modules: " + library);
                """);
            Rmf2EsmV5Tests.Run("bun", [Path.Combine(root, "build.mjs")], root);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    // A library package as npm ships it: its packed runic.json and sources, its prebuilt ESM and the package.json contract.
    private static void Library(string root, string name, string catalog, string project, params (string Locale, string Source)[] sources)
    {
        string package = Path.Combine([root, "node_modules", .. name.Split('/')]);
        Write(package, "package.json", "{ \"name\": " + Json(name) + ", \"version\": \"1.0.0\", \"type\": \"module\", " +
            "\"exports\": { \".\": \"./index.js\", \"./runic-translations\": \"./translations/" + catalog + ".esm-v5/web-module-manifest-v3.json\" }, " +
            "\"runicTranslations\": { \"catalog\": " + Json(catalog) + ", \"project\": \"./runic/" + catalog + "/runic.json\", \"futureMember\": true } }");
        Write(package, "runic/" + catalog + "/runic.json", project);
        foreach ((string locale, string source) in sources) Write(package, "runic/" + catalog + "/" + locale + ".rmf2", source);
        Rmf2ProjectCompilation compilation = TranslationCompiler.CompileRmf2ProjectV5(new TranslationSource("runic.json", Encoding.UTF8.GetBytes(project)),
            sources.Select(source => new TranslationSource(source.Locale + ".rmf2", Encoding.UTF8.GetBytes(source.Source))).ToArray());
        Assert.True(compilation.Success, string.Join("; ", compilation.Diagnostics.Select(item => item.Message)));
        foreach (TranslationGeneratedOutput output in TranslationOutputRenderer.RenderRmf2V5EsmModules(compilation.Project!))
            Write(package, "translations/" + output.RelativePath, output.Text);
    }

    private static void Write(string root, string relativePath, string text)
    {
        string path = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text, new UTF8Encoding(false));
    }

    private static string Json(string value) => System.Text.Json.JsonSerializer.Serialize(value);
}
