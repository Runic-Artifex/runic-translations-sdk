using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using TUnit.Core;

using Runic.Translations.Generated;

namespace Runic.Translations.Runtime.Tests;

// Localized dates, times, time zones and percentages, the formatting locale (a de-CH request over de text), the
// content locale of string messages and the fallback event. The shared .NET/ESM expectations are in
// specs/translations/corpus/rmf2-date-percent-conformance.json; these cases cover the .NET-only surface.
internal sealed class LocalizedFormattingTests
{
    private static CompiledRmf2Value Input(string name) => new("input", name);
    private static CompiledRmf2Value Text(string value) => new("string-literal", value);
    private static CompiledRmf2Message Single(TextArgumentType type, string function, string? contentLocale, params CompiledRmf2Option[] options) =>
        new([new("value", type)], [], [], [new([], [new CompiledRmf2Node(new CompiledRmf2Expression(Input("value"), type, function, options))])], contentLocale);
    private static readonly DateTimeOffset Instant = new(2026, 11, 4, 20, 30, 0, TimeSpan.Zero);

    [Test, DisplayName("localized date-times use the local zone by default, utc, or an IANA zone; iso stays UTC")]
    public void TimeZones()
    {
        var local = Single(TextArgumentType.DateTime, "datetime", "en", new CompiledRmf2Option("style", Text("short")));
        var explicitLocal = Single(TextArgumentType.DateTime, "datetime", "en", new CompiledRmf2Option("style", Text("short")), new CompiledRmf2Option("timeZone", Text("local")));
        DateTime wall = TimeZoneInfo.ConvertTime(Instant, TimeZoneInfo.Local).DateTime;
        CultureInfo english = CultureInfo.GetCultureInfo("en");
        string expected = (wall.ToString("M/d/yyyy", english) + " " + wall.ToString(english.DateTimeFormat.ShortTimePattern, english)).Replace('\u202F', ' ');
        Assert.Equal(expected, local.Format([new("value", Instant)], "en"));
        Assert.Equal(expected, explicitLocal.Format([new("value", Instant)], "en"));
        var tokyo = Single(TextArgumentType.DateTime, "datetime", "en", new CompiledRmf2Option("style", Text("short")), new CompiledRmf2Option("timeZone", Text("Asia/Tokyo")));
        Assert.Equal("11/5/2026 5:30 AM", tokyo.Format([new("value", Instant)], "en"));
        var utc = Single(TextArgumentType.DateTime, "datetime", "en", new CompiledRmf2Option("style", Text("short")), new CompiledRmf2Option("timeZone", Text("utc")));
        Assert.Equal("11/4/2026 8:30 PM", utc.Format([new("value", Instant)], "en"));
        // An offset input is the same instant.
        Assert.Equal("11/4/2026 8:30 PM", utc.Format([new("value", new DateTimeOffset(2026, 11, 5, 5, 30, 0, TimeSpan.FromHours(9)))], "en"));
        var iso = Single(TextArgumentType.DateTime, "datetime", "en", new CompiledRmf2Option("timeZone", Text("Asia/Tokyo")));
        Assert.Equal("2026-11-04T20:30:00Z", iso.Format([new("value", Instant)], "en"));
        var unknown = Single(TextArgumentType.DateTime, "datetime", "en", new CompiledRmf2Option("style", Text("short")), new CompiledRmf2Option("timeZone", Text("Mars/Olympus_Mons")));
        Assert.Throws<TranslationFormatException>(() => unknown.Format([new("value", Instant)], "en"));
        Assert.Throws<ArgumentException>(() => Single(TextArgumentType.DateTime, "datetime", "en", new CompiledRmf2Option("timeZone", Text("UTC+1 hour"))));
    }

