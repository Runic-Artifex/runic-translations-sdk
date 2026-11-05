using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Runic.Translations.Compiler.Generation;
using TUnit.Core;

namespace Runic.Translations.Compiler.Tests;

// :currency, :unit and compact notation in the compiler (D055.4): validation of codes and units (RTR0101), declared
// run-time sets (RTR0102), and the data each output embeds: only the currencies, units and styles used, for the
// catalog's languages, and nothing for a catalog that uses none of them.
[Category("rmf2-semantic-v5")]
internal sealed class Rmf2CurrencyUnitCompilerTests
{
    private static Rmf2ProjectCompilation Compile(string source, string? formats = null, string locales = "[\"en\"]", params (string Locale, string Source)[] others)
    {
        string json = "{\"schemaVersion\":1,\"catalog\":\"app\",\"code\":{\"namespace\":\"Example\",\"className\":\"Text\"},\"baseLocale\":\"en\",\"locales\":" + locales +
            (formats is null ? "" : ",\"formats\":" + formats) + "}";
        return TranslationCompiler.CompileRmf2ProjectV5(new TranslationSource("translations/runic.json", Encoding.UTF8.GetBytes(json)),
            [new TranslationSource("translations/en.rmf2", Encoding.UTF8.GetBytes(source)), .. others.Select(item => new TranslationSource("translations/" + item.Locale + ".rmf2", Encoding.UTF8.GetBytes(item.Source)))]);
    }

    private static string Diagnostics(Rmf2ProjectCompilation result) => string.Join("; ", result.Diagnostics.Select(item => item.Id + " " + item.Message));

    private static void Rejects(string id, string text, Rmf2ProjectCompilation result)
    {
        Assert.True(!result.Success, "Compiled although it should report " + id + ".");
        Assert.True(result.Diagnostics.Any(item => item.Id == id && item.Message.Contains(text, StringComparison.Ordinal)), "Expected " + id + " with '" + text + "', got: " + Diagnostics(result));
    }

    [Test, DisplayName("unknown currencies and units are RTR0101, in messages and in runic.json formats")]
    public void UnknownCodes()
    {
        Rejects("RTR0101", "Unknown currency 'XYZ'", Compile("total = {{{$n :currency currency=XYZ}}}\n"));
        Rejects("RTR0101", "Unknown currency 'DEM'", Compile("total = {{{$n :currency currency=DEM}}}\n"));
        Rejects("RTR0101", "Unknown unit 'furlong'", Compile("distance = {{{$n :unit unit=furlong}}}\n"));
        Rejects("RTR0101", "Unknown unit 'kilometer-per-furlong'", Compile("speed = {{{$n :unit unit=kilometer-per-furlong}}}\n"));
        Rejects("RTR0101", "Unknown currency 'usd' in formats.currencies", Compile("title = Title\n", "{\"currencies\":[\"usd\"]}"));
        Rejects("RTR0101", "Unknown unit 'parsec' in formats.units", Compile("title = Title\n", "{\"units\":[\"parsec\"]}"));
        Rejects("RTR0004", "Duplicate currency 'EUR'", Compile("title = Title\n", "{\"currencies\":[\"EUR\",\"EUR\"]}"));
        Rejects("RTR0004", "formats.units must be an array", Compile("title = Title\n", "{\"units\":\"meter\"}"));
        // A composed X-per-Y of two pinned simple units and CLDR's own compounds compile.
        Rmf2ProjectCompilation composed = Compile("a = {{{$n :unit unit=megabyte-per-second}}}\nb = {{{$n :unit unit=kilometer-per-hour}}}\n");
        Assert.True(composed.Success, Diagnostics(composed));
    }

