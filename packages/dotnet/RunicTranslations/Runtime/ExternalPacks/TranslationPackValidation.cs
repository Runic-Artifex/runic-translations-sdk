using System;

namespace RunicTranslations;

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
    {
        if (string.IsNullOrEmpty(value) || value[0] == '-' || value[^1] == '-') return false;
        int segmentStart = 0;
        int segmentIndex = 0;
        bool extension = false;
        for (int i = 0; i <= value.Length; i++)
        {
            if (i != value.Length && value[i] != '-') continue;
            int length = i - segmentStart;
            if (length is < 1 or > 8) return false;
            for (int p = segmentStart; p < i; p++)
            {
                char character = value[p];
                if (!IsAsciiLetter(character) && (character < '0' || character > '9')) return false;
            }
            if (segmentIndex == 0)
            {
                if (length is < 2 or > 8) return false;
                for (int p = segmentStart; p < i; p++) if (value[p] < 'a' || value[p] > 'z') return false;
            }
            else if (length == 1)
            {
                extension = true;
                if (value[segmentStart] < 'a' || value[segmentStart] > 'z') return false;
            }
            else if (!extension && length == 4 && AllLetters(value.AsSpan(segmentStart, length)))
            {
                if (value[segmentStart] < 'A' || value[segmentStart] > 'Z') return false;
                for (int p = segmentStart + 1; p < i; p++) if (value[p] < 'a' || value[p] > 'z') return false;
            }
            else if (!extension && length == 2 && AllLetters(value.AsSpan(segmentStart, length)))
            {
                for (int p = segmentStart; p < i; p++) if (value[p] < 'A' || value[p] > 'Z') return false;
            }
            else if (!extension && length == 3 && AllDigits(value.AsSpan(segmentStart, length)))
            {
                // Numeric regions have no casing.
            }
            else
            {
                for (int p = segmentStart; p < i; p++)
                {
                    char character = value[p];
                    if (character >= 'A' && character <= 'Z') return false;
                }
            }
            segmentStart = i + 1;
            segmentIndex++;
        }
        return segmentIndex > 0;
    }

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
