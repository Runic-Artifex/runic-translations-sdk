using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace Runic.Translations.Compiler;

/// <summary>
/// The pinned CLDR currency, unit and compact-notation data (<c>specs/translations/cldr/runic-formats-48.2.json</c>) and
/// the part of it that a catalog embeds (D055.4): the currencies and units its messages use or declare in
/// <c>runic.json</c> <c>formats</c>, compact patterns when a message uses compact notation, for the CLDR locales of the
/// catalog's languages only. The generated ESM runtime and each .NET locale (and locale artifact) carry that selection;
/// neither runtime ships the data itself.
/// </summary>
internal static class Rmf2FormatDataV1
{
    private static readonly Lazy<JsonDocument> s_data = new(Load);
    private static readonly Lazy<HashSet<string>> s_currencies = new(() =>
        s_data.Value.RootElement.GetProperty("currencies").EnumerateObject().Select(item => item.Name).ToHashSet(StringComparer.Ordinal));
    private static readonly Lazy<HashSet<string>> s_units = new(() =>
        s_data.Value.RootElement.GetProperty("units").EnumerateArray().Select(item => item.GetString()!).ToHashSet(StringComparer.Ordinal));
    private static readonly Lazy<string[]> s_localeIds = new(() =>
        s_data.Value.RootElement.GetProperty("locales").EnumerateObject().Select(item => item.Name).Order(StringComparer.Ordinal).ToArray());

    /// <summary>The <c>:currency</c> function and its <c>currency</c> option.</summary>
    internal const string CurrencyFunction = "currency";
    /// <summary>The <c>:unit</c> function and its <c>unit</c> option.</summary>
    internal const string UnitFunction = "unit";
    /// <summary>The Runic options of <c>:number</c> that select compact notation (MF2 has none; its options need a namespace).</summary>
    internal const string NotationOption = "runic:notation";
    internal const string CompactDisplayOption = "runic:compactDisplay";

    /// <summary>The ISO 4217 codes CLDR lists as current tender somewhere, in ordinal order.</summary>
    internal static IReadOnlyList<string> Currencies => s_currencies.Value.Order(StringComparer.Ordinal).ToArray();

    /// <summary>The simple units ECMA-402 sanctions and CLDR's compound units of two of them, in ordinal order.</summary>
    internal static IReadOnlyList<string> Units => s_units.Value.Order(StringComparer.Ordinal).ToArray();

    internal static bool IsCurrency(string code) => code.Length == 3 && s_currencies.Value.Contains(code);

    /// <summary>A pinned unit, or <c>X-per-Y</c> of two pinned simple units, which the runtimes compose with CLDR's per patterns.</summary>
    internal static bool IsUnit(string unit)
    {
        if (s_units.Value.Contains(unit)) return true;
        int split = unit.IndexOf("-per-", StringComparison.Ordinal);
        return split > 0 && IsSimple(unit[..split]) && IsSimple(unit[(split + 5)..]);
        static bool IsSimple(string value) => !value.Contains("-per-", StringComparison.Ordinal) && s_units.Value.Contains(value);
    }

    /// <summary>Whether a content locale has pinned currency, unit and compact data (every CLDR locale with number data).</summary>
    internal static bool Supports(string locale) =>
        TranslationCompiler.TryCanonicalizeLocale(locale.Replace('_', '-'), out string canonical) && CldrLocales.Find(canonical, s_localeIds.Value, truncate: false) is not null;

    /// <summary>What a set of messages needs: the literal and declared currencies and units, and compact styles.</summary>
    internal sealed class Usage
    {
        internal SortedSet<string> Currencies { get; } = new(StringComparer.Ordinal);
        internal SortedSet<string> Units { get; } = new(StringComparer.Ordinal);
        internal SortedSet<string> CompactStyles { get; } = new(StringComparer.Ordinal);
        /// <summary>The <c>runic.json</c> sets that a run-time <c>currency</c> or <c>unit</c> value must come from; empty when no message reads one.</summary>
        internal SortedSet<string> DynamicCurrencies { get; } = new(StringComparer.Ordinal);
        internal SortedSet<string> DynamicUnits { get; } = new(StringComparer.Ordinal);
        internal bool IsEmpty => Currencies.Count == 0 && Units.Count == 0 && CompactStyles.Count == 0;
    }

    /// <summary>Collects what <paramref name="messages"/> use; dynamic options add the declared sets.</summary>
    internal static Usage Collect(IEnumerable<Rmf2MessageV5> messages, IReadOnlyCollection<string> declaredCurrencies, IReadOnlyCollection<string> declaredUnits)
    {
        var usage = new Usage();
        foreach (Rmf2MessageV5 message in messages)
            foreach (Rmf2ExpressionV5 expression in Expressions(message))
            {
                if (expression.Function == CurrencyFunction) Option(expression, "currency", usage.Currencies, usage.DynamicCurrencies, declaredCurrencies);
                else if (expression.Function == UnitFunction) Option(expression, "unit", usage.Units, usage.DynamicUnits, declaredUnits);
                else if (expression.Function == "number" && expression.Options.Any(option => option.Name == NotationOption && (option.Value.Kind != "string-literal" || option.Value.Value == "compact")))
                {
                    Rmf2OptionV5? display = expression.Options.FirstOrDefault(option => option.Name == CompactDisplayOption);
                    if (display is null || display.Value.Kind == "string-literal") usage.CompactStyles.Add(display?.Value.Value ?? "short");
                    else { usage.CompactStyles.Add("short"); usage.CompactStyles.Add("long"); }
                }
            }
        return usage;

        static void Option(Rmf2ExpressionV5 expression, string name, SortedSet<string> used, SortedSet<string> dynamic, IReadOnlyCollection<string> declared)
        {
            Rmf2OptionV5? option = expression.Options.FirstOrDefault(item => item.Name == name);
            if (option is null) return;
            if (option.Value.Kind == "string-literal") used.Add(option.Value.Value);
            else { used.UnionWith(declared); dynamic.UnionWith(declared); }
        }
    }

