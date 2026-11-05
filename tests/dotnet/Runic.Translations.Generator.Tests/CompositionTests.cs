using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.Tracing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Runic.Translations.Compiler;
using Runic.Translations.Compiler.Generation;
using TUnit.Core;

namespace Runic.Translations.Generator.Tests;

// Catalog composition (T1a, T1b): generated libraries and an application catalog in one TranslationContext, with
// application overlays checked key by key against the library release they are attached to.
internal sealed class CompositionTests
{
    private const string GridId = "acme-grid";

    // Release 1 of a component library: en (default) and de.
    private static readonly Catalog GridV1 = Catalog.Compile(GridProject,
        ("en", """
            title = Grid
            empty = No rows
            rows =
              .input {$count :integer}
              {{{$count} rows}}
            help = Read the {#link}guide{/link}.
            """),
        ("de", """
            title = Raster
            empty = Keine Zeilen
            rows =
              .input {$count :integer}
              {{{$count} Zeilen}}
            help = Lies die {#link}Anleitung{/link}.
            """));

    // Release 2 adds sort, removes help and gives empty an input.
    private static readonly Catalog GridV2 = Catalog.Compile(GridProject,
        ("en", """
            title = Grid
            empty =
              .input {$filter :string}
              {{No rows match {$filter}}}
            rows =
              .input {$count :integer}
              {{{$count} rows}}
            sort = Sort
            """),
        ("de", """
            title = Raster
            empty =
              .input {$filter :string}
              {{Keine Zeilen für {$filter}}}
            rows =
              .input {$count :integer}
              {{{$count} Zeilen}}
            sort = Sortieren
            """));

    private static readonly Catalog App = Catalog.Compile("""
        {
          "schemaVersion": 1,
          "catalog": "app",
          "code": { "namespace": "Example.App", "className": "AppText", "visibility": "public" },
          "baseLocale": "en",
          "locales": [ "en", "de", "fr" ]
        }
        """,
        ("en", "greeting = Hello\n"),
        ("de", "greeting = Hallo\n"),
        ("fr", "greeting = Bonjour\n"));

    private const string GridProject = """
        {
          "schemaVersion": 1,
          "catalog": "acme-grid",
          "code": { "namespace": "Acme.Grid", "className": "GridText", "visibility": "public" },
          "baseLocale": "en",
          "locales": [ "en", "de" ]
        }
        """;

    // The application's overrides, written against release 1: an English title, a French translation and the help text.
    private const string OverlayProject = """
        {
          "schemaVersion": 1,
          "catalog": "acme-grid",
          "code": { "namespace": "Example.Overrides", "className": "GridOverrides", "visibility": "internal" },
          "baseLocale": "en",
          "locales": [ "en", "fr" ],
          "validation": { "translationCompleteness": "allow" }
        }
        """;

    private static readonly Dictionary<string, byte[]> Overlay = Packs(OverlayProject,
        ("en", """
            title = Data grid
            empty = Nothing here
            rows =
              .input {$count :integer}
              {{{$count} rows}}
            help = See the {#link}manual{/link}.
            """),
        ("fr", """
            title = Grille
            rows =
              .input {$count :integer}
              {{{$count} lignes}}
            """));

    [Test, DisplayName("a context switches every catalog to one requested locale and reports how each resolved it")]
    public async Task SharedLocale()
    {
        TranslationContext context = await TranslationContext.CreateBuilder()
            .Add(App.Registration)
            .Add(GridV1.Registration)
            .BuildAsync("de");
        Assert.Equal("Hallo|Raster", Text(context, "app", "greeting") + "|" + Text(context, GridId, "title"), "German");
        Assert.Equal("de", context.RequestedLocale, "requested");

        await context.SetLocaleAsync("fr-CA");
        Assert.Equal("Bonjour|Grid", Text(context, "app", "greeting") + "|" + Text(context, GridId, "title"), "French");
        TranslationCatalogStatus app = context.Catalogs[0], grid = context.Catalogs[1];
        Assert.Equal("app:fr-CA>fr:Parent", Describe(app), "app status");
        Assert.Equal("acme-grid:fr-CA>en:Default", Describe(grid), "grid status");
        Assert.Equal("de|en", string.Join("|", grid.SupportedLocales), "grid locales");
        Assert.Equal("de|en", string.Join("|", context.SupportedLocales), "locales every catalog shows");
        Assert.Equal("fr-CA|Default", context.GetManager(GridId).Current.RequestedLocale + "|" + context.GetManager(GridId).Current.LocaleResolution,
            "snapshot request");

        // A manager's switch switches the whole context.
        await context.GetManager(GridId).SetLocaleAsync("de");
        Assert.Equal("de|de|de", context.RequestedLocale + "|" + context.GetManager("app").CurrentLocale + "|" + context.GetManager(GridId).CurrentLocale,
            "manager switch");
        Assert.True(!context.TryGetManager("missing", out _), "unknown catalog");
    }

    [Test, DisplayName("generated catalogs isolate placeholders in right-to-left locales by default, always with on and never with off")]
    public async Task BidiIsolation()
    {
        static async Task<string> Greeting(string project, string locale, params (string Locale, string Text)[] sources)
        {
            ITranslationProvider provider = Catalog.Compile(project, sources).Registration.CreateProvider();
            ITranslationSnapshot snapshot = await provider.GetSnapshotAsync(locale);
            Assert.True(snapshot.TryGetKey("greeting", out TranslationKey key), "greeting");
            return snapshot.Format(key, [Runic.Translations.Generated.GeneratedCatalogSupport.CreateArgument("name", new TextArgument("_", "Ada"))]);
        }
        static string Project(string id, string locales, string? runtime = null) => "{\"schemaVersion\":1,\"catalog\":\"" + id +
            "\",\"code\":{\"namespace\":\"Example.Bidi\",\"className\":\"" + char.ToUpperInvariant(id[0]) + id[1..] + "Text\",\"visibility\":\"public\"},\"baseLocale\":\"en\",\"locales\":[" +
            locales + "]" + (runtime is null ? "" : ",\"runtime\":{\"bidiIsolation\":\"" + runtime + "\"}") + "}";

        // default is decided per locale at run time: English text of an en/ar catalog stays free of isolates.
        Assert.Equal("Hello Ada", await Greeting(Project("mixed", "\"en\",\"ar\""), "en", ("en", "greeting = Hello {$name}\n"), ("ar", "greeting = مرحبا {$name}\n")), "en in an en/ar catalog");
        Assert.Equal("مرحبا \u2068Ada\u2069", await Greeting(Project("mixed", "\"en\",\"ar\""), "ar", ("en", "greeting = Hello {$name}\n"), ("ar", "greeting = مرحبا {$name}\n")), "ar");
        Assert.Equal("Hello Ada", await Greeting(Project("plain", "\"en\",\"de\""), "en", ("en", "greeting = Hello {$name}\n"), ("de", "greeting = Hallo {$name}\n")), "en/de catalog");
        Assert.Equal("Hello \u2068Ada\u2069", await Greeting(Project("forced", "\"en\"", "on"), "en", ("en", "greeting = Hello {$name}\n")), "on");
        Assert.Equal("Hello Ada", await Greeting(Project("quiet", "\"en\",\"he\"", "off"), "en", ("en", "greeting = Hello {$name}\n"), ("he", "greeting = שלום {$name}\n")), "off");
    }

