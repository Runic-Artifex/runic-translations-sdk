using System;
using System.Collections.Generic;
using System.Text;

namespace Runic.Translations;

internal static class LocaleTag
{
    internal static bool TryCanonicalize(string value, out string canonical)
    {
        canonical = string.Empty;
        if (string.IsNullOrEmpty(value) || value[0] == '-' || value[^1] == '-')
        {
            return false;
        }

        string[] parts = value.Split('-');
        if (parts[0].Length is < 2 or > 8 || !AllLetters(parts[0]))
        {
            return false;
        }

        var result = new StringBuilder(value.Length).Append(parts[0].ToLowerInvariant());
        int index = 1;
        if (parts[0].Length <= 3)
        {
            for (int count = 0; count < 3 && index < parts.Length &&
                parts[index].Length == 3 && AllLetters(parts[index]); count++, index++)
            {
                AppendLower(result, parts[index]);
            }
        }

        if (index < parts.Length && parts[index].Length == 4 && AllLetters(parts[index]))
        {
            string script = parts[index++];
            result.Append('-').Append(char.ToUpperInvariant(script[0])).Append(script[1..].ToLowerInvariant());
        }
        if (index < parts.Length &&
            ((parts[index].Length == 2 && AllLetters(parts[index])) ||
             (parts[index].Length == 3 && AllDigits(parts[index]))))
        {
            result.Append('-').Append(parts[index++].ToUpperInvariant());
        }

        var variants = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (index < parts.Length && IsVariant(parts[index]))
        {
            if (!variants.Add(parts[index])) return false;
            AppendLower(result, parts[index++]);
        }

        var singletons = new HashSet<char>();
        while (index < parts.Length && IsExtensionSingleton(parts[index]))
        {
            char singleton = char.ToLowerInvariant(parts[index++][0]);
            if (!singletons.Add(singleton)) return false;
            result.Append('-').Append(singleton);
            int firstSubtag = index;
            while (index < parts.Length && parts[index].Length is >= 2 and <= 8 && AllAlphaNumeric(parts[index]))
                AppendLower(result, parts[index++]);
            if (index == firstSubtag) return false;
        }

        if (index < parts.Length && parts[index].Length == 1 &&
            (parts[index][0] == 'x' || parts[index][0] == 'X'))
        {
            result.Append("-x");
            index++;
            int firstSubtag = index;
            while (index < parts.Length && parts[index].Length is >= 1 and <= 8 && AllAlphaNumeric(parts[index]))
                AppendLower(result, parts[index++]);
            if (index == firstSubtag) return false;
        }

        if (index != parts.Length) return false;
        canonical = result.ToString();
        return true;
    }

    private static void AppendLower(StringBuilder result, string part) =>
        result.Append('-').Append(part.ToLowerInvariant());

    private static bool IsVariant(string value) =>
        (value.Length is >= 5 and <= 8 && AllAlphaNumeric(value)) ||
        (value.Length == 4 && value[0] is >= '0' and <= '9' && AllAlphaNumeric(value));

    private static bool IsExtensionSingleton(string value) => value.Length == 1 &&
        ((value[0] is >= '0' and <= '9') ||
         (value[0] is >= 'A' and <= 'W') || (value[0] is >= 'Y' and <= 'Z') ||
         (value[0] is >= 'a' and <= 'w') || (value[0] is >= 'y' and <= 'z'));

    private static bool AllLetters(string value)
    {
        for (int i = 0; i < value.Length; i++)
        {
            if ((value[i] < 'A' || value[i] > 'Z') && (value[i] < 'a' || value[i] > 'z'))
            {
                return false;
            }
        }

        return true;
    }

    private static bool AllDigits(string value)
    {
        for (int i = 0; i < value.Length; i++)
        {
            if (value[i] < '0' || value[i] > '9')
            {
                return false;
            }
        }

        return true;
    }

    private static bool AllAlphaNumeric(string value)
    {
        for (int i = 0; i < value.Length; i++)
        {
            char character = value[i];
            if ((character < 'A' || character > 'Z') &&
                (character < 'a' || character > 'z') &&
                (character < '0' || character > '9'))
            {
                return false;
            }
        }

        return true;
    }
}
