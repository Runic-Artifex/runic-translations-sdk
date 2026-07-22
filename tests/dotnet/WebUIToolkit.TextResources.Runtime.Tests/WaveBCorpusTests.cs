using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace WebUIToolkit.TextResources.Runtime.Tests;

internal static class WaveBCorpusTests
{
    private const string Fingerprint = "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private static readonly string WaveBRoot = FindWaveBRoot();

    public static void Register(TestRunner runner)
    {
        runner.Add("wave-b corpus index integrity and routing", () => ValidateIndexAndRoutes(runner));
        using JsonDocument formatting = Read("formatting.json");
        foreach (JsonElement item in formatting.RootElement.GetProperty("cases").EnumerateArray())
        {
            string id = item.GetProperty("id").GetString()!;
            string json = item.GetRawText();
            runner.Add("wave-b formatting corpus " + id, () => ExecuteFormatting(json));
        }

        using JsonDocument packs = Read("external-packs.json");
        foreach (JsonElement item in packs.RootElement.GetProperty("cases").EnumerateArray())
        {
            if (!item.TryGetProperty("document", out _) || item.TryGetProperty("cancellation", out _)) continue;
            string id = item.GetProperty("id").GetString()!;
            string json = item.GetRawText();
            runner.Add("wave-b external-pack corpus " + id, () => ExecuteExternalPack(json));
        }
    }

    private static void ValidateIndexAndRoutes(TestRunner runner)
    {
        using JsonDocument index = Read("index.json");
        Assert.Equal(1, index.RootElement.GetProperty("formatVersion").GetInt32());
        HashSet<string> suiteIds = new(StringComparer.Ordinal);
        HashSet<string> caseIds = new(StringComparer.Ordinal);
        Dictionary<string, int> counts = new(StringComparer.Ordinal);
        foreach (JsonElement suite in index.RootElement.GetProperty("suites").EnumerateArray())
        {
            string suiteId = suite.GetProperty("id").GetString()!;
            Assert.True(suiteIds.Add(suiteId), "Duplicate Wave B suite ID " + suiteId);
            string fixture = suite.GetProperty("fixture").GetString()!;
            string fixturePath = Resolve(fixture);
            Assert.True(File.Exists(fixturePath), "Missing suite fixture " + fixture);
            if (suite.TryGetProperty("schema", out JsonElement schema))
                Assert.True(File.Exists(Path.GetFullPath(Path.Combine(WaveBRoot, schema.GetString()!))), "Missing suite schema.");
            using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(fixturePath));
            int actual = document.RootElement.GetProperty("cases").GetArrayLength();
            int declared = suite.GetProperty("caseCount").GetInt32();
            Assert.Equal(declared, actual, "Suite count drift for " + suiteId);
            counts.Add(suiteId, actual);
            foreach (JsonElement item in document.RootElement.GetProperty("cases").EnumerateArray())
                Assert.True(caseIds.Add(suiteId + "/" + item.GetProperty("id").GetString()), "Duplicate corpus case ID.");
        }

        foreach (JsonElement fixture in index.RootElement.GetProperty("schemaFixtures").EnumerateArray())
        {
            Assert.True(File.Exists(Resolve(fixture.GetProperty("path").GetString()!)), "Missing schema fixture path.");
            Assert.True(File.Exists(Path.GetFullPath(Path.Combine(WaveBRoot, fixture.GetProperty("schema").GetString()!))), "Missing schema fixture schema.");
        }

        Assert.Equal(19, counts["formatting"]);
        Assert.Equal(18, counts["external-packs"]);
        Assert.Equal(8, counts["runtime"]);
        Assert.Equal(10, counts["generation"]);

        Dictionary<string, string> runtimeRoutes = new(StringComparer.Ordinal)
        {
            ["fallback-is-per-key"] = "snapshot resolves fallback values",
            ["unsupported-parents-then-default"] = "provider resolves parents then default",
            ["successful-swap-linearization"] = "manager swaps atomically and raises exactly once",
            ["failed-swap-preserves-current"] = "manager provider failure preserves old snapshot",
            ["cancelled-swap-preserves-current"] = "manager canceled transition preserves current",
            ["same-locale-load-coalesces"] = "provider caches and coalesces canonical locale",
            ["one-waiter-cancellation-does-not-poison-shared-load"] = "provider isolates a canceled coalesced caller",
            ["accessor-observes-current-snapshot"] = "manager concurrent transitions have exactly-once event chain",
        };
        ValidateRoutes("runtime.json", runtimeRoutes, runner);

