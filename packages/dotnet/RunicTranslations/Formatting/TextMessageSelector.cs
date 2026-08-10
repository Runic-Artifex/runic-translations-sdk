using System;

namespace RunicTranslations;

/// <summary>Portable selector primitives used by generated schema version 2 accessors.</summary>
public static class TextMessageSelector
{
    /// <summary>Selects a CLDR-style plural category for the currently supported built-in locale families.</summary>
    /// <remarks>Generated compilers reject locale families outside this registry before using plural messages.</remarks>
    public static string SelectPlural(decimal value, string locale, bool ordinal)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(locale);
        string language = locale.Split('-')[0].ToLowerInvariant();
        decimal absolute = Math.Abs(value);
        if (ordinal)
        {
            if (language == "en" && decimal.Truncate(absolute) == absolute)
            {
                int mod100 = (int)(absolute % 100);
                int mod10 = (int)(absolute % 10);
                if (mod10 == 1 && mod100 != 11) return "one";
                if (mod10 == 2 && mod100 != 12) return "two";
                if (mod10 == 3 && mod100 != 13) return "few";
            }
            return "other";
        }

        return language switch
        {
            "fr" when decimal.Truncate(absolute) is 0 or 1 => "one",
            "en" or "de" or "es" or "it" or "nl" or "sv" or "no" or "da" when absolute == 1 => "one",
            _ => "other",
        };
    }
}
