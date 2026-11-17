using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Threading;

namespace Runic.Translations;

/// <summary>Runtime access to the pinned CLDR data: plural rules, grouping, number formats, list patterns and relative time.</summary>
internal static partial class GeneratedLocaleData
{
    // The requested tags seen last, emptied when full so that any tags callers send leave room for the next ones. The
    // entries themselves are cached by their CLDR anchors (CldrLocales.Anchors), which the pinned data bounds, so a
    // tag that is not in s_locales costs its canonicalization and anchors, never the full lookups.
    private const int MaximumCachedLocales = 256;
    // Sized for the whole cache with a fixed lock count: a ConcurrentDictionary that grows adds locks, and Count and Clear
    // take every lock. s_localeCount counts the additions since the last clear, approximately under concurrency.
    private static readonly ConcurrentDictionary<string, LocaleEntry> s_locales = new(Environment.ProcessorCount, MaximumCachedLocales * 2, StringComparer.Ordinal);
    private static int s_localeCount;
    private static readonly ConcurrentDictionary<(string? Data, string? Truncated), LocaleEntry> s_entries = new();
    private static readonly ConcurrentDictionary<string, string?[]> s_records = new(StringComparer.Ordinal);
    private static ResourceIndex? s_resource;

    /// <summary>
    /// The feature switch of an application that embeds only its catalogs' CLDR data (the <c>RunicTranslationsCldrSubset</c>
    /// MSBuild property of Runic.Translations.Build). A trimmed or NativeAOT publish that sets it also drops the full
    /// <c>GeneratedLocaleData.bin</c> (Properties/ILLink.Substitutions.xml).
    /// </summary>
    internal const string SubsetFeatureSwitch = "Runic.Translations.CldrSubset";

    /// <summary>The manifest resource of the entry assembly that holds the subset, in the format of <c>GeneratedLocaleData.bin</c>.</summary>
    internal const string SubsetResourceName = "Runic.Translations.CldrSubset.bin";

    /// <summary>Whether the CLDR number, list and relative-time data come from the application's subset (<see cref="SubsetFeatureSwitch"/>).</summary>
    [FeatureSwitchDefinition(SubsetFeatureSwitch)]
    internal static bool IsSubset => AppContext.TryGetSwitch(SubsetFeatureSwitch, out bool enabled) && enabled;

    /// <summary>Applies the pinned CLDR rules to the operands of a visible decimal, or returns null for a locale without CLDR plural rules.</summary>
    internal static string? SelectPlural(string locale, bool ordinal, in PluralOperands operands)
    {
        LocaleEntry? entry = Entry(locale);
        int rule = entry is null ? -1 : ordinal ? entry.Ordinal : entry.Cardinal;
        return rule < 0 ? null : ApplyPluralRule(rule, in operands);
    }

    /// <summary>
    /// Applies a pinned CLDR plural rule set (an index from <see cref="CldrLocales.PluralRule"/>) to the operands of a
    /// visible decimal by interpreting its bytecode (<see cref="PluralRules"/>; eng/cldr-plural-rules.mjs documents
    /// the format). A compiled selector per rule set took about 32 KB of NativeAOT code (D072.2, T4).
    /// </summary>
    internal static string ApplyPluralRule(int rule, in PluralOperands operands)
    {
        ReadOnlySpan<byte> code = PluralRules;
        if ((uint)rule >= GeneratedLocaleTables.PluralRuleCount) throw new ArgumentOutOfRangeException(nameof(rule));
        int position = code[2 * rule] | (code[(2 * rule) + 1] << 8);
        for (int categories = code[position++]; categories > 0; categories--)
        {
            int category = code[position++];
            bool any = false;
            for (int alternatives = code[position++]; alternatives > 0; alternatives--)
            {
                bool all = true;
                // Every relation is read, so the position stays in step; relations have no side effects.
                for (int relations = code[position++]; relations > 0; relations--)
                    all &= Relation(code, ref position, in operands);
                any |= all;
            }
            if (any) return category switch { 0 => "zero", 1 => "one", 2 => "two", 3 => "few", _ => "many" };
        }
        return "other";
    }

