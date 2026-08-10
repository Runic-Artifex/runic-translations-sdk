using System;
using System.Globalization;
using System.Linq;

namespace RunicTranslations.Runtime.Tests;

internal static class FormatterTests
{
    public static void Register(TestRunner runner)
    {
        runner.Add("formatter substitutes arguments independent of order", SubstitutesByName);
        runner.Add("formatter supports repeated placeholders", RepeatedPlaceholder);
        runner.Add("formatter renders escaped braces", EscapedBraces);
        runner.Add("formatter renders string", () => FormatSingle(new TextArgument("value", "hello"), "hello"));
        runner.Add("formatter renders invariant plain integer", () => FormatSingle(new TextArgument("value", -1234), "-1234"));
        runner.Add("formatter renders grouped integer", GroupedInteger);
        runner.Add("formatter renders invariant plain number", () => FormatSingle(new TextArgument("value", 1234.50m), "1234.5"));
        runner.Add("formatter renders grouped number", GroupedNumber);
        for (int precision = 0; precision <= 6; precision++)
        {
            int captured = precision;
            runner.Add($"formatter renders fixed{captured}", () => Fixed(captured));
        }
        for (int precision = 0; precision <= 4; precision++)
        {
            int captured = precision;
            runner.Add($"formatter renders percent{captured}", () => Percent(captured));
        }
        runner.Add("formatter renders lowercase Boolean", Boolean);
        runner.Add("formatter renders date formats", DateFormats);
        runner.Add("formatter renders time formats", TimeFormats);
        runner.Add("formatter renders date-time formats", DateTimeFormats);
        runner.Add("formatter renders GUID formats", GuidFormats);
        runner.Add("formatter rejects missing argument", MissingArgument);
        runner.Add("formatter rejects extra argument", ExtraArgument);
        runner.Add("formatter rejects duplicate argument", DuplicateArgument);
        runner.Add("formatter rejects unmatched opening brace", InvalidOpeningBrace);
        runner.Add("formatter rejects unmatched closing brace", InvalidClosingBrace);
        runner.Add("formatter rejects nested placeholder", NestedPlaceholder);
        runner.Add("formatter rejects invalid placeholder name", InvalidPlaceholder);
        runner.Add("formatter enforces argument bound", ArgumentBound);
        runner.Add("formatter accepts exact argument bound", ExactArgumentBound);
        runner.Add("formatter enforces output bound for literal", LiteralOutputBound);
        runner.Add("formatter enforces output bound for substitution", SubstitutionOutputBound);
        runner.Add("formatter accepts exact output bound", ExactOutputBound);
        runner.Add("formatter rejects nonpositive output bound", InvalidOutputBound);
        runner.Add("formatter rejects invalid locale", InvalidLocale);
        runner.Add("formatter invariant format does not require locale data", InvariantFormatWithoutLocaleData);
        runner.Add("formatter rejects null custom output", NullCustomOutput);
        runner.Add("text argument validates name and type-format pairs", ArgumentValidation);
        runner.Add("default text argument is rejected", DefaultArgument);
    }

    private static void SubstitutesByName()
    {
        TextArgument[] args = [new("second", "B"), new("first", "A")];
        Assert.Equal("A/B", TextPatternFormatter.Format("{first}/{second}", args, "en-US"));
    }

    private static void RepeatedPlaceholder() =>
        Assert.Equal("x-x", TextPatternFormatter.Format("{v}-{v}", [new TextArgument("v", "x")], "en-US"));

    private static void EscapedBraces() =>
        Assert.Equal("{ok} x", TextPatternFormatter.Format("{{ok}} {v}", [new TextArgument("v", "x")], "en-US"));

    private static void FormatSingle(TextArgument argument, string expected) =>
        Assert.Equal(expected, TextPatternFormatter.Format("{value}", [argument], "en-US"));

    private static void GroupedInteger()
    {
        CultureInfo culture = CultureInfo.GetCultureInfo("en-US");
        string expected = 1234567L.ToString("N0", culture);
        FormatSingle(new TextArgument("value", 1234567L, TextArgumentFormat.Grouped), expected);
    }

