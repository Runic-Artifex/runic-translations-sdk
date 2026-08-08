using System;
using System.Collections.Generic;
using System.Text.Json;

namespace RunicTextResources.Runtime.Tests;

internal static class TextResourceReferenceTests
{
    public static void Register(TestRunner runner)
    {
        runner.Add("text references validate versioned catalog identity and bounds", Validate);
        runner.Add("text references serialize the exact AOT-safe ESM wire contract", JsonWire);
    }

    private static void Validate()
    {
        const string fingerprint = "sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
        var reference = new TextResourceReference(
            "app",
            fingerprint,
            "Files.Deleted",
            new Dictionary<string, TextResourceReferenceArgument>(StringComparer.Ordinal)
            {
                ["count"] = new(TextArgumentType.Int, "3"),
                ["folder"] = new(TextArgumentType.String, "Archive"),
            },
            "3 files were deleted from Archive.");
        Assert.Equal(TextResourceTransport.Version, reference.Version);
        Assert.Equal(2, reference.Arguments.Count);
        reference.ValidateCatalog("app", fingerprint);
        Assert.Throws<TextResourceContractException>(() => reference.ValidateCatalog("other", fingerprint));
        Assert.Throws<ArgumentException>(() => _ = new TextResourceReference("app", "bad", "Key"));
        Assert.Throws<ArgumentException>(() => _ = new TextResourceReference("app", "sha256:" + new string('A', 64), "Key"));
        Assert.Throws<ArgumentException>(() => _ = new TextResourceReference("app", "sha256:" + new string('z', 64), "Key"));
    }

    private static void JsonWire()
    {
        const string fingerprint = "sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
        var reference = new TextResourceReference(
            "app", fingerprint, "Files.Deleted",
            new Dictionary<string, TextResourceReferenceArgument>(StringComparer.Ordinal)
            {
                ["folder"] = new(TextArgumentType.String, "Archive"),
                ["count"] = new(TextArgumentType.Int, "3"),
                ["confirmed"] = new(TextArgumentType.Bool, "true"),
            }, "fallback");
        string json = JsonSerializer.Serialize(reference, TextResourceReferenceJsonContext.Default.TextResourceReference);
        Assert.Equal("{\"version\":1,\"catalog\":\"app\",\"contractFingerprint\":\"" + fingerprint + "\",\"key\":\"Files.Deleted\",\"arguments\":{\"confirmed\":true,\"count\":\"3\",\"folder\":\"Archive\"},\"fallbackText\":\"fallback\"}", json);
        TextResourceReference parsed = JsonSerializer.Deserialize(json, TextResourceReferenceJsonContext.Default.TextResourceReference)!;
        Assert.Equal("true", parsed.Arguments["confirmed"].Value);
        Assert.Throws<ArgumentException>(() => _ = new TextResourceReferenceArgument(TextArgumentType.Int, "03"));
    }
}
