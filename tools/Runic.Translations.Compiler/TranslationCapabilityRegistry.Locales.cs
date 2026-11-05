using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace Runic.Translations.Compiler;

/// <summary>Locale capabilities and the ESM locale data, from the pinned CLDR tables shared with the runtime.</summary>
internal static partial class TranslationCapabilityRegistry
{
    private static readonly Lazy<JsonDocument> s_localeData = new(LoadLocaleData);

    /// <summary>A locale with CLDR plural rules, or with CLDR data whose plurals are the root rule (<c>agq</c>, <c>rw</c>).</summary>
    internal static bool SupportsCardinal(string locale) => Canonical(locale) is { } canonical && CldrLocales.PluralRule(canonical, ordinal: false) >= 0;

    /// <summary>Every locale with cardinal rules has ordinal rules; CLDR's root rule (<c>other</c>) serves languages without their own.</summary>
    internal static bool SupportsOrdinal(string locale) => SupportsCardinal(locale);

    internal static bool SupportsRelativeTime(string locale) =>
        SupportsCardinal(locale) && CldrLocales.Find(Canonical(locale)!, GeneratedLocaleTables.RelativeTimeIds, truncate: false) is not null;

    internal static bool SupportsList(string locale) =>
        Canonical(locale) is { } canonical && CldrLocales.Find(canonical, GeneratedLocaleTables.ListIds, truncate: false) is not null;

    /// <summary>The plural categories a locale's cardinal or ordinal rules can select, in CLDR order; empty without rules.</summary>
    internal static IReadOnlyList<string> PluralCategories(string locale, bool ordinal)
    {
        int rule = Canonical(locale) is { } canonical ? CldrLocales.PluralRule(canonical, ordinal) : -1;
        return rule < 0 ? Array.Empty<string>() : PluralRuleCategories[rule];
    }

    /// <summary>
    /// The ESM runtime's locale data and plural rules for a catalog: only the CLDR entries of its locales'
    /// languages and of the languages their CLDR parents link to (<c>nb</c> and <c>nn</c> with <c>no</c>).
    /// </summary>
    internal static string EsmLocaleSource(IEnumerable<string> locales)
    {
        var languages = new HashSet<string>(locales.Select(locale => CldrLocales.Language(CldrLocales.Parse(locale).Language)), StringComparer.Ordinal);
        for (bool changed = true; changed;)
        {
            changed = false;
            for (int index = 0; index < GeneratedLocaleTables.ParentKeys.Length; index++)
            {
                string child = GeneratedLocaleTables.ParentKeys[index], parent = GeneratedLocaleTables.ParentValues[index];
                if (parent == "root") continue;
                string childLanguage = CldrLocales.Parse(child).Language, parentLanguage = CldrLocales.Parse(parent).Language;
                if (languages.Contains(childLanguage)) changed |= languages.Add(parentLanguage);
                if (languages.Contains(parentLanguage) && child == childLanguage) changed |= languages.Add(childLanguage);
            }
        }
        bool Selected(string id) => languages.Contains(CldrLocales.Parse(id).Language);

        var rules = new SortedSet<int> { 0 };
        for (int index = 0; index < GeneratedLocaleTables.CardinalKeys.Length; index++)
            if (Selected(GeneratedLocaleTables.CardinalKeys[index])) rules.Add(GeneratedLocaleTables.CardinalValues[index]);
        for (int index = 0; index < GeneratedLocaleTables.OrdinalKeys.Length; index++)
            if (Selected(GeneratedLocaleTables.OrdinalKeys[index])) rules.Add(GeneratedLocaleTables.OrdinalValues[index]);
        var json = new StringBuilder("{");
        json.Append("\"rightToLeftScripts\":").Append(RightToLeftScriptsJson);
        Table("likely", GeneratedLocaleTables.LikelyKeys, GeneratedLocaleTables.LikelyValues.Select(Quote).ToArray());
        Table("parents", GeneratedLocaleTables.ParentKeys, GeneratedLocaleTables.ParentValues.Select(Quote).ToArray());
        var aliases = new StringBuilder();
        for (int index = 0; index < GeneratedLocaleTables.AliasKeys.Length; index++)
            if (languages.Contains(CldrLocales.Parse(GeneratedLocaleTables.AliasValues[index]).Language))
                aliases.Append(aliases.Length == 0 ? "" : ",").Append(Quote(GeneratedLocaleTables.AliasKeys[index])).Append(':').Append(Quote(GeneratedLocaleTables.AliasValues[index]));
        json.Append(",\"aliases\":{").Append(aliases).Append('}');
        Table("cardinal", GeneratedLocaleTables.CardinalKeys, GeneratedLocaleTables.CardinalValues.Select(Rule).ToArray());
        Table("ordinal", GeneratedLocaleTables.OrdinalKeys, GeneratedLocaleTables.OrdinalValues.Select(Rule).ToArray());
        Table("grouping", GeneratedLocaleTables.GroupingKeys, GeneratedLocaleTables.GroupingValues.Select(value => value.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToArray());
        foreach (string section in new[] { "numbers", "list", "relativeTime" })
        {
            json.Append(",\"").Append(section).Append("\":{");
            bool first = true;
            foreach (System.Text.Json.JsonProperty property in s_localeData.Value.RootElement.GetProperty(section).EnumerateObject())
            {
                if (!Selected(property.Name)) continue;
                json.Append(first ? "" : ",").Append(Quote(property.Name)).Append(':').Append(property.Value.GetRawText());
                first = false;
            }
            json.Append('}');
        }
        json.Append('}');

        var source = new StringBuilder("const generatedLocaleData = ").Append(json).Append(";\n");
        source.Append("// CLDR plural rules over BigInt operands { i, v, w, f, t, e } of the visible decimal.\n");
        foreach (int rule in rules) source.Append("const pluralRule").Append(rule).Append(" = ").Append(EsmPluralRuleSources[rule]).Append(";\n");
        source.Append("const generatedPluralRules = Object.freeze({ ").Append(string.Join(", ", rules.Select(rule => rule + ": pluralRule" + rule))).Append(" });");
        return source.ToString();

        void Table(string name, string[] keys, string[] values)
        {
            json.Append(",\"").Append(name).Append("\":{");
            bool first = true;
            for (int index = 0; index < keys.Length; index++)
            {
                if (!Selected(keys[index])) continue;
                json.Append(first ? "" : ",").Append(Quote(keys[index])).Append(':').Append(values[index]);
                first = false;
            }
            json.Append('}');
        }
        static string Rule(byte value) => value.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    // Locale tags are ASCII letters, digits and hyphens.
    private static string Quote(string value) => "\"" + value + "\"";

    private static string? Canonical(string locale) =>
        TranslationCompiler.TryCanonicalizeLocale(locale.Replace('_', '-'), out string canonical) ? canonical : null;

    private static JsonDocument LoadLocaleData()
    {
        using Stream compressed = typeof(TranslationCapabilityRegistry).Assembly.GetManifestResourceStream("Runic.Translations.Compiler.GeneratedLocaleData.bin")
            ?? throw new InvalidOperationException("The pinned CLDR locale data resource is missing.");
        using var inflater = new DeflateStream(compressed, CompressionMode.Decompress);
        using var buffer = new MemoryStream();
        inflater.CopyTo(buffer);
        return JsonDocument.Parse(buffer.ToArray());
    }
}
