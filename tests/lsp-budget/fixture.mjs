// The large catalog of the language server budgets (tests/lsp-budget/README.md): `features` feature folders, each a
// group with one <locale>.rmf2 per locale of `messagesPerFeature` messages, so `features × messagesPerFeature` messages
// in every locale. The messages follow the NativeAOT probe's pattern of 20 (tests/aot-size/fixture.mjs): 14 plain
// texts, 3 with a string input, 2 plurals with the locale's integer categories and 1 number, in sections of 50.
import { mkdirSync, rmSync, writeFileSync } from "node:fs";
import { resolve } from "node:path";
import { allLocales, integerPluralCategories } from "../aot-size/fixture.mjs";

const texts = {
  en: "Open the selected document in a new window", de: "Das ausgewählte Dokument in einem neuen Fenster öffnen",
  fr: "Ouvrir le document sélectionné dans une nouvelle fenêtre", pl: "Otwórz wybrany dokument w nowym oknie",
  ar: "افتح المستند المحدد في نافذة جديدة", ja: "選択したドキュメントを新しいウィンドウで開く",
  ru: "Открыть выбранный документ в новом окне", es: "Abrir el documento seleccionado en una ventana nueva",
  it: "Apri il documento selezionato in una nuova finestra", pt: "Abrir o documento selecionado em uma nova janela",
};

function kind(index) {
  const position = index % 20;
  return position < 14 ? "text" : position < 17 ? "input" : position < 19 ? "plural" : "number";
}

// One feature's resource in one locale.
export function featureSource(locale, messages) {
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

export const featureName = index => `feature${String(index).padStart(2, "0")}`;

// Writes runic.json and the features to `directory`, replacing what is there, and returns the file count. With `review`,
// also a review file with stale German entries (RTR0090), so every validation compiles for review.
export function writeCatalog(directory, { features, messagesPerFeature, locales }, { review = false } = {}) {
  const localeList = allLocales.slice(0, locales);
  rmSync(directory, { recursive: true, force: true });
  mkdirSync(directory, { recursive: true });
  writeFileSync(resolve(directory, "runic.json"), `${JSON.stringify({
    schemaVersion: 1, catalog: "budget", code: { namespace: "Budget.Translations", className: "AppText" },
    baseLocale: localeList[0], locales: localeList,
  }, null, 2)}\n`);
  if (review && localeList.includes("de"))
    writeFileSync(resolve(directory, "budget.review.json"), `${JSON.stringify({ $schema: "runic.translations.interchange-review/2", catalog: "budget",
      entries: Array.from({ length: 10 }, (_, index) => ({ key: `feature01_s0_m${index}`, locale: "de", state: "approved",
        sourceFingerprint: "sha256:" + "0".repeat(64) })) }, null, 2)}\n`);
  for (let feature = 0; feature < features; feature++) {
    const folder = resolve(directory, featureName(feature));
    mkdirSync(folder);
    for (const locale of localeList) writeFileSync(resolve(folder, `${locale}.rmf2`), featureSource(locale, messagesPerFeature));
  }
  return features * localeList.length;
}
