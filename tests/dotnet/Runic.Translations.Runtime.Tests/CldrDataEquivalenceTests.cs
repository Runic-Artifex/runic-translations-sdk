using System;
using System.Collections.Generic;
using System.Linq;
using TUnit.Core;

namespace Runic.Translations.Runtime.Tests;

/// <summary>
/// The CLDR tables and plural rules are data that the runtime decodes and interprets (D072.2, T4). These tests compare
/// them with <see cref="CldrOracle"/>, the same pinned data generated as code the way it was before: every table entry,
/// and every plural rule set and every CLDR locale over a sweep of integers, decimals and exponents.
/// </summary>
internal sealed class CldrDataEquivalenceTests
{
    [Test, DisplayName("decoded CLDR tables equal the tables generated as code")]
    public void Tables()
    {
        Same(CldrOracle.LikelyKeys, GeneratedLocaleTables.LikelyKeys, "LikelyKeys");
        Same(CldrOracle.LikelyValues, GeneratedLocaleTables.LikelyValues, "LikelyValues");
        Same(CldrOracle.ParentKeys, GeneratedLocaleTables.ParentKeys, "ParentKeys");
        Same(CldrOracle.ParentValues, GeneratedLocaleTables.ParentValues, "ParentValues");
        Same(CldrOracle.AliasKeys, GeneratedLocaleTables.AliasKeys, "AliasKeys");
        Same(CldrOracle.AliasValues, GeneratedLocaleTables.AliasValues, "AliasValues");
        Same(CldrOracle.CardinalKeys, GeneratedLocaleTables.CardinalKeys, "CardinalKeys");
        Same(CldrOracle.CardinalValues, GeneratedLocaleTables.CardinalValues, "CardinalValues");
        Same(CldrOracle.OrdinalKeys, GeneratedLocaleTables.OrdinalKeys, "OrdinalKeys");
        Same(CldrOracle.OrdinalValues, GeneratedLocaleTables.OrdinalValues, "OrdinalValues");
        Same(CldrOracle.GroupingKeys, GeneratedLocaleTables.GroupingKeys, "GroupingKeys");
        Same(CldrOracle.GroupingValues, GeneratedLocaleTables.GroupingValues, "GroupingValues");
        Same(CldrOracle.NumberIds, GeneratedLocaleTables.NumberIds, "NumberIds");
        Same(CldrOracle.WeekdayIds, GeneratedLocaleTables.WeekdayIds, "WeekdayIds");
        Same(CldrOracle.DayPeriodIds, GeneratedLocaleTables.DayPeriodIds, "DayPeriodIds");
        Same(CldrOracle.ListIds, GeneratedLocaleTables.ListIds, "ListIds");
        Same(CldrOracle.RelativeTimeIds, GeneratedLocaleTables.RelativeTimeIds, "RelativeTimeIds");
        Same(CldrOracle.RightToLeftScripts, GeneratedLocaleTables.RightToLeftScripts, "RightToLeftScripts");
    }

    [Test, DisplayName("plural rule bytecode selects as the compiled selectors over a sweep of operands")]
    public void RuleSets()
    {
        List<PluralOperands> sweep = Sweep(full: true);
        Assert.True(sweep.Count > 50_000, "Too few operands: " + sweep.Count);
        for (int rule = 0; rule < GeneratedLocaleTables.PluralRuleCount; rule++)
            foreach (PluralOperands operands in sweep)
            {
                PluralOperands o = operands;
                string expected = CldrOracle.ApplyPluralRule(rule, in o), actual = GeneratedLocaleData.ApplyPluralRule(rule, in o);
                if (!string.Equals(expected, actual, StringComparison.Ordinal))
                    Assert.Equal(expected, actual, $"rule {rule}: i={o.I} v={o.V} w={o.W} f={o.F} t={o.T} e={o.E}");
            }
        Assert.True(Throws(() => GeneratedLocaleData.ApplyPluralRule(GeneratedLocaleTables.PluralRuleCount, default)), "A rule past the last must throw.");
        Assert.True(Throws(() => GeneratedLocaleData.ApplyPluralRule(-1, default)), "A negative rule must throw.");
    }

