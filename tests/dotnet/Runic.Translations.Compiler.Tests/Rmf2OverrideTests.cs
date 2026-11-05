using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using Runic.Translations.Compiler.Generation;
using TUnit.Core;

namespace Runic.Translations.Compiler.Tests;

// Build-time overrides (runic.json "overrides", T1d): a library catalog compiled from its packed runic.json and sources with
// an application's translations/overrides/<catalog>/<locale>.rmf2, each message checked against the library's key.
[Category("rmf2-semantic-v5")]
internal sealed class Rmf2OverrideTests
{
    private const string LibraryProject = """
        { "schemaVersion": 1, "catalog": "acme-grid", "code": { "namespace": "Acme.Grid", "className": "GridText" },
          "baseLocale": "en", "locales": ["en", "de"], "sourceRoots": [{ "path": "text", "namespace": ["grid"] }] }
        """;
    private const string LibraryEnglish = "empty = No rows\nrows =\n  .input {$count :integer}\n  .match $count\n  one {{One row}}\n  * {{{$count} rows}}\nsort = Sort by {$column}\nhelp = Read {#link}the guide{/link}\n";
    private const string LibraryGerman = "empty = Keine Zeilen\nrows =\n  .input {$count :integer}\n  .match $count\n  one {{Eine Zeile}}\n  * {{{$count} Zeilen}}\nsort = Sortieren nach {$column}\nhelp = Lies {#link}die Anleitung{/link}\n";

    [Test, DisplayName("overrides replace library messages per locale, add locales and keep the library's contract")]
    public void CompilesOverlay()
    {
        Rmf2ProjectCompilation library = CompileLibrary();
        Rmf2ProjectCompilation result = Compile(
            ("translations/overrides/acme-grid/en.rmf2", "grid {\n  empty = Nothing here\n}\n"),
            ("translations/overrides/acme-grid/fr.rmf2", "grid {\n  empty = Aucune ligne\n  rows =\n    .input {$count :integer}\n    .match $count\n    one {{Une ligne}}\n    many {{{$count} de lignes}}\n    * {{{$count} lignes}}\n  help = Lisez {#link}le guide{/link}\n}\n"));
        Assert.True(result.Success, Describe(result));
        Rmf2ProjectV5 overlay = result.Project!;
        Assert.Equal(library.Project!.CallerFingerprint, overlay.CallerFingerprint, "the library's contract fingerprint");
        Assert.Equal("en=grid_empty|fr=grid_empty,grid_help,grid_rows",
            string.Join("|", overlay.OverlayKeys.Select(pair => pair.Key + "=" + string.Join(",", pair.Value))), "overridden keys");
        Assert.Equal("fr", string.Join(",", overlay.OverlayAddedLocales), "added locales");
        Assert.Equal("en", overlay.Locales.Single(locale => locale.Tag == "fr").FallbackTag, "fr falls back to the base locale");

        using JsonDocument english = JsonDocument.Parse(Rmf2LocaleArtifactV5.RenderOverlay(overlay, "en").Text);
        Assert.Equal("grid_empty", string.Join(",", english.RootElement.GetProperty("messages").EnumerateObject().Select(message => message.Name)), "only overrides");
        Assert.Equal("en", english.RootElement.GetProperty("messages").GetProperty("grid_empty").GetProperty("contentLocale").GetString(), "content locale");
        Assert.Equal(library.Project.CallerFingerprint, english.RootElement.GetProperty("contractFingerprint").GetString(), "fingerprint");
        using JsonDocument french = JsonDocument.Parse(Rmf2LocaleArtifactV5.RenderOverlay(overlay, "fr").Text);
        Assert.Equal("grid_empty,grid_help,grid_rows", string.Join(",", french.RootElement.GetProperty("messages").EnumerateObject().Select(message => message.Name)), "fr overrides");
        // The markup contract maps fr to its own content for the overridden keys and to English for the others.
        JsonElement contentLocales = french.RootElement.GetProperty("markupContract").GetProperty("messages");
        Assert.Equal("fr", contentLocales.GetProperty("grid_empty").GetProperty("contentLocales").GetProperty("fr").GetString(), "fr content");
        Assert.Equal("en", contentLocales.GetProperty("grid_sort").GetProperty("contentLocales").GetProperty("fr").GetString(), "fr falls back");
        Assert.Throws<ArgumentException>(() => Rmf2LocaleArtifactV5.RenderOverlay(overlay, "de"), "de has no overrides");
    }