    // One relation: a header byte (operand code | 8 when negated | 16 with a modulus), the modulus, then the ranges.
    // CLDR's n compares as i when the visible fraction t is zero and matches no value otherwise; every operand is a
    // nonnegative integer.
    private static bool Relation(ReadOnlySpan<byte> code, ref int position, in PluralOperands o)
    {
        int header = code[position++];
        int operand = header & 7;
        UInt128 value = operand switch
        {
            0 or 1 => o.I,
            2 => (UInt128)o.V,
            3 => (UInt128)o.W,
            4 => o.F,
            5 => o.T,
            _ => (UInt128)o.E,
        };
        if ((header & 16) != 0)
        {
            ulong modulus = ReadVarint(code, ref position);
            // 64-bit division where the value allows it: a 128-bit one by a variable divisor is several times slower.
            value = value <= ulong.MaxValue ? (ulong)value % modulus : value % modulus;
        }
        bool match = false;
        for (int ranges = code[position++]; ranges > 0; ranges--)
        {
            ulong low = ReadVarint(code, ref position), high = ReadVarint(code, ref position);
            match |= value >= low && value <= high;
        }
        if (operand == 0 && o.T != 0) match = false;
        return match != ((header & 8) != 0);
    }

    private static ulong ReadVarint(ReadOnlySpan<byte> code, ref int position)
    {
        ulong value = 0;
        int shift = 0;
        byte current;
        do
        {
            current = code[position++];
            value |= (ulong)(current & 127) << shift;
            shift += 7;
        }
        while ((current & 128) != 0);
        return value;
    }

    /// <summary>CLDR <c>minimumGroupingDigits</c> of a locale: 1 for every locale without pinned data.</summary>
    internal static int MinimumGroupingDigits(string locale) => Entry(locale)?.Grouping ?? 1;

    /// <summary>
    /// The number formats of a locale, or null without CLDR data: the default numbering system, then 12 fields for it
    /// and, when it is not <c>latn</c>, 12 for <c>latn</c> (see <see cref="Rmf2NumberFormat"/>).
    /// </summary>
    internal static string?[]? FindNumbers(string locale) =>
        NumbersId(locale) is { } id ? FindNumbersById(id) : null;

    /// <summary>The CLDR data ID whose number formats a locale uses, or null without CLDR data.</summary>
    internal static string? NumbersId(string locale) => Entry(locale)?.NumbersId;

