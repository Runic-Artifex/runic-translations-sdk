using System;
using System.Text;

namespace RunicTextResources;

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

        var result = new StringBuilder(value.Length);
        result.Append(parts[0].ToLowerInvariant());
        bool extension = false;
        for (int i = 1; i < parts.Length; i++)
        {
            string part = parts[i];
            if (part.Length is 0 or > 8 || !AllAlphaNumeric(part))
            {
                return false;
            }

            result.Append('-');
            if (part.Length == 1)
            {
                extension = true;
                result.Append(part.ToLowerInvariant());
            }
            else if (!extension && part.Length == 4 && AllLetters(part))
            {
                result.Append(char.ToUpperInvariant(part[0]));
                result.Append(part[1..].ToLowerInvariant());
            }
            else if (!extension &&
                ((part.Length == 2 && AllLetters(part)) || (part.Length == 3 && AllDigits(part))))
            {
                result.Append(part.ToUpperInvariant());
            }
            else
            {
                result.Append(part.ToLowerInvariant());
            }
        }

        canonical = result.ToString();
        return true;
    }

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
