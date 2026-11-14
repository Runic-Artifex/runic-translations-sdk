using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Runic.Translations.Compiler.Generation;
using TUnit.Core;

namespace Runic.Translations.Compiler.Tests;

// messages.d.ts.map (#177): Go to Definition on m.key() opens the message's entry in the base locale's source. tsserver
// silently ignores a whole map with one invalid segment, so every segment is checked.
[Category("rmf2-semantic-v5")]
internal sealed class Rmf2DeclarationMapTests
{
    private const string English = """
        # Shop text.
        title = Shop
        cart {
          items =
            .input {$count :integer}
            .match $count
            one {{One item}}
            * {{{$count} items}}
          greeting = Hello {$name}, you have {$count :integer} new {$kind}
        }
        """
        + "\n";
    private const string German = "title = Laden\ncart {\n  items = {$count :integer} Artikel\n  greeting = Hallo {$name} {$count :integer} {$kind}\n}\n";

    private static readonly Func<string, string?> Sources = path => "../" + path;

    [Test, DisplayName("messages.d.ts.map maps each message and input to its base-locale entry, and every segment is valid")]
    public void MapsMessagesToBaseLocale()
    {
        Rmf2ProjectV5 project = Compile();
        IReadOnlyList<TranslationGeneratedOutput> outputs = TranslationOutputRenderer.RenderRmf2V5EsmModules(project, declarationMapSource: Sources);
        string declarations = outputs.Single(item => item.RelativePath == "app.esm-v5/messages.d.ts").Text;
        TranslationGeneratedOutput map = outputs.Single(item => item.RelativePath == "app.esm-v5/messages.d.ts.map");
        Assert.Equal(TranslationGeneratedOutputKind.EsmMessagesTypesMap, map.Kind);
        Assert.Equal("application/json", map.MediaType);
        Assert.True(declarations.EndsWith("}>;\n//# sourceMappingURL=messages.d.ts.map\n", StringComparison.Ordinal), declarations);

        var mapped = Validate(map.Text, declarations, new Dictionary<string, string> { ["../translations/en.rmf2"] = English });
        // The member, then its inputs in declaration order, each to the line of the message's key (0-based line, column).
        Assert.Equal(string.Join("; ",
            "\"cart_greeting\" -> 8:2", "\"count\" -> 8:2", "\"kind\" -> 8:2", "\"name\" -> 8:2",
            "\"cart_items\" -> 3:2", "\"count\" -> 3:2",
            "\"title\" -> 1:0"),
            string.Join("; ", mapped.Select(item => item.Name + " -> " + item.Line + ":" + item.Column)));
    }

    [Test, DisplayName("without a source for the map, messages.d.ts is unchanged; the map never changes the JavaScript")]
    public void JavaScriptUnchanged()
    {
        Rmf2ProjectV5 project = Compile();
        var plain = TranslationOutputRenderer.RenderRmf2V5EsmModules(project).ToDictionary(item => item.RelativePath, StringComparer.Ordinal);
        var mapped = TranslationOutputRenderer.RenderRmf2V5EsmModules(project, declarationMapSource: Sources).ToDictionary(item => item.RelativePath, StringComparer.Ordinal);
        var skipped = TranslationOutputRenderer.RenderRmf2V5EsmModules(project, declarationMapSource: _ => null).ToDictionary(item => item.RelativePath, StringComparer.Ordinal);
        Assert.Equal(string.Join(",", plain.Keys.Append("app.esm-v5/messages.d.ts.map").Order(StringComparer.Ordinal)), string.Join(",", mapped.Keys));
        Assert.Equal(string.Join(",", plain.Keys), string.Join(",", skipped.Keys));
        foreach ((string path, TranslationGeneratedOutput output) in plain)
        {
            Assert.Equal(output.Sha256, skipped[path].Sha256, path + " with no source the map can name");
            if (path.EndsWith(".js", StringComparison.Ordinal) || path.EndsWith(".d.ts", StringComparison.Ordinal) && !path.EndsWith("messages.d.ts", StringComparison.Ordinal))
                Assert.Equal(output.Sha256, mapped[path].Sha256, path);
        }
        Assert.Equal(plain["app.esm-v5/messages.d.ts"].Text + "//# sourceMappingURL=messages.d.ts.map\n", mapped["app.esm-v5/messages.d.ts"].Text);
        // An editor aid: the manifest does not list the map, so plugins that accept only modules keep loading the catalog.
        Assert.True(!mapped["app.esm-v5/web-module-manifest-v3.json"].Text.Contains(".map", StringComparison.Ordinal), "the manifest lists the map");
    }