    private static void GroupedNumber()
    {
        CultureInfo culture = CultureInfo.GetCultureInfo("de-DE");
        string expected = 1234.5m.ToString("#,0.############################", culture);
        Assert.Equal(expected, TextPatternFormatter.Format("{value}",
            [new TextArgument("value", 1234.5m, TextArgumentFormat.Grouped)], "de-DE"));
    }

    private static void Fixed(int precision)
    {
        TextArgumentFormat format = Enum.Parse<TextArgumentFormat>("Fixed" + precision.ToString(CultureInfo.InvariantCulture));
        string expected = 12.34567m.ToString("F" + precision.ToString(CultureInfo.InvariantCulture), CultureInfo.GetCultureInfo("en-US"));
        FormatSingle(new TextArgument("value", 12.34567m, format), expected);
    }

    private static void Percent(int precision)
    {
        TextArgumentFormat format = Enum.Parse<TextArgumentFormat>("Percent" + precision.ToString(CultureInfo.InvariantCulture));
        string expected = 0.12345m.ToString("P" + precision.ToString(CultureInfo.InvariantCulture), CultureInfo.GetCultureInfo("en-US"));
        FormatSingle(new TextArgument("value", 0.12345m, format), expected);
    }

    private static void Boolean()
    {
        FormatSingle(new TextArgument("value", true), "true");
        FormatSingle(new TextArgument("value", false), "false");
    }

    private static void DateFormats()
    {
        DateOnly value = new(2026, 7, 22);
        CultureInfo culture = CultureInfo.GetCultureInfo("en-US");
        FormatSingle(new TextArgument("value", value, TextArgumentFormat.Iso), "2026-07-22");
        FormatSingle(new TextArgument("value", value, TextArgumentFormat.Short), value.ToString("d", culture));
        FormatSingle(new TextArgument("value", value, TextArgumentFormat.Medium), value.ToString("d MMM yyyy", culture));
        FormatSingle(new TextArgument("value", value, TextArgumentFormat.Long), value.ToString("D", culture));
    }

    private static void TimeFormats()
    {
        TimeOnly value = new(13, 14, 15, 123);
        CultureInfo culture = CultureInfo.GetCultureInfo("en-US");
        FormatSingle(new TextArgument("value", value, TextArgumentFormat.Iso), "13:14:15.123");
        FormatSingle(new TextArgument("value", value, TextArgumentFormat.Short), value.ToString("t", culture));
        FormatSingle(new TextArgument("value", value, TextArgumentFormat.Medium), value.ToString("T", culture));
    }

    private static void DateTimeFormats()
    {
        DateTimeOffset value = new(2026, 7, 22, 13, 14, 15, TimeSpan.FromHours(2));
        CultureInfo culture = CultureInfo.GetCultureInfo("en-US");
        FormatSingle(new TextArgument("value", value, TextArgumentFormat.Iso), "2026-07-22T11:14:15.0000000Z");
        FormatSingle(new TextArgument("value", value, TextArgumentFormat.Short), value.ToString("g", culture));
        FormatSingle(new TextArgument("value", value, TextArgumentFormat.Medium), value.ToString("G", culture));
        FormatSingle(new TextArgument("value", value, TextArgumentFormat.Long), value.ToString("F", culture));
    }

    private static void GuidFormats()
    {
        Guid value = Guid.Parse("01234567-89ab-cdef-0123-456789abcdef");
        FormatSingle(new TextArgument("value", value, TextArgumentFormat.D), value.ToString("D"));
        FormatSingle(new TextArgument("value", value, TextArgumentFormat.N), value.ToString("N"));
    }

    private static void MissingArgument() => Assert.Throws<TranslationFormatException>(
        () => TextPatternFormatter.Format("{missing}", [], "en-US"), "was not supplied");

    private static void ExtraArgument() => Assert.Throws<TranslationFormatException>(
        () => TextPatternFormatter.Format("literal", [new TextArgument("extra", "x")], "en-US"), "Unknown argument");

