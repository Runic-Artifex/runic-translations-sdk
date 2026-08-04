using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace RunicTextResources.Build.Tests;

internal static class WaveCAssetManifestTests
{
    public static void Register(TestRunner runner)
    {
        runner.Add("Wave C asset manifest inventories every selected non-C# output", SelectedOutputCombinations);
        runner.Add("Wave C asset manifest v1 hashes exact generated UTF-8 bytes", ExactBytesAndSchema);
        runner.Add("Wave C asset manifest is stable when document enumeration reverses", ReverseEnumerationIsStable);
        runner.Add("Wave C asset manifest excludes hostile metadata and unsafe output identities", HostileInputsAreRejectedOrContained);
    }

    private static void SelectedOutputCombinations()
    {
        string corpusRoot = RepositoryPaths.Resolve("spec", "corpus", "wave-c");
        using JsonDocument index = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(corpusRoot, "index.json")));
        Assert.Equal(1, index.RootElement.GetProperty("formatVersion").GetInt32());
        Assert.Equal("RunicTextResources.WaveC.Conformance", index.RootElement.GetProperty("identity").GetString());
        JsonElement suite = index.RootElement.GetProperty("suites").EnumerateArray().Single();
        Assert.Equal("asset-manifest", suite.GetProperty("id").GetString());
        string fixturePath = Path.Combine(corpusRoot, suite.GetProperty("fixture").GetString()!);
        using JsonDocument fixture = JsonDocument.Parse(File.ReadAllBytes(fixturePath));
        JsonElement root = fixture.RootElement;
        Assert.Equal(1, root.GetProperty("formatVersion").GetInt32());
        Assert.Equal("RunicTextResources.WaveC.AssetManifest", root.GetProperty("identity").GetString());
        Assert.Equal(1, root.GetProperty("assetManifestVersion").GetInt32());
        Assert.Equal(suite.GetProperty("caseCount").GetInt32(), root.GetProperty("cases").GetArrayLength());
        Assert.True(File.Exists(Path.GetFullPath(root.GetProperty("schema").GetString()!, Path.GetDirectoryName(fixturePath)!)), "Frozen Asset Manifest v1 schema is missing.");

        foreach (JsonElement testCase in root.GetProperty("cases").EnumerateArray())
        {
            using TemporaryDirectory temporary = new();
            TestFixture.CopyMinimal(temporary);
            var arguments = new List<string> { "generate", "--catalog", "manifest.json", "--documents", "en.json", "--output", "generated" };
            arguments.AddRange(testCase.GetProperty("emit").EnumerateArray().Select(static value => value.GetString()!));
            ProcessResult result = TestFixture.RunTool(temporary, arguments.ToArray());
            Assert.Equal(0, result.ExitCode, result.Combined);

            string manifestPath = temporary.Resolve("generated", "minimal.asset-manifest-v1.json");
            bool expectsManifest = testCase.GetProperty("assetManifest").GetBoolean();
            Assert.Equal(expectsManifest, File.Exists(manifestPath), testCase.GetProperty("id").GetString() + " asset-manifest selection.");
            if (!expectsManifest)
                continue;
            using JsonDocument manifest = JsonDocument.Parse(File.ReadAllBytes(manifestPath));
            string[] expected = testCase.GetProperty("assets").EnumerateArray().Select(static value => value.GetString()!).ToArray();
            string[] actual = manifest.RootElement.GetProperty("assets").EnumerateArray().Select(static value => value.GetProperty("path").GetString()!).ToArray();
            Assert.Equal(string.Join('|', expected), string.Join('|', actual), testCase.GetProperty("id").GetString());
            Assert.False(actual.Contains("minimal.asset-manifest-v1.json", StringComparer.Ordinal), "Asset manifest listed itself.");
        }
    }

    private static void ExactBytesAndSchema()
    {
        using TemporaryDirectory temporary = new();
        TestFixture.CopyMinimal(temporary);
        ProcessResult result = TestFixture.Generate(temporary);
        Assert.Equal(0, result.ExitCode, result.Combined);

        string output = temporary.Resolve("generated");
        using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(output, "minimal.asset-manifest-v1.json")));
        ValidateAssetManifestV1(document.RootElement, "minimal");
        JsonElement[] assets = document.RootElement.GetProperty("assets").EnumerateArray().ToArray();
        string[] paths = assets.Select(static asset => asset.GetProperty("path").GetString()!).ToArray();
        Assert.Equal(string.Join('|', paths.Order(StringComparer.Ordinal)), string.Join('|', paths), "Asset paths were not ordinal-sorted.");
        foreach (JsonElement asset in assets)
        {
            string relativePath = asset.GetProperty("path").GetString()!;
            byte[] bytes = File.ReadAllBytes(Path.Combine(output, relativePath));
            Assert.Equal(bytes.LongLength, asset.GetProperty("byteLength").GetInt64(), relativePath + " byte length");
            Assert.Equal(Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), asset.GetProperty("sha256").GetString(), relativePath + " SHA-256");
            bool isLocale = relativePath.EndsWith(".locale-v1.json", StringComparison.Ordinal);
            Assert.Equal(isLocale ? "application/json" : relativePath.EndsWith(".d.ts", StringComparison.Ordinal) ? "text/typescript" : "application/json", asset.GetProperty("mediaType").GetString(), relativePath + " media type");
            if (isLocale)
                Assert.Equal("en", asset.GetProperty("locale").GetString(), relativePath + " locale");
            else
                Assert.Equal(JsonValueKind.Null, asset.GetProperty("locale").ValueKind, relativePath + " locale");
        }
    }

    private static void ReverseEnumerationIsStable()
    {
        using TemporaryDirectory temporary = new();
        WriteMultiLocaleSources(temporary);
        ProcessResult first = TestFixture.RunTool(temporary, "generate", "--catalog", "manifest.json", "--documents", "en.json", "de.json", "--output", "forward");
        ProcessResult reversed = TestFixture.RunTool(temporary, "generate", "--catalog", "manifest.json", "--documents", "de.json", "en.json", "--output", "reversed");
        Assert.Equal(0, first.ExitCode, first.Combined);
        Assert.Equal(0, reversed.ExitCode, reversed.Combined);
        string[] paths = TestFixture.RelativeFiles(temporary.Resolve("forward"));
        Assert.Equal(string.Join('|', paths), string.Join('|', TestFixture.RelativeFiles(temporary.Resolve("reversed"))));
        foreach (string path in paths)
            Assert.FileBytesEqual(Path.Combine(temporary.Resolve("forward"), path), Path.Combine(temporary.Resolve("reversed"), path));
    }

    private static void HostileInputsAreRejectedOrContained()
    {
        using (TemporaryDirectory temporary = new())
        {
            File.WriteAllText(temporary.Resolve("manifest.json"), Manifest, new UTF8Encoding(false));
            const string hostileMetadata = "secret\\u0001metadata";
            string document = "{\"schemaVersion\":1,\"catalog\":\"app\",\"locale\":\"en\",\"layer\":\"base\",\"resources\":{\"Message\":{\"$value\":\"Hello\",\"$description\":\"" + hostileMetadata + "\"}}}";
            File.WriteAllText(temporary.Resolve("en.json"), document, new UTF8Encoding(false));
            ProcessResult result = TestFixture.RunTool(temporary, "generate", "--catalog", "manifest.json", "--documents", "en.json", "--output", "generated", "--emit-json");
            Assert.Equal(0, result.ExitCode, result.Combined);
            string manifest = File.ReadAllText(temporary.Resolve("generated", "app.asset-manifest-v1.json"), Encoding.UTF8);
            Assert.False(manifest.Contains("secret", StringComparison.Ordinal), "Hostile metadata leaked into the asset manifest.");
            Assert.False(manifest.Any(static value => value < ' '), "Control Unicode leaked into the asset manifest.");
            Assert.False(manifest.Contains("..", StringComparison.Ordinal), "Unsafe path syntax reached the asset manifest.");
        }

        using (TemporaryDirectory temporary = new())
        {
            File.WriteAllText(temporary.Resolve("manifest.json"), Manifest, new UTF8Encoding(false));
            File.WriteAllText(temporary.Resolve("en.json"), "{\"schemaVersion\":1,\"catalog\":\"app\",\"locale\":\"en\",\"layer\":\"base\",\"resources\":{\"Message\":{\"$value\":\"Hello\",\"$description\":\"secret\\uD800\"}}}", new UTF8Encoding(false));
            ProcessResult result = TestFixture.RunTool(temporary, "generate", "--catalog", "manifest.json", "--documents", "en.json", "--output", "generated", "--emit-json");
            Assert.Equal(1, result.ExitCode, result.Combined);
            Assert.Contains("RTR0001", result.StandardError);
            Assert.False(result.StandardError.Contains("secret", StringComparison.Ordinal), "Hostile metadata leaked into diagnostics.");
            Assert.False(Directory.Exists(temporary.Resolve("generated")), "Unpaired surrogate created output.");
        }

        using (TemporaryDirectory temporary = new())
        {
            File.WriteAllText(temporary.Resolve("manifest.json"), Manifest.Replace("\"app\"", "\"app/../escape\"", StringComparison.Ordinal), new UTF8Encoding(false));
            File.WriteAllText(temporary.Resolve("en.json"), "{\"schemaVersion\":1,\"catalog\":\"app/../escape\",\"locale\":\"en\",\"layer\":\"base\",\"resources\":{\"Message\":\"Hello\"}}", new UTF8Encoding(false));
            ProcessResult result = TestFixture.RunTool(temporary, "generate", "--catalog", "manifest.json", "--documents", "en.json", "--output", "generated", "--emit-json");
            Assert.Equal(1, result.ExitCode, result.Combined);
            Assert.Contains("RTR0006", result.StandardError);
            Assert.False(result.StandardError.Contains("app/../escape", StringComparison.Ordinal), "Unsafe catalog identity leaked into diagnostics.");
            Assert.False(Directory.Exists(temporary.Resolve("generated")), "Unsafe catalog identity created an output directory.");
        }
    }

    private static void ValidateAssetManifestV1(JsonElement root, string catalog)
    {
        string[] expectedProperties = ["assetManifestVersion", "catalog", "assets"];
        string[] actualProperties = root.EnumerateObject().Select(static property => property.Name).ToArray();
        Assert.Equal(string.Join('|', expectedProperties), string.Join('|', actualProperties), "Asset Manifest v1 root fields");
        Assert.Equal(1, root.GetProperty("assetManifestVersion").GetInt32());
        Assert.Equal(catalog, root.GetProperty("catalog").GetString());
        foreach (JsonElement asset in root.GetProperty("assets").EnumerateArray())
        {
            string[] expectedAssetProperties = ["path", "sha256", "byteLength", "mediaType", "locale"];
            Assert.Equal(string.Join('|', expectedAssetProperties), string.Join('|', asset.EnumerateObject().Select(static property => property.Name)), "Asset Manifest v1 asset fields");
            string path = asset.GetProperty("path").GetString()!;
            Assert.True(path.Length != 0 && path[0] != '/' && !path.Contains("..", StringComparison.Ordinal), "Asset path failed v1 containment shape.");
            string hash = asset.GetProperty("sha256").GetString()!;
            Assert.True(hash.Length == 64 && hash.All(static value => value is >= '0' and <= '9' or >= 'a' and <= 'f'), "Asset SHA-256 failed v1 lowercase shape.");
            Assert.True(asset.GetProperty("byteLength").GetInt64() >= 0, "Asset length failed v1 range.");
            Assert.True(asset.GetProperty("mediaType").GetString()!.Contains('/'), "Asset media type failed v1 shape.");
            Assert.True(asset.GetProperty("locale").ValueKind is JsonValueKind.String or JsonValueKind.Null, "Asset locale failed v1 shape.");
        }
    }

    private static void WriteMultiLocaleSources(TemporaryDirectory temporary)
    {
        File.WriteAllText(temporary.Resolve("manifest.json"), Manifest.Replace("[{\"tag\":\"en\"}]", "[{\"tag\":\"en\"},{\"tag\":\"de\",\"fallback\":\"en\"}]", StringComparison.Ordinal), new UTF8Encoding(false));
        File.WriteAllText(temporary.Resolve("en.json"), "{\"schemaVersion\":1,\"catalog\":\"app\",\"locale\":\"en\",\"layer\":\"base\",\"resources\":{\"Message\":\"Hello\"}}", new UTF8Encoding(false));
        File.WriteAllText(temporary.Resolve("de.json"), "{\"schemaVersion\":1,\"catalog\":\"app\",\"locale\":\"de\",\"layer\":\"base\",\"resources\":{\"Message\":\"Hallo\"}}", new UTF8Encoding(false));
    }

    private const string Manifest = "{\"schemaVersion\":1,\"catalog\":\"app\",\"code\":{\"namespace\":\"Tests\",\"className\":\"AppText\"},\"defaultLocale\":\"en\",\"locales\":[{\"tag\":\"en\"}],\"layers\":[{\"name\":\"base\",\"priority\":0}]}";
}
