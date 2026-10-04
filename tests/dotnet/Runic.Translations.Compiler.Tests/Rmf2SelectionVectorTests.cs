using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Runic.Translations.Compiler.Generation;

namespace Runic.Translations.Compiler.Tests;

// The selection vectors in rmf2-execution-v2.json are the shared .NET/ESM oracle
// for plural, ordinal and exact selection on the visible decimal.
internal static class Rmf2SelectionVectorTests
{
    private sealed record Vector(string Id, string Locale, string Message, JsonObject Arguments, string Expected);

    internal static void Register(TestRunner runner)
    {
        runner.Add("RMF2 v5 selection vectors select on the visible decimal in .NET", DotNet);
        runner.Add("RMF2 v5 selection vectors select on the visible decimal in generated ESM", Esm);
    }

    private static List<Vector> Vectors()
    {
        var profile = JsonNode.Parse(File.ReadAllBytes(RepositoryPaths.Resolve("specs", "translations", "rmf2-execution-v2.json")))!;
        var vectors = profile["selectionVectors"]!.AsArray().Select(item => new Vector(
            item!["id"]!.GetValue<string>(), item["locale"]!.GetValue<string>(), item["message"]!.GetValue<string>(),
            item["arguments"]!.AsObject(), item["expected"]!.GetValue<string>())).ToList();
        Assert.True(vectors.Count > 20, "The selection vectors are missing.");
        Assert.Equal(vectors.Count, vectors.Select(vector => vector.Id).Distinct(StringComparer.Ordinal).Count(), "Duplicate selection vector ids.");
        return vectors;
    }

    private static void DotNet()
    {
        foreach (Vector vector in Vectors())
        {
            var result = Rmf2SemanticCompilerV5.Compile(new TranslationSource(vector.Id + ".mf2", Encoding.UTF8.GetBytes(vector.Message)));
            Assert.True(result.Success, vector.Id + ": " + string.Join("; ", result.Diagnostics.Select(item => item.Message)));
            var arguments = vector.Arguments.OrderBy(item => item.Key, StringComparer.Ordinal).Select(item =>
            {
                string value = item.Value!["value"]!.GetValue<string>();
                return item.Value["type"]!.GetValue<string>() == "int64"
                    ? new TextArgument(item.Key, long.Parse(value, CultureInfo.InvariantCulture))
                    : new TextArgument(item.Key, decimal.Parse(value, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture));
            }).ToArray();
            Assert.Equal(vector.Expected, Rmf2RuntimeV5Tests.Lower(result.Message!).Format(arguments, vector.Locale), vector.Id);
        }
    }

    private static void Esm()
    {
        foreach (var group in Vectors().GroupBy(vector => vector.Locale, StringComparer.Ordinal))
        {
            Vector[] vectors = group.ToArray();
            var source = new StringBuilder();
            for (int index = 0; index < vectors.Length; index++)
            {
                source.Append("vector").Append(index).Append(" =\n");
                foreach (string line in vectors[index].Message.Split('\n')) source.Append("  ").Append(line).Append('\n');
            }
            Rmf2ProjectV5 project = Rmf2EsmV5Tests.CompileProject("vectors", group.Key, [group.Key], false, (group.Key, source.ToString()));
            string directory = Rmf2EsmV5Tests.Write(TranslationOutputRenderer.RenderRmf2V5EsmModules(project));
            try
            {
                var cases = new JsonArray(vectors.Select((vector, index) => (JsonNode)new JsonObject
                {
                    ["id"] = vector.Id, ["key"] = "vector" + index, ["arguments"] = vector.Arguments.DeepClone(), ["expected"] = vector.Expected,
                }).ToArray());
                string script = Path.Combine(directory, "selection-vectors.mjs");
                File.WriteAllText(script, """
                    import { m } from "./vectors.esm-v5/messages.js";
                    import { decimal } from "./vectors.esm-v5/runtime.js";
                    const locale =
                    """ + JsonSerializer.Serialize(group.Key) + ";\nconst cases = " + cases.ToJsonString() + ";\n" + """
                    for (const item of cases) {
                      const inputs = Object.fromEntries(Object.entries(item.arguments).map(([name, value]) => [name, value.type === "int64" ? BigInt(value.value) : decimal(value.value)]));
                      const actual = m[item.key](inputs, { locale });
                      if (actual !== item.expected) throw new Error(`${item.id}: expected ${JSON.stringify(item.expected)}, actual ${JSON.stringify(actual)}`);
                    }
                    """, new UTF8Encoding(false));
                Rmf2EsmV5Tests.Run("bun", [script], directory);
            }
            finally { Directory.Delete(directory, true); }
        }
    }
}