    [Test, DisplayName("every CLDR locale selects cardinal and ordinal categories as the compiled selectors")]
    public void Locales()
    {
        List<PluralOperands> sweep = Sweep(full: false);
        var locales = new SortedSet<string>(StringComparer.Ordinal);
        foreach (string[] table in new[]
        {
            CldrOracle.CardinalKeys, CldrOracle.OrdinalKeys, CldrOracle.LikelyKeys, CldrOracle.LikelyValues, CldrOracle.ParentKeys,
            CldrOracle.AliasKeys, CldrOracle.NumberIds, CldrOracle.ListIds, CldrOracle.RelativeTimeIds,
        })
            locales.UnionWith(table);
        locales.UnionWith(["pt-AO", "pt-BR", "en-AU", "sr-Latn-RS", "zh-TW", "de-CH", "tlh", "und", "x-unknown"]);
        int checkedLocales = 0;
        foreach (string locale in locales)
        {
            if (GeneratedLocaleData.Canonical(locale) is not { } canonical) continue;
            foreach (bool ordinal in new[] { false, true })
            {
                int rule = CldrLocales.PluralRule(canonical, ordinal);
                foreach (PluralOperands operands in sweep)
                {
                    PluralOperands o = operands;
                    string? expected = rule < 0 ? null : CldrOracle.ApplyPluralRule(rule, in o);
                    string? actual = GeneratedLocaleData.SelectPlural(locale, ordinal, in o);
                    if (!string.Equals(expected, actual, StringComparison.Ordinal))
                        Assert.Equal(expected, actual, $"{locale} {(ordinal ? "ordinal" : "cardinal")}: i={o.I} v={o.V} f={o.F} e={o.E}");
                }
            }
            checkedLocales++;
        }
        Assert.True(checkedLocales > 400, "Too few locales: " + checkedLocales);
    }

    // CLDR operands of integers, decimals with one to four visible fraction digits (trailing zeros included), large
    // values around powers of ten and the moduli CLDR uses, and compact exponents.
    private static List<PluralOperands> Sweep(bool full)
    {
        var result = new List<PluralOperands>();
        int integers = full ? 20_000 : 1_200;
        for (int value = 0; value <= integers; value++) result.Add(PluralOperands.FromVisible((ulong)value, 0));
        int decimals = full ? 1_000 : 25;
        for (int whole = 0; whole <= decimals; whole++)
            for (int fraction = 0; fraction < 10; fraction++) result.Add(PluralOperands.FromVisible((ulong)(whole * 10 + fraction), 1));
        for (int whole = 0; whole <= (full ? 120 : 3); whole++)
            for (int fraction = 0; fraction < 100; fraction++) result.Add(PluralOperands.FromVisible((ulong)(whole * 100 + fraction), 2));
        for (int whole = 0; whole <= (full ? 12 : 1); whole++)
            for (int fraction = 0; fraction < 1000; fraction += full ? 1 : 7) result.Add(PluralOperands.FromVisible((ulong)(whole * 1000 + fraction), 3));
        foreach (ulong coefficient in new ulong[] { 10, 11, 100, 101, 110, 1000, 1001, 1010, 1100, 12340, 12345 })
            result.Add(PluralOperands.FromVisible(coefficient, 4));
        UInt128 power = 1;
        for (int exponent = 0; exponent <= 32; exponent++, power *= 10)
            foreach (int offset in new[] { -11, -2, -1, 0, 1, 2, 3, 11, 21, 1_000, 20_000, 100_000, 1_000_000 })
                if (offset >= 0 || power >= (UInt128)(-offset)) result.Add(PluralOperands.FromVisible(offset >= 0 ? power + (UInt128)offset : power - (UInt128)(-offset), 0));
        foreach (ulong million in new ulong[] { 1, 2, 3, 10, 21, 100 })
            foreach (ulong rest in new ulong[] { 0, 1, 1_000, 20_000, 40_000, 100_000, 999_999 })
                result.Add(PluralOperands.FromVisible(million * 1_000_000 + rest, 0));
        UInt128 large = UInt128.MaxValue / 10;
        result.Add(PluralOperands.FromVisible(large, 0));
        result.Add(PluralOperands.FromVisible(large, 5));
        for (int exponent = 1; exponent <= 8; exponent++)
            foreach (ulong coefficient in new ulong[] { 0, 1, 2, 5, 11, 15, 21, 100, 101, 1_000_000, 1_100_000, 2_000_000 })
            {
                result.Add(PluralOperands.FromVisible(coefficient, 0, exponent));
                result.Add(PluralOperands.FromVisible(coefficient, 1, exponent));
            }
        return result;
    }

    private static void Same<T>(T[] expected, T[] actual, string name)
    {
        Assert.Equal(expected.Length, actual.Length, name + " length");
        for (int index = 0; index < expected.Length; index++)
            if (!EqualityComparer<T>.Default.Equals(expected[index], actual[index])) Assert.Equal(expected[index], actual[index], $"{name}[{index}]");
        Assert.True(expected.SequenceEqual(actual), name);
    }

    private static bool Throws(Action action)
    {
        try { action(); return false; }
        catch (ArgumentOutOfRangeException) { return true; }
    }
}
