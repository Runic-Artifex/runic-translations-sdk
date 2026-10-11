using System;
using System.Globalization;
using System.Linq;
using TUnit.Core;

namespace Runic.Translations.Runtime.Tests;

internal sealed class FormatterTests
{
    [Test, DisplayName("formatter substitutes arguments independent of order")]
    public void SubstitutesByName()
    {
        TextArgument[] args = [new("second", "B"), new("first", "A")];
        Assert.Equal("A/B", TextPatternFormatter.Format("{first}/{second}", args, "en-US"));
    }

    [Test, DisplayName("formatter supports repeated placeholders")]
    public void RepeatedPlaceholder() =>
        Assert.Equal("x-x", TextPatternFormatter.Format("{v}-{v}", [new TextArgument("v", "x")], "en-US"));

    [Test, DisplayName("formatter renders escaped braces")]
    public void EscapedBraces() =>
        Assert.Equal("{ok} x", TextPatternFormatter.Format("{{ok}} {v}", [new TextArgument("v", "x")], "en-US"));

    [Test, DisplayName("formatter renders string")]
    public void StringValue() => FormatSingle(new TextArgument("value", "hello"), "hello");

    [Test, DisplayName("formatter renders invariant plain integer")]
    public void PlainInteger() => FormatSingle(new TextArgument("value", -1234), "-1234");

    [Test, DisplayName("formatter renders invariant plain number")]
    public void PlainNumber() => FormatSingle(new TextArgument("value", 1234.50m), "1234.5");

    private static void FormatSingle(TextArgument argument, string expected) =>
        Assert.Equal(expected, TextPatternFormatter.Format("{value}", [argument], "en-US"));

    [Test, DisplayName("formatter renders grouped integer")]
    public void GroupedInteger()
    {
        CultureInfo culture = CultureInfo.GetCultureInfo("en-US");
        string expected = 1234567L.ToString("N0", culture);
        FormatSingle(new TextArgument("value", 1234567L, TextArgumentFormat.Grouped), expected);
    }

    [Test, DisplayName("formatter renders grouped number")]
    public void GroupedNumber()
    {
        CultureInfo culture = CultureInfo.GetCultureInfo("de-DE");
        string expected = 1234.5m.ToString("#,0.############################", culture);
        Assert.Equal(expected, TextPatternFormatter.Format("{value}",
            [new TextArgument("value", 1234.5m, TextArgumentFormat.Grouped)], "de-DE"));
    }

    [Test, DisplayName("formatter renders fixed$precision")]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    [Arguments(4)]
    [Arguments(5)]
    [Arguments(6)]
    public void Fixed(int precision)
    {
        TextArgumentFormat format = Enum.Parse<TextArgumentFormat>("Fixed" + precision.ToString(CultureInfo.InvariantCulture));
        string expected = 12.34567m.ToString("F" + precision.ToString(CultureInfo.InvariantCulture), CultureInfo.GetCultureInfo("en-US"));
        FormatSingle(new TextArgument("value", 12.34567m, format), expected);
    }

    [Test, DisplayName("formatter renders percent$precision")]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    [Arguments(4)]
    public void Percent(int precision)
    {
        TextArgumentFormat format = Enum.Parse<TextArgumentFormat>("Percent" + precision.ToString(CultureInfo.InvariantCulture));
        string expected = 0.12345m.ToString("P" + precision.ToString(CultureInfo.InvariantCulture), CultureInfo.GetCultureInfo("en-US"));
        FormatSingle(new TextArgument("value", 0.12345m, format), expected);
    }

    [Test, DisplayName("formatter renders lowercase Boolean")]
    public void Boolean()
    {
        FormatSingle(new TextArgument("value", true), "true");
        FormatSingle(new TextArgument("value", false), "false");
    }

