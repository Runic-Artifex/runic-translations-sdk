using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using TUnit.Core;

namespace Runic.Translations.Runtime.Tests;

// Also compiled into the NativeAOT runtime probe (tests/fixtures/translations/rmf2-v5-runtime-aot).
internal sealed class TranslationReferenceTests
{
    private const string Fingerprint = "sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
    private const string OtherFingerprint = "sha256:fedcba9876543210fedcba9876543210fedcba9876543210fedcba9876543210";

    private static Dictionary<string, TranslationReferenceArgument> Arguments(params (string Name, TranslationReferenceArgument Value)[] values) =>
        values.ToDictionary(item => item.Name, item => item.Value, StringComparer.Ordinal);

    private static string Json(TranslationReference reference) => JsonSerializer.Serialize(reference, TranslationReferenceJsonContext.Default.TranslationReference);
    private static TranslationReference Parse(string json) => JsonSerializer.Deserialize(json, TranslationReferenceJsonContext.Default.TranslationReference)!;

    [Test, DisplayName("text references validate versioned catalog identity and bounds")]
    public void Validate()
    {
        var reference = new TranslationReference(
            "app",
            Fingerprint,
            "Files.Deleted",
            Arguments(("count", new(TextArgumentType.Int, "3")), ("folder", new(TextArgumentType.String, "Archive"))),
            "3 files were deleted from Archive.");
        Assert.Equal(TranslationTransport.Version, reference.Version);
        Assert.Equal(2, reference.Version);
        Assert.Equal(2, reference.Arguments.Count);
        reference.ValidateCatalog("app", Fingerprint);
        Assert.Throws<TranslationContractException>(() => reference.ValidateCatalog("other", Fingerprint));
        Assert.Throws<ArgumentException>(() => _ = new TranslationReference("app", "bad", "Key"));
        Assert.Throws<ArgumentException>(() => _ = new TranslationReference("app", "sha256:" + new string('A', 64), "Key"));
        Assert.Throws<ArgumentException>(() => _ = new TranslationReference("app", "sha256:" + new string('z', 64), "Key"));
    }

    [Test, DisplayName("text reference arguments accept only canonical spellings")]
    public void Canonical()
    {
        foreach ((TextArgumentType type, string value) in new[]
        {
            (TextArgumentType.Int, "-12"), (TextArgumentType.Number, "0.125"), (TextArgumentType.Number, "-1.50"), (TextArgumentType.Date, "2026-10-14"),
            (TextArgumentType.Time, "08:30:00"), (TextArgumentType.DateTime, "2026-10-14T08:30:00Z"), (TextArgumentType.Guid, "0f8fad5b-d9cb-469f-a165-70867728950e"),
        })
            Assert.Equal(value, new TranslationReferenceArgument(type, value).Value);
        foreach ((TextArgumentType type, string value) in new[]
        {
            (TextArgumentType.Int, "03"), (TextArgumentType.Int, "-0"), (TextArgumentType.Int, "+1"), (TextArgumentType.Number, "-0"), (TextArgumentType.Number, "-0.00"),
            (TextArgumentType.Number, ".5"), (TextArgumentType.Number, "1e2"), (TextArgumentType.Time, "08:30:00.5"), (TextArgumentType.DateTime, "2026-10-14T08:30:00+02:00"),
            (TextArgumentType.DateTime, "2026-10-14T08:30:00"), (TextArgumentType.Guid, "0F8FAD5B-D9CB-469F-A165-70867728950E"), (TextArgumentType.List, "a"),
        })
            Assert.Throws<ArgumentException>(() => _ = new TranslationReferenceArgument(type, value), null);
    }

