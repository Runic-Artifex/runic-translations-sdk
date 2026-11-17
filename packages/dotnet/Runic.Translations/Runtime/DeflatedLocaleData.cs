using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;

namespace Runic.Translations;

/// <summary>
/// The locale data of a generated catalog for RMF2 runtime ABI 4: the compiler's UTF-8 JSON as raw DEFLATE data, about a
/// tenth of its size (D072.2). It is inflated on the locale's first use into a pooled buffer, which goes back to the pool
/// once the values are read, so only the values stay in memory.
/// </summary>
internal static class DeflatedLocaleData
{
    /// <summary>The largest inflated locale the runtime accepts: the limit of one external pack's content.</summary>
    internal const int MaximumLength = 64 * 1024 * 1024;

    internal static IReadOnlyList<CompiledTranslationValue> Read(ReadOnlySpan<byte> deflated, int length, Rmf2FormatData? formatData)
    {
        if (length is < 0 or > MaximumLength)
            throw new TranslationPackException("The generated locale data declares an invalid length.", TranslationPackFailureReason.Malformed);
        byte[] buffer = ArrayPool<byte>.Shared.Rent(Math.Max(length, 1));
        try
        {
            Inflate(deflated, buffer.AsSpan(0, length));
            return CompiledTranslationLocale.ReadRmf2Values(buffer.AsSpan(0, length), formatData);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <summary>Inflates <paramref name="deflated"/> into <paramref name="destination"/>, which it must fill exactly.</summary>
    internal static void Inflate(ReadOnlySpan<byte> deflated, Span<byte> destination)
    {
        // DeflateStream reads a stream; the compressed data is about a tenth of the JSON, so a pooled copy is cheap.
        byte[] source = ArrayPool<byte>.Shared.Rent(Math.Max(deflated.Length, 1));
        try
        {
            deflated.CopyTo(source);
            using var compressed = new MemoryStream(source, 0, deflated.Length, writable: false);
            using var inflater = new DeflateStream(compressed, CompressionMode.Decompress);
            int read = 0;
            while (read < destination.Length)
            {
                int count = inflater.Read(destination[read..]);
                if (count == 0) break;
                read += count;
            }
            Span<byte> extra = stackalloc byte[1];
            if (read != destination.Length || inflater.Read(extra) != 0)
                throw new TranslationPackException("The generated locale data does not inflate to its declared length.", TranslationPackFailureReason.Malformed);
        }
        catch (InvalidDataException exception)
        {
            throw new TranslationPackException("The generated locale data is not valid DEFLATE data.", TranslationPackFailureReason.Malformed, exception);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(source);
        }
    }
}
