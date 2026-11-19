using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;

namespace Runic.Translations;

/// <summary>
/// The locale data of a generated catalog as raw DEFLATE data: for RMF2 runtime ABI 4 the compiler's UTF-8 JSON, about a
/// tenth of its size (D072.2), for ABI 5 its binary encoding (<see cref="GeneratedLocaleBinary"/>). It is inflated on the locale's first use into an array of exactly the inflated length,
/// which becomes garbage once the values are read, so only the values stay in memory.
/// </summary>
/// <remarks>
/// The buffers are deliberately not rented from <see cref="System.Buffers.ArrayPool{T}.Shared"/>: a locale is read once,
/// and the shared pool would keep a power-of-two array of at least the inflated length (and the copy of the DEFLATE
/// data) for the life of the process, about twice the inflated JSON.
/// </remarks>
internal static class DeflatedLocaleData
{
    /// <summary>The largest inflated locale the runtime accepts: the limit of one external pack's content.</summary>
    internal const int MaximumLength = 64 * 1024 * 1024;

    internal static IReadOnlyList<CompiledTranslationValue> Read(ReadOnlySpan<byte> deflated, int length, Rmf2FormatData? formatData)
    {
        if (length is < 0 or > MaximumLength)
            throw new TranslationPackException("The generated locale data declares an invalid length.", TranslationPackFailureReason.Malformed);
        byte[] buffer = new byte[length];
        Inflate(deflated, buffer);
        return CompiledTranslationLocale.ReadRmf2Values(buffer, formatData);
    }

    /// <summary>
    /// Reads a locale's binary data (RMF2 runtime ABI 5). The values keep the inflated array, from which each message is
    /// built on its first format, so it is not shared with anything.
    /// </summary>
    internal static IReadOnlyList<CompiledTranslationValue> ReadBinary(ReadOnlySpan<byte> deflated, int length, Rmf2FormatData? formatData) =>
        GeneratedLocaleBinary.Read(InflateExact(deflated, length), formatData);

    /// <summary>
    /// Inflates into a new array of exactly <paramref name="length"/> bytes. The length must be one the DEFLATE data can
    /// reach, at most 1,032 times its size (RFC 1951's largest expansion), so a few bytes cannot ask for a large array.
    /// </summary>
    internal static byte[] InflateExact(ReadOnlySpan<byte> deflated, int length)
    {
        if (length is < 0 or > MaximumLength || length > (long)deflated.Length * MaximumExpansion + MaximumExpansion)
            throw new TranslationPackException("The generated data declares an invalid length.", TranslationPackFailureReason.Malformed);
        byte[] buffer = new byte[length];
        Inflate(deflated, buffer);
        return buffer;
    }

    // RFC 1951: a length/distance pair of at most 3 bits (fixed Huffman codes, the shortest) yields 258 bytes, so DEFLATE
    // data expands at most about 1,032 times.
    private const int MaximumExpansion = 1032;

    /// <summary>Inflates UTF-8 text, such as a generated catalog's markup contract (RMF2 runtime ABI 4).</summary>
    internal static string ReadText(ReadOnlySpan<byte> deflated, int length)
    {
        if (length is < 0 or > MaximumLength)
            throw new TranslationPackException("The generated data declares an invalid length.", TranslationPackFailureReason.Malformed);
        byte[] buffer = new byte[length];
        Inflate(deflated, buffer);
        try
        {
            return StrictUtf8.GetString(buffer);
        }
        catch (System.Text.DecoderFallbackException exception)
        {
            throw new TranslationPackException("The generated text is not valid UTF-8.", TranslationPackFailureReason.Malformed, exception);
        }
    }

    private static readonly System.Text.UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>Inflates <paramref name="deflated"/> into <paramref name="destination"/>, which it must fill exactly.</summary>
    internal static void Inflate(ReadOnlySpan<byte> deflated, Span<byte> destination)
    {
        // DeflateStream reads a stream; the compressed data is about a tenth of the inflated data, so a copy is cheap. It is
        // a plain array, not a pooled one, for the reason in the type's remarks.
        try
        {
            using var compressed = new MemoryStream(deflated.ToArray(), writable: false);
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
    }
}
