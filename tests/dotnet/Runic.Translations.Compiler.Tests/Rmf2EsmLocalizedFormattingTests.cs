using System.IO;
using System.Linq;
using System.Text;
using Runic.Translations.Compiler.Generation;
using TUnit.Core;

namespace Runic.Translations.Compiler.Tests;

// The ESM half of localized formatting beyond the shared corpus: Date and sub-second or offset date-time inputs, the
// local time zone default, the requested locale (getRequestedLocale, getMessageLocale, formatLocale on content) and
// onFallback, plus their TypeScript declarations.
[Category("rmf2-semantic-v5")]
internal sealed class Rmf2EsmLocalizedFormattingTests
{
    // German leaves farewell untranslated, so it falls back to English.
    private static Rmf2ProjectV5 Compile(params (string Locale, string Source)[] sources)
    {
        const string json = """{"schemaVersion":1,"catalog":"formats","code":{"namespace":"Example","className":"Text"},"baseLocale":"en","locales":["en","de"],"validation":{"translationCompleteness":"allow"}}""";
        Rmf2ProjectCompilation result = TranslationCompiler.CompileRmf2ProjectV5(new TranslationSource("translations/runic.json", Encoding.UTF8.GetBytes(json)),
            sources.Select(source => new TranslationSource("translations/" + source.Locale + ".rmf2", Encoding.UTF8.GetBytes(source.Source))).ToArray());
        Assert.True(result.Success, string.Join("; ", result.Diagnostics.Select(item => item.Message)));
        return result.Project!;
    }