    [Test, DisplayName("date styles follow the CLDR lengths: short shows the full year, long has no weekday, full has one")]
    public void DateStyles()
    {
        var date = new DateOnly(2026, 11, 4);
        string Format(string style, string locale) => Single(TextArgumentType.Date, "date", locale, new CompiledRmf2Option("style", Text(style))).Format([new("value", date)], locale);
        Assert.Equal("11/4/2026", Format("short", "en"));
        Assert.Equal("Nov 4, 2026", Format("medium", "en"));
        Assert.Equal("November 4, 2026", Format("long", "en"));
        Assert.Equal("Wednesday, November 4, 2026", Format("full", "en"));
        Assert.Equal("04.11.2026", Format("short", "de"));
        Assert.Equal("04.11.2026", Format("medium", "de"));
        Assert.Equal("4. November 2026", Format("long", "de"));
        Assert.Equal("2026/11/04", Format("medium", "ja"));
        var time = Single(TextArgumentType.Time, "time", "en", new CompiledRmf2Option("style", Text("full")));
        Assert.Equal("1:05:09 PM", time.Format([new("value", new TimeOnly(13, 5, 9))], "en"));
    }

    [Test, DisplayName("percentages take the locale's pattern, percent sign and minus sign")]
    public void Percent()
    {
        var percent = Single(TextArgumentType.Number, "number", null, new CompiledRmf2Option("style", Text("percent")));
        Assert.Equal("50%", percent.Format([new("value", 0.5m)], "en"));
        Assert.Equal("50\u00A0%", percent.Format([new("value", 0.5m)], "de"));
        Assert.Equal("-12,5\u00A0%", percent.Format([new("value", -0.125m)], "de"));
        Assert.Equal("%50", percent.Format([new("value", 0.5m)], "tr"));
        Assert.Equal("-%50", percent.Format([new("value", 0.5m * -1)], "tr"));
        Assert.Equal("0%", percent.Format([new("value", -0.0000001m)], "en"));
        Assert.Equal("-0.0001%", percent.Format([new("value", -0.000001m)], "en"));
        Assert.Equal("7,922,816,251,426,433,759,354,395,033,500%", percent.Format([new("value", decimal.MaxValue)], "en"));
    }

    [Test, DisplayName("numbers and dates format in the requested locale when it shares the content language")]
    public void FormattingLocale()
    {
        var number = Single(TextArgumentType.Number, "number", "de");
        string swiss = 1234567.5m.ToString("#,0.######", CultureInfo.GetCultureInfo("de-CH"));
        Assert.Equal(swiss, number.Format([new("value", 1234567.5m)], "de-CH"));
        Assert.Equal("1.234.567,5", number.Format([new("value", 1234567.5m)], "fr"));
        LocalizedTextContent content = number.FormatContent([new("value", 1m)], "de-ch");
        Assert.Equal("de", content.Locale);
        Assert.Equal("de-CH", content.FormattingLocale);
        Assert.Equal("de", number.FormatContent([new("value", 1m)], "fr").FormattingLocale);
        var latin = Single(TextArgumentType.Number, "number", "sr-Latn");
        Assert.Equal("sr-Latn", latin.FormatContent([new("value", 1m)], "sr-Cyrl-RS").FormattingLocale);
        Assert.Equal("sr-Latn-RS", latin.FormatContent([new("value", 1m)], "sr-Latn-RS").FormattingLocale);
        var date = Single(TextArgumentType.Date, "date", "en", new CompiledRmf2Option("style", Text("short")));
        Assert.Equal("04/11/2026", date.Format([new("value", new DateOnly(2026, 11, 4))], "en-GB"));
    }

    [Test, DisplayName("localized dates, times and percentages fall back to invariant data under InvariantGlobalization")]
    public void InvariantGlobalization()
    {
        // The mode is fixed at startup, so the probe below runs in a child process of this test application.
        string application = Environment.ProcessPath!;
        var start = new ProcessStartInfo(application) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        if (Path.GetFileNameWithoutExtension(application) == "dotnet") start.ArgumentList.Add(typeof(LocalizedFormattingTests).Assembly.Location);
        foreach (string argument in new[] { "--treenode-filter", "/*/*/LocalizedFormattingTests/InvariantProbe" }) start.ArgumentList.Add(argument);
        start.Environment["DOTNET_SYSTEM_GLOBALIZATION_INVARIANT"] = "1";
        using Process process = Process.Start(start)!;
        string output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.Equal(0, process.ExitCode, output);
    }

