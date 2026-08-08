using System;
using System.Collections.Generic;

namespace RunicTextResources.Runtime.Tests;

internal static class TextResourceReferenceTests
{
    public static void Register(TestRunner runner)
    {
        runner.Add("text references validate versioned catalog identity and bounds", Validate);
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
    }
}
