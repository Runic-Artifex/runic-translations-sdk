using System;
using System.Globalization;

namespace RunicTextResources;

/// <summary>The closed relative-time registry shared by generated schema version 2 messages.</summary>
public static class TextRelativeTimeFormatter
{
    /// <summary>Formats a relative duration for a compiler-supported locale family.</summary>
    public static string Format(decimal value, string unit, string numeric, string locale)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(unit);
        ArgumentException.ThrowIfNullOrWhiteSpace(numeric);
        ArgumentException.ThrowIfNullOrWhiteSpace(locale);
        if (unit is not ("second" or "minute" or "hour" or "day" or "week" or "month" or "year"))
            throw new ArgumentOutOfRangeException(nameof(unit));
        if (numeric is not ("always" or "auto")) throw new ArgumentOutOfRangeException(nameof(numeric));
        string language = locale.Split('-')[0].ToLowerInvariant();
        if (numeric == "auto" && unit == "day" && value is >= -1 and <= 1 && decimal.Truncate(value) == value)
        {
            int day = (int)value;
            return language switch
            {
                "en" => day switch { -1 => "yesterday", 0 => "today", _ => "tomorrow" },
                "de" => day switch { -1 => "gestern", 0 => "heute", _ => "morgen" },
                "fr" => day switch { -1 => "hier", 0 => "aujourd’hui", _ => "demain" },
                _ => throw Unsupported(locale),
            };
        }
        decimal absolute = Math.Abs(value);
        string number = absolute.ToString(CultureInfo.InvariantCulture);
        bool one = TextMessageSelector.SelectPlural(absolute, locale, false) == "one";
        string noun = Noun(language, unit, one);
        return language switch
        {
            "en" => value < 0 ? number + " " + noun + " ago" : "in " + number + " " + noun,
            "de" => value < 0 ? "vor " + number + " " + noun : "in " + number + " " + noun,
            "fr" => value < 0 ? "il y a " + number + " " + noun : "dans " + number + " " + noun,
            _ => throw Unsupported(locale),
        };
    }

    private static string Noun(string language, string unit, bool one) => (language, unit, one) switch
    {
        ("en", "second", true) => "second", ("en", "second", false) => "seconds",
        ("en", "minute", true) => "minute", ("en", "minute", false) => "minutes",
        ("en", "hour", true) => "hour", ("en", "hour", false) => "hours",
        ("en", "day", true) => "day", ("en", "day", false) => "days",
        ("en", "week", true) => "week", ("en", "week", false) => "weeks",
        ("en", "month", true) => "month", ("en", "month", false) => "months",
        ("en", "year", true) => "year", ("en", "year", false) => "years",
        ("de", "second", true) => "Sekunde", ("de", "second", false) => "Sekunden",
        ("de", "minute", true) => "Minute", ("de", "minute", false) => "Minuten",
        ("de", "hour", true) => "Stunde", ("de", "hour", false) => "Stunden",
        ("de", "day", true) => "Tag", ("de", "day", false) => "Tagen",
        ("de", "week", true) => "Woche", ("de", "week", false) => "Wochen",
        ("de", "month", true) => "Monat", ("de", "month", false) => "Monaten",
        ("de", "year", true) => "Jahr", ("de", "year", false) => "Jahren",
        ("fr", "second", true) => "seconde", ("fr", "second", false) => "secondes",
        ("fr", "minute", true) => "minute", ("fr", "minute", false) => "minutes",
        ("fr", "hour", true) => "heure", ("fr", "hour", false) => "heures",
        ("fr", "day", true) => "jour", ("fr", "day", false) => "jours",
        ("fr", "week", true) => "semaine", ("fr", "week", false) => "semaines",
        ("fr", "month", true) => "mois", ("fr", "month", false) => "mois",
        ("fr", "year", true) => "an", ("fr", "year", false) => "ans",
        _ => throw Unsupported(language),
    };

    private static TextResourceFormatException Unsupported(string locale) =>
        new("Relative-time formatting is not supported for locale '" + locale + "'.");
}