    [Test, DisplayName("a currency or unit from a variable needs a runic.json formats set (RTR0102); the functions need their option")]
    public void DeclaredSets()
    {
        const string currency = "price =\n  .input {$code :string}\n  {{{$n :currency currency=$code}}}\n";
        const string unit = "distance =\n  .input {$u :string}\n  {{{$n :unit unit=$u}}}\n";
        Rejects("RTR0102", "formats.currencies", Compile(currency));
        Rejects("RTR0102", "formats.units", Compile(unit));
        Rejects("RTR0102", "formats.currencies", Compile(currency, "{\"units\":[\"meter\"]}"));
        Rmf2ProjectCompilation declared = Compile(currency + unit, "{\"currencies\":[\"EUR\",\"USD\"],\"units\":[\"meter\",\"mile\"]}");
        Assert.True(declared.Success, Diagnostics(declared));
        Rejects("RTR0065", "requires the 'currency' option", Compile("total = {{{$n :currency}}}\n"));
        Rejects("RTR0065", "requires the 'unit' option", Compile("distance = {{{$n :unit unitDisplay=long}}}\n"));
        Rejects("RTR0065", "Compact notation does not apply to percentages", Compile("share = {{{$n :number style=percent runic:notation=compact}}}\n"));
        Rejects("RTR0065", "Invalid literal for option 'currencyDisplay'", Compile("total = {{{$n :currency currency=EUR currencyDisplay=never}}}\n"));
    }

    [Test, DisplayName("outputs embed only the currencies, units, compact styles and languages a catalog uses")]
    public void EmbeddedSelection()
    {
        Rmf2ProjectCompilation result = Compile("total = {{{$n :currency currency=USD}}}\nreach = {{{$n :number runic:notation=compact}}}\n", "{\"units\":[\"mile\"]}",
            "[\"en\",\"de\"]", ("de", "total = {{{$n :currency currency=USD}}}\nreach = {{{$n :number runic:notation=compact}}}\n"));
        Assert.True(result.Success, Diagnostics(result));
        Rmf2ProjectV5 project = result.Project!;
        using JsonDocument data = JsonDocument.Parse(project.FormatData(["en", "de"], project.Locales.SelectMany(locale => locale.DirectResources).Select(item => item.Message))!);
        JsonElement root = data.RootElement;
        Assert.Equal("USD", string.Join(",", root.GetProperty("currencies").EnumerateObject().Select(item => item.Name)), "Embedded currencies");
        // mile is declared, but no message reads a unit from a variable.
        Assert.True(!root.TryGetProperty("dynamicUnits", out _) && !root.TryGetProperty("dynamicCurrencies", out _), "Unused declared sets were embedded.");
        string[] ids = root.GetProperty("locales").EnumerateObject().Select(item => item.Name).ToArray();
        Assert.True(ids.Contains("en") && ids.Contains("de") && ids.Contains("de-CH") && ids.Contains("en-IN"), "Regional data of the catalog's languages is missing: " + string.Join(' ', ids));
        Assert.True(ids.All(id => id.StartsWith("en", StringComparison.Ordinal) || id.StartsWith("de", StringComparison.Ordinal)), "Another language was embedded: " + string.Join(' ', ids));
        foreach (System.Text.Json.JsonProperty locale in root.GetProperty("locales").EnumerateObject())
            foreach (System.Text.Json.JsonProperty section in locale.Value.EnumerateObject())
            {
                Assert.True(section.Name is "currencyFormats" or "currencies" or "compact", "Unused section " + section.Name + " in " + locale.Name);
                if (section.Name == "currencies") Assert.Equal("USD", string.Join(",", section.Value.EnumerateObject().Select(item => item.Name)), "Currencies of " + locale.Name);
                if (section.Name == "compact")
                    foreach (System.Text.Json.JsonProperty system in section.Value.EnumerateObject())
                        Assert.Equal("short", string.Join(",", system.Value.EnumerateObject().Select(item => item.Name)), "Compact styles of " + locale.Name);
            }

        // Each locale artifact carries the data of its messages; the ESM runtime the catalog's.
        using JsonDocument artifact = JsonDocument.Parse(Rmf2LocaleArtifactV5.Render(project, "de").GetUtf8Bytes());
        Assert.True(artifact.RootElement.GetProperty("formatData").GetProperty("locales").EnumerateObject().All(item => item.Name.StartsWith("de", StringComparison.Ordinal)),
            "The de artifact embeds another language's data.");
        string runtime = TranslationOutputRenderer.RenderRmf2V5EsmModules(project).Single(item => item.RelativePath.EndsWith("/runtime.js", StringComparison.Ordinal)).Text;
        Assert.True(runtime.Contains("\"formats\":{\"currencies\":{\"USD\":2}", StringComparison.Ordinal) && runtime.Contains("function formatCurrency(value, options", StringComparison.Ordinal),
            "The ESM runtime lacks the catalog's format data or formatters.");

        // A catalog without the functions carries neither data nor formatters.
        Rmf2ProjectCompilation plain = Compile("title = Title\n");
        Assert.True(plain.Success, Diagnostics(plain));
        string plainRuntime = TranslationOutputRenderer.RenderRmf2V5EsmModules(plain.Project!).Single(item => item.RelativePath.EndsWith("/runtime.js", StringComparison.Ordinal)).Text;
        Assert.True(!plainRuntime.Contains("\"formats\":", StringComparison.Ordinal) && !plainRuntime.Contains("function formatCompact(", StringComparison.Ordinal), "A plain catalog carries format data or formatters.");
        Assert.True(!Rmf2LocaleArtifactV5.Render(plain.Project!, "en").Text.Contains("formatData", StringComparison.Ordinal), "A plain locale artifact carries formatData.");
    }