    [Test, DisplayName("formatter renders date formats")]
    public void DateFormats()
    {
        DateOnly value = new(2026, 7, 22);
        CultureInfo culture = CultureInfo.GetCultureInfo("en-US");
        FormatSingle(new TextArgument("value", value, TextArgumentFormat.Iso), "2026-07-22");
        FormatSingle(new TextArgument("value", value, TextArgumentFormat.Short), value.ToString("d", culture));
        FormatSingle(new TextArgument("value", value, TextArgumentFormat.Medium), value.ToString("d MMM yyyy", culture));
        FormatSingle(new TextArgument("value", value, TextArgumentFormat.Long), value.ToString("D", culture));
    }

    [Test, DisplayName("formatter renders time formats")]
    public void TimeFormats()
    {
        TimeOnly value = new(13, 14, 15, 123);
        CultureInfo culture = CultureInfo.GetCultureInfo("en-US");
        FormatSingle(new TextArgument("value", value, TextArgumentFormat.Iso), "13:14:15.123");
        FormatSingle(new TextArgument("value", value, TextArgumentFormat.Short), value.ToString("t", culture));
        FormatSingle(new TextArgument("value", value, TextArgumentFormat.Medium), value.ToString("T", culture));
    }

    [Test, DisplayName("formatter renders date-time formats")]
    public void DateTimeFormats()
    {
        DateTimeOffset value = new(2026, 7, 22, 13, 14, 15, TimeSpan.FromHours(2));
        CultureInfo culture = CultureInfo.GetCultureInfo("en-US");
        FormatSingle(new TextArgument("value", value, TextArgumentFormat.Iso), "2026-07-22T11:14:15.0000000Z");
        FormatSingle(new TextArgument("value", value, TextArgumentFormat.Short), value.ToString("g", culture));
        FormatSingle(new TextArgument("value", value, TextArgumentFormat.Medium), value.ToString("G", culture));
        FormatSingle(new TextArgument("value", value, TextArgumentFormat.Long), value.ToString("F", culture));
    }

    [Test, DisplayName("formatter renders GUID formats")]
    public void GuidFormats()
    {
        Guid value = Guid.Parse("01234567-89ab-cdef-0123-456789abcdef");
        FormatSingle(new TextArgument("value", value, TextArgumentFormat.D), value.ToString("D"));
        FormatSingle(new TextArgument("value", value, TextArgumentFormat.N), value.ToString("N"));
    }

    [Test, DisplayName("formatter rejects missing argument")]
    public void MissingArgument() => Assert.Throws<TranslationFormatException>(
        () => TextPatternFormatter.Format("{missing}", [], "en-US"), "was not supplied");

    [Test, DisplayName("formatter rejects extra argument")]
    public void ExtraArgument() => Assert.Throws<TranslationFormatException>(
        () => TextPatternFormatter.Format("literal", [new TextArgument("extra", "x")], "en-US"), "Unknown argument");

    [Test, DisplayName("formatter rejects duplicate argument")]
    public void DuplicateArgument() => Assert.Throws<TranslationFormatException>(
        () => TextPatternFormatter.Format("{v}", [new TextArgument("v", "a"), new TextArgument("v", "b")], "en-US"), "more than once");

    [Test, DisplayName("formatter rejects unmatched opening brace")]
    public void InvalidOpeningBrace() => Assert.Throws<TranslationFormatException>(
        () => TextPatternFormatter.Format("bad {", [], "en-US"), "character 4");

    [Test, DisplayName("formatter rejects unmatched closing brace")]
    public void InvalidClosingBrace() => Assert.Throws<TranslationFormatException>(
        () => TextPatternFormatter.Format("bad }", [], "en-US"), "character 4");

    [Test, DisplayName("formatter rejects nested placeholder")]
    public void NestedPlaceholder() => Assert.Throws<TranslationFormatException>(
        () => TextPatternFormatter.Format("{a{b}", [], "en-US"), "invalid");

    [Test, DisplayName("formatter rejects invalid placeholder name")]
    public void InvalidPlaceholder() => Assert.Throws<TranslationFormatException>(
        () => TextPatternFormatter.Format("{not-valid}", [], "en-US"), "invalid placeholder");

