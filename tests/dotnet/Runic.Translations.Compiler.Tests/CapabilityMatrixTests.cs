using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Runic.Translations.Compiler.Tests;

internal static class CapabilityMatrixTests
{
    public static void Register(TestRunner runner)
    {
        runner.Add("capability matrix matches the compiler locale registry", MatchesCompilerRegistry);
        runner.Add("capability matrix declares closed formatter equivalence", DeclaresFormatterEquivalence);
    }

    private static void MatchesCompilerRegistry()
    {
        using JsonDocument document = ReadMatrix();
        JsonElement root = document.RootElement;
        Assert.Equal(1, root.GetProperty("capabilityMatrixVersion").GetInt32());
        Assert.Equal("0.1.0-public-preview", root.GetProperty("releaseTarget").GetString());
        JsonElement cldr = root.GetProperty("cldr");
        Assert.Equal("48.2", cldr.GetProperty("version").GetString());
        Assert.Equal("48.2.0", cldr.GetProperty("jsonTag").GetString());
        Assert.Equal("Unicode-3.0", cldr.GetProperty("license").GetString());
        JsonElement messageProfile = root.GetProperty("messageProfile");
        Assert.Equal("exact", messageProfile.GetProperty("cardinal").GetString());
        Assert.Equal("exact", messageProfile.GetProperty("ordinal").GetString());
        Assert.Equal("semantic", messageProfile.GetProperty("relativeTime").GetString());
        JsonElement backends = root.GetProperty("backends");
        Assert.Equal("dotnet", backends[0].GetString());
        Assert.Equal("esm", backends[1].GetString());

        string? previous = null;
        foreach (JsonElement locale in root.GetProperty("locales").EnumerateArray())
        {
            string tag = locale.GetProperty("tag").GetString() ?? string.Empty;
            Assert.True(previous is null || string.CompareOrdinal(previous, tag) < 0, "Capability locales must be unique and ordinal-sorted.");
            previous = tag;
            AssertCapability(tag, "plural", locale.GetProperty("cardinal").GetBoolean());
            AssertCapability(tag, "ordinal", locale.GetProperty("ordinal").GetBoolean());
            AssertCapability(tag, "relativeTime", locale.GetProperty("relativeTime").GetBoolean());
        }

        AssertCapability("de-DE", "plural", expected: true);
        AssertCapability("en-GB", "ordinal", expected: true);
        AssertCapability("fr-CA", "relativeTime", expected: true);
        AssertCapability("pl", "plural", expected: false);
        AssertCapability("pl", "ordinal", expected: false);
        AssertCapability("pl", "relativeTime", expected: false);
    }

    private static void DeclaresFormatterEquivalence()
    {
        using JsonDocument document = ReadMatrix();
        var formatters = new HashSet<string>(StringComparer.Ordinal);
        foreach (System.Text.Json.JsonProperty formatter in document.RootElement.GetProperty("formatterProfile").EnumerateObject())
        {
            formatters.Add(formatter.Name);
            foreach (System.Text.Json.JsonProperty form in formatter.Value.EnumerateObject())
                Assert.True(form.Value.GetString() is "exact" or "semantic", $"Unknown equivalence for {formatter.Name}.{form.Name}.");
        }
        Assert.Equal("boolean|date|datetime|integer|number|string|time|uuid", Sorted(formatters));
    }

    private static JsonDocument ReadMatrix() => JsonDocument.Parse(
        File.ReadAllBytes(RepositoryPaths.Resolve("spec", "capabilities-v1.json")),
        new JsonDocumentOptions { AllowTrailingCommas = false, CommentHandling = JsonCommentHandling.Disallow, MaxDepth = 32 });

    private static void AssertCapability(string locale, string capability, bool expected)
    {
        string manifest = "{\"schemaVersion\":2,\"catalog\":\"capability\",\"code\":{\"namespace\":\"Tests\",\"className\":\"CapabilityText\"},\"defaultLocale\":\""
            + locale + "\",\"locales\":[{\"tag\":\"" + locale + "\"}],\"layers\":[{\"name\":\"base\",\"priority\":0}]}";
        string message = capability == "relativeTime"
            ? "{\"inputs\":{\"value\":{\"type\":\"decimal\"}},\"declarations\":[{\"name\":\"formatted\",\"input\":\"value\",\"function\":\"relativeTime\",\"unit\":\"day\",\"numeric\":\"auto\"}],\"selectors\":[],\"variants\":[{\"match\":{},\"value\":[{\"local\":\"formatted\"}]}]}"
            : "{\"inputs\":{\"value\":{\"type\":\"int64\"}},\"selectors\":[{\"name\":\"category\",\"input\":\"value\",\"function\":\"" + capability + "\"}],\"variants\":[{\"match\":{\"category\":\"*\"},\"value\":\"other\"}]}";
        string resource = "{\"schemaVersion\":2,\"catalog\":\"capability\",\"locale\":\"" + locale
            + "\",\"layer\":\"base\",\"resources\":{\"Message\":{\"$value\":" + message + "}}}";
        TranslationCompilation compilation = Runic.Translations.Compiler.TranslationCompiler.Compile(
            [CompilerTests.Source("manifest.json", manifest)],
            [CompilerTests.Source("locale.json", resource)]);

        Assert.Equal(expected, compilation.Success,
            locale + " " + capability + ": " + CompilerTests.DiagnosticsText(compilation.Diagnostics));
        if (!expected)
        {
            bool hasCapabilityDiagnostic = false;
            foreach (TranslationDiagnostic diagnostic in compilation.Diagnostics)
                if (diagnostic.Id == "RTR0031") hasCapabilityDiagnostic = true;
            Assert.True(hasCapabilityDiagnostic, locale + " " + capability + " failed without RTR0031.");
        }
    }

    private static string Sorted(HashSet<string> values)
    {
        string[] items = new string[values.Count];
        values.CopyTo(items);
        Array.Sort(items, StringComparer.Ordinal);
        return string.Join('|', items);
    }
}