    [Test, DisplayName("bidiIsolation default: an application's right-to-left overlay of a left-to-right library catalog is isolated")]
    public async Task RightToLeftOverlayIsolated()
    {
        Dictionary<string, byte[]> packs = Packs(OverlayProject.Replace("[ \"en\", \"fr\" ]", "[ \"en\", \"ar\" ]", StringComparison.Ordinal),
            ("en", "title = Data grid\nrows =\n  .input {$count :integer}\n  {{{$count} rows}}\n"),
            ("ar", "title = شبكة\nrows =\n  .input {$count :integer}\n  {{{$count} صفوف}}\n"));
        TranslationContext context = await TranslationContext.CreateBuilder()
            .Add(GridV1.Registration)
            .AddOverlay(TranslationOverlay.FromPacks(GridId, new MemoryPackSource(packs), [new TranslationOverlayLocale("ar")]))
            .BuildAsync("en");
        // The library (en/de) was compiled with bidiIsolation default, which a pre-0.9 compile-time decision turned off.
        Assert.Equal("3 rows", Text(context, GridId, "rows", new TextArgument("count", 3L)), "English stays bare");

        await context.SetLocaleAsync("ar");
        Assert.Equal("acme-grid:ar>ar:Exact", Describe(context.Catalogs[0]), "added locale");
        Assert.Equal("\u20673\u2069 صفوف|شبكة", Text(context, GridId, "rows", new TextArgument("count", 3L)) + "|" + Text(context, GridId, "title"),
            "the Arabic overlay's placeholder is isolated");
        // A message the overlay lacks falls back to the library's English, which an Arabic request isolates too.
        Assert.Equal("No rows", Text(context, GridId, "empty"), "English fallback without placeholders");
    }

    [Test, DisplayName("a registration creates the catalog's readable surface over a context manager")]
    public async Task ReadableRegistration()
    {
        TranslationContext context = await TranslationContext.CreateBuilder().Add(App.Registration).Add(GridV1.Registration).BuildAsync("de");
        Assert.True(context.TryGetRegistration(GridId, out TranslationCatalogRegistration? registration) && ReferenceEquals(registration, GridV1.Registration),
            "registration by ID");
        Assert.True(!context.TryGetRegistration("missing", out TranslationCatalogRegistration? missing) && missing is null, "unknown catalog");
        Assert.True(registration!.HasReadableMessages, "generated registrations have a readable surface");
        IReadableMessages messages = registration.CreateReadableMessages(context.GetManager(GridId))!;
        Assert.Equal("Acme.Grid.GridTextMessages", messages.GetType().FullName, "readable type");
        Assert.Equal(1, messages.ReadableNameVersion, "readable name version");
        // The surface reads the context's manager, so it follows the context's locale.
        Func<string> title = () => (string)messages.GetType().GetProperty("title")!.GetValue(messages)!;
        Assert.Equal("Raster", title(), "German title");
        await context.SetLocaleAsync("en");
        Assert.Equal("Grid", title(), "English title");

        // A registration created by hand without a factory has none, and a factory that returns null is an error.
        var manual = new TranslationCatalogRegistration(GridId, "en", GridV1.Registration.ContractFingerprint, GridV1.Registration.Rmf2MarkupContract,
            GridV1.Registration.CreateProvider);
        Assert.True(!manual.HasReadableMessages && manual.CreateReadableMessages(context.GetManager(GridId)) is null, "no factory");
        var broken = new TranslationCatalogRegistration(GridId, "en", GridV1.Registration.ContractFingerprint, GridV1.Registration.Rmf2MarkupContract,
            GridV1.Registration.CreateProvider, static _ => null!);
        Assert.Throws<InvalidOperationException>(() => broken.CreateReadableMessages(context.GetManager(GridId)));
        Assert.Throws<ArgumentNullException>(() => manual.CreateReadableMessages(null!));
    }

    [Test, DisplayName("a context publishes every catalog together and changes none when one fails")]
    public async Task AtomicSwitch()
    {
        var packs = new MemoryPackSource(Overlay) { Failing = "de" };
        TranslationContext context = await TranslationContext.CreateBuilder()
            .Add(App.Registration)
            .Add(GridV1.Registration)
            .AddOverlay(TranslationOverlay.FromPacks(GridId, packs))
            .BuildAsync("en");

        var seen = new List<string>();
        context.GetManager("app").LocaleChanged += (_, change) =>
            seen.Add("app " + change.NewLocale + " sees grid " + context.GetManager(GridId).CurrentLocale);
        context.GetManager(GridId).SnapshotPublished += (_, published) => seen.Add("grid " + published.Reason + " " + published.Snapshot.Locale);
        context.LocaleChanged += (_, change) => seen.Add("context " + change.OldRequestedLocale + ">" + change.RequestedLocale + " " +
            string.Join(",", change.Catalogs.Select(Describe)));

        // The overlay source fails for de: neither catalog changes.
        TranslationPackException failure = await Throws<TranslationPackException>(() => context.SetLocaleAsync("de").AsTask());
        Assert.Equal(TranslationPackFailureReason.SourceFailure, failure.Reason, "failure reason");
        Assert.Equal("en|en|en|Data grid", context.RequestedLocale + "|" + context.GetManager("app").CurrentLocale + "|" +
            context.GetManager(GridId).CurrentLocale + "|" + Text(context, GridId, "title"), "unchanged after failure");
        Assert.Equal(0, seen.Count, "no events after failure");

        // A canceled switch changes nothing either.
        using (var canceled = new CancellationTokenSource())
        {
            await canceled.CancelAsync();
            await Throws<OperationCanceledException>(() => context.SetLocaleAsync("fr", canceled.Token).AsTask());
        }
        Assert.Equal("en", context.RequestedLocale, "unchanged after cancellation");

        packs.Failing = null;
        await context.SetLocaleAsync("de");
        Assert.Equal(
            "app de sees grid de|grid LocaleChanged de|context en>de app:de>de:Exact,acme-grid:de>de:Exact",
            string.Join("|", seen), "events");
    }

