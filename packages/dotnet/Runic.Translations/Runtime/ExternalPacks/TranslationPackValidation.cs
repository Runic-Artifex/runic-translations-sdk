using System;
using System.Text;

namespace Runic.Translations;

internal static class TranslationPackValidation
{
    internal static bool IsCatalog(string? value)
    {
        if (string.IsNullOrEmpty(value) || value[0] < 'a' || value[0] > 'z') return false;
        for (int i = 1; i < value.Length; i++)
        {
            char character = value[i];
            if ((character < 'a' || character > 'z') && (character < '0' || character > '9') && character != '.' && character != '-') return false;
        }
        return true;
    }

    internal static bool IsResourceKey(string? value)
    {
        if (string.IsNullOrEmpty(value)) return false;
        int segmentStart = 0;
        for (int i = 0; i <= value.Length; i++)
        {
            if (i != value.Length && value[i] != '.') continue;
            int length = i - segmentStart;
            if (length == 0 || !IsIdentifier(value.AsSpan(segmentStart, length))) return false;
            segmentStart = i + 1;
        }
        return true;
    }

    internal static bool IsIdentifier(string? value) => !string.IsNullOrEmpty(value) && IsIdentifier(value.AsSpan());

    internal static bool IsRmf2Name(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || !value.IsNormalized(NormalizationForm.FormC)) return false;
        bool first = true;
        foreach (Rune rune in value.EnumerateRunes())
        {
            int scalar = rune.Value;
            if (!IsRmf2NameStart(scalar) && (first || scalar is not (>= '0' and <= '9' or '-' or '.'))) return false;
            first = false;
        }
        return !first;
    }

    private static bool IsRmf2NameStart(int value) => value is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or '+' or '_' or
        >= 0xA1 and <= 0x61B or >= 0x61D and <= 0x167F or >= 0x1681 and <= 0x1FFF or >= 0x200B and <= 0x200D or
        >= 0x2010 and <= 0x2027 or >= 0x2030 and <= 0x205E or >= 0x2060 and <= 0x2065 or >= 0x206A and <= 0x2FFF or
        >= 0x3001 and <= 0xD7FF or >= 0xE000 and <= 0xFDCF or >= 0xFDF0 and <= 0xFFFD ||
        value >= 0x10000 && value <= 0x10FFFD && (value & 0xFFFF) <= 0xFFFD;

    private static bool IsIdentifier(ReadOnlySpan<char> value)
    {
        if (value.IsEmpty || (!IsAsciiLetter(value[0]) && value[0] != '_')) return false;
        for (int i = 1; i < value.Length; i++)
        {
            char character = value[i];
            if (!IsAsciiLetter(character) && (character < '0' || character > '9') && character != '_') return false;
        }
        return true;
    }

    internal static bool IsCanonicalLocale(string? value)
        => value is not null && LocaleTag.TryCanonicalize(value, out string canonical) &&
            string.Equals(value, canonical, StringComparison.Ordinal);

    internal static bool IsFingerprint(string? value)
    {
        if (value is null || value.Length != 71 || !value.StartsWith("sha256:", StringComparison.Ordinal)) return false;
        for (int i = 7; i < value.Length; i++)
        {
            char character = value[i];
            if ((character < '0' || character > '9') && (character < 'a' || character > 'f')) return false;
        }
        return true;
    }

    internal static bool IsFormatAllowed(TextArgumentType type, TextArgumentFormat format) => type switch
    {
        TextArgumentType.String => format == TextArgumentFormat.None,
        TextArgumentType.Int => format is TextArgumentFormat.Plain or TextArgumentFormat.Grouped,
        TextArgumentType.Number => format is >= TextArgumentFormat.Plain and <= TextArgumentFormat.Percent4,
        TextArgumentType.Bool => format == TextArgumentFormat.Lower,
        TextArgumentType.Date => format is TextArgumentFormat.Iso or TextArgumentFormat.Short or TextArgumentFormat.Medium or TextArgumentFormat.Long,
        TextArgumentType.Time => format is TextArgumentFormat.Iso or TextArgumentFormat.Short or TextArgumentFormat.Medium,
        TextArgumentType.DateTime => format is TextArgumentFormat.Iso or TextArgumentFormat.Short or TextArgumentFormat.Medium or TextArgumentFormat.Long,
        TextArgumentType.Guid => format is TextArgumentFormat.D or TextArgumentFormat.N,
        _ => false,
    };

    private static bool IsAsciiLetter(char character) =>
        (character >= 'A' && character <= 'Z') || (character >= 'a' && character <= 'z');

    private static bool AllLetters(ReadOnlySpan<char> value)
    {
        for (int i = 0; i < value.Length; i++) if (!IsAsciiLetter(value[i])) return false;
        return true;
    }

    private static bool AllDigits(ReadOnlySpan<char> value)
    {
        for (int i = 0; i < value.Length; i++) if (value[i] < '0' || value[i] > '9') return false;
        return true;
    }
}