    [Test, DisplayName("an added regional locale falls back to the library locale it extends")]
    public void RegionalFallback()
    {
        Rmf2ProjectCompilation result = Compile(("translations/overrides/acme-grid/de-CH.rmf2", "grid {\n  empty = Keine Zeilen vorhanden\n}\n"));
        Assert.True(result.Success, Describe(result));
        Assert.Equal("de", result.Project!.Locales.Single(locale => locale.Tag == "de-CH").FallbackTag, "de-CH falls back to de");
    }

    private static readonly string[] AddedTags = ["de-CH", "de", "zh-TW", "zh-Hant", "es-MX", "es-419", "sr-Latn-RS", "pt-BR"];

    [Test, DisplayName("added locales fall back by the runtimes' rule, to added parents too, whatever the file order")]
    public void AddedLocaleFallbacks()
    {
        // The library declares en only; the application adds de and de-CH (de-CH.rmf2 sorts before de.rmf2), zh-Hant and
        // zh-TW, es-419 and es-MX, sr-Latn-RS (never the Cyrillic sr) and pt-BR (no parent added: the base locale).
        const string english = "{ \"schemaVersion\": 1, \"catalog\": \"acme-grid\", \"code\": { \"namespace\": \"Acme.Grid\", \"className\": \"GridText\" }, \"baseLocale\": \"en\", \"locales\": [\"en\", \"sr\"] }";
        Rmf2ProjectCompilation result = TranslationCompiler.CompileRmf2OverlayV5(
            new TranslationSource("packages/acme.grid/runic/acme-grid/runic.json", Encoding.UTF8.GetBytes(english)),
            [new TranslationSource("packages/acme.grid/runic/acme-grid/en.rmf2", Encoding.UTF8.GetBytes("empty = No rows\ntitle = Grid\n")),
             new TranslationSource("packages/acme.grid/runic/acme-grid/sr.rmf2", Encoding.UTF8.GetBytes("empty = Нема редова\ntitle = Мрежа\n"))],
            AddedTags.Select(locale =>
                new TranslationSource("translations/overrides/acme-grid/" + locale + ".rmf2", Encoding.UTF8.GetBytes("title = " + locale + "\n"))));
        Assert.True(result.Success, Describe(result));
        Rmf2ProjectV5 overlay = result.Project!;
        Assert.Equal("de>en|de-CH>de|es-419>en|es-MX>es-419|pt-BR>en|sr-Latn-RS>en|zh-Hant>en|zh-TW>zh-Hant",
            string.Join("|", overlay.OverlayAddedLocales.Select(locale => locale + ">" + overlay.Locales.Single(item => item.Tag == locale).FallbackTag)), "fallbacks");
        // The generated overlay names each fallback, so the .NET runtime follows the compiler's chain.
        string registration = Rmf2CSharpOutputRendererV5.RenderRegistration(CompileLibrary().Project!, overlays: [overlay]).Text;
        Assert.True(registration.Contains("new global::Runic.Translations.TranslationOverlayLocale(\"de-CH\", \"de\")", StringComparison.Ordinal), registration);
    }

    private static readonly string[] FilterLocales = ["en", "en-001", "fr", "sr", "sr-Latn", "zh", "zh-Hant"];