    [Test, DisplayName("text references serialize the exact AOT-safe version 2 wire contract")]
    public void JsonWire()
    {
        var step = new TranslationReference("app", Fingerprint, "steps.build", Arguments(("name", new(TextArgumentType.String, "gimp"))), "Building gimp");
        var reference = new TranslationReference(
            "app", Fingerprint, "Files.Deleted",
            Arguments(
                ("folder", new(TextArgumentType.String, "Archive")),
                ("count", new(TextArgumentType.Int, "3")),
                ("confirmed", new(TextArgumentType.Bool, "true")),
                ("names", new(["a.txt", "b.txt"])),
                ("step", new(step))),
            "fallback");
        string json = Json(reference);
        Assert.Equal("{\"version\":2,\"catalog\":\"app\",\"contractFingerprint\":\"" + Fingerprint + "\",\"key\":\"Files.Deleted\",\"arguments\":{" +
            "\"confirmed\":{\"type\":\"boolean\",\"value\":true},\"count\":{\"type\":\"int64\",\"value\":\"3\"},\"folder\":{\"type\":\"string\",\"value\":\"Archive\"}," +
            "\"names\":{\"type\":\"list\",\"value\":[\"a.txt\",\"b.txt\"]},\"step\":{\"type\":\"message\",\"value\":{\"key\":\"steps.build\",\"arguments\":" +
            "{\"name\":{\"type\":\"string\",\"value\":\"gimp\"}},\"fallbackText\":\"Building gimp\"}}},\"fallbackText\":\"fallback\"}", json);
        TranslationReference parsed = Parse(json);
        Assert.Equal(json, Json(parsed));
        Assert.Equal("true", parsed.Arguments["confirmed"].Value);
        Assert.Equal("b.txt", parsed.Arguments["names"].Items![1]);
        Assert.Equal("steps.build", parsed.Arguments["step"].Message!.Key);
        Assert.Equal(TextArgumentType.String, parsed.Arguments["step"].Type);
        Assert.Throws<ArgumentException>(() => _ = new TranslationReferenceArgument(TextArgumentType.Int, "03"));
    }

    [Test, DisplayName("version 1 text references are still read and written unchanged")]
    public void Version1()
    {
        string json = "{\"version\":1,\"catalog\":\"app\",\"contractFingerprint\":\"" + Fingerprint +
            "\",\"key\":\"Files.Deleted\",\"arguments\":{\"confirmed\":true,\"count\":\"3\"},\"fallbackText\":\"fallback\"}";
        TranslationReference parsed = Parse(json);
        Assert.Equal(1, parsed.Version);
        Assert.Equal(json, Json(parsed));
        Assert.Equal(null, Parse(json.Replace("\"fallback\"", "null", StringComparison.Ordinal)).FallbackText);
        Assert.Throws<JsonException>(() => Parse(json.Replace("\"version\":1", "\"version\":3", StringComparison.Ordinal)));
        Assert.Throws<JsonException>(() => Parse(json.Replace("\"count\":\"3\"", "\"count\":3", StringComparison.Ordinal)));
        Assert.Throws<ArgumentException>(() => _ = new TranslationReferenceArgument(parsed));
    }

    [Test, DisplayName("text references bound list items, nesting depth and the number of references")]
    public void Limits()
    {
        TranslationReference Chain(int depth)
        {
            var reference = new TranslationReference("app", Fingerprint, "leaf");
            for (int level = 1; level < depth; level++)
                reference = new TranslationReference("app", Fingerprint, "wrap", Arguments(("inner", new(reference))));
            return reference;
        }
        Assert.Equal(TranslationTransport.MaximumDepth, Chain(TranslationTransport.MaximumDepth).Depth);
        Assert.Throws<ArgumentOutOfRangeException>(() => Chain(TranslationTransport.MaximumDepth + 1));
        string deep = Json(Chain(TranslationTransport.MaximumDepth));
        Assert.Equal(deep, Json(Parse(deep)));
        string tooDeep = deep.Replace("{\"key\":\"leaf\",\"arguments\":{}}",
            "{\"key\":\"wrap\",\"arguments\":{\"inner\":{\"type\":\"message\",\"value\":{\"key\":\"leaf\",\"arguments\":{}}}}}", StringComparison.Ordinal);
        Assert.Throws<JsonException>(() => Parse(tooDeep));

        var leaf = new TranslationReference("app", Fingerprint, "leaf");
        var wide = Arguments(Enumerable.Range(0, 31).Select(index => ("a" + index, new TranslationReferenceArgument(leaf))).ToArray());
        Assert.Equal(TranslationTransport.MaximumReferences, new TranslationReference("app", Fingerprint, "wide", wide).ReferenceCount);
        var pair = new TranslationReference("app", Fingerprint, "pair", Arguments(("a", new(leaf)), ("b", new(leaf))));
        wide["a0"] = new(pair);
        Assert.Throws<ArgumentOutOfRangeException>(() => _ = new TranslationReference("app", Fingerprint, "wide", wide));

        Assert.Throws<ArgumentOutOfRangeException>(() => _ = new TranslationReferenceArgument(Enumerable.Repeat("x", TranslationTransport.MaximumListItems + 1).ToArray()));
        Assert.Throws<ArgumentOutOfRangeException>(() => _ = new TranslationReferenceArgument([new string('x', TranslationTransport.MaximumArgumentLength), "y"]));
        Assert.Throws<ArgumentException>(() => _ = new TranslationReferenceArgument(["x", null!]));
        Assert.Throws<ArgumentException>(() => _ = new TranslationReference("app", Fingerprint, "wrap",
            Arguments(("inner", new(new TranslationReference("other", Fingerprint, "leaf"))))));
        Assert.Throws<ArgumentException>(() => _ = new TranslationReference("app", Fingerprint, "wrap",
            Arguments(("inner", new(new TranslationReference("app", OtherFingerprint, "leaf"))))));
    }

