using System;
using System.Collections.Generic;
using System.Text.Json;

namespace RunicTranslations.Runtime.Tests;

internal static class TranslationReferenceTests
{
    public static void Register(TestRunner runner)
    {
        runner.Add("text references validate versioned catalog identity and bounds", Validate);
        runner.Add("text references serialize the exact AOT-safe ESM wire contract", JsonWire);
    }

    private static void Validate()
    {
        const string fingerprint = "sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
        var reference = new TranslationReference(
            "app",
            fingerprint,
            "Files.Deleted",
            new Dictionary<string, TranslationReferenceArgument>(StringComparer.Ordinal)
            {
                ["count"] = new(TextArgumentType.Int, "3"),
                ["folder"] = new(TextArgumentType.String, "Archive"),
            },
            "3 files were deleted from Archive.");
        Assert.Equal(TranslationTransport.Version, reference.Version);
        Assert.Equal(2, reference.Arguments.Count);
        reference.ValidateCatalog("app", fingerprint);
        Assert.Throws<TranslationContractException>(() => reference.ValidateCatalog("other", fingerprint));
        Assert.Throws<ArgumentException>(() => _ = new TranslationReference("app", "bad", "Key"));
        Assert.Throws<ArgumentException>(() => _ = new TranslationReference("app", "sha256:" + new string('A', 64), "Key"));
        Assert.Throws<ArgumentException>(() => _ = new TranslationReference("app", "sha256:" + new string('z', 64), "Key"));
    }

    private static void JsonWire()
    {
        const string fingerprint = "sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
        var reference = new TranslationReference(
            "app", fingerprint, "Files.Deleted",
            new Dictionary<string, TranslationReferenceArgument>(StringComparer.Ordinal)
            {
                ["folder"] = new(TextArgumentType.String, "Archive"),
                ["count"] = new(TextArgumentType.Int, "3"),
                ["confirmed"] = new(TextArgumentType.Bool, "true"),
            }, "fallback");
        string json = JsonSerializer.Serialize(reference, TranslationReferenceJsonContext.Default.TranslationReference);
        Assert.Equal("{\"version\":1,\"catalog\":\"app\",\"contractFingerprint\":\"" + fingerprint + "\",\"key\":\"Files.Deleted\",\"arguments\":{\"confirmed\":true,\"count\":\"3\",\"folder\":\"Archive\"},\"fallbackText\":\"fallback\"}", json);
        TranslationReference parsed = JsonSerializer.Deserialize(json, TranslationReferenceJsonContext.Default.TranslationReference)!;
        Assert.Equal("true", parsed.Arguments["confirmed"].Value);
        Assert.Throws<ArgumentException>(() => _ = new TranslationReferenceArgument(TextArgumentType.Int, "03"));
    }
}