    [Test, Explicit, DisplayName("invariant globalization probe (run by InvariantGlobalization)")]
    public void InvariantProbe()
    {
        Assert.Throws<CultureNotFoundException>(() => CultureInfo.GetCultureInfo("de"));
        CultureInfo invariant = CultureInfo.InvariantCulture;
        var date = Single(TextArgumentType.Date, "date", "de", new CompiledRmf2Option("style", Text("long")));
        Assert.Equal(new DateOnly(2026, 11, 4).ToString(invariant.DateTimeFormat.LongDatePattern, invariant), date.Format([new("value", new DateOnly(2026, 11, 4))], "de"));
        var time = Single(TextArgumentType.Time, "time", "de", new CompiledRmf2Option("style", Text("short")));
        Assert.Equal("13:05", time.Format([new("value", new TimeOnly(13, 5, 9))], "de"));
        var instant = Single(TextArgumentType.DateTime, "datetime", "de", new CompiledRmf2Option("style", Text("short")), new CompiledRmf2Option("timeZone", Text("utc")));
        Assert.Equal("11/04/2026 20:30", instant.Format([new("value", Instant)], "de"));
        var percent = Single(TextArgumentType.Number, "number", "de", new CompiledRmf2Option("style", Text("percent")));
        Assert.Equal(Rmf2Percent(invariant), percent.Format([new("value", 0.5m)], "de-CH"));
        static string Rmf2Percent(CultureInfo culture) => culture.NumberFormat.PercentPositivePattern switch { 0 => "50\u00A0%", 1 => "50%", 2 => "%50", _ => "%\u00A050" };
    }

    private const string English = """
        [[0,{"contentLocale":"en","ast":{"astVersion":5,"profile":"rmf2-execution-v2","inputs":[{"name":"n","type":"decimal"}],"declarations":[],"selectors":[],"variants":[{"keys":[],"nodes":[{"kind":"expression","expression":{"operand":{"kind":"input","value":"n"},"valueType":"decimal","function":"number","options":[],"annotations":[]}}]}]}}],
         [1,{"contentLocale":"en","ast":{"astVersion":5,"profile":"rmf2-execution-v2","inputs":[],"declarations":[],"selectors":[],"variants":[{"keys":[],"nodes":[{"kind":"text","value":"Goodbye"}]}]}}]]
        """;
    private const string German = """
        [[0,{"contentLocale":"de","ast":{"astVersion":5,"profile":"rmf2-execution-v2","inputs":[{"name":"n","type":"decimal"}],"declarations":[],"selectors":[],"variants":[{"keys":[],"nodes":[{"kind":"text","value":"Betrag "},{"kind":"expression","expression":{"operand":{"kind":"input","value":"n"},"valueType":"decimal","function":"number","options":[],"annotations":[]}}]}]}}]]
        """;

    private static CompiledTranslationCatalog Catalog(string id) => new(id, "en",
        [CompiledTranslationDefinition.FromRmf2Inputs("amount", [new CompiledRmf2Input("n", TextArgumentType.Number)]), CompiledTranslationDefinition.FromRmf2Inputs("farewell", [])],
        [CompiledTranslationLocale.CreateDeferred("de", "en", () => CompiledTranslationLocale.ReadRmf2Values(Encoding.UTF8.GetBytes(German))),
         CompiledTranslationLocale.CreateDeferred("en", null, () => CompiledTranslationLocale.ReadRmf2Values(Encoding.UTF8.GetBytes(English)))]);

