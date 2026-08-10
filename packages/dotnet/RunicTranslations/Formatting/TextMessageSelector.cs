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
        GeneratedPluralLocale? localeRules = GeneratedLocaleData.FindPlural(language);
        if (localeRules is null) return "other";
        string rule = ordinal ? localeRules.OrdinalRule : localeRules.CardinalRule;
        if (rule == "english")
        {
            if (decimal.Truncate(absolute) == absolute)
            {
                int mod100 = (int)(absolute % 100);
                int mod10 = (int)(absolute % 10);
                if (mod10 == 1 && mod100 != 11) return "one";
                if (mod10 == 2 && mod100 != 12) return "two";
                if (mod10 == 3 && mod100 != 13) return "few";
            }
            return "other";
        }
        if (rule == "italian")
            return absolute is 8 or 11 or 80 or 800 ? "many" : "other";
        if (rule == "swedish")
        {
            decimal mod100 = absolute % 100;
            decimal mod10 = absolute % 10;
            return (mod10 is 1 or 2) && (mod100 is not (11 or 12)) ? "one" : "other";
        }
        if (rule == "one") return absolute == 1 ? "one" : "other";
        if (rule == "other") return "other";
        if (rule == "danish")
            return absolute == 1 || (absolute != decimal.Truncate(absolute) && decimal.Truncate(absolute) is 0 or 1) ? "one" : "other";
        if (rule == "integer-one") return absolute == 1 ? "one" : "other";
        if (rule == "one-and-million")
        {
            if (absolute == 1) return "one";
            return absolute != 0 && absolute == decimal.Truncate(absolute) && absolute % 1_000_000 == 0 ? "many" : "other";
        }
        if (rule == "french")
        {
            if (absolute != 0 && absolute == decimal.Truncate(absolute) && absolute % 1_000_000 == 0) return "many";
            return decimal.Truncate(absolute) is 0 or 1 ? "one" : "other";
        }
        return "other";
    }
}
