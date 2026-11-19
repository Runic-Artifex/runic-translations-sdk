// The NativeAOT size probe's catalog (tests/aot-size/README.md): `messages` messages in each of `locales`, written as
// translations/<locale>.rmf2 and runic.json, and a Program.cs that calls every message's accessor, so none is trimmed.
// The messages repeat a pattern of 20: 14 plain texts, 3 with a string input, 2 plurals and 1 number, in sections of 50.
// A plural has a variant for each CLDR category an integer reaches in its locale, as a translator writes it.
import { mkdirSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import { resolve } from "node:path";
import { fileURLToPath } from "node:url";

const texts = {
  en: "Open the selected document in a new window", de: "Das ausgewählte Dokument in einem neuen Fenster öffnen",
  fr: "Ouvrir le document sélectionné dans une nouvelle fenêtre", pl: "Otwórz wybrany dokument w nowym oknie",
  ar: "افتح المستند المحدد في نافذة جديدة", ja: "選択したドキュメントを新しいウィンドウで開く",
  ru: "Открыть выбранный документ в новом окне", es: "Abrir el documento seleccionado en una ventana nueva",
  it: "Apri il documento selezionato in una nuova finestra", pt: "Abrir o documento selecionado em uma nova janela",
};
export const allLocales = Object.keys(texts);

// The cardinal plural categories an integer reaches in a locale, from the pinned CLDR data the compiler uses
// (specs/translations/cldr): those whose rule has @integer samples, in CLDR's order, without "other", which is "*".
// For :integer the compiler asks for exactly these (RTR0095).
const cldr = JSON.parse(readFileSync(resolve(fileURLToPath(import.meta.url), "../../../specs/translations/cldr/runic-subset-48.2.json"), "utf8"));
const categoryOrder = ["zero", "one", "two", "few", "many"];
export function integerPluralCategories(locale) {
  const rules = cldr.plurals.cardinal[locale];
  if (!rules) throw new Error(`The pinned CLDR data has no cardinal plural rules for ${locale}.`);
  return categoryOrder.filter(category => rules[category]?.includes("@integer"));
}

// The message kind of index i, and the accessor call that formats it.
function kind(index) {
  const position = index % 20;
  return position < 14 ? "text" : position < 17 ? "input" : position < 19 ? "plural" : "number";
}

function source(locale, messages) {
  const text = texts[locale];
  const lines = [];
  for (let section = 0; section < messages; section += 50) {
    lines.push(`s${section / 50} {`);
    for (let index = section; index < Math.min(messages, section + 50); index++) {
      switch (kind(index)) {
        case "text": lines.push(`  m${index} = ${text} ${index}`); break;
        case "input": lines.push(`  m${index} =\n    .input {$name :string}\n    {{${text} {$name} ${index}}}`); break;
        case "plural": {
          const variants = integerPluralCategories(locale).map(category => `    ${category} {{{$count} ${text} ${index} (${category})}}\n`).join("");
          lines.push(`  m${index} =\n    .input {$count :integer}\n    .match $count\n${variants}    * {{{$count} ${text} ${index}}}`);
          break;
        }
        default: lines.push(`  m${index} = ${text}: {$amount :number} ${index}`);
      }
    }
    lines.push("}");
  }
  return `${lines.join("\n")}\n`;
}

function call(index) {
  const name = `text.Messages.s${Math.floor(index / 50)}_m${index}`;
  switch (kind(index)) {
    case "text": return `Console.WriteLine(${name});`;
    case "input": return `Console.WriteLine(${name}(name: "x"));`;
    case "plural": return `Console.WriteLine(${name}(count: 3));`;
    default: return `Console.WriteLine(${name}(amount: 3));`;
  }
}

// Writes the catalog and Program.cs to `directory`, replacing what is there.
export function writeFixture(directory, messages, locales) {
  for (const locale of locales) if (!texts[locale]) throw new Error(`The probe has no text for ${locale}.`);
  rmSync(directory, { recursive: true, force: true });
  mkdirSync(resolve(directory, "translations"), { recursive: true });
  for (const locale of locales) writeFileSync(resolve(directory, "translations", `${locale}.rmf2`), source(locale, messages));
  writeFileSync(resolve(directory, "translations", "runic.json"), `${JSON.stringify({
    $schema: "https://runic-artifex.eu/schemas/translations/project-v1.schema.json", schemaVersion: 1, catalog: "aot-probe",
    code: { namespace: "AotSizeProbe.Translations", className: "AppText", visibility: "internal" },
    baseLocale: locales[0], locales,
  }, null, 2)}\n`);
  // Each message is formatted from its own method of Calls, which is never inlined, so the probe's own code is one small
  // method per message whatever ILC decides to inline. measure.mjs leaves Main and Calls.* out of the catalog's records:
  // they are the application's code, not the catalog's. In one Main of 3,000 lines, ILC's inlining budget decided how
  // much of each accessor counted as the catalog (D077.16).
  const methods = Array.from({ length: messages }, (_, index) =>
    `    [MethodImpl(MethodImplOptions.NoInlining)] internal static void M${index}(AppText text) => ${call(index)}`);
  writeFileSync(resolve(directory, "Program.cs"), [
    "using System.Runtime.CompilerServices;", "using AotSizeProbe.Translations;", "",
    // Registered in a translation context, as Runic applications register their catalogs (runic-sdk#1109).
    "var context = await Runic.Translations.TranslationContext.CreateBuilder().AddCatalog(AppTextCatalog.Registration)",
    `    .BuildAsync(Environment.GetEnvironmentVariable("PROBE_LOCALE") ?? "${locales[0]}");`,
    "var text = new AppText(context);",
    ...Array.from({ length: messages }, (_, index) => `Calls.M${index}(text);`),
    "return 0;", "",
    "static class Calls", "{", ...methods, "}", "",
  ].join("\n"));
}
