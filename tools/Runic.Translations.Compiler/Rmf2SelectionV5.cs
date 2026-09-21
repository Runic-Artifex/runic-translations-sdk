using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Runic.Translations.Compiler;

// A pure rank model, shared by tests and the next generation/runtime slice. The
// caller supplies the pinned CLDR category; this foundation does not format text.
internal sealed record Rmf2SelectorValueV5(string Type, string Value, string? Category = null);
internal static class Rmf2SelectionV5
{
    internal static int Select(IReadOnlyList<Rmf2VariantV5> variants, IReadOnlyList<Rmf2SelectorV5> selectors, IReadOnlyList<Rmf2SelectorValueV5> values)
    {
        if (values.Count != selectors.Count) throw new ArgumentException("Selector value count does not match the message.", nameof(values));
        for (int index = 0; index < values.Count; index++) ValidateValue(selectors[index], values[index]);
        int best = -1; int[]? bestRank = null;
        for (int index = 0; index < variants.Count; index++)
        {
            var variant = variants[index];
            if (variant.Keys.Count != selectors.Count) throw new ArgumentException("Invalid variant key count.", nameof(variants));
            var ranks = new int[selectors.Count]; bool matches = true;
            for (int selector = 0; selector < ranks.Length; selector++)
            {
                ranks[selector] = Rank(variant.Keys[selector], selectors[selector], values[selector]);
                if (ranks[selector] < 0) { matches = false; break; }
            }
            if (matches && (bestRank is null || Compare(ranks, bestRank) > 0)) { best = index; bestRank = ranks; }
        }
        return best;
    }

    internal static int Rank(Rmf2KeyV5 key, Rmf2SelectorV5 selector, Rmf2SelectorValueV5 value)
    {
        ValidateValue(selector, value);
        if (key.Kind == "wildcard") return 0;
        if (value.Type is "int64" or "decimal")
        {
            if (!Rmf2DecimalV5.TryCanonicalize(value.Value, out string number)) throw new ArgumentException("Selector number is outside the portable decimal domain.", nameof(value));
            if (key.Canonical is not null) return key.Canonical == number ? 2 : -1;
            return selector.Function is "plural" or "ordinal" && key.Value!.Normalize(NormalizationForm.FormC) == value.Category ? 1 : -1;
        }
        return string.Equals(key.Value!.Normalize(NormalizationForm.FormC), value.Value.Normalize(NormalizationForm.FormC), StringComparison.Ordinal) ? 1 : -1;
    }
    private static void ValidateValue(Rmf2SelectorV5 selector, Rmf2SelectorValueV5 value)
    {
        if (value.Type != selector.Type) throw new ArgumentException("Selector value type does not match the message.", nameof(value));
        if (value.Type is "int64" or "decimal" && (!Rmf2DecimalV5.TryCanonicalize(value.Value, out string number) ||
            (value.Type == "int64" && !long.TryParse(number, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out _))))
            throw new ArgumentException("Selector number is outside its portable domain.", nameof(value));
        if (value.Type == "boolean" && value.Value is not ("true" or "false")) throw new ArgumentException("Invalid boolean selector value.", nameof(value));
    }
    private static int Compare(int[] left, int[] right)
    {
        for (int index = 0; index < left.Length; index++) if (left[index] != right[index]) return left[index].CompareTo(right[index]);
        return 0;
    }
}
