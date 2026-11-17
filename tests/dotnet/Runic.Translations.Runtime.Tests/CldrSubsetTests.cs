using System;
using System.Collections.Generic;
using System.Diagnostics.Tracing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using TUnit.Core;

namespace Runic.Translations.Runtime.Tests;

/// <summary>
/// An application built with RunicTranslationsCldrSubset reads its catalogs' CLDR data from a subset (D072.6). These cases
/// build such a subset from the pinned data and resolve locales against it, without switching the process to it.
/// </summary>
internal sealed class CldrSubsetTests
{
    [Test, DisplayName("a CLDR subset resolves its languages as the full data does")]
    public void InsideSubset()
    {
        GeneratedLocaleData.ResourceIndex full = Full(), subset = Subset(full, "en", "de");
        foreach (string locale in new[] { "de", "de-CH", "de-AT", "en", "en-GB", "en-AU", "en-IN" })
        {
            var anchors = CldrLocales.Anchors(locale);
            GeneratedLocaleData.LocaleEntry expected = GeneratedLocaleData.CreateEntry(anchors, null), actual = GeneratedLocaleData.CreateEntry(anchors, subset);
            Assert.False(actual.OutsideSubset, locale + " is outside the subset");
            Assert.Equal(Describe(expected), Describe(actual), locale);
            foreach ((string section, string? id) in new[] { ("numbers", actual.NumbersId), ("list", actual.ListId), ("relativeTime", actual.RelativeTimeId) })
                if (id is not null) Assert.Equal(string.Join('|', full.Record(section + ":" + id)), string.Join('|', subset.Record(section + ":" + id)), $"{locale} {section}");
        }
        Assert.Equal("de-CH", GeneratedLocaleData.CreateEntry(CldrLocales.Anchors("de-CH"), subset).NumbersId);
    }

    [Test, DisplayName("a locale outside the CLDR subset formats as one without CLDR data, lists and relative times as the base locale, and is reported once")]
    public void OutsideSubset()
    {
        GeneratedLocaleData.ResourceIndex subset = Subset(Full(), "en", "de");
        GeneratedLocaleData.LocaleEntry unknown = GeneratedLocaleData.CreateEntry(CldrLocales.Anchors("tlh"), null);
        foreach (string locale in new[] { "fr", "fr-CA", "ar-EG", "ja", "pl" })
        {
            var anchors = CldrLocales.Anchors(locale);
            GeneratedLocaleData.LocaleEntry full = GeneratedLocaleData.CreateEntry(anchors, null), entry = GeneratedLocaleData.CreateEntry(anchors, subset);
            Assert.True(entry.OutsideSubset, locale + " is inside the subset");
            // Number and date data as for a locale without CLDR data (tlh); lists and relative times with the subset's
            // fallback, the base locale, where tlh has none and fails. Plural rules and grouping are code tables, which the
            // subset keeps whole.
            Assert.Equal((unknown.NumbersId, unknown.WeekdaysId, unknown.DayPeriodsId), (entry.NumbersId, entry.WeekdaysId, entry.DayPeriodsId), locale);
            Assert.Equal((null as string, null as string), (unknown.ListId, unknown.RelativeTimeId), "tlh");
            Assert.Equal(("en", "en"), (entry.ListId, entry.RelativeTimeId), locale);
            Assert.Equal((full.Cardinal, full.Ordinal, full.Grouping), (entry.Cardinal, entry.Ordinal, entry.Grouping), locale);
            Assert.True(entry.MarkReported(), locale + ": first report");
            Assert.False(entry.MarkReported(), locale + ": second report");
        }

        // An empty subset (the switch without the build's resource, in a publish without the full data): every locale
        // falls back and is reported.
        GeneratedLocaleData.ResourceIndex empty = GeneratedLocaleData.ResourceIndex.FromDeflated(new MemoryStream(Deflate("{}"u8.ToArray())));
        GeneratedLocaleData.LocaleEntry english = GeneratedLocaleData.CreateEntry(CldrLocales.Anchors("en"), empty);
        Assert.True(english.OutsideSubset, "en is inside an empty subset");
        Assert.Equal(null, english.NumbersId);
        Assert.Equal(0, empty.Ids("list").Length);
    }