    [Test, DisplayName("overlays resolve locale-first: the library's own locale beats an override in its fallback")]
    public async Task LocaleFirstOverlay()
    {
        TranslationContext context = await TranslationContext.CreateBuilder()
            .Add(GridV1.Registration)
            .AddOverlay(TranslationOverlay.FromPacks(GridId, new MemoryPackSource(Overlay), [new TranslationOverlayLocale("fr")]))
            .BuildAsync("en");
        Assert.Equal("Data grid|Nothing here|3 rows", Text(context, GridId, "title") + "|" + Text(context, GridId, "empty") + "|" +
            Text(context, GridId, "rows", new TextArgument("count", 3L)), "English overrides");

        // German: the library's German text beats the application's English override.
        await context.SetLocaleAsync("de");
        Assert.Equal("Raster|Keine Zeilen", Text(context, GridId, "title") + "|" + Text(context, GridId, "empty"), "German");

        // French: added by the overlay, falling back to the overlay's English, then the library's.
        await context.SetLocaleAsync("fr-BE");
        Assert.Equal("Grille|3 lignes|Nothing here", Text(context, GridId, "title") + "|" +
            Text(context, GridId, "rows", new TextArgument("count", 3L)) + "|" + Text(context, GridId, "empty"), "French");
        Assert.Equal("acme-grid:fr-BE>fr:Parent", Describe(context.Catalogs[0]), "added locale resolves");
        Assert.Equal("de|en|fr", string.Join("|", context.Catalogs[0].SupportedLocales), "added locale reported");
        Assert.Equal("de|en|fr", string.Join("|", context.SupportedLocales), "context locales include added ones");
        Assert.Equal(0, context.Catalogs[0].OverlayDiagnostics.Count, "no diagnostics against the release the overlay was written for");
    }

    [Test, DisplayName("an overlay keeps working when a library release adds, removes and changes keys")]
    public async Task OverlayAcrossLibraryReleases()
    {
        TranslationContext context = await TranslationContext.CreateBuilder()
            .Add(GridV2.Registration)
            .AddOverlay(TranslationOverlay.FromPacks(GridId, new MemoryPackSource(Overlay), [new TranslationOverlayLocale("fr", "en")]))
            .BuildAsync("fr");
        Assert.True(!string.Equals(GridV1.Registration.ContractFingerprint, GridV2.Registration.ContractFingerprint, StringComparison.Ordinal),
            "the releases have different contracts");
        Assert.Equal("Grille|Sort|No rows match x", Text(context, GridId, "title") + "|" + Text(context, GridId, "sort") + "|" +
            Text(context, GridId, "empty", new TextArgument("filter", "x")), "French on release 2");
        Assert.Equal(
            "en:empty:IncompatibleInputs|en:help:UnknownKey",
            string.Join("|", context.Catalogs[0].OverlayDiagnostics.Select(item => item.Locale + ":" + item.Key + ":" + item.Reason).Order(StringComparer.Ordinal)),
            "skipped keys");
    }

    [Test, DisplayName("overlay messages must keep the library's content kind and markup")]
    public async Task OverlayMarkupChecks()
    {
        Dictionary<string, byte[]> packs = Packs(OverlayProject.Replace("[ \"en\", \"fr\" ]", "[ \"en\" ]", StringComparison.Ordinal),
            ("en", """
                title = {#strong}Data{/strong} grid
                empty = No data
                rows =
                  .input {$count :integer}
                  {{{$count} rows}}
                help = See the {#strong}manual{/strong}.
                """));
        TranslationContext context = await TranslationContext.CreateBuilder()
            .Add(GridV1.Registration)
            .AddOverlay(TranslationOverlay.FromPacks(GridId, new MemoryPackSource(packs)))
            .BuildAsync();
        Assert.Equal("Grid|No data", Text(context, GridId, "title") + "|" + Text(context, GridId, "empty"), "checked overrides");
        Assert.Equal(
            "en:help:IncompatibleMarkup|en:title:IncompatibleMarkup",
            string.Join("|", context.Catalogs[0].OverlayDiagnostics.Select(item => item.Locale + ":" + item.Key + ":" + item.Reason).Order(StringComparer.Ordinal)),
            "markup diagnostics");
    }

    [Test, DisplayName("an overlay pack for another locale or rejected by the integrity policy fails the switch")]
    public async Task OverlayPackRejections()
    {
        var misplaced = new Dictionary<string, byte[]>(StringComparer.Ordinal) { ["de"] = Overlay["en"] };
        TranslationContext context = await TranslationContext.CreateBuilder()
            .Add(GridV1.Registration)
            .AddOverlay(TranslationOverlay.FromPacks(GridId, new MemoryPackSource(misplaced)))
            .BuildAsync("en");
        TranslationPackException wrongLocale = await Throws<TranslationPackException>(() => context.SetLocaleAsync("de").AsTask());
        Assert.Equal("RTR0023/locale-mismatch|en", wrongLocale.RejectionId + "|" + context.RequestedLocale, "pack for another locale");

        TranslationPackException rejected = await Throws<TranslationPackException>(() => TranslationContext.CreateBuilder()
            .Add(GridV1.Registration)
            .AddOverlay(TranslationOverlay.FromPacks(GridId, new MemoryPackSource(Overlay), integrityVerifier: static (_, _) => ValueTask.FromResult(false)))
            .BuildAsync("en").AsTask());
        Assert.Equal(TranslationPackFailureReason.IntegrityRejected, rejected.Reason, "integrity policy");
    }

    [Test, DisplayName("refreshing a context reloads overlay packs")]
    public async Task RefreshReloadsOverlays()
    {
        var packs = new MemoryPackSource(Overlay);
        TranslationContext context = await TranslationContext.CreateBuilder()
            .Add(App.Registration)
            .Add(GridV1.Registration)
            .AddOverlay(TranslationOverlay.FromPacks(GridId, packs))
            .BuildAsync("en");
        packs.Replace(Packs(OverlayProject, ("en", """
            title = Table
            empty = Nothing here
            rows =
              .input {$count :integer}
              {{{$count} rows}}
            help = See the {#link}manual{/link}.
            """)));
        Assert.Equal("Data grid", Text(context, GridId, "title"), "cached before refresh");
        var reasons = new List<string>();
        context.GetManager(GridId).SnapshotPublished += (_, published) => reasons.Add(published.Reason.ToString());
        await context.GetManager(GridId).RefreshAsync();
        Assert.Equal("Table|Refresh", Text(context, GridId, "title") + "|" + string.Join(",", reasons), "refreshed");
    }

