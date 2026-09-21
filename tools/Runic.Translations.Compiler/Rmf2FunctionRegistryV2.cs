using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Runic.Translations.Compiler;

internal sealed record Rmf2OptionRuleV2(string Name, string InputType, bool Literal, bool Dynamic,
    string Default, IReadOnlyList<string> Values, int? Minimum = null, int? Maximum = null)
{
    internal bool AcceptsLiteral(Rmf2ValueV5 value)
    {
        if (!Literal) return false;
        if (InputType == "string") return value.Kind == "string-literal" && Values.Contains(value.Value.Normalize(NormalizationForm.FormC), StringComparer.Ordinal);
        return value.Kind == "number-literal" && int.TryParse(value.Canonical, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int number) && number >= Minimum && number <= Maximum;
    }
}
internal sealed record Rmf2FunctionRuleV2(string Name, string InputType, string Selection,
    IReadOnlyList<Rmf2OptionRuleV2> Options);

internal static class Rmf2FunctionRegistryV2
{
    internal static readonly IReadOnlyList<Rmf2FunctionRuleV2> Functions = Array.AsReadOnly(new[] {
        Function("string", "string", "exact", Enum("select", "exact", false, "exact")),
        Function("integer", "int64", "plural", Enum("select", "plural", false, "plural", "ordinal", "exact"), Enum("useGrouping", "never", true, "always", "never")),
        Function("number", "decimal", "plural", Enum("select", "plural", false, "plural", "ordinal", "exact"), Enum("style", "decimal", true, "decimal", "percent"),
            new Rmf2OptionRuleV2("minimumFractionDigits", "int64", true, true, "0", Array.Empty<string>(), 0, 6),
            new Rmf2OptionRuleV2("maximumFractionDigits", "int64", true, true, "6 (decimal), 4 (percent)", Array.Empty<string>(), 0, 6)),
        Function("date", "date", "none", Enum("style", "iso", true, "iso", "short", "long")),
        Function("time", "time", "none", Enum("style", "iso", true, "iso", "short", "long")),
        Function("datetime", "datetime", "none", Enum("style", "iso", true, "iso", "short", "long")),
        Function("runic:uuid", "guid", "none", Enum("style", "d", true, "d", "n", "b", "p")),
        Function("runic:boolean", "boolean", "exact", Enum("select", "exact", false, "exact")),
        Function("runic:relative-time", "decimal", "plural", Enum("unit", "day", true, "second", "minute", "hour", "day", "week", "month", "year"), Enum("numeric", "always", true, "always", "auto")),
    });

    internal static Rmf2FunctionRuleV2? Find(string name) => Functions.FirstOrDefault(function => function.Name == name);
    private static Rmf2FunctionRuleV2 Function(string name, string type, string selection, params Rmf2OptionRuleV2[] options) => new(name, type, selection, Array.AsReadOnly(options));
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
        if (function == "number")
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
