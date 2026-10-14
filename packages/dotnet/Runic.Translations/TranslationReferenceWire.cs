using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using System.Text;

namespace Runic.Translations;

// The byte-exact transport writer and the value rules shared with the generated ESM transport.js.
internal static class TranslationReferenceWire
{
    private static readonly BigInteger MaximumCoefficient = BigInteger.Parse("79228162514264337593543950335", CultureInfo.InvariantCulture);

    // Writes the reference as ECMAScript JSON.stringify writes the same object: members in writer order, arguments in ordinal
    // name order, and strings escaped only where JSON requires it.
    internal static string Write(TranslationReference reference)
    {
        var builder = new StringBuilder(256);
        builder.Append("{\"version\":").Append(reference.Version.ToString(CultureInfo.InvariantCulture)).Append(",\"catalog\":");
        Quote(builder, reference.Catalog);
        builder.Append(",\"contractFingerprint\":");
        Quote(builder, reference.ContractFingerprint);
        builder.Append(',');
        WriteBody(builder, reference);
        return builder.Append('}').ToString();
    }

    private static void WriteBody(StringBuilder builder, TranslationReference reference)
    {
        builder.Append("\"key\":");
        Quote(builder, reference.Key);
        builder.Append(",\"arguments\":{");
        var names = new List<string>(reference.Arguments.Keys);
        names.Sort(StringComparer.Ordinal);
        for (int index = 0; index < names.Count; index++)
        {
            if (index != 0) builder.Append(',');
            Quote(builder, names[index]);
            builder.Append(':');
            TranslationReferenceArgument argument = reference.Arguments[names[index]];
            if (reference.Version == 1)
            {
                if (argument.Type == TextArgumentType.Bool) builder.Append(argument.Value);
                else Quote(builder, argument.Value);
                continue;
            }
            builder.Append("{\"type\":");
            if (argument.Message is { } message)
            {
                builder.Append("\"message\",\"value\":{");
                WriteBody(builder, message);
                builder.Append('}');
            }
            else
            {
                Quote(builder, TranslationTransport.WireType(argument.Type));
                builder.Append(",\"value\":");
                if (argument.Items is { } items)
                {
                    builder.Append('[');
                    for (int item = 0; item < items.Count; item++)
                    {
                        if (item != 0) builder.Append(',');
                        Quote(builder, items[item]);
                    }
                    builder.Append(']');
                }
                else if (argument.Type == TextArgumentType.Bool) builder.Append(argument.Value);
                else Quote(builder, argument.Value);
            }
            builder.Append('}');
        }
        builder.Append('}');
        if (reference.FallbackText is not null)
        {
            builder.Append(",\"fallbackText\":");
            Quote(builder, reference.FallbackText);
        }
    }

    // JSON.stringify's QuoteJSONString: the two-character escapes, lowercase \u00XX for other controls and for lone surrogates,
    // everything else literal.
    internal static void Quote(StringBuilder builder, string value)
    {
        builder.Append('"');
        for (int index = 0; index < value.Length; index++)
        {
            char character = value[index];
            switch (character)
            {
                case '"': builder.Append("\\\""); break;
                case '\\': builder.Append("\\\\"); break;
                case '\b': builder.Append("\\b"); break;
                case '\f': builder.Append("\\f"); break;
                case '\n': builder.Append("\\n"); break;
                case '\r': builder.Append("\\r"); break;
                case '\t': builder.Append("\\t"); break;
                default:
                    if (character < ' ') Escape(builder, character);
                    else if (char.IsHighSurrogate(character) && index + 1 < value.Length && char.IsLowSurrogate(value[index + 1]))
                    {
                        builder.Append(character).Append(value[index + 1]);
                        index++;
                    }
                    else if (char.IsSurrogate(character)) Escape(builder, character);
                    else builder.Append(character);
                    break;
            }
        }
        builder.Append('"');
    }

    private static void Escape(StringBuilder builder, char character) =>
        builder.Append("\\u").Append(((int)character).ToString("x4", CultureInfo.InvariantCulture));

    // Lone surrogates cannot be read back by System.Text.Json, so neither writer accepts them.
    internal static void RequireWellFormed(string value, string parameter)
    {
        for (int index = 0; index < value.Length; index++)
        {
            char character = value[index];
            if (!char.IsSurrogate(character)) continue;
            if (char.IsHighSurrogate(character) && index + 1 < value.Length && char.IsLowSurrogate(value[index + 1])) index++;
            else throw new ArgumentException("Text-reference strings cannot contain a lone surrogate.", parameter);
        }
    }

    // Version 2 decimals: invariant digits, an optional fraction of at most 28 digits, a coefficient (all digits, scale kept)
    // within the .NET decimal range, and no negative zero.
    internal static bool IsCanonicalDecimal(string value)
    {
        int start = value.StartsWith('-') ? 1 : 0, point = value.IndexOf('.', StringComparison.Ordinal);
        string integer = point < 0 ? value[start..] : value[start..point], fraction = point < 0 ? string.Empty : value[(point + 1)..];
        if (integer.Length == 0 || !AllDigits(integer) || (integer.Length > 1 && integer[0] == '0')) return false;
        if (point >= 0 && (fraction.Length == 0 || fraction.Length > 28 || !AllDigits(fraction))) return false;
        BigInteger coefficient = BigInteger.Parse(integer + fraction, NumberStyles.None, CultureInfo.InvariantCulture);
        return coefficient <= MaximumCoefficient && !(start == 1 && coefficient.IsZero);
    }