    [Test, DisplayName("a context resolves references of every registered catalog")]
    public async Task ResolvesReferences()
    {
        TranslationContext context = await TranslationContext.CreateBuilder()
            .Add(App.Registration)
            .Add(GridV1.Registration)
            .BuildAsync("de");
        var rows = new TranslationReference(GridId, GridV1.Registration.ContractFingerprint, "rows",
            new Dictionary<string, TranslationReferenceArgument> { ["count"] = new TranslationReferenceArgument(TextArgumentType.Int, "2") }, "2 rows");
        TranslationReferenceResolution grid = context.Resolve(rows);
        Assert.Equal("Translated|2 Zeilen", grid.Outcome + "|" + grid.Text, "library reference");
        TranslationReferenceResolution app = context.Resolve(new TranslationReference("app", App.Registration.ContractFingerprint, "greeting", null, "Hello"));
        Assert.Equal("Translated|Hallo", app.Outcome + "|" + app.Text, "application reference");
        TranslationReferenceResolution other = context.Resolve(new TranslationReference("other", App.Registration.ContractFingerprint, "x", null, "fallback"));
        Assert.Equal("Fallback|fallback|CatalogMismatch", other.Outcome + "|" + other.Text + "|" + other.Diagnostics.Single().Reason, "unregistered catalog");
    }

    [Test, DisplayName("the latest switch wins: a request for the published locale supersedes a switch in flight")]
    public async Task LatestSwitchWins()
    {
        var packs = new GatedPackSource(Overlay, "de", observeCancellation: true);
        TranslationContext context = await TranslationContext.CreateBuilder()
            .Add(App.Registration)
            .Add(GridV1.Registration)
            .AddOverlay(TranslationOverlay.FromPacks(GridId, packs))
            .BuildAsync("en");
        var seen = new List<string>();
        context.LocaleChanged += (_, change) => seen.Add(change.OldRequestedLocale + ">" + change.RequestedLocale);

        // de waits for its overlay pack; the user switches back to en before it arrives.
        Task loadingGerman = context.SetLocaleAsync("de").AsTask();
        await packs.Entered.Task;
        await context.SetLocaleAsync("en");
        await loadingGerman;
        Assert.Equal("en|en|Data grid|True|0", context.RequestedLocale + "|" + context.GetManager(GridId).CurrentLocale + "|" +
            Text(context, GridId, "title") + "|" + packs.SawCancellation + "|" + seen.Count, "en wins and the de preparation was canceled");

        // The caller's own cancellation still fails its call and changes nothing.
        using (var cancellation = new CancellationTokenSource())
        {
            var gated = new GatedPackSource(Overlay, "de", observeCancellation: true);
            TranslationContext other = await TranslationContext.CreateBuilder().Add(GridV1.Registration)
                .AddOverlay(TranslationOverlay.FromPacks(GridId, gated)).BuildAsync("en");
            Task canceled = other.SetLocaleAsync("de", cancellation.Token).AsTask();
            await gated.Entered.Task;
            await cancellation.CancelAsync();
            await Throws<OperationCanceledException>(() => canceled);
            Assert.Equal("en", other.RequestedLocale, "caller cancellation");
        }
    }

    [Test, DisplayName("a slow switch that ignores cancellation never blocks a later switch, and its result is discarded")]
    public async Task SlowSwitchIsSuperseded()
    {
        var packs = new GatedPackSource(Overlay, "de", observeCancellation: false);
        TranslationContext context = await TranslationContext.CreateBuilder()
            .Add(App.Registration)
            .Add(GridV1.Registration)
            .AddOverlay(TranslationOverlay.FromPacks(GridId, packs, [new TranslationOverlayLocale("fr")]))
            .BuildAsync("en");
        var seen = new List<string>();
        context.GetManager(GridId).LocaleChanged += (_, change) => seen.Add("grid " + change.OldLocale + ">" + change.NewLocale);
        context.LocaleChanged += (_, change) => seen.Add("context " + change.OldRequestedLocale + ">" + change.RequestedLocale);

        Task slow = context.SetLocaleAsync("de").AsTask();
        await packs.Entered.Task;
        await context.SetLocaleAsync("fr");
        Assert.Equal("fr|Grille", context.RequestedLocale + "|" + Text(context, GridId, "title"), "fr published while de is still loading");

        packs.Release();
        await slow;
        Assert.Equal("fr|Grille", context.RequestedLocale + "|" + Text(context, GridId, "title"), "the superseded de result is discarded");
        Assert.Equal("grid en>fr|context en>fr", string.Join("|", seen), "only the winning switch raises events");
    }

    [Test, DisplayName("a regional switch over the same catalog locale (de to de-CH) raises LocaleChanged and formats for the region")]
    public async Task RegionalSwitchRaisesLocaleChanged()
    {
        TranslationContext context = await TranslationContext.CreateBuilder()
            .Add(GridV1.Registration)
            .BuildAsync("de");
        var seen = new List<string>();
        context.GetManager(GridId).LocaleChanged += (_, change) =>
            seen.Add("grid " + change.OldLocale + ">" + change.NewLocale + " " + change.RequestedLocale + " " + change.LocaleResolution);
        context.GetManager(GridId).SnapshotPublished += (_, published) => seen.Add("published " + published.RequestedLocale + " " + published.Reason);
        context.LocaleChanged += (_, change) => seen.Add("context " + change.OldRequestedLocale + ">" + change.RequestedLocale);
        string german = Text(context, GridId, "rows", new TextArgument("count", 1234567L));

        await context.SetLocaleAsync("de-CH");
        Assert.Equal("grid de>de de-CH Parent|published de-CH LocaleChanged|context de>de-CH", string.Join("|", seen), "events");
        string swiss = Text(context, GridId, "rows", new TextArgument("count", 1234567L));
        Assert.Equal("1'234'567 Zeilen", swiss, "Swiss grouping (pinned CLDR 48.2, U+0027 on every platform)");
        Assert.True(german != swiss, "de and de-CH group digits differently: " + german);

        await context.SetLocaleAsync("de-CH");
        Assert.Equal(3, seen.Count, "the same request raises nothing");
    }

    [Test, DisplayName("events follow publication order and subscribers end on the latest locale when a handler switches again")]
    public async Task OverlappingSwitchEvents()
    {
        TranslationContext context = await TranslationContext.CreateBuilder()
            .Add(App.Registration)
            .Add(GridV1.Registration)
            .BuildAsync("en");
        var seen = new List<string>();
        Task? nested = null;
        context.GetManager("app").LocaleChanged += (_, change) =>
        {
            seen.Add("app " + change.OldLocale + ">" + change.NewLocale);
            // Switches again while the de events are being raised.
            if (change.NewLocale == "de") nested = context.SetLocaleAsync("fr").AsTask();
        };
        context.GetManager(GridId).LocaleChanged += (_, change) => seen.Add("grid " + change.OldLocale + ">" + change.NewLocale);
        context.LocaleChanged += (_, change) => seen.Add("context " + change.OldRequestedLocale + ">" + change.RequestedLocale);

        await context.SetLocaleAsync("de");
        await nested!;
        // The grid's and the context's de events were superseded before they were raised; the fr events start from
        // what each subscriber last saw (the grid shows en for fr, but the requested tag changed from en to fr).
        Assert.Equal("app en>de|app de>fr|grid en>en|context en>fr", string.Join("|", seen), "events");
        Assert.Equal("fr|fr|en", context.RequestedLocale + "|" + context.GetManager("app").CurrentLocale + "|" + context.GetManager(GridId).CurrentLocale,
            "final state");
    }

