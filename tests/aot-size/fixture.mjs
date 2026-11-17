// The NativeAOT size probe's catalog (tests/aot-size/README.md): `messages` messages in each of `locales`, written as
// translations/<locale>.rmf2 and runic.json, and a Program.cs that calls every message's accessor, so none is trimmed.
// The messages repeat a pattern of 20: 14 plain texts, 3 with a string input, 2 plurals and 1 number, in sections of 50.
import { mkdirSync, rmSync, writeFileSync } from "node:fs";
import { resolve } from "node:path";

const texts = {
  en: "Open the selected document in a new window", de: "Das ausgewählte Dokument in einem neuen Fenster öffnen",
  fr: "Ouvrir le document sélectionné dans une nouvelle fenêtre", pl: "Otwórz wybrany dokument w nowym oknie",
  ar: "افتح المستند المحدد في نافذة جديدة", ja: "選択したドキュメントを新しいウィンドウで開く",
  ru: "Открыть выбранный документ в новом окне", es: "Abrir el documento seleccionado en una ventana nueva",
  it: "Apri il documento selezionato in una nuova finestra", pt: "Abrir o documento selecionado em uma nova janela",
};
export const allLocales = Object.keys(texts);

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
          const one = locale === "ja" ? "" : `    one {{{$count} ${text} ${index}}}\n`;
          lines.push(`  m${index} =\n    .input {$count :integer}\n    .match $count\n${one}    * {{{$count} ${text} ${index}}}`);
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
  const calls = Array.from({ length: messages }, (_, index) => call(index));
  writeFileSync(resolve(directory, "Program.cs"), [
    "using AotSizeProbe.Translations;", "",
    `var manager = await AppTextCatalog.CreateManagerAsync(Environment.GetEnvironmentVariable("PROBE_LOCALE") ?? "${locales[0]}");`,
    "var text = new AppText(manager);",
    ...calls,
    "return 0;", "",
  ].join("\n"));
}
