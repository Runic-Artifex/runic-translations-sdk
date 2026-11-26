using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Runic.Translations.Compiler.Generation;
using TUnit.Core;

namespace Runic.Translations.Compiler.Tests;

// The ESM conformance adapter's catalogs (packages/web/vite-plugin-runic-translations/test/conformance-adapter.ts). The ESM
// runtime is generated per catalog: a pack is decoded by its own catalog's dynamic.js, against that catalog's contract
// tables and CLDR data. So for each compiled group of specs/translations/corpus/compiled this writes the ESM modules
// (generate --emit-esm) of the very project that produced the group's committed artifact, and for each group of
// locale-negotiation.json a catalog with that group's locales. The adapter's build step runs this case only; it never
// runs in the suite (Explicit).
internal sealed class ConformanceEsmCatalogs
{
    [Test, Explicit, NotInParallel, DisplayName("write the ESM conformance adapter's catalogs")]
    public void Write()
    {
        string output = TestContext.Parameters.TryGetValue("esm-catalogs", out var values) && values.Count > 0 ? values[^1]
            : throw new InvalidOperationException("Pass --test-parameter esm-catalogs=<directory>.");
        if (Directory.Exists(output)) Directory.Delete(output, true);
        Directory.CreateDirectory(output);
        var index = new JsonObject();
        var directories = new Dictionary<Rmf2ProjectV5, string>(ReferenceEqualityComparer.Instance);
        foreach (CompiledCorpus.Group group in CompiledCorpus.Groups())
        {
            if (!directories.TryGetValue(group.Project, out string? directory))
                directories[group.Project] = directory = WriteCatalog(output, "c" + directories.Count, group.Project);
            index[group.Id] = directory;
        }
        JsonNode corpus = JsonNode.Parse(File.ReadAllBytes(RepositoryPaths.Resolve("specs", "translations", "corpus", "locale-negotiation.json")))!;
        foreach (JsonNode? item in corpus["groups"]!.AsArray())
        {
            string[] locales = item!["locales"]!.AsArray().Select(locale => locale!.GetValue<string>()).ToArray();
            Rmf2ProjectV5 project = Rmf2EsmV5Tests.CompileProject("negotiation", item["base"]!.GetValue<string>(), locales, false,
                locales.Select(locale => (locale, "x = {{x}}\n")).ToArray());
            index["negotiation/" + item["id"]!.GetValue<string>()] = WriteCatalog(output, "n" + index.Count, project);
        }
        File.WriteAllText(Path.Combine(output, "index.json"), index.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n", new UTF8Encoding(false));
    }

    // The catalog's ESM modules in <output>/<name>; the index names the module directory, <name>/<catalog>.esm-v5.
    private static string WriteCatalog(string output, string name, Rmf2ProjectV5 project)
    {
        foreach (TranslationGeneratedOutput file in TranslationOutputRenderer.RenderRmf2V5EsmModules(project))
        {
            string path = Path.Combine(output, name, file.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, file.GetUtf8Bytes());
        }
        return name + "/" + project.Id + ".esm-v5";
    }
}