    [Test, NotInParallel, DisplayName("an ESM overlay brings the CLDR data of its messages; a catalog without the formatters rejects it")]
    public void OverlayFormatData()
    {
        Rmf2ProjectV5 library = Rmf2EsmV5Tests.CompileProject("shop", "en", ["en", "fr"], false, ("en", "total = {{Total: {$n :currency currency=EUR}}}\n"),
            ("fr", "total = {{Total : {$n :currency currency=EUR}}}\n"));
        Rmf2ProjectV5 plain = Rmf2EsmV5Tests.CompileProject("plain", "en", ["en"], false, ("en", "total =\n  .input {$n :number}\n  {{Total: {$n}}}\n"));
        // An application's French overlay with another currency, which the library's runtime does not embed.
        Rmf2ProjectV5 overlay = Rmf2EsmV5Tests.CompileProject("shop", "fr", ["fr"], false, ("fr", "total = {{Total : {$n :currency currency=CHF}}}\n"));
        Rmf2ProjectV5 plainOverlay = Rmf2EsmV5Tests.CompileProject("plain", "fr", ["fr"], false, ("fr", "total = {{Total : {$n :currency currency=CHF}}}\n"));
        string directory = Rmf2EsmV5Tests.Write([.. TranslationOutputRenderer.RenderRmf2V5EsmModules(library), .. TranslationOutputRenderer.RenderRmf2V5EsmModules(plain)]);
        try
        {
            File.WriteAllBytes(Path.Combine(directory, "overlay.fr.json"), Rmf2LocaleArtifactV5.Render(overlay, "fr").GetUtf8Bytes());
            File.WriteAllBytes(Path.Combine(directory, "plain.fr.json"), Rmf2LocaleArtifactV5.Render(plainOverlay, "fr").GetUtf8Bytes());
            string script = Path.Combine(directory, "overlay.mjs");
            File.WriteAllText(script, """
                import { readFile } from "node:fs/promises";
                import { m } from "./shop.esm-v5/messages.js";
                import { installOverlay } from "./shop.esm-v5/dynamic.js";
                import { installOverlay as installPlain } from "./plain.esm-v5/dynamic.js";
                const artifact = async name => JSON.parse(await readFile(new URL(name, import.meta.url), "utf8"));
                const installed = installOverlay(await artifact("overlay.fr.json"));
                if (!installed.ok) throw new Error(JSON.stringify(installed));
                const french = m.total({ n: "12.5" }, { locale: "fr" }), english = m.total({ n: "12.5" }, { locale: "en" });
                if (french !== "Total : 12,50\u00a0CHF" || english !== "Total: €12.50") throw new Error(JSON.stringify([french, english]));
                const rejected = installPlain(await artifact("plain.fr.json"));
                if (rejected.ok || rejected.reason !== "RTR0023/malformed") throw new Error(JSON.stringify(rejected));
                """, new UTF8Encoding(false));
            foreach (string host in Rmf2DatePercentConformanceTests.EsmHosts()) Rmf2EsmV5Tests.Run(host, [script], directory);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Test, NotInParallel, DisplayName("a run-time currency or unit outside the declared set fails in .NET and ESM alike")]
    public void UndeclaredValueAtRunTime()
    {
        Rmf2ProjectCompilation result = Compile("price =\n  .input {$code :string}\n  {{{$n :currency currency=$code}}}\ndistance =\n  .input {$u :string}\n  {{{$n :unit unit=$u}}}\n",
            "{\"currencies\":[\"EUR\"],\"units\":[\"meter\"]}");
        Assert.True(result.Success, Diagnostics(result));
        Rmf2ProjectV5 project = result.Project!;
        VerifiedExternalTranslationPack pack = TranslationPackLoader.VerifyAsync(new ExternalTranslationPack(Rmf2LocaleArtifactV5.Render(project, "en").GetUtf8Bytes()),
            Rmf2V1CorpusTests.PackContract(project, "en")).AsTask().GetAwaiter().GetResult();
        string Format(string key, string name, string value)
        {
            try
            {
                return pack.Messages.Single(item => item.Key.Name == key).Message!.Rmf2V5!.Format(
                    [TextArgument.CreateRmf2(name, new TextArgument("_", value)), TextArgument.CreateRmf2("n", new TextArgument("_", 2m))], "en");
            }
            catch (TranslationFormatException exception) { return "error: " + exception.Message; }
        }
        Assert.Equal("€2.00", Format("price", "code", "EUR"), ".NET declared currency");
        Assert.Equal("error: Currency 'USD' is not in the catalog's runic.json formats.currencies.", Format("price", "code", "USD"), ".NET undeclared currency");
        Assert.Equal("2 m", Format("distance", "u", "meter"), ".NET declared unit");
        Assert.Equal("error: Unit 'mile' is not in the catalog's runic.json formats.units.", Format("distance", "u", "mile"), ".NET undeclared unit");

        string directory = Rmf2EsmV5Tests.Write(TranslationOutputRenderer.RenderRmf2V5EsmModules(project));
        try
        {
            string script = Path.Combine(directory, "undeclared.mjs");
            File.WriteAllText(script, """
                import { m } from "./app.esm-v5/messages.js";
                const attempt = run => { try { return run(); } catch (error) { return `${error.name}: ${error.message}`; } };
                const results = [
                  attempt(() => m.price({ n: "2", code: "EUR" }, { locale: "en" })),
                  attempt(() => m.price({ n: "2", code: "USD" }, { locale: "en" })),
                  attempt(() => m.distance({ n: "2", u: "meter" }, { locale: "en" })),
                  attempt(() => m.distance({ n: "2", u: "mile" }, { locale: "en" })),
                ];
                const expected = ["€2.00", "RangeError: Currency 'USD' is not in the catalog's runic.json formats.currencies.", "2 m", "RangeError: Unit 'mile' is not in the catalog's runic.json formats.units."];
                if (JSON.stringify(results) !== JSON.stringify(expected)) throw new Error(JSON.stringify(results));
                """, new UTF8Encoding(false));
            foreach (string host in Rmf2DatePercentConformanceTests.EsmHosts()) Rmf2EsmV5Tests.Run(host, [script], directory);
        }
        finally { Directory.Delete(directory, true); }
    }
}
