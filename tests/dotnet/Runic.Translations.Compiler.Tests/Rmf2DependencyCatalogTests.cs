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
            // acme-chart is reached only through @acme/grid's dependencies.
            Library(root, "@acme/grid", "acme.grid", LibraryProject, "\"dependencies\": { \"acme-chart\": \"1.0.0\" }, ",
                ("en", "empty = No rows\nrows =\n  .input {$count :integer}\n  .match $count\n  one {{One row}}\n  * {{{$count} rows}}\n"),
                ("de", "empty = Keine Zeilen\nrows =\n  .input {$count :integer}\n  .match $count\n  one {{Eine Zeile}}\n  * {{{$count} Zeilen}}\n"),
                ("it", "empty = Nessuna riga\nrows =\n  .input {$count :integer}\n  .match $count\n  one {{Una riga}}\n  * {{{$count} righe}}\n"));
            File.WriteAllText(Path.Combine(root, "node_modules", "@acme", "grid", "index.js"),
                "import { m } from \"./translations/acme.grid.esm-v5/messages.js\";\nexport const empty = () => m.empty();\nexport const rows = count => m.rows({ count });\n");
            Library(root, "acme-chart", "acme.chart", ChartProject, "", ("en", "axis = Axis\n"), ("de", "axis = Achse\n"));
            File.WriteAllText(Path.Combine(root, "node_modules", "acme-chart", "index.js"),
                "import { m } from \"./translations/acme.chart.esm-v5/messages.js\";\nexport const axis = () => m.axis();\n");

            Write(root, "package.json", """{ "name": "shop", "type": "module", "dependencies": { "@acme/grid": "1.0.0" } }""");
            // A module an earlier generation wrote is removed when the catalog is regenerated.
            Write(root, ".runic/translations/dependencies/acme.grid.esm-v5/messages/stale.js", "export const stale = 1;\n");
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
                const generated = await readdir(join(root, ".runic", "translations", "dependencies", "acme.grid.esm-v5", "messages"));
                if (generated.includes("stale.js")) throw new Error("a stale module survived the regeneration");
                """);
            Rmf2EsmV5Tests.Run("bun", [Path.Combine(root, "build.mjs")], root);

            // Builds that must fail: an override of a catalog no dependency ships, and packed sources that do not match the
            // library's prebuilt catalog.
            string runic = File.ReadAllText(Path.Combine(root, "translations", "runic.json"));
            Write(root, "fail.mjs", "import { join } from \"node:path\";\nimport { createRequire } from \"node:module\";\nconst plugin = " + Json(plugin) + ", tool = " + Json(tool) + ";\n" + """
                const { runicTranslations } = await import(plugin);
                const { build } = await import(createRequire(plugin).resolve("vite"));
                const root = import.meta.dirname;
                try {
                  await build({ configFile: false, root, logLevel: "silent",
                    plugins: [runicTranslations({ project: join(root, "translations"), cwd: root, dependencies: "auto", command: "dotnet", commandArguments: [tool], persistentCompiler: false })],
                    build: { outDir: join(root, "dist-fail"), lib: { entry: join(root, "app.js"), formats: ["es"], fileName: () => "bundle.js" } } });
                } catch (error) {
                  if (!new RegExp(process.argv[2], "s").test(error.message)) throw new Error("unexpected error: " + error.message);
                  process.exit(0);
                }
                throw new Error("the build succeeded");
                """);
            Write(root, "translations/runic.json", runic.Replace("[{ \"catalog\": \"acme.grid\", \"from\": \"@acme/grid\" }]",
                "[{ \"catalog\": \"acme.grid\", \"from\": \"@acme/grid\" }, { \"catalog\": \"acme.menu\", \"from\": \"@acme/menu\" }]", StringComparison.Ordinal));
            Rmf2EsmV5Tests.Run("bun", [Path.Combine(root, "fail.mjs"), "RTR0104: Catalog 'acme\\.menu' to override is not shipped by any dependency"], root);
            Write(root, "translations/runic.json", runic);
            Write(Path.Combine(root, "node_modules", "acme-chart"), "runic/acme.chart/en.rmf2", "axis = Axis\nlegend = Legend\n");
            Write(Path.Combine(root, "node_modules", "acme-chart"), "runic/acme.chart/de.rmf2", "axis = Achse\nlegend = Legende\n");
            Rmf2EsmV5Tests.Run("bun", [Path.Combine(root, "fail.mjs"), "regenerated from the packed runic\\.json of 'acme-chart@1\\.0\\.0' has contract fingerprint"], root);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Test, DisplayName("a regenerated catalog falls back from an added de-CH to an added de, as the .NET overlay does")]
    public void AddedLocaleFallbackInEsm()
    {
        // The library declares en only; the application adds de (every key) and de-CH (one key). The compiler chooses de for
        // de-CH (Rmf2OverrideTests.AddedLocaleFallbacks, and CompositionTests.CompiledOverrideFallbacks for .NET).
        const string project = """{ "schemaVersion": 1, "catalog": "acme.grid", "code": { "namespace": "Acme.Grid", "className": "GridText" }, "baseLocale": "en", "locales": ["en"] }""";
        Rmf2ProjectCompilation compilation = TranslationCompiler.CompileRmf2OverlayV5(new TranslationSource("runic/acme.grid/runic.json", Encoding.UTF8.GetBytes(project)),
            [new TranslationSource("runic/acme.grid/en.rmf2", Encoding.UTF8.GetBytes("empty = No rows\ntitle = Grid\n"))],
            [new TranslationSource("translations/overrides/acme.grid/de-CH.rmf2", Encoding.UTF8.GetBytes("title = Raster (CH)\n")),
             new TranslationSource("translations/overrides/acme.grid/de.rmf2", Encoding.UTF8.GetBytes("empty = Keine Zeilen\ntitle = Raster\n"))]);
        Assert.True(compilation.Success, string.Join("; ", compilation.Diagnostics.Select(item => item.Message)));
        string directory = Rmf2EsmV5Tests.Write(TranslationOutputRenderer.RenderRmf2V5EsmModules(compilation.Project!));
        try
        {
            File.WriteAllText(Path.Combine(directory, "check.mjs"), """
                import { m } from "./acme.grid.esm-v5/messages.js";
                import { configureSharedLocaleResolver } from "./acme.grid.esm-v5/runtime.js";
                configureSharedLocaleResolver(() => "de-CH");
                const text = [m.title(), m.empty()].join("|");
                if (text !== "Raster (CH)|Keine Zeilen") throw new Error("de-CH: " + text);
                """, new UTF8Encoding(false));
            Rmf2EsmV5Tests.Run("bun", [Path.Combine(directory, "check.mjs")], directory);
        }
        finally { Directory.Delete(directory, true); }
    }

    // A library package as npm ships it: its packed runic.json and sources, its prebuilt ESM and the package.json contract.
    private static void Library(string root, string name, string catalog, string project, string members, params (string Locale, string Source)[] sources)
    {
        string package = Path.Combine([root, "node_modules", .. name.Split('/')]);
        Write(package, "package.json", "{ \"name\": " + Json(name) + ", \"version\": \"1.0.0\", \"type\": \"module\", " + members +
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