    [Test, DisplayName("a library catalog regenerated with overrides maps an overridden key to the override, the others to the library")]
    public void OverridesMapToTheTextTheApplicationGets()
    {
        const string library = """{ "schemaVersion": 1, "catalog": "acme.grid", "code": { "namespace": "Acme.Grid", "className": "GridText" }, "baseLocale": "en", "locales": ["en", "de"] }""";
        const string librarySource = "empty = No rows\ntitle = Grid\n", overrideSource = "# The application's wording.\nempty = Nothing to show\n";
        Rmf2ProjectCompilation compilation = TranslationCompiler.CompileRmf2OverlayV5(
            new TranslationSource("node_modules/acme-grid/runic/acme.grid/runic.json", Encoding.UTF8.GetBytes(library)),
            [new TranslationSource("node_modules/acme-grid/runic/acme.grid/en.rmf2", Encoding.UTF8.GetBytes(librarySource)),
             new TranslationSource("node_modules/acme-grid/runic/acme.grid/de.rmf2", Encoding.UTF8.GetBytes("empty = Keine Zeilen\ntitle = Raster\n"))],
            [new TranslationSource("translations/overrides/acme.grid/en.rmf2", Encoding.UTF8.GetBytes(overrideSource))]);
        Assert.True(compilation.Success, string.Join("; ", compilation.Diagnostics.Select(item => item.Message)));
        var outputs = TranslationOutputRenderer.RenderRmf2V5EsmModules(compilation.Project!, declarationMapSource: Sources);
        var mapped = Validate(outputs.Single(item => item.RelativePath == "acme.grid.esm-v5/messages.d.ts.map").Text,
            outputs.Single(item => item.RelativePath == "acme.grid.esm-v5/messages.d.ts").Text,
            new Dictionary<string, string>
            {
                ["../node_modules/acme-grid/runic/acme.grid/en.rmf2"] = librarySource,
                ["../translations/overrides/acme.grid/en.rmf2"] = overrideSource,
            });
        Assert.Equal("\"empty\" -> ../translations/overrides/acme.grid/en.rmf2:1; \"title\" -> ../node_modules/acme-grid/runic/acme.grid/en.rmf2:1",
            string.Join("; ", mapped.Select(item => item.Name + " -> " + item.Source + ":" + item.Line)));
    }