    [Test, NotInParallel, DisplayName("RMF2 v5 ESM accepts Date inputs, formats in the requested locale and reports fallbacks")]
    public void Executes()
    {
        Rmf2ProjectV5 project = Compile(("en", """
                amount = Amount {$n :number}
                farewell = Goodbye
                rich = {{{#link ref=doc}Total {$n :number}{/link}}}
                when = {$at :datetime style=short timeZone=utc}
                whenIso = {$at :datetime}
                whenLocal = {$at :datetime style=short}
                whenZone =
                  .input {$zone :string}
                  {{{$at :datetime style=short timeZone=$zone}}}
                """),
            ("de", """
                amount = Betrag {$n :number}
                rich = {{{#link ref=doc}Summe {$n :number}{/link}}}
                when = {$at :datetime style=short timeZone=utc}
                whenIso = {$at :datetime}
                whenLocal = {$at :datetime style=short}
                whenZone =
                  .input {$zone :string}
                  {{{$at :datetime style=short timeZone=$zone}}}
                """));
        string directory = Rmf2EsmV5Tests.Write(TranslationOutputRenderer.RenderRmf2V5EsmModules(project));
        try
        {
            string script = Path.Combine(directory, "formats.mjs");
            File.WriteAllText(script, """
                import { m } from "./formats.esm-v5/messages.js";
                import { configureLocaleResolver, configureTimeZoneResolver, createLocaleSource, getLocale, getMessageLocale, getRequestedLocale, onFallback } from "./formats.esm-v5/runtime.js";
                import { getRequestLocale, runWithLocale } from "./formats.esm-v5/server.js";
                import { createTextReference, decodeTextReference } from "./formats.esm-v5/transport.js";
                const check = (condition, message) => { if (!condition) throw new Error(message); };
                const throwsType = (operation, type, message) => { try { operation(); } catch (error) { check(error instanceof type, `${message}: ${error}`); return; } throw new Error(`${message}: accepted`); };
                const swiss = new Intl.NumberFormat("de-CH", { maximumFractionDigits: 6 }).format(1234567.5);
                // Numbers format in the requested locale when it shares the content language; the text stays German.
                check(m.amount({ n: "1234567.5" }, { locale: "de-CH" }) === `Betrag ${swiss}`, `de-CH: ${m.amount({ n: "1234567.5" }, { locale: "de-CH" })}`);
                check(m.amount({ n: "1234567.5" }, { locale: "de" }) === "Betrag 1.234.567,5", "de");
                const restore = configureLocaleResolver(() => "de-ch");
                try {
                  check(getLocale() === "de" && getRequestedLocale() === "de-CH", `resolver: ${getLocale()} ${getRequestedLocale()}`);
                  check(m.amount({ n: "1234567.5" }) === `Betrag ${swiss}`, "resolver de-CH");
                  const content = m.rich({ n: "1" });
                  check(content.locale === "de" && content.formatLocale === "de-CH", `content ${content.locale} ${content.formatLocale}`);
                  check(JSON.stringify(getMessageLocale("amount")) === '{"key":"amount","locale":"de","formatLocale":"de-CH"}', JSON.stringify(getMessageLocale("amount")));
                  check(JSON.stringify(getMessageLocale("farewell")) === '{"key":"farewell","locale":"en","formatLocale":"en"}', JSON.stringify(getMessageLocale("farewell")));
                } finally { restore(); }
                check(getMessageLocale("amount", { locale: "en-GB" }).formatLocale === "en-GB", "en-GB format locale");
                // An explicit script that is the likely one of a language written in one script is dropped, as in .NET.
                for (const [requested, expected] of [["en-Latn-US", "en-US"], ["en-Shaw-GB", "en"], ["de-Latn-CH", "de-CH"]])
                  check(getMessageLocale("amount", { locale: requested }).formatLocale === expected, `${requested}: ${getMessageLocale("amount", { locale: requested }).formatLocale}`);
                throwsType(() => getMessageLocale("missing"), RangeError, "unknown message");
                throwsType(() => getMessageLocale("amount", { locale: "en-" }), RangeError, "invalid tag");
                // A message that shows fallback text calls the listeners; a throwing listener does not break formatting.
                const events = [];
                const removeFailing = onFallback(() => { throw new Error("listener failure"); });
                const remove = onFallback((...values) => events.push(values.join(" ")));
                check(m.farewell({ locale: "de-AT" }) === "Goodbye", "fallback text");
                check(m.amount({ n: "1" }, { locale: "de" }) === "Betrag 1", "translated");
                remove(); removeFailing(); remove();
                check(m.farewell({ locale: "de" }) === "Goodbye", "after removal");
                check(events.join("|") === "farewell de en", `events: ${events.join("|")}`);
                throwsType(() => onFallback("no"), TypeError, "listener type");
                // Date inputs and ISO strings with fractions or offsets name the same instant; sub-second digits are dropped.
                const at = "2026-11-04T13:05:09Z", expected = m.when({ at }, { locale: "en" });
                check(expected === "11/4/2026 1:05 PM", `utc short: ${expected}`);
                for (const value of [new Date("2026-11-04T13:05:09.987Z"), "2026-11-04T13:05:09.5Z", "2026-11-04T14:05:09.123456789+01:00", "2026-11-04T08:05:09-05:00"])
                  check(m.when({ at: value }, { locale: "en" }) === expected, `input ${value}`);
                check(m.whenIso({ at: new Date("2026-11-04T13:05:09.987Z") }, { locale: "en" }) === at, "iso truncates");
                for (const value of [new Date(Number.NaN), "2026-11-04T13:05:09+15:00", "2026-11-04T13:05:09+01:60", "2026-02-30T00:00:00Z", "2026-11-04T13:05:09.Z", "2026-11-04 13:05:09Z", "0000-01-01T00:00:00Z", Date.parse(at)])
                  throwsType(() => m.when({ at: value }, { locale: "en" }), TypeError, `input ${String(value)}`);
                // A reference writes a Date in the canonical UTC wire form; the wire never takes offsets or fractions.
                const reference = createTextReference("when", { at: new Date("2026-11-04T13:05:09.987Z") });
                check(JSON.stringify(reference).includes('"value":"2026-11-04T13:05:09Z"'), JSON.stringify(reference));
                throwsType(() => createTextReference("when", { at: "2026-11-04T14:05:09+01:00" }), TypeError, "offset wire");
                check(!decodeTextReference(JSON.parse(JSON.stringify(reference).replace("13:05:09Z", "14:05:09+01:00"))).ok, "offset wire decode");
                // The localized styles use the host's zone unless timeZone names one; an unknown zone is a RangeError.
                const zone = new Intl.DateTimeFormat().resolvedOptions().timeZone;
                check(m.whenLocal({ at }, { locale: "en" }) === m.whenZone({ at, zone }, { locale: "en" }), `local zone ${zone}`);
                check(m.whenZone({ at, zone: "Asia/Tokyo" }, { locale: "en" }) === "11/4/2026 10:05 PM", "Tokyo");
                throwsType(() => m.whenZone({ at, zone: "Mars/Olympus_Mons" }, { locale: "en" }), RangeError, "unknown zone");
                try { m.whenZone({ at, zone: "Mars/Olympus_Mons" }, { locale: "en" }); } catch (error) { check(error.message.startsWith("The time zone 'Mars/Olympus_Mons' is not available."), error.message); }
                throwsType(() => m.whenZone({ at, zone: "UTC+1 hour" }, { locale: "en" }), RangeError, "zone syntax");
                // "local" follows a changed host zone at once (TZ), and the time-zone resolver when it names a known zone.
                const savedZone = process.env.TZ;
                try {
                  process.env.TZ = "Asia/Tokyo"; check(m.whenLocal({ at }, { locale: "en" }) === "11/4/2026 10:05 PM", `TZ Tokyo: ${m.whenLocal({ at }, { locale: "en" })}`);
                  process.env.TZ = "America/New_York"; check(m.whenLocal({ at }, { locale: "en" }) === "11/4/2026 8:05 AM", `TZ New York: ${m.whenLocal({ at }, { locale: "en" })}`);
                  const removeZone = configureTimeZoneResolver(() => "Asia/Kolkata");
                  check(m.whenLocal({ at }, { locale: "en" }) === "11/4/2026 6:35 PM", "resolver zone");
                  const removeUnknown = configureTimeZoneResolver(() => "Mars/Olympus_Mons");
                  check(m.whenLocal({ at }, { locale: "en" }) === "11/4/2026 8:05 AM", "an unknown resolver zone uses the host's");
                  removeUnknown(); removeZone();
                  check(m.whenLocal({ at }, { locale: "en" }) === "11/4/2026 8:05 AM", "resolver removed");
                  // The server entry scopes the requested tag and the zone to a request.
                  const rendered = runWithLocale("de", () => [getRequestLocale(), getLocale(), getRequestedLocale(), m.amount({ n: "1234567.5" }), m.whenLocal({ at })], { requestedLocale: "de-CH", timeZone: "Asia/Tokyo" });
                  check(JSON.stringify(rendered) === JSON.stringify(["de", "de", "de-CH", `Betrag ${swiss}`, "04.11.2026 22:05"]), `request ${JSON.stringify(rendered)}`);
                  check(runWithLocale("de", () => getRequestedLocale(), { requestedLocale: "fr-CH" }) === "de", "a requested tag of another locale is ignored");
                  check(runWithLocale("de", () => m.whenLocal({ at }), { timeZone: "Mars/Olympus_Mons" }) === "04.11.2026 08:05", "an unknown request zone uses the host's");
                } finally { if (savedZone === undefined) delete process.env.TZ; else process.env.TZ = savedZone; }
                // Extensions of the requested tag do not reach formatting.
                check(m.when({ at }, { locale: "en-US-u-ca-buddhist" }) === "11/4/2026 1:05 PM", `buddhist: ${m.when({ at }, { locale: "en-US-u-ca-buddhist" })}`);
                check(getMessageLocale("amount", { locale: "de-CH-u-nu-arab" }).formatLocale === "de-CH", "extension dropped");
                // A locale source keeps the requested tag beside the catalog locale.
                const source = createLocaleSource({ initialLocale: "de-ch" }), seen = [];
                source.subscribe(locale => seen.push(`${locale}/${source.getRequestedLocale()}`));
                check(source.getLocale() === "de" && source.getRequestedLocale() === "de-CH", "source initial");
                source.setLocale("de"); check(source.getRequestedLocale() === "de-CH" && seen.length === 0, "setting the current catalog locale keeps the region");
                source.setLocale("de-AT"); source.setLocale("en"); source.setLocale("en-GB"); source.setLocale("en-GB");
                check(seen.join(" ") === "de/de-AT en/en en/en-GB", `source events: ${seen.join(" ")}`);
                """, new UTF8Encoding(false));
            foreach (string host in Rmf2DatePercentConformanceTests.EsmHosts()) Rmf2EsmV5Tests.Run(host, [script], directory);

            string typecheck = Path.Combine(directory, "formats-types.ts");
            File.WriteAllText(typecheck, """
                import { m } from "./formats.esm-v5/messages.js";
                import { configureTimeZoneResolver, createLocaleSource, getMessageLocale, getRequestedLocale, onFallback, type Locale, type MessageLocale } from "./formats.esm-v5/runtime.js";
                import { runWithLocale, type RequestLocaleOptions } from "./formats.esm-v5/server.js";
                const removeZone: () => void = configureTimeZoneResolver(() => undefined);
                const source = createLocaleSource({ initialLocale: "de-CH" }); const sourceRequested: string = source.getRequestedLocale(); source.setLocale("de");
                // @ts-expect-error setLocale takes a catalog locale.
                source.setLocale("fr");
                const requestOptions: RequestLocaleOptions = { requestedLocale: "de-CH", timeZone: "Europe/Zurich" };
                const scoped: string = runWithLocale("de", () => "", requestOptions);
                void removeZone; void sourceRequested; void scoped;
                const fromDate: string = m.when({ at: new Date() });
                const fromText: string = m.when({ at: "2026-11-04T13:05:09.123+01:00" });
                const located: MessageLocale = getMessageLocale("farewell", { locale: "de-CH" });
                const lang: Locale = located.locale;
                const formatLocale: string = located.formatLocale;
                const requested: string = getRequestedLocale();
                const remove: () => void = onFallback((key: string, wanted: Locale, content: Locale) => { void key; void wanted; void content; });
                const content = m.rich({ n: 1 }); const contentFormat: string = content.formatLocale;
                // @ts-expect-error a number is not a date-time input.
                m.when({ at: 0 });
                void fromDate; void fromText; void lang; void formatLocale; void requested; void remove; void contentFormat;
                """, new UTF8Encoding(false));
            Rmf2EsmV5Tests.Run("bun", [RepositoryPaths.Resolve("node_modules", "typescript", "bin", "tsc"), "--strict", "--noEmit", "--target", "ES2022", "--module", "NodeNext", "--moduleResolution", "NodeNext", "--lib", "ES2022,DOM", "--skipLibCheck", typecheck], directory);
        }
        finally { Directory.Delete(directory, true); }
    }
}
