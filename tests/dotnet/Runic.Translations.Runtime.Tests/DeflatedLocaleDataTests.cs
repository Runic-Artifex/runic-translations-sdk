using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using Runic.Translations.Generated;
using TUnit.Core;

namespace Runic.Translations.Runtime.Tests;

// RMF2 runtime ABI 4: generated catalogs embed each locale's JSON as raw DEFLATE data, which
// GeneratedCatalogSupport.ReadDeflatedValues inflates on the locale's first use. It must read exactly what ReadValues reads
// from the same JSON, and refuse data that does not inflate to the declared length.
internal sealed class DeflatedLocaleDataTests
{
    private static readonly TranslationKey Greeting = new("deflated", 1, "greeting");
    private static readonly TranslationKey Farewell = new("deflated", 0, "farewell");

    private const string Json = """
        [[0,{"contentLocale":"de","ast":{"astVersion":5,"profile":"rmf2-execution-v2","inputs":[],"declarations":[],"selectors":[],"variants":[{"keys":[],"nodes":[{"kind":"text","value":"Grüße aus Köln"}]}]}}],
         [1,{"contentLocale":"de","ast":{"astVersion":5,"profile":"rmf2-execution-v2","inputs":[{"name":"name","type":"string"}],"declarations":[],"selectors":[],"variants":[{"keys":[],"nodes":[{"kind":"text","value":"Hallo "},{"kind":"expression","expression":{"operand":{"kind":"input","value":"name"},"valueType":"string","function":"string","options":[],"annotations":[]}}]}]}}]]
        """;

    private static byte[] Deflate(byte[] data, CompressionLevel level = CompressionLevel.SmallestSize)
    {
        using var output = new MemoryStream();
        using (var deflater = new DeflateStream(output, level, leaveOpen: true)) deflater.Write(data);
        return output.ToArray();
    }

    private static CompiledTranslationCatalog Catalog(Func<IReadOnlyList<CompiledTranslationValue>> values) => new(
        "deflated", "de",
        [
            CompiledTranslationDefinition.FromRmf2Inputs("farewell", []),
            CompiledTranslationDefinition.FromRmf2Inputs("greeting", [new CompiledRmf2Input("name", TextArgumentType.String)]),
        ],
        [GeneratedCatalogSupport.CreateDeferredLocale("de", null, values)]);

    private static string Describe(Func<IReadOnlyList<CompiledTranslationValue>> values)
    {
        ITranslationSnapshot snapshot = new CompiledTranslationProvider(Catalog(values)).GetSnapshotAsync("de").AsTask().GetAwaiter().GetResult();
        return snapshot.Format(Farewell, []) + " | " + snapshot.Format(Greeting, [new TextArgument("name", "Ada")]);
    }

    [Test, DisplayName("deflated locale data reads the same values as its JSON")]
    public void RoundTrip()
    {
        byte[] json = Encoding.UTF8.GetBytes(Json);
        string expected = Describe(() => GeneratedCatalogSupport.ReadValues(json));
        Assert.Equal("Grüße aus Köln | Hallo Ada", expected);
        foreach (CompressionLevel level in new[] { CompressionLevel.SmallestSize, CompressionLevel.Optimal, CompressionLevel.Fastest, CompressionLevel.NoCompression })
        {
            byte[] deflated = Deflate(json, level);
            Assert.Equal(expected, Describe(() => GeneratedCatalogSupport.ReadDeflatedValues(deflated, json.Length)), level.ToString());
        }
    }

    [Test, DisplayName("deflated locale data inflates to the original bytes, also for a large locale")]
    public void InflatesExactly()
    {
        var builder = new StringBuilder("[");
        for (int index = 0; index < 3000; index++)
            builder.Append(index == 0 ? "" : ",").Append('[').Append(index)
                .Append(",{\"contentLocale\":\"ja\",\"ast\":{\"astVersion\":5,\"profile\":\"rmf2-execution-v2\",\"inputs\":[],\"declarations\":[],\"selectors\":[],\"variants\":[{\"keys\":[],\"nodes\":[{\"kind\":\"text\",\"value\":\"選択したドキュメント ")
                .Append(index).Append("\"}]}]}}]");
        byte[] json = Encoding.UTF8.GetBytes(builder.Append(']').ToString());
        byte[] inflated = new byte[json.Length];
        DeflatedLocaleData.Inflate(Deflate(json), inflated);
        Assert.True(json.AsSpan().SequenceEqual(inflated), "The inflated bytes differ from the JSON.");
        Assert.Equal(3000, GeneratedCatalogSupport.ReadDeflatedValues(Deflate(json), json.Length).Count);
    }

    [Test, DisplayName("deflated locale data that does not inflate to its declared length is refused")]
    public void RefusesWrongLength()
    {
        byte[] json = Encoding.UTF8.GetBytes(Json);
        byte[] deflated = Deflate(json);
        foreach (int length in new[] { json.Length - 1, json.Length + 1, 0 })
            Assert.Equal(TranslationPackFailureReason.Malformed,
                Assert.Throws<TranslationPackException>(() => GeneratedCatalogSupport.ReadDeflatedValues(deflated, length), "declared length").Reason);
        foreach (int length in new[] { -1, DeflatedLocaleData.MaximumLength + 1 })
            Assert.Throws<TranslationPackException>(() => GeneratedCatalogSupport.ReadDeflatedValues(deflated, length), "invalid length");
    }

    [Test, DisplayName("locale data that is not DEFLATE data is refused")]
    public void RefusesCorruptData()
    {
        byte[] json = Encoding.UTF8.GetBytes(Json);
        // 0xFF starts a block of the reserved type 3, which no inflater accepts; plain JSON is not DEFLATE data either.
        foreach (byte[] data in new[] { new byte[] { 0xFF, 0xFF, 0xFF }, json, Deflate(json)[..^4] })
            Assert.Throws<TranslationPackException>(() => GeneratedCatalogSupport.ReadDeflatedValues(data, json.Length));
    }

    [Test, DisplayName("a deferred locale inflates its data once")]
    public void DeferredLocaleInflatesOnce()
    {
        byte[] json = Encoding.UTF8.GetBytes(Json);
        byte[] deflated = Deflate(json);
        int reads = 0;
        var provider = new CompiledTranslationProvider(Catalog(() =>
        {
            reads++;
            return GeneratedCatalogSupport.ReadDeflatedValues(deflated, json.Length);
        }));
        for (int index = 0; index < 3; index++)
            Assert.Equal("Hallo Ada", provider.GetSnapshotAsync("de").AsTask().GetAwaiter().GetResult().Format(Greeting, [new TextArgument("name", "Ada")]));
        Assert.Equal(1, reads, "inflations");
    }
}