    // The CLI names the sources relative to <output>/<catalog>.esm-v5/, verify accepts what generate wrote, and the real
    // tsserver goes from m.key() to the .rmf2 line.
    [Test, NotInParallel, DisplayName("generate writes messages.d.ts.map that tsserver follows from m.key() to the .rmf2 line")]
    public void TsserverFollowsTheMap()
    {
        string root = Path.Combine(Path.GetTempPath(), "runic-dts-map-" + Guid.NewGuid().ToString("N"));
        try
        {
            Write(root, "translations/runic.json", """{ "schemaVersion": 1, "catalog": "app", "code": { "namespace": "Example", "className": "AppText" }, "baseLocale": "en", "locales": ["en", "de"] }""");
            Write(root, "translations/en.rmf2", English);
            Write(root, "translations/de.rmf2", German);
            string configuration = new DirectoryInfo(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar)).Parent!.Name;
            string tool = RepositoryPaths.Resolve("tools", "dotnet-runic-translations", "bin", configuration, "net10.0", "dotnet-runic-translations.dll");
            Assert.True(File.Exists(tool), "the tool is built: " + tool);
            Rmf2EsmV5Tests.Run("dotnet", [tool, "generate", "--project", "translations", "--output", "obj/translations", "--emit-esm"], root);
            Rmf2EsmV5Tests.Run("dotnet", [tool, "verify", "--project", "translations", "--output", "obj/translations", "--emit-esm"], root);
            string generated = Path.Combine(root, "obj", "translations", "app.esm-v5");
            var mapped = Validate(File.ReadAllText(Path.Combine(generated, "messages.d.ts.map")), File.ReadAllText(Path.Combine(generated, "messages.d.ts")),
                new Dictionary<string, string> { ["../../../translations/en.rmf2"] = English });
            Assert.Equal(7, mapped.Count);

            Write(root, "src/app.ts", """
                import { m } from "../obj/translations/app.esm-v5/messages.js";
                export const title = m.title();
                export const items = m.cart_items({ count: 2 });
                export const greeting = m.cart_greeting({ name: "Ada", count: 2, kind: "x" });
                """);
            Write(root, "tsconfig.json", """{ "compilerOptions": { "module": "ESNext", "moduleResolution": "Bundler", "target": "ES2022", "strict": true, "noEmit": true }, "include": ["src"] }""");
            string helper = RepositoryPaths.Resolve("packages", "web", "vite-plugin-runic-translations", "test", "tsserver.js");
            Write(root, "check.mjs", "import { join } from \"node:path\";\nconst { definitions } = await import(" + JsonSerializer.Serialize(helper) + ");\n" + """
                const root = import.meta.dirname, source = join(root, "translations", "en.rmf2");
                // m.title, m.cart_items, m.cart_greeting, the greeting's kind input, and m itself.
                const found = await definitions(join(root, "src", "app.ts"), [[2, 24], [3, 24], [4, 27], [4, 67], [2, 22]]);
                const expected = [`${source}:2:1`, `${source}:4:3`, `${source}:9:3`, `${source}:9:3`];
                for (let index = 0; index < expected.length; index++)
                  if (JSON.stringify(found[index]) !== JSON.stringify([expected[index]])) throw new Error(`definition ${index}: ${JSON.stringify(found[index])}, expected ${expected[index]}`);
                if (!/messages\.d\.ts:\d+:\d+$/.test(found[4][0] ?? "")) throw new Error("m itself: " + JSON.stringify(found[4]));
                """);
            Rmf2EsmV5Tests.Run("bun", [Path.Combine(root, "check.mjs")], root);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    private static Rmf2ProjectV5 Compile()
    {
        Rmf2ProjectCompilation compilation = TranslationCompiler.CompileRmf2ProjectV5(
            Rmf2ProjectV5Tests.Project(",\"locales\":[\"en\",\"de\"]"),
            [new TranslationSource("translations/de.rmf2", Encoding.UTF8.GetBytes(German)), new TranslationSource("translations/en.rmf2", Encoding.UTF8.GetBytes(English))]);
        Assert.True(compilation.Success, string.Join("; ", compilation.Diagnostics.Select(item => item.Message)));
        return compilation.Project!;
    }

    // Decodes the map, checks the map's shape and that every segment lies within its generated and source line, and returns
    // the mapped names (the JSON string at the segment's column) with their 0-based source positions in declaration order.
    private static List<(string Name, string Source, int Line, int Column)> Validate(string mapJson, string declarations, IReadOnlyDictionary<string, string> sourceTexts)
    {
        using JsonDocument document = JsonDocument.Parse(mapJson);
        JsonElement map = document.RootElement;
        Assert.Equal(3, map.GetProperty("version").GetInt32());
        Assert.Equal("messages.d.ts", map.GetProperty("file").GetString());
        Assert.True(!map.TryGetProperty("sourcesContent", out _), "tsserver ignores a declaration map with sourcesContent");
        string[] sources = map.GetProperty("sources").EnumerateArray().Select(item => item.GetString()!).ToArray();
        Assert.Equal(string.Join(",", sourceTexts.Keys.Order(StringComparer.Ordinal)), string.Join(",", sources));
        string[] lines = declarations.Split('\n')[..^1];
        string[] mappings = map.GetProperty("mappings").GetString()!.Split(';');
        Assert.Equal(lines.Length, mappings.Length, "one mappings line per declarations line");
        var result = new List<(string, string, int, int)>();
        int source = 0, sourceLine = 0, sourceColumn = 0;
        for (int line = 0; line < mappings.Length; line++)
        {
            int column = 0;
            bool startsLine = false;
            foreach (string field in mappings[line].Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                int[] values = DecodeVlq(field);
                Assert.True(values.Length is 1 or 4, $"segment {field} on line {line}");
                column += values[0];
                Assert.True(column >= 0 && column <= lines[line].Length, $"generated column {column} on line {line}");
                startsLine |= column == 0;
                if (values.Length == 1) continue;
                source += values[1]; sourceLine += values[2]; sourceColumn += values[3];
                Assert.True(source >= 0 && source < sources.Length, $"source index {source}");
                string[] sourceLines = sourceTexts[sources[source]].Split('\n');
                Assert.True(sourceLine >= 0 && sourceLine < sourceLines.Length && sourceColumn >= 0 && sourceColumn <= sourceLines[sourceLine].Length,
                    $"source position {sourceLine}:{sourceColumn} in {sources[source]}");
                string name = lines[line][column..];
                Assert.True(name.StartsWith('"'), $"a mapped segment starts a name: {lines[line]}");
                result.Add((name[..(name.IndexOf('"', 1) + 1)], sources[source], sourceLine, sourceColumn));
            }
            Assert.True(startsLine, $"line {line} starts with a segment, so a position on it does not resolve to the next message");
        }
        return result;
    }

    private static int[] DecodeVlq(string field)
    {
        const string digits = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";
        var values = new List<int>();
        int value = 0, shift = 0;
        foreach (char character in field)
        {
            int digit = digits.IndexOf(character);
            Assert.True(digit >= 0, "base64 digit " + character);
            value += (digit & 31) << shift;
            if ((digit & 32) != 0) { shift += 5; continue; }
            values.Add((value & 1) != 0 ? -(value >> 1) : value >> 1);
            value = 0; shift = 0;
        }
        Assert.Equal(0, shift, "a complete VLQ value");
        return values.ToArray();
    }

    private static void Write(string root, string relativePath, string text)
    {
        string path = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text, new UTF8Encoding(false));
    }
}