    [Test, DisplayName("an added regional locale falls back to the parent the library declares before the default locale")]
    public async Task AddedLocaleFallsBackToLibraryParent()
    {
        Dictionary<string, byte[]> swiss = Packs(OverlayProject.Replace("[ \"en\", \"fr\" ]", "[ \"en\", \"de-CH\" ]", StringComparison.Ordinal),
            ("en", """
                title = Data grid
                empty = Nothing here
                rows =
                  .input {$count :integer}
                  {{{$count} rows}}
                help = See the {#link}manual{/link}.
                """),
            ("de-CH", "title = Datenraster\n"));
        var onlySwiss = new Dictionary<string, byte[]>(StringComparer.Ordinal) { ["de-CH"] = swiss["de-CH"] };

        TranslationContext context = await TranslationContext.CreateBuilder()
            .Add(GridV1.Registration)
            .AddOverlay(TranslationOverlay.FromPacks(GridId, new MemoryPackSource(onlySwiss), [new TranslationOverlayLocale("de-CH")]))
            .BuildAsync("de-CH");
        Assert.Equal("Datenraster|Keine Zeilen|3 Zeilen", Text(context, GridId, "title") + "|" + Text(context, GridId, "empty") + "|" +
            Text(context, GridId, "rows", new TextArgument("count", 3L)), "de-CH falls back to the library's de, not its en default");
        Assert.Equal("acme-grid:de-CH>de-CH:Exact", Describe(context.Catalogs[0]), "added locale is exact");

        // An explicit fallback still wins.
        TranslationContext english = await TranslationContext.CreateBuilder()
            .Add(GridV1.Registration)
            .AddOverlay(TranslationOverlay.FromPacks(GridId, new MemoryPackSource(onlySwiss), [new TranslationOverlayLocale("de-CH", "en")]))
            .BuildAsync("de-CH");
        Assert.Equal("Datenraster|No rows", Text(english, GridId, "title") + "|" + Text(english, GridId, "empty"), "explicit fallback");

        // Two overlays that add one locale with different fallbacks are an error; an overlay without one accepts the other's.
        await Throws<ArgumentException>(() => TranslationContext.CreateBuilder().Add(GridV1.Registration)
            .AddOverlay(TranslationOverlay.FromPacks(GridId, new MemoryPackSource(onlySwiss), [new TranslationOverlayLocale("de-CH", "en")]))
            .AddOverlay(TranslationOverlay.FromPacks(GridId, new MemoryPackSource(Overlay), [new TranslationOverlayLocale("de-CH", "de")]))
            .BuildAsync().AsTask());
        TranslationContext merged = await TranslationContext.CreateBuilder().Add(GridV1.Registration)
            .AddOverlay(TranslationOverlay.FromPacks(GridId, new MemoryPackSource(onlySwiss), [new TranslationOverlayLocale("de-CH")]))
            .AddOverlay(TranslationOverlay.FromPacks(GridId, new MemoryPackSource([]), [new TranslationOverlayLocale("de-CH", "en")]))
            .BuildAsync("de-CH");
        Assert.Equal("No rows", Text(merged, GridId, "empty"), "the explicit fallback of either overlay applies");
    }

    // A library whose locales exercise CLDR parent locales, likely subtags and scripts.
    private const string RegionalProject = """
        {
          "schemaVersion": 1,
          "catalog": "acme-regional",
          "code": { "namespace": "Acme.Regional", "className": "RegionalText", "visibility": "public" },
          "baseLocale": "en",
          "locales": [ "en", "de-DE", "es-419", "sr" ]
        }
        """;

    private static readonly Catalog Regional = Catalog.Compile(RegionalProject,
        ("en", "title = Grid\nempty = No rows\n"),
        ("de-DE", "title = Raster\nempty = Keine Zeilen\n"),
        ("es-419", "title = Cuadrícula\nempty = Sin filas\n"),
        ("sr", "title = Мрежа\nempty = Нема редова\n"));

    [Test, DisplayName("a catalog matches an undeclared locale with CLDR likely subtags and reports a parent or a related locale")]
    public async Task LikelySubtagResolution()
    {
        TranslationContext context = await TranslationContext.CreateBuilder().Add(Regional.Registration).BuildAsync("es-AR");
        Assert.Equal("acme-regional:es-AR>es-419:Parent", Describe(context.Catalogs[0]), "CLDR parent locale");
        await context.SetLocaleAsync("de-AT");
        Assert.Equal("acme-regional:de-AT>de-DE:Related", Describe(context.Catalogs[0]), "sibling region");
        await context.SetLocaleAsync("de");
        Assert.Equal("acme-regional:de>de-DE:Related", Describe(context.Catalogs[0]), "more specific locale");
        await context.SetLocaleAsync("sr-Latn-RS");
        Assert.Equal("acme-regional:sr-Latn-RS>en:Default", Describe(context.Catalogs[0]), "a Latin request never shows Cyrillic text");
        await context.SetLocaleAsync("sr-Cyrl-ME");
        Assert.Equal("acme-regional:sr-Cyrl-ME>sr:Parent", Describe(context.Catalogs[0]), "same script");
    }

    [Test, DisplayName("an added locale falls back along CLDR parents into the library and keeps its script")]
    public async Task AddedLocaleFollowsCldrParents()
    {
        Dictionary<string, byte[]> packs = Packs(RegionalProject.Replace("[ \"en\", \"de-DE\", \"es-419\", \"sr\" ]",
                "[ \"en\", \"de-CH\", \"es-MX\", \"sr-Latn\" ]", StringComparison.Ordinal)
                .Replace("\"baseLocale\": \"en\",", "\"baseLocale\": \"en\", \"validation\": { \"translationCompleteness\": \"allow\" },", StringComparison.Ordinal),
            ("en", "title = Data grid\nempty = Nothing here\n"),
            ("de-CH", "title = Datenraster\n"),
            ("es-MX", "title = Tabla\n"),
            ("sr-Latn", "title = Mreža\n"));
        packs.Remove("en");

        TranslationContext context = await TranslationContext.CreateBuilder()
            .Add(Regional.Registration)
            .AddOverlay(TranslationOverlay.FromPacks("acme-regional", new MemoryPackSource(packs),
                [new TranslationOverlayLocale("de-CH"), new TranslationOverlayLocale("es-MX"), new TranslationOverlayLocale("sr-Latn")]))
            .BuildAsync("es-MX");
        Assert.Equal("Tabla|Sin filas", Text(context, "acme-regional", "title") + "|" + Text(context, "acme-regional", "empty"),
            "es-MX falls back to its CLDR parent es-419");
        await context.SetLocaleAsync("de-CH");
        Assert.Equal("Datenraster|Keine Zeilen", Text(context, "acme-regional", "title") + "|" + Text(context, "acme-regional", "empty"),
            "de-CH falls back to the de-DE that likely subtags match, without a declared de");
        await context.SetLocaleAsync("sr-Latn");
        Assert.Equal("Mreža|No rows", Text(context, "acme-regional", "title") + "|" + Text(context, "acme-regional", "empty"),
            "sr-Latn never falls back to the Cyrillic sr");
        await context.SetLocaleAsync("es-CO");
        Assert.Equal("acme-regional:es-CO>es-419:Parent", Describe(context.Catalogs[0]), "a declared parent beats an added sibling");
    }