    [Test, DisplayName("the locale filter keeps the library locales the runtimes resolve the application's locales to")]
    public void LocaleFilterMatchesLikeTheRuntimes()
    {
        // zh-TW resolves to zh-Hant (never the Simplified zh), sr-ME to sr-Latn (never the Cyrillic sr), en-GB to en-001.
        const string project = "{ \"schemaVersion\": 1, \"catalog\": \"acme-grid\", \"code\": { \"namespace\": \"Acme.Grid\", \"className\": \"GridText\" }, \"baseLocale\": \"en\", " +
            "\"locales\": [\"en\", \"en-001\", \"fr\", \"sr\", \"sr-Latn\", \"zh\", \"zh-Hant\"], \"validation\": { \"translationCompleteness\": \"allow\" } }";
        TranslationSource[] sources = FilterLocales.Select(locale => new TranslationSource("runic/acme-grid/" + locale + ".rmf2", Encoding.UTF8.GetBytes("title = " + locale + "\n"))).ToArray();
        string Kept(params string[] locales)
        {
            Rmf2ProjectCompilation result = TranslationCompiler.CompileRmf2OverlayV5(new TranslationSource("runic/acme-grid/runic.json", Encoding.UTF8.GetBytes(project)),
                sources, [], locales: locales);
            Assert.True(result.Success, Describe(result));
            return string.Join(",", result.Project!.Locales.Select(locale => locale.Tag));
        }
        Assert.True(Kept("en", "zh-TW").Split(',').Contains("zh-Hant"), "zh-TW keeps zh-Hant: " + Kept("en", "zh-TW"));
        Assert.True(Kept("en", "sr-ME").Split(',').Contains("sr-Latn"), "sr-ME keeps sr-Latn: " + Kept("en", "sr-ME"));
        Assert.True(Kept("en-GB").Split(',').Contains("en-001"), "en-GB keeps en-001: " + Kept("en-GB"));
        Assert.True(!Kept("en", "zh-TW", "sr-ME").Split(',').Contains("fr"), "fr is not an application locale: " + Kept("en", "zh-TW", "sr-ME"));
        Assert.Equal("en,fr", Kept("fr-CA"), "fr-CA keeps fr and the base locale");
    }

    [Test, DisplayName("overrides that do not match the library's keys are errors in the override source")]
    public void RejectsMismatches()
    {
        Rmf2ProjectCompilation result = Compile(("translations/overrides/acme-grid/en.rmf2",
            "grid {\n  emtpy = Typo\n  removed = Gone\n  sort = Sort by {$column} and {$order}\n  empty = {#link}Plain{/link} text\n  rows =\n    .input {$count :number}\n    {{{$count}}}\n}\n"));
        Assert.True(!result.Success, "fails");
        string[] reported = result.Diagnostics.Where(diagnostic => diagnostic.Severity == TranslationDiagnosticSeverity.Error)
            .Select(diagnostic => diagnostic.Id + " " + diagnostic.Location.Path + " " + diagnostic.Message).ToArray();
        Assert.True(reported.Any(line => line.StartsWith("RTR0103 translations/overrides/acme-grid/en.rmf2", StringComparison.Ordinal) && line.Contains("'grid.emtpy'") && line.Contains("Did you mean 'grid.empty'?")), Describe(result));
        Assert.True(reported.Any(line => line.StartsWith("RTR0103", StringComparison.Ordinal) && line.Contains("'grid.removed'")), Describe(result));
        Assert.True(reported.Any(line => line.StartsWith("RTR0016", StringComparison.Ordinal) && line.Contains("'order'")), "a new input: " + Describe(result));
        Assert.True(reported.Any(line => line.StartsWith("RTR0065", StringComparison.Ordinal) && line.Contains("'count'")), "a changed input type: " + Describe(result));
        Assert.True(reported.Any(line => line.StartsWith("RTR0103", StringComparison.Ordinal) && line.Contains("plain text")), "markup in plain text: " + Describe(result));
        Assert.True(result.Diagnostics.All(diagnostic => diagnostic.Location.Path.StartsWith("translations/overrides/", StringComparison.Ordinal)),
            "every diagnostic is in the override source: " + Describe(result));
    }