    [Test, DisplayName("a locale outside the CLDR subset is reported as a warning event")]
    public void Diagnostic()
    {
        using var listener = new Listener();
        TranslationsEventSource.ReportOutsideCldrSubset("fr-CA");
        EventWrittenEventArgs written = listener.Events.Single(item => item.EventId == 4);
        Assert.Equal(EventLevel.Warning, written.Level);
        Assert.Equal("fr-CA", (string?)written.Payload![0]);
        Assert.True((written.Message ?? "").Contains("RunicTranslationsCldrSubset", StringComparison.Ordinal), written.Message ?? "no message");
    }

    [Test, DisplayName("the CLDR subset switch is off unless an application sets it")]
    public void SwitchDefaultsOff()
    {
        Assert.Equal("Runic.Translations.CldrSubset", GeneratedLocaleData.SubsetFeatureSwitch);
        Assert.False(GeneratedLocaleData.IsSubset, "IsSubset is on in the test process");
    }

    private static string Describe(GeneratedLocaleData.LocaleEntry entry) =>
        $"{entry.Cardinal}|{entry.Ordinal}|{entry.Grouping}|{entry.NumbersId}|{entry.WeekdaysId}|{entry.DayPeriodsId}|{entry.ListId}|{entry.RelativeTimeId}";

    private static GeneratedLocaleData.ResourceIndex Full()
    {
        using Stream stream = typeof(GeneratedLocaleData).Assembly.GetManifestResourceStream("Runic.Translations.GeneratedLocaleData.bin")!;
        return GeneratedLocaleData.ResourceIndex.FromDeflated(stream);
    }

    // The records of the given languages, as the build's CldrLocaleSubset task writes them for a catalog of those languages.
    private static GeneratedLocaleData.ResourceIndex Subset(GeneratedLocaleData.ResourceIndex full, params string[] languages)
    {
        using Stream stream = typeof(GeneratedLocaleData).Assembly.GetManifestResourceStream("Runic.Translations.GeneratedLocaleData.bin")!;
        using var inflater = new DeflateStream(stream, CompressionMode.Decompress);
        using JsonDocument document = JsonDocument.Parse(inflater);
        using var output = new MemoryStream();
        using (var writer = new Utf8JsonWriter(output))
        {
            writer.WriteStartObject();
            writer.WriteString("fallback", languages[0]);
            foreach (JsonProperty section in document.RootElement.EnumerateObject())
            {
                writer.WriteStartObject(section.Name);
                foreach (JsonProperty record in section.Value.EnumerateObject())
                    if (languages.Contains(record.Name.Split('-')[0])) record.WriteTo(writer);
                writer.WriteEndObject();
            }
            writer.WriteEndObject();
        }
        GeneratedLocaleData.ResourceIndex subset = GeneratedLocaleData.ResourceIndex.FromDeflated(new MemoryStream(Deflate(output.ToArray())));
        Assert.True(subset.Ids("numbers").Length < full.Ids("numbers").Length, "The subset is not smaller than the full data.");
        return subset;
    }

    private static byte[] Deflate(byte[] data)
    {
        using var buffer = new MemoryStream();
        using (var deflater = new DeflateStream(buffer, CompressionLevel.Fastest)) deflater.Write(data);
        return buffer.ToArray();
    }

    private sealed class Listener : EventListener
    {
        internal List<EventWrittenEventArgs> Events { get; } = [];

        protected override void OnEventSourceCreated(EventSource eventSource)
        {
            if (eventSource.Name == "Runic-Translations") EnableEvents(eventSource, EventLevel.Warning);
        }

        protected override void OnEventWritten(EventWrittenEventArgs eventData)
        {
            lock (Events) Events.Add(eventData);
        }
    }
}
