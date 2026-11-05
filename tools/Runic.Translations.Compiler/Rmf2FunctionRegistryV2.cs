using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Runic.Translations.Compiler;

internal sealed record Rmf2OptionRuleV2(string Name, string InputType, bool Literal, bool Dynamic,
    string Default, IReadOnlyList<string> Values, int? Minimum = null, int? Maximum = null, string? Pattern = null)
{
    /// <summary>A literal string outside <see cref="Values"/> that the option accepts: a pinned currency or unit.</summary>
    internal Func<string, bool>? Accepts { get; init; }
    internal bool AcceptsLiteral(Rmf2ValueV5 value)
    {
        if (!Literal) return false;
        if (InputType == "string") return value.Kind == "string-literal" && (Values.Contains(value.Value.Normalize(NormalizationForm.FormC), StringComparer.Ordinal) ||
            Pattern is not null && Rmf2FunctionRegistryV2.IsTimeZone(value.Value) || Accepts?.Invoke(value.Value) == true);
        return value.Kind == "number-literal" && int.TryParse(value.Canonical, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int number) && number >= Minimum && number <= Maximum;
    }
}
internal sealed record Rmf2FunctionRuleV2(string Name, string InputType, string Selection,
    IReadOnlyList<Rmf2OptionRuleV2> Options);

internal static class Rmf2FunctionRegistryV2
{
    internal static readonly IReadOnlyList<Rmf2FunctionRuleV2> Functions = Array.AsReadOnly(new[] {
        Function("string", "string", "exact", Enum("select", "exact", false, "exact")),
        Function("integer", "int64", "plural", Enum("select", "plural", false, "plural", "ordinal", "exact"), Grouping()),
        Function("number", "decimal", "plural", Enum("select", "plural", false, "plural", "ordinal", "exact"), Enum("style", "decimal", true, "decimal", "percent"), Grouping(),
            new Rmf2OptionRuleV2("minimumFractionDigits", "int64", true, true, "0", Array.Empty<string>(), 0, 6),
            new Rmf2OptionRuleV2("maximumFractionDigits", "int64", true, true, "6 (decimal), 4 (percent)", Array.Empty<string>(), 0, 6),
            // MF2 has no notation option, so Runic's are namespaced (MF2 functions: "Such options MUST use an implementation-specific namespace").
            Enum(Rmf2FormatDataV1.NotationOption, "standard", true, "standard", "compact"), Enum(Rmf2FormatDataV1.CompactDisplayOption, "short", true, "short", "long")),
        // MF2 :currency and :unit: formatters only (no selection), over a number with a pinned ISO 4217 code or unit.
        Function(Rmf2FormatDataV1.CurrencyFunction, "decimal", "none",
            new Rmf2OptionRuleV2("currency", "string", true, true, "(required)", Array.Empty<string>()) { Accepts = Rmf2FormatDataV1.IsCurrency },
            Enum("currencyDisplay", "symbol", true, "symbol", "narrowSymbol", "code", "name"), Enum("currencySign", "standard", true, "standard", "accounting"), Grouping(),
            new Rmf2OptionRuleV2("fractionDigits", "int64", true, true, "the currency's CLDR digits", Array.Empty<string>(), 0, 6)),
        Function(Rmf2FormatDataV1.UnitFunction, "decimal", "none",
            new Rmf2OptionRuleV2("unit", "string", true, true, "(required)", Array.Empty<string>()) { Accepts = Rmf2FormatDataV1.IsUnit },
            Enum("unitDisplay", "short", true, "short", "long", "narrow"), Grouping(),
            new Rmf2OptionRuleV2("minimumFractionDigits", "int64", true, true, "0", Array.Empty<string>(), 0, 6),
            new Rmf2OptionRuleV2("maximumFractionDigits", "int64", true, true, "6", Array.Empty<string>(), 0, 6)),
        Function("date", "date", "none", Enum("style", "iso", true, "iso", "short", "medium", "long", "full")),
        Function("time", "time", "none", Enum("style", "iso", true, "iso", "short", "medium", "long", "full")),
        Function("datetime", "datetime", "none", Enum("style", "iso", true, "iso", "short", "medium", "long", "full"),
            new Rmf2OptionRuleV2("timeZone", "string", true, true, "local", Array.AsReadOnly(new[] { "local", "utc" }), Pattern: TimeZonePattern)),
        Function("runic:uuid", "guid", "none", Enum("style", "d", true, "d", "n", "b", "p")),
        Function("runic:boolean", "boolean", "exact", Enum("select", "exact", false, "exact")),
        Function("runic:relative-time", "decimal", "plural", Enum("unit", "day", true, "second", "minute", "hour", "day", "week", "month", "year"), Enum("numeric", "always", true, "always", "auto")),
        Function("runic:list", "list", "none", Enum("type", "conjunction", true, "conjunction", "disjunction", "unit"), Enum("style", "long", true, "long", "short", "narrow")),
    });