    [Test, DisplayName("runic.json overrides entries are validated")]
    public void ValidatesEntries()
    {
        string project = """
            { "schemaVersion": 1, "catalog": "shop", "code": { "namespace": "Shop", "className": "ShopText" }, "baseLocale": "en",
              "overrides": [{ "catalog": "acme-grid", "from": "Acme.Grid" }, { "catalog": "acme-grid", "from": "Acme.Grid" },
                            { "catalog": "shop", "from": "Shop" }, { "catalog": "Bad", "from": "x" }, { "catalog": "acme-chart", "from": "not a package" },
                            { "catalog": "acme-menu", "from": "@acme/menu", "extra": 1 }] }
            """;
        Rmf2ProjectCompilation result = TranslationCompiler.CompileRmf2ProjectV5(new TranslationSource("translations/runic.json", Encoding.UTF8.GetBytes(project)),
            [new TranslationSource("translations/en.rmf2", Encoding.UTF8.GetBytes("title = Shop\n"))]);
        string reported = string.Join("\n", result.Diagnostics.Select(diagnostic => diagnostic.Id + " " + diagnostic.Message));
        Assert.True(reported.Contains("RTR0104 Catalog 'acme-grid' is overridden more than once.", StringComparison.Ordinal), reported);
        Assert.True(reported.Contains("RTR0104 A project cannot override its own catalog 'shop'", StringComparison.Ordinal), reported);
        Assert.True(reported.Contains("RTR0006 Invalid catalog ID 'Bad'.", StringComparison.Ordinal), reported);
        Assert.True(reported.Contains("RTR0104 'not a package' is not a NuGet or npm package ID.", StringComparison.Ordinal), reported);
        Assert.True(reported.Contains("RTR0019 Unknown or misplaced member 'extra'.", StringComparison.Ordinal), reported);

        string valid = """
            { "schemaVersion": 1, "catalog": "shop", "code": { "namespace": "Shop", "className": "ShopText" }, "baseLocale": "en",
              "overrides": [{ "catalog": "acme-grid", "from": "Acme.Grid" }, { "catalog": "acme-menu", "from": "@acme/menu" }] }
            """;
        Rmf2ProjectCompilation compiled = TranslationCompiler.CompileRmf2ProjectV5(new TranslationSource("translations/runic.json", Encoding.UTF8.GetBytes(valid)),
            [new TranslationSource("translations/en.rmf2", Encoding.UTF8.GetBytes("title = Shop\n"))]);
        Assert.True(compiled.Success, Describe(compiled));
        Assert.Equal("acme-grid<Acme.Grid|acme-menu<@acme/menu", string.Join("|", compiled.Project!.Overrides.Select(item => item.Catalog + "<" + item.From)), "entries");
        Assert.Equal("acme-grid,acme-menu", string.Join(",", TranslationCompiler.ReadOverridesV5(new TranslationSource("runic.json", Encoding.UTF8.GetBytes(valid))).Select(item => item.Catalog)), "reader");
    }

    private static Rmf2ProjectCompilation CompileLibrary() => TranslationCompiler.CompileRmf2ProjectV5(
        new TranslationSource("packages/acme.grid/runic/acme-grid/runic.json", Encoding.UTF8.GetBytes(LibraryProject)),
        [new TranslationSource("packages/acme.grid/runic/acme-grid/text/de.rmf2", Encoding.UTF8.GetBytes(LibraryGerman)),
         new TranslationSource("packages/acme.grid/runic/acme-grid/text/en.rmf2", Encoding.UTF8.GetBytes(LibraryEnglish))]);

    private static Rmf2ProjectCompilation Compile(params (string Path, string Source)[] overrides) => TranslationCompiler.CompileRmf2OverlayV5(
        new TranslationSource("packages/acme.grid/runic/acme-grid/runic.json", Encoding.UTF8.GetBytes(LibraryProject)),
        [new TranslationSource("packages/acme.grid/runic/acme-grid/text/de.rmf2", Encoding.UTF8.GetBytes(LibraryGerman)),
         new TranslationSource("packages/acme.grid/runic/acme-grid/text/en.rmf2", Encoding.UTF8.GetBytes(LibraryEnglish))],
        overrides.Select(item => new TranslationSource(item.Path, Encoding.UTF8.GetBytes(item.Source))));

    private static string Describe(Rmf2ProjectCompilation result) =>
        string.Join("; ", result.Diagnostics.Select(diagnostic => diagnostic.Id + " " + diagnostic.Location.Path + " " + diagnostic.Message));
}