    [Test, DisplayName("a regional request keeps its tag on the snapshot, through the manager and a refresh")]
    public async Task RequestedLocale()
    {
        var provider = new CompiledTranslationProvider(Catalog("regional"));
        TranslationKey amount = new("regional", 0, "amount");
        ITranslationSnapshot swiss = await provider.GetSnapshotAsync("de-ch");
        Assert.Equal("de", swiss.Locale);
        Assert.Equal("de-CH", swiss.RequestedLocale);
        Assert.Same(swiss, await provider.GetSnapshotAsync("DE-ch"));
        Assert.Equal("Betrag " + 1234.5m.ToString("#,0.######", CultureInfo.GetCultureInfo("de-CH")), swiss.Format(amount, [new TextArgument("n", 1234.5m)]));
        Assert.Equal("de-CH", swiss.FormatContent(amount, [new TextArgument("n", 1m)]).FormattingLocale);
        ITranslationSnapshot german = await provider.GetSnapshotAsync("de");
        Assert.Equal("de", german.RequestedLocale);
        Assert.Equal("Betrag 1.234,5", german.Format(amount, [new TextArgument("n", 1234.5m)]));

        var manager = new TranslationManager(provider, german);
        var changes = new List<(string Old, string New)>();
        manager.LocaleChanged += (_, change) => changes.Add((change.OldSnapshot.RequestedLocale, change.NewSnapshot.RequestedLocale));
        await manager.SetLocaleAsync("de-CH");
        await manager.SetLocaleAsync("de-CH");
        Assert.Equal("de", manager.CurrentLocale);
        Assert.Equal("de-CH", manager.Current.RequestedLocale);
        Assert.Equal(1, changes.Count, "Switching de to de-CH must publish once.");
        Assert.Equal(("de", "de-CH"), changes[0]);
        await manager.RefreshAsync();
        Assert.Equal("de-CH", manager.Current.RequestedLocale);
        await manager.SetLocaleAsync("de");
        Assert.Equal("de", manager.Current.RequestedLocale);
        Assert.Equal(2, changes.Count);
    }

    [Test, NotInParallel("TranslationEvents"), DisplayName("string messages report their content locale and fallen-back messages raise TranslationFallback")]
    public async Task Fallback()
    {
        var provider = new CompiledTranslationProvider(Catalog("fallback-events"));
        TranslationKey amount = new("fallback-events", 0, "amount"), farewell = new("fallback-events", 1, "farewell");
        ITranslationSnapshot german = await provider.GetSnapshotAsync("de-AT");
        Assert.True(german.TryGetContentLocale(farewell, out string content) && content == "en", "farewell falls back to en");
        Assert.True(german.TryGetContentLocale(amount, out content) && content == "de", "amount is German");
        Assert.False(german.TryGetContentLocale(new TranslationKey("other", 0, "amount"), out _), "a foreign key has no content locale");

        var events = new List<TranslationFallbackEventArgs>();
        EventHandler<TranslationFallbackEventArgs> handler = (sender, change) => { if (change.Catalog == "fallback-events") lock (events) events.Add(change); };
        EventHandler<TranslationFallbackEventArgs> failing = (_, _) => throw new InvalidOperationException("listener failure");
        TranslationEvents.TranslationFallback += failing;
        TranslationEvents.TranslationFallback += handler;
        try
        {
            Assert.Equal("Goodbye", german.Format(farewell, []));
            Assert.Equal("Betrag 1", german.Format(amount, [new TextArgument("n", 1m)]));
        }
        finally
        {
            TranslationEvents.TranslationFallback -= handler;
            TranslationEvents.TranslationFallback -= failing;
        }
        Assert.Equal("Goodbye", german.Format(farewell, []));
        Assert.Equal(1, events.Count, "Only the fallen-back message raises the event, and only while subscribed.");
        Assert.Equal(("farewell", "de", "en", "de-AT"), (events[0].Key, events[0].Locale, events[0].ContentLocale, events[0].RequestedLocale));
    }
}
