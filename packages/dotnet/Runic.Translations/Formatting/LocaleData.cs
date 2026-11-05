using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text.Json;

namespace Runic.Translations;

/// <summary>Runtime access to the pinned CLDR data: plural rules, grouping, number formats, list patterns and relative time.</summary>
internal static partial class GeneratedLocaleData
{
    private const int MaximumCachedLocales = 64;
    private static readonly ConcurrentDictionary<string, LocaleEntry> s_locales = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, string?[]> s_records = new(StringComparer.Ordinal);
    private static ResourceIndex? s_resource;

    /// <summary>Applies the pinned CLDR rules to the operands of a visible decimal, or returns null for a locale without CLDR plural rules.</summary>
    internal static string? SelectPlural(string locale, bool ordinal, in PluralOperands operands)
    {
        LocaleEntry? entry = Entry(locale);
        int rule = entry is null ? -1 : ordinal ? entry.Ordinal : entry.Cardinal;
        return rule < 0 ? null : ApplyPluralRule(rule, in operands);
    }

    /// <summary>CLDR <c>minimumGroupingDigits</c> of a locale: 1 for every locale without pinned data.</summary>
    internal static int MinimumGroupingDigits(string locale) => Entry(locale)?.Grouping ?? 1;

    /// <summary>
    /// The number formats of a locale, or null without CLDR data: the default numbering system, then 12 fields for it
    /// and, when it is not <c>latn</c>, 12 for <c>latn</c> (see <see cref="Rmf2NumberFormat"/>).
    /// </summary>
    internal static string?[]? FindNumbers(string locale) =>
        Entry(locale)?.NumbersId is { } id ? Record("numbers:" + id) : null;

    /// <summary>
    /// The CLDR format-context weekday names of a locale (Sunday first) where .NET's <c>dddd</c> writes the stand-alone
    /// names instead, or null.
    /// </summary>
    internal static string?[]? FindWeekdays(string locale) =>
        Entry(locale)?.WeekdaysId is { } id && Record("weekdays:" + id) is { Length: 7 } names ? names : null;

    /// <summary>
    /// The CLDR flexible day periods of a locale whose time patterns use them (<c>B</c>, as <c>zh-Hant</c>'s), as pairs
    /// of the first minute of the day and the name, or null.
    /// </summary>
    internal static string?[]? FindDayPeriods(string locale) =>
        Entry(locale)?.DayPeriodsId is { } id && Record("dayPeriods:" + id) is { Length: > 0 } periods ? periods : null;

    /// <summary>The 36 CLDR list patterns of a locale (type × style × start, middle, end, 2), or null.</summary>
    internal static string?[]? FindList(string locale) =>
        Entry(locale)?.ListId is { } id ? Record("list:" + id) : null;

    /// <summary>
    /// The relative-time data of a locale, or null: for each unit (second … year) 17 slots, the past patterns
    /// by plural category (zero … other), the future patterns likewise, then relative-type -2 … 2.
    /// </summary>
    internal static string?[]? FindRelativeTime(string locale) =>
        Entry(locale) is { Cardinal: >= 0, RelativeTimeId: { } id } ? Record("relativeTime:" + id) : null;

    /// <summary>Canonicalizes a tag (POSIX <c>en_US</c> included); null when it is not a valid BCP 47 tag.</summary>
    internal static string? Canonical(string locale) =>
        LocaleTag.TryCanonicalize(locale, out string canonical) ? canonical : null;

    private static LocaleEntry? Entry(string locale)
    {
        if (s_locales.TryGetValue(locale, out LocaleEntry? cached)) return cached;
        string? canonical = Canonical(locale);
        if (canonical is null) return null;
        var entry = new LocaleEntry(
            CldrLocales.PluralRule(canonical, ordinal: false),
            CldrLocales.PluralRule(canonical, ordinal: true),
            CldrLocales.MinimumGroupingDigits(canonical),
            CldrLocales.Find(canonical, GeneratedLocaleTables.NumberIds, truncate: false),
            CldrLocales.Find(canonical, GeneratedLocaleTables.WeekdayIds, truncate: false),
            CldrLocales.Find(canonical, GeneratedLocaleTables.DayPeriodIds, truncate: false),
            CldrLocales.Find(canonical, GeneratedLocaleTables.ListIds, truncate: false),
            CldrLocales.Find(canonical, GeneratedLocaleTables.RelativeTimeIds, truncate: false));
        if (s_locales.Count < MaximumCachedLocales) s_locales.TryAdd(locale, entry);
        return entry;
    }

    private static string?[] Record(string key)
    {
        if (s_records.TryGetValue(key, out string?[]? cached)) return cached;
        ResourceIndex resource = s_resource ??= ResourceIndex.Load();
        (int start, int length) = resource.Offsets[key];
        var reader = new Utf8JsonReader(resource.Bytes.AsSpan(start, length));
        var values = new List<string?>();
        reader.Read();
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
            values.Add(reader.TokenType == JsonTokenType.Null ? null : reader.GetString());
        string?[] record = values.ToArray();
        s_records.TryAdd(key, record);
        return record;
    }

    private sealed record LocaleEntry(int Cardinal, int Ordinal, int Grouping, string? NumbersId, string? WeekdaysId, string? DayPeriodsId, string? ListId, string? RelativeTimeId);

    /// <summary>The decompressed <c>GeneratedLocaleData.bin</c> and the position of each locale's array in it.</summary>
    private sealed class ResourceIndex(byte[] bytes, Dictionary<string, (int Start, int Length)> offsets)
    {
        internal byte[] Bytes { get; } = bytes;
        internal Dictionary<string, (int Start, int Length)> Offsets { get; } = offsets;

        internal static ResourceIndex Load()
        {
            using Stream compressed = typeof(GeneratedLocaleData).Assembly.GetManifestResourceStream("Runic.Translations.GeneratedLocaleData.bin")
                ?? throw new InvalidOperationException("The pinned CLDR locale data resource is missing.");
            using var inflater = new DeflateStream(compressed, CompressionMode.Decompress);
            using var buffer = new MemoryStream();
            inflater.CopyTo(buffer);
            byte[] bytes = buffer.ToArray();
            var offsets = new Dictionary<string, (int, int)>(StringComparer.Ordinal);
            var reader = new Utf8JsonReader(bytes);
            reader.Read();
            while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
            {
                string section = reader.GetString()!;
                reader.Read();
                while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
                {
                    string id = reader.GetString()!;
                    reader.Read();
                    int start = (int)reader.TokenStartIndex;
                    reader.Skip();
                    offsets.Add(section + ":" + id, (start, (int)reader.BytesConsumed - start));
                }
            }
            return new ResourceIndex(bytes, offsets);
        }
    }
}