        Dictionary<string, string> generatedPackRoutes = new(StringComparer.Ordinal)
        {
            ["limit-raw-bytes"] = "external pack enforces document limit",
            ["limit-json-depth"] = "external pack enforces depth limit",
            ["limit-message-count"] = "external pack enforces message limit",
            ["limit-pattern-utf8-bytes"] = "external pack enforces pattern byte limit",
            ["limit-argument-count"] = "external pack enforces argument limit",
            ["cancelled-pack-is-not-cached"] = "external pack honors cancellation",
        };
        using JsonDocument external = Read("external-packs.json");
        foreach (JsonElement item in external.RootElement.GetProperty("cases").EnumerateArray())
        {
            if (item.TryGetProperty("document", out _) && !item.TryGetProperty("cancellation", out _)) continue;
            string id = item.GetProperty("id").GetString()!;
            Assert.True(generatedPackRoutes.TryGetValue(id, out string? route) && runner.HasTest(route),
                "Unrouted generated external-pack case " + id);
            string expectedReason = item.GetProperty("expected").GetProperty("reason").GetString()!;
            string routedReason = id == "cancelled-pack-is-not-cached" ? "cancelled" : "limitExceeded";
            Assert.Equal(routedReason, expectedReason, "Generated external-pack reason drift for " + id);
        }