    [Test, DisplayName("version 1 references can still be written for receivers that do not read version 2")]
    public void WriteVersion1()
    {
        TranslationReference reference = TranslationReference.CreateVersion1("app", Fingerprint, "files.deleted",
            Arguments(("count", new(TextArgumentType.Int, "3")), ("confirmed", new(TextArgumentType.Bool, "true"))), "fallback");
        Assert.Equal(1, reference.Version);
        Assert.Equal("{\"version\":1,\"catalog\":\"app\",\"contractFingerprint\":\"" + Fingerprint +
            "\",\"key\":\"files.deleted\",\"arguments\":{\"confirmed\":true,\"count\":\"3\"},\"fallbackText\":\"fallback\"}", Json(reference));
        Assert.Throws<ArgumentException>(() => TranslationReference.CreateVersion1("app", Fingerprint, "k", Arguments(("names", new(["a"])))), "needs transport version 2");
        Assert.Throws<ArgumentException>(() => TranslationReference.CreateVersion1("app", Fingerprint, "k",
            Arguments(("step", new(new TranslationReference("app", Fingerprint, "leaf"))))), "needs transport version 2");
    }

    [Test, DisplayName("text references are written as JSON.stringify writes them and reject lone surrogates")]
    public void Escaping()
    {
        var reference = new TranslationReference("app", Fingerprint, "k",
            Arguments(("name", new(TextArgumentType.String, "Gr\u00f6\u00dfe <b>&'+\"\\ \u0000\u0001\b\f\n\r\t\u001f\u007f\u2028 \ud83d\ude00"))));
        Assert.Equal("{\"version\":2,\"catalog\":\"app\",\"contractFingerprint\":\"" + Fingerprint + "\",\"key\":\"k\",\"arguments\":{\"name\":{\"type\":\"string\",\"value\":" +
            "\"Gr\u00f6\u00dfe <b>&'+\\\"\\\\ \\u0000\\u0001\\b\\f\\n\\r\\t\\u001f\u007f\u2028 \ud83d\ude00\"}}}", Json(reference));
        // The serializer's own encoder does not change the transport's escaping.
        Assert.Equal(Json(reference), JsonSerializer.Serialize(reference, new TranslationReferenceJsonContext(new JsonSerializerOptions()).TranslationReference));
        Assert.Throws<ArgumentException>(() => _ = new TranslationReferenceArgument(TextArgumentType.String, "\ud800"));
        Assert.Throws<ArgumentException>(() => _ = new TranslationReference("app", Fingerprint, "k", fallbackText: "a\udc00"));
        Assert.Throws<ArgumentException>(() => _ = new TranslationReferenceArgument(["ok", "\ud83d"]));
        Assert.Throws<JsonException>(() => Parse("{\"version\":2,\"catalog\":\"app\",\"contractFingerprint\":\"" + Fingerprint + "\",\"key\":\"k\",\"arguments\":{},\"fallbackText\":\"\\ud800\"}"));
        var builder = new System.Text.StringBuilder();
        TranslationReferenceWire.Quote(builder, "\ud800x\udc00");
        Assert.Equal("\"\\ud800x\\udc00\"", builder.ToString());
    }

