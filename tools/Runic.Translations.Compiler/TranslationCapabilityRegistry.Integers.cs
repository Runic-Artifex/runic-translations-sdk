using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace Runic.Translations.Compiler;

internal static partial class TranslationCapabilityRegistry
{
    private static readonly ConcurrentDictionary<int, string[]> s_integerCategories = new();

    /// <summary>
    /// The plural categories an integer operand (<c>:integer</c>, no visible fraction digits) can select under a locale's
    /// cardinal or ordinal rules, in CLDR order: Czech <c>many</c> is for fractions only. Empty without rules.
    /// </summary>
    internal static IReadOnlyList<string> IntegerPluralCategories(string locale, bool ordinal)
    {
        int rule = Canonical(locale) is { } canonical ? CldrLocales.PluralRule(canonical, ordinal) : -1;
        return rule < 0 ? Array.Empty<string>() : s_integerCategories.GetOrAdd(rule, static index =>
        {
            // "(o) => { … }": the reachability evaluator reads the statements between the braces.
            string source = EsmPluralRuleSources[index];
            int open = source.IndexOf('{'), close = source.LastIndexOf('}');
            IReadOnlySet<string>? reachable = open < 0 || close <= open ? null : PluralRuleReachability.IntegerCategories(source[(open + 1)..close]);
            return reachable is null ? PluralRuleCategories[index] : PluralRuleCategories[index].Where(reachable.Contains).ToArray();
        });
    }
}