    [Test, DisplayName("a context negotiates a preference list against the locales every catalog shows, then each catalog's")]
    public async Task ContextPreferences()
    {
        TranslationContext context = await TranslationContext.CreateBuilder()
            .Add(App.Registration)
            .Add(GridV1.Registration)
            .BuildAsync("en");
        await context.SetLocaleFromPreferencesAsync(["fr-CH", "de-AT", "en"]);
        Assert.Equal("de|app:de>de:Exact|acme-grid:de>de:Exact", context.RequestedLocale + "|" + Describe(context.Catalogs[0]) + "|" +
            Describe(context.Catalogs[1]), "the first preference every catalog shows");

        await context.GetManager(GridId).SetLocaleFromPreferencesAsync(["fr-CA", "ja"]);
        Assert.Equal("fr|app:fr>fr:Exact|acme-grid:fr>en:Default", context.RequestedLocale + "|" + Describe(context.Catalogs[0]) + "|" +
            Describe(context.Catalogs[1]), "else the first catalog with a match, through any manager");

        await context.SetLocaleFromPreferencesAsync(["ja", "x-invalid-", "und"]);
        Assert.Equal("en", context.RequestedLocale, "else the first catalog's default locale");
        await Throws<ArgumentNullException>(() => context.SetLocaleFromPreferencesAsync(null!).AsTask());
    }

    [Test, DisplayName("fallback copies in an overlay pack never replace the library's text for a locale it declares")]
    public async Task FallbackCopiesYieldToLibrary()
    {
        // A fr pack built with relaxed completeness carries en copies of the keys it does not translate.
        var onlyFrench = new Dictionary<string, byte[]>(StringComparer.Ordinal) { ["fr"] = Overlay["fr"] };
        TranslationContext context = await TranslationContext.CreateBuilder()
            .Add(GridV1.Registration)
            .AddOverlay(TranslationOverlay.FromPacks(GridId, new MemoryPackSource(onlyFrench), [new TranslationOverlayLocale("fr")]))
            .BuildAsync("fr");
        string french = Text(context, GridId, "title") + "|" + Text(context, GridId, "empty");
        await context.SetLocaleAsync("en");
        Assert.Equal("Grille|No rows|No rows", french + "|" + Text(context, GridId, "empty"), "an fr user sees the same English as an en user");
    }

    [Test, DisplayName("contexts from one builder share providers, so a further context loads no overlay pack")]
    public async Task ContextsShareProviders()
    {
        var packs = new MemoryPackSource(Overlay);
        TranslationContextBuilder builder = TranslationContext.CreateBuilder()
            .Add(App.Registration)
            .Add(GridV1.Registration)
            .AddOverlay(TranslationOverlay.FromPacks(GridId, packs, [new TranslationOverlayLocale("fr")]));
        TranslationContext first = await builder.BuildAsync("fr");
        int loads = packs.Loads;
        TranslationContext second = await builder.BuildAsync("fr");
        Assert.Equal(loads, packs.Loads, "the second context reuses the composed overlay snapshot");
        Assert.True(ReferenceEquals(first.GetManager(GridId).Current, second.GetManager(GridId).Current), "one snapshot for both contexts");

        // Each context still owns its locale.
        await second.SetLocaleAsync("de");
        Assert.Equal("fr|de", first.RequestedLocale + "|" + second.RequestedLocale, "independent locales");
    }

    [Test, DisplayName("a non-exact resolution is reported at the verbose level of the Runic-Translations event source")]
    public async Task ReportsNonExactResolution()
    {
        using var listener = new ResolutionListener("pt-BR");
        TranslationContext context = await TranslationContext.CreateBuilder()
            .Add(App.Registration)
            .Add(GridV1.Registration)
            .BuildAsync("de");
        await context.SetLocaleAsync("pt-BR");
        Assert.Equal("LocaleDefaultFallback:acme-grid:pt-BR>en|LocaleDefaultFallback:app:pt-BR>en",
            string.Join("|", listener.Events.Order(StringComparer.Ordinal)), "events");
    }

    [Test, DisplayName("snapshots of the external pack provider record the requested locale")]
    public async Task ExternalSnapshotsRecordRequest()
    {
        var provider = (ITranslationProvider)GridV1.CatalogType.GetMethod("CreateExternalProvider")!
            .Invoke(null, [new MemoryPackSource([]), null, null, null, null])!;
        ITranslationSnapshot snapshot = await provider.GetSnapshotAsync("de-CH");
        Assert.Equal("de-CH>de:Parent", snapshot.RequestedLocale + ">" + snapshot.Locale + ":" + snapshot.LocaleResolution, "external snapshot");
    }