    /// <summary>The expressions of a message: declarations and every placeholder of every variant.</summary>
    internal static IEnumerable<Rmf2ExpressionV5> Expressions(Rmf2MessageV5 message) =>
        message.Declarations.Select(declaration => declaration.Expression)
            .Concat(message.Variants.SelectMany(variant => variant.Nodes).OfType<Rmf2ExpressionNodeV5>().Select(node => node.Expression));

    /// <summary>
    /// The data <paramref name="usage"/> needs for the CLDR locales of <paramref name="locales"/>' languages, as compact
    /// JSON: <c>{"currencies": {code: digits}, "dynamicCurrencies": [...], "dynamicUnits": [...], "locales": {id: {...}}}</c>
    /// with each locale's sections reduced to the used members; null when nothing is used.
    /// </summary>
    internal static string? Select(IEnumerable<string> locales, Usage usage)
    {
        if (usage.IsEmpty) return null;
        HashSet<string> languages = TranslationCapabilityRegistry.CatalogLanguages(locales);
        // A composed X-per-Y unit needs both parts and the per patterns; a CLDR compound unit has its own entry.
        var units = new SortedSet<string>(StringComparer.Ordinal);
        bool per = false;
        foreach (string unit in usage.Units)
        {
            if (s_units.Value.Contains(unit)) { units.Add(unit); continue; }
            int split = unit.IndexOf("-per-", StringComparison.Ordinal);
            units.Add(unit[..split]); units.Add(unit[(split + 5)..]); per = true;
        }
        JsonElement root = s_data.Value.RootElement;
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            writer.WriteStartObject();
            writer.WriteStartObject("currencies");
            foreach (string code in usage.Currencies) writer.WriteNumber(code, root.GetProperty("currencies").GetProperty(code).GetInt32());
            writer.WriteEndObject();
            WriteList("dynamicCurrencies", usage.DynamicCurrencies);
            WriteList("dynamicUnits", usage.DynamicUnits);
            writer.WriteStartObject("locales");
            foreach (System.Text.Json.JsonProperty locale in root.GetProperty("locales").EnumerateObject())
            {
                if (!languages.Contains(CldrLocales.Parse(locale.Name).Language)) continue;
                var sections = new List<(string Name, List<System.Text.Json.JsonProperty> Members)>();
                foreach (System.Text.Json.JsonProperty section in locale.Value.EnumerateObject())
                {
                    Func<string, bool>? keep = section.Name switch
                    {
                        "currencyFormats" when usage.Currencies.Count > 0 => static _ => true,
                        "currencies" => usage.Currencies.Contains,
                        "units" => units.Contains,
                        "per" when per => static _ => true,
                        "compact" when usage.CompactStyles.Count > 0 => static _ => true,
                        _ => null,
                    };
                    if (keep is null) continue;
                    List<System.Text.Json.JsonProperty> members = section.Value.EnumerateObject().Where(member => keep(member.Name)).ToList();
                    if (members.Count > 0) sections.Add((section.Name, members));
                }
                if (sections.Count == 0) continue;
                writer.WriteStartObject(locale.Name);
                foreach (var (name, members) in sections)
                {
                    writer.WriteStartObject(name);
                    foreach (System.Text.Json.JsonProperty member in members)
                    {
                        if (name != "compact") { member.WriteTo(writer); continue; }
                        // Compact tables by numbering system, reduced to the styles in use.
                        writer.WriteStartObject(member.Name);
                        foreach (System.Text.Json.JsonProperty style in member.Value.EnumerateObject()) if (usage.CompactStyles.Contains(style.Name)) style.WriteTo(writer);
                        writer.WriteEndObject();
                    }
                    writer.WriteEndObject();
                }
                writer.WriteEndObject();
            }
            writer.WriteEndObject();
            writer.WriteEndObject();

            void WriteList(string name, SortedSet<string> values)
            {
                if (values.Count == 0) return;
                writer.WriteStartArray(name);
                foreach (string value in values) writer.WriteStringValue(value);
                writer.WriteEndArray();
            }
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static JsonDocument Load()
    {
        using Stream compressed = typeof(Rmf2FormatDataV1).Assembly.GetManifestResourceStream("Runic.Translations.Compiler.GeneratedFormatData.bin")
            ?? throw new InvalidOperationException("The pinned CLDR format data resource is missing.");
        using var inflater = new DeflateStream(compressed, CompressionMode.Decompress);
        using var buffer = new MemoryStream();
        inflater.CopyTo(buffer);
        return JsonDocument.Parse(buffer.ToArray());
    }
}