    /// <summary>
    /// The syntax of a <c>:datetime timeZone</c> value besides <c>local</c> and <c>utc</c>: an IANA time zone ID such as
    /// <c>Europe/Berlin</c> or <c>Etc/GMT+5</c>, at most 64 characters. Whether the host knows the zone is checked when formatting.
    /// </summary>
    internal const string TimeZonePattern = "^[A-Za-z][A-Za-z0-9_+-]*(?:/[A-Za-z0-9_+-]+)*$";
    internal static bool IsTimeZone(string value) => value.Length is > 0 and <= 64 && System.Text.RegularExpressions.Regex.IsMatch(value, TimeZonePattern, System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    internal static Rmf2FunctionRuleV2? Find(string name) => Functions.FirstOrDefault(function => function.Name == name);
    // Every function also takes MF2's u:dir (LDML 48 u: options), a literal that sets the placeholder's direction for bidi isolation.
    private static Rmf2FunctionRuleV2 Function(string name, string type, string selection, params Rmf2OptionRuleV2[] options) =>
        new(name, type, selection, Array.AsReadOnly([.. options, Enum("u:dir", "the function's", false, "ltr", "rtl", "auto", "inherit")]));
    // Unicode MessageFormat 2 spec/functions/number.md: useGrouping on :number and :integer, default auto.
    private static Rmf2OptionRuleV2 Grouping() => Enum("useGrouping", "auto", true, "auto", "always", "never", "min2");
    private static Rmf2OptionRuleV2 Enum(string name, string fallback, bool dynamic, params string[] values) => new(name, "string", true, dynamic, fallback, Array.AsReadOnly(values));

    // Used both at compile time for fully static options and by the future runtime
    // adapter after dynamic values have been resolved. Errors never clamp values.
    internal static string? ValidateResolvedOptions(string function, IReadOnlyDictionary<string, Rmf2ValueV5> supplied)
    {
        var rule = Find(function);
        if (rule is null) return "Unknown function ':" + function + "'.";
        foreach (var pair in supplied)
        {
            var option = rule.Options.FirstOrDefault(item => item.Name == pair.Key);
            if (option is null || !option.AcceptsLiteral(pair.Value)) return "Invalid option '" + pair.Key + "' for ':" + function + "'.";
        }
        if (function == "number" && supplied.TryGetValue(Rmf2FormatDataV1.NotationOption, out var notation) && notation.Value == "compact" &&
            supplied.TryGetValue("style", out var compactStyle) && compactStyle.Value == "percent")
            return "Compact notation does not apply to percentages.";
        if (function is "number" or Rmf2FormatDataV1.UnitFunction)
        {
            bool percent = supplied.TryGetValue("style", out var style) && style.Value == "percent";
            int limit = percent ? 4 : 6;
            int minimum = supplied.TryGetValue("minimumFractionDigits", out var min) ? int.Parse(min.Canonical!, CultureInfo.InvariantCulture) : 0;
            int maximum = supplied.TryGetValue("maximumFractionDigits", out var max) ? int.Parse(max.Canonical!, CultureInfo.InvariantCulture) : limit;
            if (minimum > maximum || minimum > limit || maximum > limit) return "Fraction digits require 0 <= minimum <= maximum <= " + limit + ".";
        }
        return null;
    }
}
