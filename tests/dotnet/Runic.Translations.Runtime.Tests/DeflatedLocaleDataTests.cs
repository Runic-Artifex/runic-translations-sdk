using System;
using System.Buffers;
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

    [Test, DisplayName("deflated text such as a generated markup contract inflates to the same string, and wrong lengths or data are refused")]
    public void DeflatedText()
    {
        string text = "{\"version\":2,\"messages\":{\"grüße\":{}}}" + new string(' ', 5000);
        byte[] utf8 = Encoding.UTF8.GetBytes(text);
        byte[] deflated = Deflate(utf8);
        Assert.Equal(text, GeneratedCatalogSupport.ReadDeflatedText(deflated, utf8.Length));
        Assert.Equal("", GeneratedCatalogSupport.ReadDeflatedText(Deflate([]), 0));
        foreach (int length in new[] { utf8.Length - 1, utf8.Length + 1, -1 })
            Assert.Throws<TranslationPackException>(() => GeneratedCatalogSupport.ReadDeflatedText(deflated, length), null);
        Assert.Throws<TranslationPackException>(() => GeneratedCatalogSupport.ReadDeflatedText(Deflate([0xC3, 0x28]), 2), null);
        Assert.Throws<TranslationPackException>(() => GeneratedCatalogSupport.ReadDeflatedText([0xFF, 0xFF, 0xFF], 3), null);
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

    [Test, DisplayName("reading deflated locale data or text leaves none of its buffers in the shared array pool")]
    public void LeavesNoPooledBuffer()
    {
        // A pooled buffer that goes back to ArrayPool.Shared stays in this thread's cache, so the next Rent of its size on
        // the same thread returns it with the inflated bytes still in it. Plain arrays leave nothing for the pool to find.
        // The marker is unique per run, so no array that another case left in the pool can contain it.
        string marker = Guid.NewGuid().ToString("N");
        byte[] json = Encoding.UTF8.GetBytes(Json.Replace("Grüße aus Köln", "Grüße " + marker, StringComparison.Ordinal));
        byte[] deflated = Deflate(json);
        byte[] text = Encoding.UTF8.GetBytes("{\"version\":2,\"marker\":\"" + marker + "\"}");
        byte[] deflatedText = Deflate(text);
        Assert.Equal(2, GeneratedCatalogSupport.ReadDeflatedValues(deflated, json.Length).Count);
        Assert.Equal(Encoding.UTF8.GetString(text), GeneratedCatalogSupport.ReadDeflatedText(deflatedText, text.Length));

        byte[] needle = Encoding.UTF8.GetBytes(marker);
        foreach (int size in new[] { json.Length, deflated.Length, text.Length, deflatedText.Length })
        {
            byte[] rented = ArrayPool<byte>.Shared.Rent(size);
            try
            {
                Assert.True(rented.AsSpan().IndexOf(needle) < 0, $"ArrayPool.Shared kept an inflate buffer of {rented.Length} bytes for a read of {size} bytes.");
                Assert.True(rented.AsSpan().IndexOf(deflated) < 0 && rented.AsSpan().IndexOf(deflatedText) < 0,
                    $"ArrayPool.Shared kept a copy of the DEFLATE data in an array of {rented.Length} bytes.");
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(rented);
            }
        }
    }

    // RMF2 runtime ABI 5: a locale in the binary encoding, version 1, a pool of "de" and "Grüße aus Köln", default locale
    // 0, and one plain message (ID 0, flags 1, text 1).
    private static byte[] Binary()
    {
        byte[] text = Encoding.UTF8.GetBytes("Grüße aus Köln");
        return [1, 2, 2, (byte)'d', (byte)'e', (byte)text.Length, .. text, 0, 1, 0, 1, 1];
    }

    [Test, DisplayName("deflated binary locale data (RMF2 ABI 5) reads and formats through a catalog, built on first format")]
    public void BinaryRoundTrip()
    {
        byte[] binary = Binary();
        IReadOnlyList<CompiledTranslationValue> values = GeneratedCatalogSupport.ReadDeflatedBinaryValues(Deflate(binary), binary.Length);
        Assert.Equal(1, values.Count, "values");
        Assert.True(values[0].Message!.IsRmf2V5, "The deferred message is not RMF2.");
        Assert.Equal("de", values[0].Message!.ContentLocale, "content locale before the first format");
        var catalog = new CompiledTranslationCatalog("deflated", "de", [CompiledTranslationDefinition.FromRmf2Inputs("farewell", [])],
            [GeneratedCatalogSupport.CreateDeferredLocale("de", null, () => GeneratedCatalogSupport.ReadDeflatedBinaryValues(Deflate(binary), binary.Length))]);
        ITranslationSnapshot snapshot = new CompiledTranslationProvider(catalog).GetSnapshotAsync("de").AsTask().GetAwaiter().GetResult();
        Assert.Equal("Grüße aus Köln", snapshot.Format(Farewell, []), "formatted");
    }

    [Test, DisplayName("binary locale data and inflated data that do not match their length, or claim more than DEFLATE can expand to, are refused")]
    public void BinaryRefusesWrongLength()
    {
        byte[] binary = Binary();
        byte[] deflated = Deflate(binary);
        foreach (int length in new[] { binary.Length - 1, binary.Length + 1, -1, 0, DeflatedLocaleData.MaximumLength + 1 })
            Assert.Equal(TranslationPackFailureReason.Malformed,
                Assert.Throws<TranslationPackException>(() => GeneratedCatalogSupport.ReadDeflatedBinaryValues(deflated, length), "length").Reason);
        Assert.True(binary.AsSpan().SequenceEqual(GeneratedCatalogSupport.InflateData(deflated, binary.Length)), "InflateData");
        // A few bytes cannot ask for a large array: DEFLATE expands at most 1,032 times.
        Assert.Throws<TranslationPackException>(() => GeneratedCatalogSupport.InflateData([0x03, 0x00], 1_000_000), "invalid length");
        Assert.Throws<TranslationPackException>(() => GeneratedCatalogSupport.InflateData([0xFF, 0xFF, 0xFF], 3), "DEFLATE");
        // JSON is not binary locale data.
        byte[] json = Encoding.UTF8.GetBytes(Json);
        Assert.Throws<TranslationPackException>(() => GeneratedCatalogSupport.ReadDeflatedBinaryValues(Deflate(json), json.Length), "encoding version");
    }

    [Test, DisplayName("reading deflated binary locale data leaves none of its buffers in the shared array pool")]
    public void BinaryLeavesNoPooledBuffer()
    {
        string marker = Guid.NewGuid().ToString("N");
        byte[] text = Encoding.UTF8.GetBytes(marker);
        byte[] binary = [1, 2, 2, (byte)'d', (byte)'e', (byte)text.Length, .. text, 0, 1, 0, 1, 1];
        byte[] deflated = Deflate(binary);
        Assert.Equal(1, GeneratedCatalogSupport.ReadDeflatedBinaryValues(deflated, binary.Length).Count, "values");
        foreach (int size in new[] { binary.Length, deflated.Length })
        {
            byte[] rented = ArrayPool<byte>.Shared.Rent(size);
            try { Assert.True(rented.AsSpan().IndexOf(text) < 0 && rented.AsSpan().IndexOf(deflated) < 0, "ArrayPool.Shared kept a buffer of " + rented.Length + " bytes."); }
            finally { ArrayPool<byte>.Shared.Return(rented); }
        }
    }
}