    private static void DuplicateArgument() => Assert.Throws<TranslationFormatException>(
        () => TextPatternFormatter.Format("{v}", [new TextArgument("v", "a"), new TextArgument("v", "b")], "en-US"), "more than once");

    private static void InvalidOpeningBrace() => Assert.Throws<TranslationFormatException>(
        () => TextPatternFormatter.Format("bad {", [], "en-US"), "character 4");

    private static void InvalidClosingBrace() => Assert.Throws<TranslationFormatException>(
        () => TextPatternFormatter.Format("bad }", [], "en-US"), "character 4");

    private static void NestedPlaceholder() => Assert.Throws<TranslationFormatException>(
        () => TextPatternFormatter.Format("{a{b}", [], "en-US"), "invalid");

    private static void InvalidPlaceholder() => Assert.Throws<TranslationFormatException>(
        () => TextPatternFormatter.Format("{not-valid}", [], "en-US"), "invalid placeholder");

    private static void ArgumentBound()
    {
        TextArgument[] args = Enumerable.Range(0, TextPatternFormatter.MaximumArguments + 1)
            .Select(i => new TextArgument("a" + i.ToString(CultureInfo.InvariantCulture), i.ToString(CultureInfo.InvariantCulture))).ToArray();
        Assert.Throws<TranslationFormatException>(() => TextPatternFormatter.Format("", args, "en-US"), "exceeds");
    }

    private static void ExactArgumentBound()
    {
        TextArgument[] args = Enumerable.Range(0, TextPatternFormatter.MaximumArguments)
            .Select(i => new TextArgument("a" + i.ToString(CultureInfo.InvariantCulture), "x")).ToArray();
        string pattern = string.Concat(args.Select(a => "{" + a.Name + "}"));
        Assert.Equal(new string('x', TextPatternFormatter.MaximumArguments), TextPatternFormatter.Format(pattern, args, "en-US"));
    }

    private static void LiteralOutputBound() => Assert.Throws<TranslationFormatException>(
        () => TextPatternFormatter.Format("12345", [], "en-US", maximumOutputLength: 4), "output limit");

    private static void SubstitutionOutputBound() => Assert.Throws<TranslationFormatException>(
        () => TextPatternFormatter.Format("{v}", [new TextArgument("v", "12345")], "en-US", maximumOutputLength: 4), "output limit");

    private static void ExactOutputBound() =>
        Assert.Equal("1234", TextPatternFormatter.Format("12{v}", [new TextArgument("v", "34")], "en-US", maximumOutputLength: 4));

    private static void InvalidOutputBound() => Assert.Throws<ArgumentOutOfRangeException>(
        () => TextPatternFormatter.Format("", [], "en-US", maximumOutputLength: 0));

    private static void InvalidLocale() => Assert.Throws<TranslationFormatException>(
        () => TextPatternFormatter.Format("{v}", [new TextArgument("v", 1L, TextArgumentFormat.Grouped)], "not_a_locale!"), "not available");

    private static void InvariantFormatWithoutLocaleData() => Assert.Equal(
        "1", TextPatternFormatter.Format("{v}", [new TextArgument("v", 1L)], "not_a_locale!"));

    private static void NullCustomOutput() => Assert.Throws<TranslationFormatException>(
        () => TextPatternFormatter.Format("{v}", [new TextArgument("v", "x")], "en-US", new NullFormatter()), "returned null");

    private static void ArgumentValidation()
    {
        Assert.Throws<ArgumentException>(() => _ = new TextArgument("not-valid", "x"));
        Assert.Throws<ArgumentOutOfRangeException>(() => _ = new TextArgument("v", 1L, TextArgumentFormat.Fixed1));
        Assert.Throws<ArgumentOutOfRangeException>(() => _ = new TextArgument("v", true, TextArgumentFormat.Iso));
    }

    private static void DefaultArgument() => Assert.Throws<TranslationFormatException>(
        () => TextPatternFormatter.Format("literal", [default], "en-US"), "invalid name");

    private sealed class NullFormatter : ITextValueFormatter
    {
        public string Format(in TextArgument value, string resourceLocale) => null!;
    }
}