    [Test, DisplayName("the builder ignores a repeated registration and rejects conflicts and orphan overlays")]
    public async Task BuilderRules()
    {
        TranslationContext context = await TranslationContext.CreateBuilder().Add(GridV1.Registration).Add(GridV1.Registration).BuildAsync();
        Assert.Equal(1, context.Catalogs.Count, "repeated registration");
        Assert.Equal("en", context.RequestedLocale, "default locale of the first catalog");
        await Throws<ArgumentException>(() => Task.FromResult(TranslationContext.CreateBuilder().Add(GridV1.Registration).Add(GridV2.Registration)));
        // Options belong to one registration: a registration without options accepts another's, different options conflict.
        var exact = new TranslationOptions { UnsupportedLocale = UnsupportedLocalePolicy.Exact };
        TranslationContextBuilder withOptions = TranslationContext.CreateBuilder().Add(GridV1.Registration)
            .Add(GridV1.Registration, exact).Add(GridV1.Registration, new TranslationOptions { UnsupportedLocale = UnsupportedLocalePolicy.Exact })
            .Add(GridV1.Registration);
        await Throws<TranslationNotFoundException>(() => withOptions.BuildAsync("pt").AsTask());
        ArgumentException conflict = await Throws<ArgumentException>(() => Task.FromResult(TranslationContext.CreateBuilder()
            .Add(GridV1.Registration, exact).Add(GridV1.Registration, new TranslationOptions { MissingKey = MissingTranslationPolicy.ReturnMarker })));
        Assert.True(conflict.Message.Contains("other options", StringComparison.Ordinal), conflict.Message);
        await Throws<InvalidOperationException>(() => TranslationContext.CreateBuilder().Add(App.Registration)
            .AddOverlay(TranslationOverlay.FromPacks(GridId, new MemoryPackSource(Overlay))).BuildAsync().AsTask());
        await Throws<InvalidOperationException>(() => TranslationContext.CreateBuilder().BuildAsync().AsTask());
        // An added locale must fall back to a locale the library declares.
        await Throws<ArgumentException>(() => TranslationContext.CreateBuilder().Add(GridV1.Registration)
            .AddOverlay(TranslationOverlay.FromPacks(GridId, new MemoryPackSource(Overlay), [new TranslationOverlayLocale("fr", "it")])).BuildAsync().AsTask());
    }

    [Test, DisplayName("generated text classes take a context and the catalog exposes its registration")]
    public void GeneratedContextConstructor()
    {
        const string source = """
            namespace Acme.Grid;
            public static class ContextProbe
            {
                public static string Run()
                {
                    global::Runic.Translations.TranslationContext context = global::Runic.Translations.TranslationContext.CreateBuilder()
                        .Add(GridTextCatalog.Registration)
                        .Build("de");
                    var text = new GridText(context);
                    string german = text.Messages.title;
                    context.SetLocaleAsync("en").AsTask().GetAwaiter().GetResult();
                    string nullArgument;
                    try
                    {
                        _ = new GridText(null!);
                        nullArgument = "none";
                    }
                    catch (global::System.ArgumentNullException exception)
                    {
                        nullArgument = exception.ParamName!;
                    }

                    return german + "|" + text.Messages.title + "|" + GridTextCatalog.Registration.CatalogId + "|" + nullArgument;
                }
            }
            """;
        GeneratorRun run = GeneratorTestHost.RunWithConsumer(source, GridV1.Inputs);
        Assert.Equal(0, run.SingleResult.Diagnostics.Length, string.Join("\n", run.SingleResult.Diagnostics));
        // new GridText(null) still compiles and picks the manager constructor.
        Assert.Equal("Raster|Grid|acme-grid|manager", Execute(run, "Acme.Grid.ContextProbe", "Run"), "generated context constructor");
    }

    [Test, DisplayName("a runtime without the composition surface gets no Registration or context constructor")]
    public void CompositionNeedsRuntimeCapability()
    {
        GeneratorRun run = GeneratorTestHost.Run(RuntimeReferenceMode.Rmf2V3Typed, GridV1.Inputs);
        string generated = string.Concat(run.SingleResult.GeneratedSources.Select(static item => item.SourceText.ToString()));
        Assert.True(!generated.Contains("ITranslationContext", StringComparison.Ordinal) &&
            !generated.Contains("TranslationCatalogRegistration", StringComparison.Ordinal), "composition surface emitted for an older runtime");
        Assert.True(GeneratorTestHost.Run(GridV1.Inputs).SingleResult.GeneratedSources
            .Any(static item => item.SourceText.ToString().Contains("TranslationCatalogRegistration Registration", StringComparison.Ordinal)), "Registration missing");
    }

    [Test, DisplayName("overlay precedence follows the case table shared with the web runtime")]
    public async Task OverlayPrecedenceTable()
    {
        // tests/fixtures/translations/overlay-precedence/cases.json; Rmf2EsmOverlayTests runs the same table on the web.
        using JsonDocument table = JsonDocument.Parse(File.ReadAllBytes(RepositoryFile("tests", "fixtures", "translations", "overlay-precedence", "cases.json")));
        JsonElement catalog = table.RootElement.GetProperty("catalog");
        string id = catalog.GetProperty("id").GetString()!;
        Catalog compiled = Catalog.Compile(TableProject(id, catalog, "Acme.Table", relaxed: false), TableSources(catalog));
        int requests = 0;
        foreach (JsonElement @case in table.RootElement.GetProperty("cases").EnumerateArray())
        {
            string name = @case.GetProperty("name").GetString()!;
            TranslationContextBuilder builder = TranslationContext.CreateBuilder().Add(compiled.Registration);
            int index = 0;
            foreach (JsonElement overlay in @case.GetProperty("overlays").EnumerateArray())
            {
                Dictionary<string, byte[]> packs = Packs(TableProject(id, overlay, "Example.Overlay" + index++, relaxed: true), TableSources(overlay));
                if (overlay.TryGetProperty("packs", out JsonElement selected))
                {
                    HashSet<string> kept = selected.EnumerateArray().Select(static item => item.GetString()!).ToHashSet(StringComparer.Ordinal);
                    packs = packs.Where(pack => kept.Contains(pack.Key)).ToDictionary(StringComparer.Ordinal);
                }

                TranslationOverlayLocale[] added = overlay.TryGetProperty("addedLocales", out JsonElement locales)
                    ? locales.EnumerateArray().Select(static item => new TranslationOverlayLocale(item.GetProperty("locale").GetString()!,
                        item.TryGetProperty("fallbackLocale", out JsonElement fallback) ? fallback.GetString() : null)).ToArray()
                    : [];
                builder.AddOverlay(TranslationOverlay.FromPacks(id, new MemoryPackSource(packs), added));
            }

            foreach (JsonElement request in @case.GetProperty("requests").EnumerateArray())
            {
                string locale = request.GetProperty("locale").GetString()!, key = request.GetProperty("key").GetString()!;
                TextArgument[] arguments = request.TryGetProperty("inputs", out JsonElement inputs)
                    ? inputs.EnumerateObject().Select(static input => new TextArgument(input.Name, input.Value.GetInt64())).ToArray()
                    : [];
                TranslationContext context = await builder.BuildAsync(locale);
                Assert.Equal(request.GetProperty("text").GetString(), Text(context, id, key, arguments), name + ": " + locale + " " + key);
                requests++;
            }
        }

        Assert.True(requests >= 15, "the table's requests ran");
    }

    private static string TableProject(string id, JsonElement project, string codeNamespace, bool relaxed)
    {
        var json = new Dictionary<string, object?>
        {
            ["schemaVersion"] = 1,
            ["catalog"] = id,
            ["code"] = new Dictionary<string, string> { ["namespace"] = codeNamespace, ["className"] = "TableText", ["visibility"] = "public" },
            ["baseLocale"] = project.GetProperty("baseLocale").GetString(),
            // A tag, or { tag, fallback }.
            ["locales"] = project.GetProperty("locales").EnumerateArray().Select(static item => item.Clone()).ToArray(),
        };
        if (relaxed) json["validation"] = new Dictionary<string, string> { ["translationCompleteness"] = "allow" };
        return JsonSerializer.Serialize(json);
    }

