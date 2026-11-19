using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;

namespace Runic.Translations;

/// <summary>
/// The locale data of a generated catalog for RMF2 runtime ABI 4: the compiler's UTF-8 JSON as raw DEFLATE data, about a
/// tenth of its size (D072.2). It is inflated on the locale's first use into an array of exactly the inflated length,
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