    /// <summary>The number formats of a CLDR data ID from <see cref="NumbersId"/>.</summary>
    internal static string?[] FindNumbersById(string id) => Record("numbers:" + id);

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
        LocaleEntry entry = s_entries.GetOrAdd(CldrLocales.Anchors(canonical), static anchors => CreateEntry(anchors, IsSubset ? Resource : null));
        if (IsSubset && entry.OutsideSubset && entry.MarkReported()) TranslationsEventSource.ReportOutsideCldrSubset(canonical);
        if (Interlocked.Increment(ref s_localeCount) > MaximumCachedLocales)
        {
            s_locales.Clear();
            Volatile.Write(ref s_localeCount, 1);
        }
        s_locales.TryAdd(locale, entry);
        return entry;
    }

    /// <summary>
    /// The CLDR data of a tag's <see cref="CldrLocales.Anchors"/>. With a <paramref name="subset"/> each lookup is limited to
    /// the IDs it holds, and the entry says when that loses data. A locale outside the subset then formats numbers with the
    /// root formats and dates with .NET's names, as a locale without CLDR data does, and lists and relative times with CLDR
    /// root's language-neutral patterns (<c>{0}, {1}</c>, <c>-{0} d</c>), the subset's <c>root</c> records, where a locale
    /// without CLDR data would fail. Plural rules and grouping are code tables, which a subset does not change.
    /// </summary>
    internal static LocaleEntry CreateEntry((string? Data, string? Truncated) anchors, ResourceIndex? subset)
    {
        bool outside = false;
        string? Find(string[] ids, string section, bool fallback = false)
        {
            string? id = CldrLocales.FindFrom(anchors.Data, ids, truncate: false);
            if (subset is null || id is null) return id;
            string[] kept = subset.Ids(section);
            string? found = CldrLocales.FindFrom(anchors.Data, kept, truncate: false);
            if (string.Equals(found, id, StringComparison.Ordinal)) return found;
            outside = true;
            return found ?? (fallback && Array.BinarySearch(kept, "root", StringComparer.Ordinal) >= 0 ? "root" : null);
        }
        return new LocaleEntry(
            CldrLocales.PluralRuleFrom(anchors, ordinal: false),
            CldrLocales.PluralRuleFrom(anchors, ordinal: true),
            CldrLocales.MinimumGroupingDigitsFrom(anchors.Data),
            Find(GeneratedLocaleTables.NumberIds, "numbers"),
            Find(GeneratedLocaleTables.WeekdayIds, "weekdays"),
            Find(GeneratedLocaleTables.DayPeriodIds, "dayPeriods"),
            Find(GeneratedLocaleTables.ListIds, "list", fallback: true),
            Find(GeneratedLocaleTables.RelativeTimeIds, "relativeTime", fallback: true),
            outside);
    }

    private static ResourceIndex Resource => s_resource ??= ResourceIndex.Load();

    private static string?[] Record(string key)
    {
        if (s_records.TryGetValue(key, out string?[]? cached)) return cached;
        string?[] record = Resource.Record(key);
        s_records.TryAdd(key, record);
        return record;
    }

    internal sealed class LocaleEntry(int cardinal, int ordinal, int grouping, string? numbersId, string? weekdaysId, string? dayPeriodsId, string? listId, string? relativeTimeId, bool outsideSubset)
    {
        private int _reported;

        internal int Cardinal { get; } = cardinal;
        internal int Ordinal { get; } = ordinal;
        internal int Grouping { get; } = grouping;
        internal string? NumbersId { get; } = numbersId;
        internal string? WeekdaysId { get; } = weekdaysId;
        internal string? DayPeriodsId { get; } = dayPeriodsId;
        internal string? ListId { get; } = listId;
        internal string? RelativeTimeId { get; } = relativeTimeId;
        /// <summary>Whether the application's CLDR subset lacks data the full data has for this locale.</summary>
        internal bool OutsideSubset { get; } = outsideSubset;

        /// <summary>True the first time only: the diagnostic for a locale outside the subset is reported once.</summary>
        internal bool MarkReported() => Interlocked.Exchange(ref _reported, 1) == 0;
    }

    /// <summary>The decompressed <c>GeneratedLocaleData.bin</c>, or an application's subset of it, and the position of each record in it.</summary>
    internal sealed class ResourceIndex
    {
        private static readonly string[] Sections = ["numbers", "weekdays", "dayPeriods", "list", "relativeTime"];
        private readonly byte[] _bytes;
        private readonly Dictionary<string, (int Start, int Length)> _offsets = new(StringComparer.Ordinal);
        // The sorted IDs of each section of Sections; arrays, so the index adds no generic instantiation to an executable.
        private readonly string[][] _ids = new string[Sections.Length][];

        private ResourceIndex(byte[] bytes)
        {
            _bytes = bytes;
            for (int index = 0; index < _ids.Length; index++) _ids[index] = [];
            if (bytes.Length == 0) return;
            var reader = new Utf8JsonReader(bytes);
            reader.Read();
            while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
            {
                string section = reader.GetString()!;
                reader.Read();
                var ids = new List<string>();
                while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
                {
                    string id = reader.GetString()!;
                    reader.Read();
                    int start = (int)reader.TokenStartIndex;
                    reader.Skip();
                    _offsets.Add(section + ":" + id, (start, (int)reader.BytesConsumed - start));
                    ids.Add(id);
                }
                int sectionIndex = Array.IndexOf(Sections, section);
                if (sectionIndex < 0) continue;
                string[] sorted = ids.ToArray();
                Array.Sort(sorted, StringComparer.Ordinal);
                _ids[sectionIndex] = sorted;
            }
        }

        /// <summary>The data IDs of a section (<c>numbers</c>, <c>weekdays</c>, <c>dayPeriods</c>, <c>list</c> or <c>relativeTime</c>), sorted.</summary>
        internal string[] Ids(string section) => _ids[Array.IndexOf(Sections, section)];

        internal string?[] Record(string key)
        {
            (int start, int length) = _offsets[key];
            var reader = new Utf8JsonReader(_bytes.AsSpan(start, length));
            var values = new List<string?>();
            reader.Read();
            while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                values.Add(reader.TokenType == JsonTokenType.Null ? null : reader.GetString());
            return values.ToArray();
        }

        /// <summary>
        /// The pinned data, or with <see cref="IsSubset"/> the entry assembly's <see cref="SubsetResourceName"/>. When the
        /// switch is set without that resource (not by the build), the full data is used where the publish kept it, else an
        /// empty subset: every locale then formats as one without CLDR data, and is reported.
        /// </summary>
        internal static ResourceIndex Load()
        {
            Stream? stream = IsSubset ? System.Reflection.Assembly.GetEntryAssembly()?.GetManifestResourceStream(SubsetResourceName) : null;
            stream ??= typeof(GeneratedLocaleData).Assembly.GetManifestResourceStream("Runic.Translations.GeneratedLocaleData.bin");
            if (stream is null)
                return IsSubset ? new ResourceIndex([]) : throw new InvalidOperationException("The pinned CLDR locale data resource is missing.");
            using (stream) return FromDeflated(stream);
        }

        /// <summary>An index of raw-deflated resource data, as eng/generate-cldr.mjs and the build's subset task write it.</summary>
        internal static ResourceIndex FromDeflated(Stream compressed)
        {
            using var inflater = new DeflateStream(compressed, CompressionMode.Decompress);
            using var buffer = new MemoryStream();
            inflater.CopyTo(buffer);
            return new ResourceIndex(buffer.ToArray());
        }
    }
}