    [Test, DisplayName("formatter enforces argument bound")]
    public void ArgumentBound()
    {
        TextArgument[] args = Enumerable.Range(0, TextPatternFormatter.MaximumArguments + 1)
            .Select(i => new TextArgument("a" + i.ToString(CultureInfo.InvariantCulture), i.ToString(CultureInfo.InvariantCulture))).ToArray();
        Assert.Throws<TranslationFormatException>(() => TextPatternFormatter.Format("", args, "en-US"), "exceeds");
    }

    [Test, DisplayName("formatter accepts exact argument bound")]
    public void ExactArgumentBound()
    {
        TextArgument[] args = Enumerable.Range(0, TextPatternFormatter.MaximumArguments)
            .Select(i => new TextArgument("a" + i.ToString(CultureInfo.InvariantCulture), "x")).ToArray();
        string pattern = string.Concat(args.Select(a => "{" + a.Name + "}"));
        Assert.Equal(new string('x', TextPatternFormatter.MaximumArguments), TextPatternFormatter.Format(pattern, args, "en-US"));
    }

    [Test, DisplayName("formatter enforces output bound for literal")]
    public void LiteralOutputBound() => Assert.Throws<TranslationFormatException>(
        () => TextPatternFormatter.Format("12345", [], "en-US", maximumOutputLength: 4), "output limit");

    [Test, DisplayName("formatter enforces output bound for substitution")]
    public void SubstitutionOutputBound() => Assert.Throws<TranslationFormatException>(
        () => TextPatternFormatter.Format("{v}", [new TextArgument("v", "12345")], "en-US", maximumOutputLength: 4), "output limit");

    [Test, DisplayName("formatter accepts exact output bound")]
    public void ExactOutputBound() =>
        Assert.Equal("1234", TextPatternFormatter.Format("12{v}", [new TextArgument("v", "34")], "en-US", maximumOutputLength: 4));

    [Test, DisplayName("formatter rejects nonpositive output bound")]
    public void InvalidOutputBound() => Assert.Throws<ArgumentOutOfRangeException>(
        () => TextPatternFormatter.Format("", [], "en-US", maximumOutputLength: 0));

    [Test, DisplayName("formatter rejects invalid locale")]
    public void InvalidLocale() => Assert.Throws<TranslationFormatException>(
        () => TextPatternFormatter.Format("{v}", [new TextArgument("v", 1L, TextArgumentFormat.Grouped)], "not_a_locale!"), "not available");

    [Test, DisplayName("formatter invariant format does not require locale data")]
    public void InvariantFormatWithoutLocaleData() => Assert.Equal(
        "1", TextPatternFormatter.Format("{v}", [new TextArgument("v", 1L)], "not_a_locale!"));

    [Test, DisplayName("formatter rejects null custom output")]
    public void NullCustomOutput() => Assert.Throws<TranslationFormatException>(
        () => TextPatternFormatter.Format("{v}", [new TextArgument("v", "x")], "en-US", new NullFormatter()), "returned null");

    [Test, DisplayName("text argument validates name and type-format pairs")]
    public void ArgumentValidation()
    {
        Assert.Throws<ArgumentException>(() => _ = new TextArgument("not-valid", "x"));
        Assert.Throws<ArgumentOutOfRangeException>(() => _ = new TextArgument("v", 1L, TextArgumentFormat.Fixed1));
        Assert.Throws<ArgumentOutOfRangeException>(() => _ = new TextArgument("v", true, TextArgumentFormat.Iso));
    }

    [Test, DisplayName("default text argument is rejected")]
    public void DefaultArgument() => Assert.Throws<TranslationFormatException>(
        () => TextPatternFormatter.Format("literal", [default], "en-US"), "invalid name");

    private sealed class NullFormatter : ITextValueFormatter
    {
        public string Format(in TextArgument value, string resourceLocale) => null!;
    }
}