        using JsonDocument generation = Read("generation.json");
        string[] generationIds = generation.RootElement.GetProperty("cases").EnumerateArray()
            .Select(item => item.GetProperty("id").GetString()!).ToArray();
        Assert.Equal(10, generationIds.Distinct(StringComparer.Ordinal).Count(),
            "Generation cases must route exactly once to WebUIToolkit.TextResources.Build.Tests.");
    }

    private static void ValidateRoutes(string fixture, Dictionary<string, string> routes, TestRunner runner)
    {
        using JsonDocument document = Read(fixture);
        foreach (JsonElement item in document.RootElement.GetProperty("cases").EnumerateArray())
        {
            string id = item.GetProperty("id").GetString()!;
            Assert.True(routes.TryGetValue(id, out string? route), "Unrouted corpus case " + id);
            Assert.True(runner.HasTest(route!), "Missing executable route " + route);
        }
        Assert.Equal(document.RootElement.GetProperty("cases").GetArrayLength(), routes.Count);
    }

    private static void ExecuteFormatting(string caseJson)
    {
        using JsonDocument document = JsonDocument.Parse(caseJson);
        JsonElement item = document.RootElement;
        string locale = item.GetProperty("locale").GetString()!;
        string pattern = item.GetProperty("pattern").GetString()!;
        TextArgument[] arguments = item.GetProperty("arguments").EnumerateArray().Select(ParseArgument).ToArray();
        JsonElement expected = item.GetProperty("expected");
        string kind = expected.GetProperty("kind").GetString()!;
        if (kind == "error")
        {
            if (item.TryGetProperty("contract", out JsonElement contract))
            {
                TextResourcePlaceholderDescriptor[] descriptors = contract.EnumerateArray().Select(ParseDescriptor).ToArray();
                CompiledTextResourceCatalog catalog = new("app", "en", [new CompiledTextResourceDefinition("Message", descriptors)],
                    [new CompiledTextResourceLocale("en", null, [new CompiledTextResourceValue(0, pattern)])]);
                Assert.Throws<TextResourceFormatException>(() => new CompiledTextResourceSnapshot(catalog, "en").Format(
                    new TextResourceKey("app", 0, "Message"), arguments));
            }
            else
            {
                Assert.Throws<TextResourceFormatException>(() => TextPatternFormatter.Format(pattern, arguments, locale));
            }
            return;
        }

        string actual = TextPatternFormatter.Format(pattern, arguments, locale);
        if (kind == "exact")
        {
            Assert.Equal(expected.GetProperty("text").GetString(), actual);
            return;
        }

        string id = item.GetProperty("id").GetString()!;
        CultureInfo culture = CultureInfo.GetCultureInfo(locale);
        string semantic = id switch
        {
            "grouped-number-semantic" => 1234.5m.ToString("#,0.############################", culture),
            "fixed2-number-semantic" => 12.5m.ToString("F2", culture),
            "percent1-number-semantic" => 0.125m.ToString("P1", culture),
            "date-long-semantic" => new DateOnly(2024, 2, 29).ToString("D", culture),
            _ => throw new InvalidOperationException("Unknown locale-semantic formatting case " + id),
        };
        Assert.Equal(semantic, actual);
    }

    private static async Task ExecuteExternalPack(string caseJson)
    {
        using JsonDocument document = JsonDocument.Parse(caseJson);
        JsonElement item = document.RootElement;
        byte[] bytes = File.ReadAllBytes(Resolve(item.GetProperty("document").GetString()!));
        bool accepted = item.GetProperty("expected").GetProperty("accepted").GetBoolean();
        bool integrityAccepts = item.GetProperty("integrity").GetString() == "accept";
        try
        {
            VerifiedExternalTextResourcePack verified = await TextResourcePackLoader.VerifyAsync(
                new ExternalTextResourcePack(bytes), PackContract(), integrityVerifier: (content, token) => ValueTask.FromResult(integrityAccepts));
            Assert.True(accepted, "Rejected corpus pack was accepted: " + item.GetProperty("id").GetString());
            string[] expectedKeys = item.GetProperty("expected").GetProperty("overlayKeys").EnumerateArray().Select(value => value.GetString()!).ToArray();
            Assert.Equal(string.Join("|", expectedKeys), string.Join("|", verified.Messages.Select(message => message.Key.Name)));
        }
        catch (TextResourcePackException exception)
        {
            Assert.False(accepted, "Accepted corpus pack was rejected: " + item.GetProperty("id").GetString());
            string expectedReason = item.GetProperty("expected").GetProperty("reason").GetString()!;
            string actualReason = JsonNamingPolicy.CamelCase.ConvertName(TextResourcePackFailure.GetReason(exception).ToString());
            Assert.Equal(expectedReason, actualReason, "External-pack reason drift for " + item.GetProperty("id").GetString());
        }
    }

    private static TextResourcePackContract PackContract() => new(
        "app", "de", Fingerprint,
        [
            new TextResourcePackMessageContract(new TextResourceKey("app", 0, "Common.Save")),
            new TextResourcePackMessageContract(new TextResourceKey("app", 1, "Files.Deleted"),
                [
                    new TextResourcePackArgumentContract("count", TextArgumentType.Int, TextArgumentFormat.Grouped),
                    new TextResourcePackArgumentContract("folder", TextArgumentType.String, TextArgumentFormat.None),
                ]),
        ]);

    private static TextArgument ParseArgument(JsonElement item)
    {
        string name = item.GetProperty("name").GetString()!;
        string type = item.GetProperty("type").GetString()!;
        TextArgumentFormat format = Enum.Parse<TextArgumentFormat>(item.GetProperty("format").GetString()!, ignoreCase: true);
        JsonElement value = item.GetProperty("value");
        return type switch
        {
            "string" => new TextArgument(name, value.GetString()!),
            "int" => new TextArgument(name, long.Parse(value.GetString()!, CultureInfo.InvariantCulture), format),
            "number" => new TextArgument(name, decimal.Parse(value.GetString()!, CultureInfo.InvariantCulture), format),
            "bool" => new TextArgument(name, value.GetBoolean(), format),
            "date" => new TextArgument(name, DateOnly.Parse(value.GetString()!, CultureInfo.InvariantCulture), format),
            "time" => new TextArgument(name, TimeOnly.Parse(value.GetString()!, CultureInfo.InvariantCulture), format),
            "datetime" => new TextArgument(name, DateTimeOffset.Parse(value.GetString()!, CultureInfo.InvariantCulture), format),
            "guid" => new TextArgument(name, Guid.Parse(value.GetString()!), format),
            _ => throw new InvalidOperationException("Unknown corpus argument type " + type),
        };
    }

    private static TextResourcePlaceholderDescriptor ParseDescriptor(JsonElement item) => new(
        item.GetProperty("name").GetString()!,
        Enum.Parse<TextArgumentType>(item.GetProperty("type").GetString()!, ignoreCase: true),
        Enum.Parse<TextArgumentFormat>(item.GetProperty("format").GetString()!, ignoreCase: true));

    private static JsonDocument Read(string relativePath) => JsonDocument.Parse(File.ReadAllBytes(Resolve(relativePath)));

    private static string Resolve(string relativePath) => Path.GetFullPath(Path.Combine(WaveBRoot, relativePath));

    private static string FindWaveBRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string candidate = Path.Combine(directory.FullName, "spec", "text-resources", "corpus", "wave-b");
            if (File.Exists(Path.Combine(candidate, "index.json"))) return candidate;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate the Wave B text-resource corpus.");
    }
}
