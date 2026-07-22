using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace WebUIToolkit.TextResources.Build.Tests;

internal static class WaveBCorpusTests
{
    public static void Register(TestRunner runner)
    {
        runner.Add("Wave B corpus index is closed and every generation case executes once", CorpusIsClosedAndGenerationCasesExecute);
    }

    private static void CorpusIsClosedAndGenerationCasesExecute()
    {
        string root = RepositoryPaths.Resolve("spec", "text-resources", "corpus", "wave-b");
        using JsonDocument indexDocument = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root, "index.json")));
        JsonElement index = indexDocument.RootElement;
        Assert.Equal(1, index.GetProperty("formatVersion").GetInt32());
        var suiteIds = new HashSet<string>(StringComparer.Ordinal);
        var suitePaths = new HashSet<string>(StringComparer.Ordinal);
        var caseIds = new HashSet<string>(StringComparer.Ordinal);
        JsonElement generationCases = default;
        int indexedCases = 0;

        foreach (JsonElement suite in index.GetProperty("suites").EnumerateArray())
        {
            string suiteId = suite.GetProperty("id").GetString()!;
            Assert.True(suiteIds.Add(suiteId), $"Duplicate Wave B suite ID: {suiteId}");
            string fixturePath = Path.Combine(root, suite.GetProperty("fixture").GetString()!);
            Assert.True(suitePaths.Add(Path.GetFullPath(fixturePath)), $"Duplicate Wave B suite fixture path: {fixturePath}");
            Assert.True(File.Exists(fixturePath), $"Missing Wave B suite fixture: {fixturePath}");
            using JsonDocument fixture = JsonDocument.Parse(File.ReadAllBytes(fixturePath));
            JsonElement cases = fixture.RootElement.GetProperty("cases");
            int expectedCount = suite.GetProperty("caseCount").GetInt32();
            Assert.Equal(expectedCount, cases.GetArrayLength(), $"Case count for {suiteId}");
            indexedCases += expectedCount;
            foreach (JsonElement item in cases.EnumerateArray())
            {
                string id = item.GetProperty("id").GetString()!;
                Assert.True(caseIds.Add(id), $"Duplicate Wave B case ID: {id}");
            }

            if (suiteId == "generation")
            {
                generationCases = cases.Clone();
            }
        }

        Assert.Equal(indexedCases, caseIds.Count, "Wave B indexed case IDs were not globally unique");
        var schemaPairs = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsonElement schemaFixture in index.GetProperty("schemaFixtures").EnumerateArray())
        {
            string fixturePath = Path.GetFullPath(schemaFixture.GetProperty("path").GetString()!, root);
            string schemaPath = Path.GetFullPath(schemaFixture.GetProperty("schema").GetString()!, root);
            Assert.True(schemaPairs.Add(fixturePath + "|" + schemaPath), "Duplicate indexed schema fixture/schema pair.");
            Assert.True(File.Exists(fixturePath), "Indexed schema fixture path is missing.");
            Assert.True(File.Exists(schemaPath), "Indexed schema path is missing.");
        }

        Assert.Equal(JsonValueKind.Array, generationCases.ValueKind, "Generation suite was not indexed");
        using GenerationHarness harness = new();
        var consumed = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsonElement item in generationCases.EnumerateArray())
        {
            string id = item.GetProperty("id").GetString()!;
            Assert.True(consumed.Add(id), $"Generation case was dispatched twice: {id}");
            ExecuteGenerationCase(root, harness, id, item);
        }

        Assert.Equal(generationCases.GetArrayLength(), consumed.Count, "A generation corpus case was silently unconsumed");
    }

    private static void ExecuteGenerationCase(string corpusRoot, GenerationHarness harness, string id, JsonElement item)
    {
        switch (id)
        {
            case "canonical-locale-artifact-bytes":
                AssertCanonicalBytes(corpusRoot, item);
                break;
            case "generated-csharp-hint-order":
                Assert.Equal(
                    JoinStrings(item.GetProperty("expectedHintNames")),
                    string.Join('|', TestFixture.RelativeFiles(harness.Output).Where(path => path.EndsWith(".g.cs", StringComparison.Ordinal))));
                break;
            case "typed-accessor-parameter-order":
                string accessors = File.ReadAllText(Path.Combine(harness.Output, "AppText.Accessors.g.cs"), Encoding.UTF8);
                Assert.Contains("Deleted(long count, string folder)", accessors);
                break;
            case "locale-artifact-file-order":
                string actualLocales = string.Join('|', TestFixture.RelativeFiles(harness.Output).Where(path => path.Contains(".locale-v1.json", StringComparison.Ordinal)));
                Assert.Equal(JoinStrings(item.GetProperty("expectedRelativePaths")), actualLocales);
                Assert.True(File.Exists(Path.Combine(harness.Output, item.GetProperty("expectedTemplateManifestPath").GetString()!)), "Template edge path is missing.");
                Assert.True(File.Exists(Path.Combine(harness.Output, item.GetProperty("expectedTypeScriptDeclarationPath").GetString()!)), "TypeScript edge path is missing.");
                break;
            case "template-manifest-is-value-free":
                AssertTemplateIsValueFree(harness, item);
                break;
            case "output-path-contained":
                AssertResolvedPath(item, accepted: true);
                break;
            case "output-path-parent-escape":
                AssertResolvedPath(item, accepted: false);
                break;
            case "verify-difference-order":
                AssertVerifyDifferences(harness, item);
                break;
            case "json-string-escaping":
                string expected = item.GetProperty("expectedJsonString").GetString()!;
                string locale = File.ReadAllText(Path.Combine(harness.Output, "app.en.locale-v1.json"), Encoding.UTF8);
                Assert.Contains("\"pattern\":" + expected, locale);
                break;
            case "canonical-output-independent-of-environment":
                harness.AssertSecondGenerationMatches();
                break;
            default:
                throw new InvalidOperationException($"Unmapped Wave B generation case: {id}");
        }
    }

    private static void AssertCanonicalBytes(string corpusRoot, JsonElement item)
    {
        byte[] expected = Encoding.UTF8.GetBytes(item.GetProperty("expectedCanonicalUtf8").GetString()!);
        JsonElement expectedMetadata = item.GetProperty("expected");
        Assert.Equal(expectedMetadata.GetProperty("byteLength").GetInt32(), expected.Length);
        Assert.False(expected.AsSpan().StartsWith(new byte[] { 0xef, 0xbb, 0xbf }), "Canonical corpus bytes have a BOM.");
        Assert.True(expected[^1] != (byte)'\n', "Canonical corpus bytes have a terminal newline.");
        Assert.Equal(expectedMetadata.GetProperty("sha256").GetString(), Convert.ToHexString(SHA256.HashData(expected)).ToLowerInvariant());

        string input = Path.GetFullPath(item.GetProperty("input").GetString()!, corpusRoot);
        using JsonDocument source = JsonDocument.Parse(File.ReadAllBytes(input));
        using JsonDocument expectedDocument = JsonDocument.Parse(expected);
        Assert.True(JsonEquivalent(source.RootElement, expectedDocument.RootElement), "Canonical expected bytes do not match their indexed source payload.");
    }

    private static bool JsonEquivalent(JsonElement left, JsonElement right)
    {
        if (left.ValueKind != right.ValueKind) return false;
        switch (left.ValueKind)
        {
            case JsonValueKind.Object:
                JsonProperty[] leftProperties = left.EnumerateObject().ToArray();
                JsonProperty[] rightProperties = right.EnumerateObject().ToArray();
                if (leftProperties.Length != rightProperties.Length) return false;
                for (int i = 0; i < leftProperties.Length; i++)
                {
                    if (!string.Equals(leftProperties[i].Name, rightProperties[i].Name, StringComparison.Ordinal) ||
                        !JsonEquivalent(leftProperties[i].Value, rightProperties[i].Value)) return false;
                }
                return true;
            case JsonValueKind.Array:
                JsonElement[] leftItems = left.EnumerateArray().ToArray();
                JsonElement[] rightItems = right.EnumerateArray().ToArray();
                if (leftItems.Length != rightItems.Length) return false;
                for (int i = 0; i < leftItems.Length; i++) if (!JsonEquivalent(leftItems[i], rightItems[i])) return false;
                return true;
            case JsonValueKind.String:
                return string.Equals(left.GetString(), right.GetString(), StringComparison.Ordinal);
            case JsonValueKind.Number:
                return string.Equals(left.GetRawText(), right.GetRawText(), StringComparison.Ordinal);
            case JsonValueKind.True:
            case JsonValueKind.False:
            case JsonValueKind.Null:
                return true;
            default:
                return string.Equals(left.GetRawText(), right.GetRawText(), StringComparison.Ordinal);
        }
    }

    private static void AssertTemplateIsValueFree(GenerationHarness harness, JsonElement item)
    {
        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(harness.Output, "app.template-manifest-v1.json")));
        var forbidden = item.GetProperty("forbiddenMembers").EnumerateArray().Select(value => value.GetString()!).ToHashSet(StringComparer.Ordinal);
        AssertNoForbiddenProperties(manifest.RootElement, forbidden);
        Assert.Equal(item.GetProperty("expectedManifestVersion").GetInt32(), manifest.RootElement.GetProperty("manifestVersion").GetInt32());
    }

    private static void AssertNoForbiddenProperties(JsonElement element, HashSet<string> forbidden)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty property in element.EnumerateObject())
            {
                Assert.False(forbidden.Contains(property.Name), $"Template manifest exposed forbidden member '{property.Name}'.");
                AssertNoForbiddenProperties(property.Value, forbidden);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement child in element.EnumerateArray())
            {
                AssertNoForbiddenProperties(child, forbidden);
            }
        }
    }

    private static void AssertResolvedPath(JsonElement item, bool accepted)
    {
        string root = Path.GetFullPath(item.GetProperty("outputRoot").GetString()!, Path.GetTempPath());
        string relative = item.GetProperty("relativePath").GetString()!;
        string resolved = Path.GetFullPath(relative.Replace('/', Path.DirectorySeparatorChar), root);
        string prefix = root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        bool contained = resolved.StartsWith(prefix, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
        Assert.Equal(accepted, contained);
        Assert.Equal(accepted, item.GetProperty("expected").GetProperty("accepted").GetBoolean());
    }

    private static void AssertVerifyDifferences(GenerationHarness harness, JsonElement item)
    {
        string verifyOutput = harness.Temporary.Resolve("verify-corpus");
        Directory.CreateDirectory(verifyOutput);
        File.Copy(Path.Combine(harness.Output, "app.en.locale-v1.json"), Path.Combine(verifyOutput, "app.en.locale-v1.json"));
        File.AppendAllText(Path.Combine(verifyOutput, "app.en.locale-v1.json"), "changed", Encoding.UTF8);
        File.Copy(Path.Combine(harness.Output, "app.template-manifest-v1.json"), Path.Combine(verifyOutput, "app.template-manifest-v1.json"));
        ProcessResult result = harness.RunVerifyJson(verifyOutput);
        JsonElement expected = item.GetProperty("expected");
        Assert.Equal(expected.GetProperty("exitCode").GetInt32(), result.ExitCode, result.Combined);
        Assert.Contains("changed: " + expected.GetProperty("changed")[0].GetString(), result.StandardError);
        Assert.Contains("extra: " + expected.GetProperty("extra")[0].GetString(), result.StandardError);
        Assert.Contains("missing: " + expected.GetProperty("missing")[0].GetString(), result.StandardError);
    }

    private static string JoinStrings(JsonElement array) => string.Join('|', array.EnumerateArray().Select(value => value.GetString()));

    private sealed class GenerationHarness : IDisposable
    {
        public GenerationHarness()
        {
            Temporary = new TemporaryDirectory();
            WriteSources();
            Output = Temporary.Resolve("generated");
            ProcessResult result = RunGenerate(Output, reverseDocuments: false);
            Assert.Equal(0, result.ExitCode, result.Combined);
        }

        public TemporaryDirectory Temporary { get; }
        public string Output { get; }

        public ProcessResult RunVerifyJson(string output) => TestFixture.RunTool(
            Temporary,
            "verify", "--catalog", "manifest.json", "--documents", "en.json", "de.json", "zh.json",
            "--output", output, "--emit-json");

        public void AssertSecondGenerationMatches()
        {
            string second = Temporary.Resolve("generated-second");
            ProcessResult result = RunGenerate(second, reverseDocuments: true);
            Assert.Equal(0, result.ExitCode, result.Combined);
            string[] paths = TestFixture.RelativeFiles(Output);
            Assert.Equal(string.Join('|', paths), string.Join('|', TestFixture.RelativeFiles(second)));
            foreach (string path in paths)
            {
                Assert.FileBytesEqual(Path.Combine(Output, path), Path.Combine(second, path));
            }
        }

        public void Dispose() => Temporary.Dispose();

        private ProcessResult RunGenerate(string output, bool reverseDocuments)
        {
            string[] documents = reverseDocuments ? ["zh.json", "de.json", "en.json"] : ["en.json", "de.json", "zh.json"];
            return TestFixture.RunTool(
                Temporary,
                "generate", "--catalog", "manifest.json", "--documents", documents[0], documents[1], documents[2],
                "--output", output);
        }

        private void WriteSources()
        {
            const string manifest = """
                {"schemaVersion":1,"catalog":"app","code":{"namespace":"WebUIToolkit.Examples.Localization","className":"AppText"},"defaultLocale":"en","locales":[{"tag":"zh-Hant-TW","fallback":"en"},{"tag":"de","fallback":"en"},{"tag":"en"}],"layers":[{"name":"base","priority":0}],"validation":{"translationCompleteness":"allow"}}
                """;
            string escaping = "quote=\" slash=/ reverse=\\ control=\n unicode=é separator=\u2028";
            string en = JsonSerializer.Serialize(new
            {
                schemaVersion = 1,
                catalog = "app",
                locale = "en",
                layer = "base",
                resources = new Dictionary<string, object>
                {
                    ["Common"] = new Dictionary<string, object> { ["Save"] = "Save" },
                    ["Files"] = new Dictionary<string, object>
                    {
                        ["Deleted"] = new Dictionary<string, object>
                        {
                            ["$value"] = "{count} files from {folder}.",
                            ["$placeholders"] = new Dictionary<string, object>
                            {
                                ["folder"] = new { type = "string" },
                                ["count"] = new { type = "int", format = "grouped" },
                            },
                        },
                    },
                    ["Escaping"] = escaping,
                },
            });
            string de = "{\"schemaVersion\":1,\"catalog\":\"app\",\"locale\":\"de\",\"layer\":\"base\",\"resources\":{\"Common\":{\"Save\":\"Speichern\"}}}";
            string zh = "{\"schemaVersion\":1,\"catalog\":\"app\",\"locale\":\"zh-Hant-TW\",\"layer\":\"base\",\"resources\":{\"Common\":{\"Save\":\"儲存\"}}}";
            File.WriteAllText(Temporary.Resolve("manifest.json"), manifest, new UTF8Encoding(false));
            File.WriteAllText(Temporary.Resolve("en.json"), en, new UTF8Encoding(false));
            File.WriteAllText(Temporary.Resolve("de.json"), de, new UTF8Encoding(false));
            File.WriteAllText(Temporary.Resolve("zh.json"), zh, new UTF8Encoding(false));
        }
    }
}
