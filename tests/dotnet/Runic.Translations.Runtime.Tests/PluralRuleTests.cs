using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using TUnit.Core;

namespace Runic.Translations.Runtime.Tests;

internal sealed class PluralRuleTests
{
    [Test, DisplayName("generated CLDR selectors match every pinned @integer and @decimal sample")]
    public void CldrSamples()
    {
        string path = Path.Combine(RepositoryRoot(), "specs", "translations", "cldr", "runic-subset-48.2.json");
        using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(path));
        int checkedSamples = 0;
        foreach (JsonElement locale in document.RootElement.GetProperty("locales").EnumerateArray())
        {
            string tag = locale.GetProperty("tag").GetString()!;
            foreach (bool ordinal in new[] { false, true })
                foreach (JsonProperty rule in locale.GetProperty(ordinal ? "ordinal" : "cardinal").EnumerateObject())
                {
                    string text = rule.Value.GetString()!;
                    int at = text.IndexOf('@', StringComparison.Ordinal);
                    if (at < 0) continue;
                    foreach (string list in text[(at + 1)..].Split('@'))
                    {
                        string[] parts = list.Trim().Split(' ', 2);
                        Assert.True(parts[0] is "integer" or "decimal", "Unknown sample list in " + tag);
                        foreach (string sample in Expand(parts[1]))
                        {
                            PluralOperands operands = Operands(sample);
                            Assert.Equal(rule.Name, GeneratedLocaleData.SelectPlural(tag, ordinal, in operands), tag + (ordinal ? " ordinal " : " cardinal ") + sample);
                            checkedSamples++;
                        }
                    }
                }
        }
        Assert.True(checkedSamples > 500, "Too few CLDR samples were checked: " + checkedSamples);
        PluralOperands one = Operands("1");
        Assert.Equal(null, GeneratedLocaleData.SelectPlural("pl", false, in one));
    }

    [Test, DisplayName("visible decimal applies percent halfExpand rounding and fraction padding")]
    public void VisibleDecimals()
    {
        Assert.Equal("1.0", Visible(1m, false, 1, 6));
        Assert.Equal("1", Visible(1.4m, false, 0, 0));
        Assert.Equal("1", Visible(0.5m, false, 0, 0));
        Assert.Equal("-1", Visible(-0.5m, false, 0, 0));
        Assert.Equal("0", Visible(-0.4m, false, 0, 0));
        Assert.Equal("1.3", Visible(1.25m, false, 0, 1));
        Assert.Equal("1.50", Visible(1.5000m, false, 2, 6));
        Assert.Equal("1", Visible(1.000m, false, 0, 6));
        Assert.Equal("1", Visible(0.01m, true, 0, 4));
        Assert.Equal("12.5", Visible(0.125m, true, 0, 4));
        Assert.Equal("13", Visible(0.125m, true, 0, 0));
        Assert.Equal("7922816251426433759354395033500", Visible(decimal.MaxValue, true, 0, 4));
        Assert.Equal("7922816251426433759354395033500.0000", Visible(decimal.MaxValue, true, 4, 4));

        PluralOperands operands = VisibleDecimal.FromNumber(1.230m, false, 3, 6).Operands;
        Assert.Equal((UInt128)1, operands.I);
        Assert.Equal(3, operands.V);
        Assert.Equal(2, operands.W);
        Assert.Equal((UInt128)230, operands.F);
        Assert.Equal((UInt128)23, operands.T);
        Assert.Equal("1.23", VisibleDecimal.FromNumber(1.230m, false, 3, 6).ToCanonicalString());
        Assert.Equal("-9223372036854775808", VisibleDecimal.FromInteger(long.MinValue).ToCanonicalString());
        Assert.Equal((UInt128)9223372036854775808UL, VisibleDecimal.FromInteger(long.MinValue).Operands.I);
    }

    [Test, DisplayName("legacy selector and relative time use canonical or displayed operands")]
    public void LegacyOperands()
    {
        // The public decimal selector keeps canonical semantics: scale is not significant.
        Assert.Equal("one", TextMessageSelector.SelectPlural(1.0m, "en", false));
        Assert.Equal("many", TextMessageSelector.SelectPlural(1.5m, "cs", false));
        Assert.Equal("few", TextMessageSelector.SelectPlural(3m, "cs", false));
        Assert.Equal("other", TextMessageSelector.SelectPlural(1000000.5m, "fr", false));
        Assert.Equal("one", TextMessageSelector.SelectPlural(0.5m, "da", false));
        // Relative time agrees with the digits it displays.
        Assert.Equal("in 1 day", TextRelativeTimeFormatter.Format(1m, "day", "always", "en"));
        Assert.Equal("in 1.0 days", TextRelativeTimeFormatter.Format(1.0m, "day", "always", "en"));
    }

    private static string Visible(decimal value, bool percent, int minimum, int maximum)
    {
        VisibleDecimal visible = VisibleDecimal.FromNumber(value, percent, minimum, maximum);
        string digits = visible.Coefficient.ToString(CultureInfo.InvariantCulture).PadLeft(visible.Scale + 1, '0');
        string text = visible.Scale == 0 ? digits : digits.Insert(digits.Length - visible.Scale, ".");
        return visible.Negative ? "-" + text : text;
    }

    // CLDR sample notation: `0.0~1.5` steps by the last visible digit and
    // `1.1c6` is the compact spelling of 1100000 with exponent 6.
    private static IEnumerable<string> Expand(string samples)
    {
        foreach (string raw in samples.Split(','))
        {
            string sample = raw.Trim();
            if (sample.Length == 0 || sample == "…") continue;
            int tilde = sample.IndexOf('~', StringComparison.Ordinal);
            if (tilde < 0) { yield return sample; continue; }
            string first = sample[..tilde], last = sample[(tilde + 1)..];
            int scale = first.Contains('.', StringComparison.Ordinal) ? first.Length - first.IndexOf('.', StringComparison.Ordinal) - 1 : 0;
            long start = long.Parse(first.Replace(".", "", StringComparison.Ordinal), CultureInfo.InvariantCulture);
            long end = long.Parse(last.Replace(".", "", StringComparison.Ordinal), CultureInfo.InvariantCulture);
            for (long value = start; value <= end; value++)
            {
                string digits = value.ToString(CultureInfo.InvariantCulture).PadLeft(scale + 1, '0');
                yield return scale == 0 ? digits : digits.Insert(digits.Length - scale, ".");
            }
        }
    }

    private static PluralOperands Operands(string sample)
    {
        int compact = sample.IndexOf('c', StringComparison.Ordinal);
        int exponent = compact < 0 ? 0 : int.Parse(sample[(compact + 1)..], CultureInfo.InvariantCulture);
        string number = compact < 0 ? sample : sample[..compact];
        int point = number.IndexOf('.', StringComparison.Ordinal);
        int scale = (point < 0 ? 0 : number.Length - point - 1) - exponent;
        UInt128 coefficient = UInt128.Parse(number.Replace(".", "", StringComparison.Ordinal), CultureInfo.InvariantCulture);
        for (; scale < 0; scale++) coefficient *= 10;
        return PluralOperands.FromVisible(coefficient, scale, exponent);
    }

    private static string RepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Runic.Translations.slnx"))) return directory.FullName;
            directory = directory.Parent;
        }
        throw new InvalidOperationException("Could not locate repository root.");
    }
}