    [Test, DisplayName("text references bound their encoded size before parsing and their total text")]
    public void EncodedSize()
    {
        string large = "{\"version\":2,\"catalog\":\"app\",\"contractFingerprint\":\"" + Fingerprint + "\",\"key\":\"k\",\"arguments\":{},\"fallbackText\":\"" +
            string.Concat(Enumerable.Repeat("\\u0001", 50_000)) + "\"}";
        Assert.True(System.Text.Encoding.UTF8.GetByteCount(large) > TranslationTransport.MaximumEncodedLength, "The probe is too small.");
        Assert.Throws<JsonException>(() => TranslationReference.Parse(large), "transport limit");
        Assert.Throws<JsonException>(() => TranslationReference.Parse(System.Text.Encoding.UTF8.GetBytes(large)), "transport limit");
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize(large, TranslationReferenceJsonContext.Default.TranslationReference), "transport limit");
        Assert.Throws<ArgumentOutOfRangeException>(() => _ = new TranslationReference("app", Fingerprint, "k", fallbackText: new string('\u0001', 50_000)));
        Assert.Throws<ArgumentOutOfRangeException>(() => _ = new TranslationReference("app", Fingerprint, "k",
            Arguments(Enumerable.Range(0, 5).Select(index => ("a" + index, new TranslationReferenceArgument(TextArgumentType.String, new string('v', 16 * 1024)))).ToArray())));
        var full = new TranslationReference("app", Fingerprint, "k",
            Arguments(Enumerable.Range(0, 4).Select(index => ("a" + index, new TranslationReferenceArgument(TextArgumentType.String, new string('v', 16 * 1024)))).ToArray()));
        Assert.Equal(TranslationTransport.MaximumTotalTextLength, full.TextLength);
        Assert.Equal(Json(full), Json(TranslationReference.Parse(Json(full))));
        Assert.Throws<JsonException>(() => Parse(Json(full).Replace("\"version\":2", "\"version\":2.0", StringComparison.Ordinal)));
    }

    private static CompiledTranslationSnapshot Snapshot() => new(new CompiledTranslationCatalog("app", "en",
        [
            new CompiledTranslationDefinition("files.deleted", [new("count", TextArgumentType.Int, TextArgumentFormat.Plain), new("folder", TextArgumentType.String, TextArgumentFormat.None)]),
            new CompiledTranslationDefinition("operation.failed", [new("step", TextArgumentType.String, TextArgumentFormat.None)]),
            new CompiledTranslationDefinition("steps.build", [new("name", TextArgumentType.String, TextArgumentFormat.None)]),
        ],
        [new CompiledTranslationLocale("en", null,
        [
            new CompiledTranslationValue(0, "{count} files were deleted from {folder}."),
            new CompiledTranslationValue(1, "{step} failed"),
            new CompiledTranslationValue(2, "Building {name}"),
        ])]), "en");

    private static string Describe(TranslationReferenceResolution resolution) =>
        resolution.Outcome + " " + (resolution.Text ?? "null") + " [" + string.Join(", ", resolution.Diagnostics.Select(item => item.Path + ":" + item.Reason)) + "]";

    [Test, DisplayName("persisted text references resolve with the receiver's catalog and fall back explicitly")]
    public void Resolve()
    {
        CompiledTranslationSnapshot snapshot = Snapshot();
        TranslationReference Build(string fingerprint, string name, string? fallback = null) =>
            new("app", fingerprint, "steps.build", Arguments(("name", new(TextArgumentType.String, name))), fallback);

        Assert.Equal("Translated Building gimp []", Describe(TranslationReferenceResolver.Resolve(snapshot, Fingerprint, Build(Fingerprint, "gimp"))));
        Assert.Equal("Translated Building gimp [$:contract-changed]", Describe(TranslationReferenceResolver.Resolve(snapshot, Fingerprint, Build(OtherFingerprint, "gimp"))));
        var failed = new TranslationReference("app", OtherFingerprint, "operation.failed", Arguments(("step", new(Build(OtherFingerprint, "gimp")))));
        Assert.Equal("Translated Building gimp failed [$:contract-changed]", Describe(TranslationReferenceResolver.Resolve(snapshot, Fingerprint, failed)));
        var gone = new TranslationReference("app", Fingerprint, "operation.failed", Arguments(("step", new(new TranslationReference("app", Fingerprint, "steps.cleanup", fallbackText: "Cleaning up")))));
        Assert.Equal("Translated Cleaning up failed [$.step:unknown-key]", Describe(TranslationReferenceResolver.Resolve(snapshot, Fingerprint, gone)));
        var count = new TranslationReference("app", OtherFingerprint, "files.deleted",
            Arguments(("count", new(TextArgumentType.String, "3")), ("folder", new(TextArgumentType.String, "Archive"))), "3 files deleted");
        Assert.Equal("Fallback 3 files deleted [$:contract-changed, $:incompatible-arguments]", Describe(TranslationReferenceResolver.Resolve(snapshot, Fingerprint, count)));
        Assert.Equal("Failed null [$:catalog-mismatch]",
            Describe(TranslationReferenceResolver.Resolve(snapshot, Fingerprint, new TranslationReference("other", Fingerprint, "steps.build"))));
        // Keys match exactly: the dotted-path alias that TryGetKey accepts for RMF2 names is not a stored identity.
        Assert.Equal("Fallback x [$:unknown-key]",
            Describe(TranslationReferenceResolver.Resolve(snapshot, Fingerprint, new TranslationReference("app", Fingerprint, "steps_build", fallbackText: "x"))));

        string version1 = "{\"version\":1,\"catalog\":\"app\",\"contractFingerprint\":\"" + OtherFingerprint +
            "\",\"key\":\"files.deleted\",\"arguments\":{\"count\":\"3\",\"folder\":\"Archive\"},\"fallbackText\":\"stored\"}";
        Assert.Equal("Fallback stored [$:fingerprint-mismatch]", Describe(TranslationReferenceResolver.Resolve(snapshot, Fingerprint, Parse(version1))));
        Assert.Equal("Translated 3 files were deleted from Archive. []",
            Describe(TranslationReferenceResolver.Resolve(snapshot, OtherFingerprint, Parse(version1))));
        Assert.Equal("Fallback stored [$:incompatible-arguments]", Describe(TranslationReferenceResolver.Resolve(snapshot, OtherFingerprint,
            Parse(version1.Replace("\"3\"", "\"three\"", StringComparison.Ordinal)))));
    }

    // A snapshot whose locale lacks a message, formatting it as MissingTranslationPolicy.ReturnKey or ReturnMarker would.
    private sealed class MissingMessageSnapshot(ITranslationSnapshot inner, string missing, string formatted) : ITranslationSnapshot
    {
        public string Catalog => inner.Catalog;
        public string Locale => inner.Locale;
        public bool TryGet(TranslationKey key, out string pattern)
        {
            if (key.Name == missing) { pattern = string.Empty; return false; }
            return inner.TryGet(key, out pattern);
        }
        public string Get(TranslationKey key) => key.Name == missing ? formatted : inner.Get(key);
        public string Format(TranslationKey key, ReadOnlySpan<TextArgument> arguments) => key.Name == missing ? formatted : inner.Format(key, arguments);
        public bool TryGetKey(string name, out TranslationKey key) => inner.TryGetKey(name, out key);
        public bool TryGetPlaceholders(TranslationKey key, out ReadOnlyMemory<TranslationPlaceholderDescriptor> placeholders) => inner.TryGetPlaceholders(key, out placeholders);
    }

    [Test, DisplayName("a key without a message in the locale resolves as unknown whatever the missing-key policy formats")]
    public void MissingInLocale()
    {
        var reference = new TranslationReference("app", Fingerprint, "steps.build", Arguments(("name", new(TextArgumentType.String, "gimp"))), "Building gimp");
        foreach (string formatted in new[] { "steps.build", "[missing: steps.build]" })
        {
            var snapshot = new MissingMessageSnapshot(Snapshot(), "steps.build", formatted);
            Assert.Equal("Fallback Building gimp [$:unknown-key]", Describe(TranslationReferenceResolver.Resolve(snapshot, Fingerprint, reference)), formatted);
            var failed = new TranslationReference("app", Fingerprint, "operation.failed", Arguments(("step", new(reference))));
            Assert.Equal("Translated Building gimp failed [$.step:unknown-key]", Describe(TranslationReferenceResolver.Resolve(snapshot, Fingerprint, failed)), formatted);
        }
    }
}