    private static (string Locale, string Text)[] TableSources(JsonElement project) =>
        project.GetProperty("sources").EnumerateObject().Select(static source => (source.Name, source.Value.GetString()!)).ToArray();

    private static string RepositoryFile(params string[] parts)
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "Runic.Translations.slnx"))) return Path.Combine([directory.FullName, .. parts]);
        throw new InvalidOperationException("The repository root was not found above " + AppContext.BaseDirectory + ".");
    }

    private static string Text(TranslationContext context, string catalog, string name, params TextArgument[] arguments)
    {
        ITranslationSnapshot snapshot = context.GetManager(catalog).Current;
        Assert.True(snapshot.TryGetKey(name, out TranslationKey key), "unknown key " + name);
        return snapshot.Format(key, arguments.Select(argument => Runic.Translations.Generated.GeneratedCatalogSupport.CreateArgument(argument.Name!, argument)).ToArray());
    }

    private static string Describe(TranslationCatalogStatus status) =>
        status.CatalogId + ":" + status.RequestedLocale + ">" + status.Locale + ":" + status.LocaleResolution;

    private static async Task<T> Throws<T>(Func<Task> action) where T : Exception
    {
        try
        {
            await action();
        }
        catch (T exception)
        {
            return exception;
        }

        throw new InvalidOperationException("Expected " + typeof(T).Name + ".");
    }

    private static string Execute(GeneratorRun run, string typeName, string methodName)
    {
        using var stream = new MemoryStream();
        Microsoft.CodeAnalysis.Emit.EmitResult emitted = run.Compilation.Emit(stream);
        Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics));
        Assembly assembly = Assembly.Load(stream.ToArray());
        return (string)assembly.GetType(typeName, throwOnError: true)!.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static)!.Invoke(null, null)!;
    }

    private static Dictionary<string, byte[]> Packs(string project, params (string Locale, string Text)[] sources)
    {
        Rmf2ProjectCompilation compilation = TranslationCompiler.CompileRmf2ProjectV5(
            new TranslationSource("translations/runic.json", Encoding.UTF8.GetBytes(project)),
            sources.Select(source => new TranslationSource("translations/" + source.Locale + ".rmf2", Encoding.UTF8.GetBytes(source.Text))).ToArray());
        Assert.True(compilation.Success && compilation.Project is not null, string.Join("; ", compilation.Diagnostics.Select(item => item.Id + " " + item.Message)));
        return compilation.Project!.Locales.ToDictionary(locale => locale.Tag,
            locale => Rmf2LocaleArtifactV5.Render(compilation.Project!, locale.Tag).GetUtf8Bytes(), StringComparer.Ordinal);
    }

    // A generated catalog, compiled and loaded like a referenced library.
    private sealed class Catalog
    {
        private Catalog(TestInput[] inputs, Type catalogType)
        {
            Inputs = inputs;
            CatalogType = catalogType;
            Registration = (TranslationCatalogRegistration)catalogType.GetProperty("Registration")!.GetValue(null)!;
        }

        internal TestInput[] Inputs { get; }
        internal Type CatalogType { get; }
        internal TranslationCatalogRegistration Registration { get; }

        internal static Catalog Compile(string project, params (string Locale, string Text)[] sources)
        {
            TestInput[] inputs = [new("C:/repo/translations/runic.json", "Project", project),
                .. sources.Select(source => new TestInput("C:/repo/translations/" + source.Locale + ".rmf2", "Rmf2", source.Text))];
            GeneratorRun run = GeneratorTestHost.Run(inputs);
            Assert.Equal(0, run.SingleResult.Diagnostics.Length, string.Join("\n", run.SingleResult.Diagnostics));
            using var stream = new MemoryStream();
            Microsoft.CodeAnalysis.Emit.EmitResult emitted = run.Compilation.Emit(stream);
            Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics));
            Assembly assembly = Assembly.Load(stream.ToArray());
            Type catalog = assembly.GetTypes().Single(static type => type.Name.EndsWith("TextCatalog", StringComparison.Ordinal));
            return new Catalog(inputs, catalog);
        }
    }

    private sealed class MemoryPackSource(Dictionary<string, byte[]> packs) : IExternalTranslationSource
    {
        private volatile Dictionary<string, byte[]> _packs = packs;
        private int _loads;

        internal string? Failing { get; set; }

        internal int Loads => Volatile.Read(ref _loads);

        internal void Replace(Dictionary<string, byte[]> packs) => _packs = packs;

        public ValueTask<ExternalTranslationPack?> LoadAsync(string catalog, string locale, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _loads);
            if (locale == Failing) throw new IOException("The pack store is unavailable.");
            return ValueTask.FromResult(_packs.TryGetValue(locale, out byte[]? bytes) ? new ExternalTranslationPack(bytes) : null);
        }
    }

    // Holds the pack of one locale until released, like a download; may ignore cancellation.
    private sealed class GatedPackSource(Dictionary<string, byte[]> packs, string gatedLocale, bool observeCancellation) : IExternalTranslationSource
    {
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private volatile bool _sawCancellation;

        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal bool SawCancellation => _sawCancellation;

        internal void Release() => _release.TrySetResult();

        public async ValueTask<ExternalTranslationPack?> LoadAsync(string catalog, string locale, CancellationToken cancellationToken)
        {
            if (locale == gatedLocale)
            {
                Entered.TrySetResult();
                try
                {
                    if (observeCancellation) await _release.Task.WaitAsync(cancellationToken);
                    else await _release.Task;
                }
                finally
                {
                    _sawCancellation = cancellationToken.IsCancellationRequested;
                }
            }

            return packs.TryGetValue(locale, out byte[]? bytes) ? new ExternalTranslationPack(bytes) : null;
        }
    }

    private sealed class ResolutionListener(string requestedLocale) : EventListener
    {
        private readonly ConcurrentQueue<string> _events = new();

        internal IEnumerable<string> Events => _events;

        protected override void OnEventSourceCreated(EventSource eventSource)
        {
            if (eventSource.Name == "Runic-Translations") EnableEvents(eventSource, EventLevel.Verbose);
        }

        protected override void OnEventWritten(EventWrittenEventArgs eventData)
        {
            if (eventData.Payload is [string catalog, string requested, string locale] && requested == requestedLocale)
                _events.Enqueue(eventData.EventName + ":" + catalog + ":" + requested + ">" + locale);
        }
    }
}