    // Version 1 values, read as the generated ESM decodeWireValue reads them: integers may be "-0", decimals may use an exponent
    // and are normalized, GUIDs may be uppercase, and dates, times and date-times use the version 2 forms.
    internal static bool TryDecodeVersion1(TextArgumentType type, TextArgumentFormat format, string value, out TextArgument carrier)
    {
        carrier = default;
        CultureInfo invariant = CultureInfo.InvariantCulture;
        switch (type)
        {
            case TextArgumentType.String:
                carrier = new TextArgument("_", value);
                return true;
            case TextArgumentType.Int:
                if (value.Length == 0 || !IsInteger(value) || !long.TryParse(value, NumberStyles.AllowLeadingSign, invariant, out long integer)) return false;
                carrier = new TextArgument("_", integer, format);
                return true;
            case TextArgumentType.Number:
                if (!TryParseDecimal(value, out decimal number)) return false;
                carrier = new TextArgument("_", number, format);
                return true;
            case TextArgumentType.Date:
                if (value.Length != 10 || value.StartsWith("0000-", StringComparison.Ordinal) ||
                    !DateOnly.TryParseExact(value, "yyyy-MM-dd", invariant, DateTimeStyles.None, out DateOnly date)) return false;
                carrier = new TextArgument("_", date, format);
                return true;
            case TextArgumentType.Time:
                if (value.Length != 8 || !TimeOnly.TryParseExact(value, "HH:mm:ss", invariant, DateTimeStyles.None, out TimeOnly time)) return false;
                carrier = new TextArgument("_", time, format);
                return true;
            case TextArgumentType.DateTime:
                if (value.Length != 20 || value.StartsWith("0000-", StringComparison.Ordinal) ||
                    !DateTimeOffset.TryParseExact(value, "yyyy-MM-dd'T'HH:mm:ss'Z'", invariant, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                        out DateTimeOffset instant)) return false;
                carrier = new TextArgument("_", instant, format);
                return true;
            case TextArgumentType.Guid:
                if (value.Length != 36 || !Guid.TryParseExact(value, "D", out Guid guid)) return false;
                carrier = new TextArgument("_", guid, format);
                return true;
            default:
                return false;
        }
    }

    private static bool IsInteger(string value)
    {
        int start = value[0] == '-' ? 1 : 0;
        string digits = value[start..];
        return digits.Length > 0 && AllDigits(digits) && (digits.Length == 1 || digits[0] != '0');
    }

    // A port of the generated ESM runtime's parseDecimal.
    private static bool TryParseDecimal(string value, out decimal result)
    {
        result = 0m;
        if (value.Length > 4096) return false;
        int index = 0;
        bool negative = index < value.Length && value[index] == '-';
        if (negative) index++;
        int integerStart = index;
        while (index < value.Length && char.IsAsciiDigit(value[index])) index++;
        string integer = value[integerStart..index];
        if (integer.Length == 0 || (integer.Length > 1 && integer[0] == '0')) return false;
        string fraction = string.Empty;
        if (index < value.Length && value[index] == '.')
        {
            int fractionStart = ++index;
            while (index < value.Length && char.IsAsciiDigit(value[index])) index++;
            fraction = value[fractionStart..index];
            if (fraction.Length == 0) return false;
        }
        long exponent = 0;
        if (index < value.Length && value[index] is 'e' or 'E')
        {
            string text = value[(index + 1)..];
            int sign = text.StartsWith('+') || text.StartsWith('-') ? 1 : 0;
            string digits = text[sign..];
            if (digits.Length == 0 || !AllDigits(digits) || (digits.Length > 1 && digits[0] == '0')) return false;
            if (!BigInteger.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out BigInteger parsed) ||
                parsed < int.MinValue || parsed > int.MaxValue) return false;
            exponent = (long)parsed;
            index = value.Length;
        }
        if (index != value.Length) return false;
        string all = (integer + fraction).TrimStart('0');
        if (all.Length == 0) return true;
        long scale = fraction.Length - exponent;
        while (all.EndsWith('0'))
        {
            all = all[..^1];
            scale--;
        }
        if (scale < 0)
        {
            if (all.Length - scale > 29) return false;
            all += new string('0', (int)-scale);
            scale = 0;
        }
        if (scale > 28 || all.Length > 29) return false;
        var coefficient = BigInteger.Parse(all, NumberStyles.None, CultureInfo.InvariantCulture);
        if (coefficient > MaximumCoefficient) return false;
        byte[] bytes = new byte[12];
        coefficient.TryWriteBytes(bytes, out _, isUnsigned: true);
        result = new decimal(BitConverter.ToInt32(bytes, 0), BitConverter.ToInt32(bytes, 4), BitConverter.ToInt32(bytes, 8), negative, (byte)scale);
        return true;
    }

    // Trailing fractional zeros removed, as the ESM decimal carrier stores them.
    internal static decimal Normalize(decimal value) => value / 1.0000000000000000000000000000m;

    private static bool AllDigits(string value)
    {
        foreach (char character in value)
            if (!char.IsAsciiDigit(character)) return false;
        return true;
    }
}
