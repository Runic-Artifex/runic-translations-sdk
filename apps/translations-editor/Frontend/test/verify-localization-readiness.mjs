import assert from "node:assert/strict";
import { readFile, readdir } from "node:fs/promises";
import { pathToFileURL } from "node:url";
import { resolve, dirname } from "node:path";
import ts from "typescript";
import { readUiMessages } from "./ui-messages.mjs";
import {
  localizationStressAttributes,
  localizationStressCases,
  pluralStressCounts,
  visualAccessibilityStressScenarios,
} from "@runic-artifex/svelte/translations/testing";

const page = await readFile(new URL("../src/routes/+page.svelte", import.meta.url), "utf8");
const editor = await readFile(new URL("../src/lib/TranslationEditor.svelte", import.meta.url), "utf8");
const inline = await readFile(new URL("../src/lib/InlineMessageEditor.svelte", import.meta.url), "utf8");
const styles = await readFile(new URL("../src/routes/layout.css", import.meta.url), "utf8");

for (const stressCase of localizationStressCases) {
  const attributes = localizationStressAttributes(stressCase);
  assert.equal(attributes.lang, stressCase.locale, `${stressCase.id} lost its document language.`);
  assert.equal(attributes.dir, stressCase.direction, `${stressCase.id} lost its text direction.`);
}
assert.deepEqual(pluralStressCounts, [0, 1, 2, 5, 11, 21, 101, 1000],
  "The shared plural-extreme fixture changed unexpectedly.");
for (const scenario of visualAccessibilityStressScenarios) {
  assert.ok(styles.includes(scenario.mediaQuery), `The editor stylesheet does not mount the shared ${scenario.id} scenario.`);
}
assert.match(page, /document\.documentElement\.lang = uiLocale/, "UI locale does not update the document language.");
assert.match(page, /document\.documentElement\.dir = uiDirection/, "UI direction does not update the document direction.");
assert.match(page, /lang=\{selectedLocale\}/, "The editing region does not expose its selected locale.");
assert.match(editor, /lang=\{locale\}/, "The translation editor does not expose its locale.");
assert.match(inline, /spellcheck="true"/, "Natural-language translation input must keep spellcheck enabled.");
assert.match(inline, /dir=\{localeDirection\(locale\)\}/, "Translation input does not expose locale direction.");

console.log(`PASS: mounted shared localization readiness fixtures (${localizationStressCases.length} text cases, ${visualAccessibilityStressScenarios.length} visual scenarios, ${pluralStressCounts.length} plural extremes) against editor language, direction, spellcheck, forced-colors, contrast, and reduced-motion hooks.`);

// Exercise real generated MF2 functions through the same locale-reactive UI adapter.
const resourceRoot = new URL("../../EditorResources/", import.meta.url);
const config = JSON.parse(await readFile(new URL("runic.json", resourceRoot), "utf8"));
assert.equal(config.sourceLayout, "locale-toml");
assert.deepEqual((await readdir(resourceRoot)).filter((name) => name.endsWith(".toml")).sort(), ["de.toml", "en.toml"]);
const manifestPath = process.env.RUNIC_TRANSLATIONS_MANIFEST;
assert.ok(manifestPath, "Generate the editor ESM module and provide RUNIC_TRANSLATIONS_MANIFEST before plural acceptance.");
const manifest = JSON.parse(await readFile(manifestPath, "utf8"));
const messageUrl = pathToFileURL(resolve(dirname(manifestPath), manifest.entrypoints.messages)).href;
const adapterSource = (await readFile(new URL("../src/lib/ui-text.ts", import.meta.url), "utf8"))
  .replace('import { createContext } from "svelte";', 'const createContext = () => [() => undefined, () => undefined];')
  .replace('"virtual:runic-translations/editor"', JSON.stringify(messageUrl));
const adapterJs = ts.transpileModule(adapterSource, { compilerOptions: { module: ts.ModuleKind.ESNext, target: ts.ScriptTarget.ES2022 } }).outputText;
const { createUiText } = await import(`data:text/javascript;base64,${Buffer.from(adapterJs).toString("base64")}`);
let locale = "en";
const ui = createUiText(() => locale);
for (locale of ["en", "de"]) {
  const messages = await readUiMessages(locale);
  for (const count of pluralStressCounts) {
    const formatted = String(count); // :integer uses the compiler's ungrouped integer format.
    assert.equal(ui.text("ui_count_affected_files", { count }), locale === "en"
      ? count === 1 ? "One affected file" : `${formatted} affected files`
      : count === 1 ? "Eine betroffene Datei" : `${formatted} betroffene Dateien`);
    assert.equal(ui.text("ui_count_messages", { count }), locale === "en"
      ? count === 1 ? "one message" : `${formatted} messages`
      : count === 1 ? "eine Nachricht" : `${formatted} Nachrichten`);
    for (const [key, source] of Object.entries(messages).filter(([key]) => key.startsWith("ui_count_"))) {
      const argumentsValue = Object.fromEntries([...source.matchAll(/\.input \{\$(\w+) :(integer|string)/g)]
        .map(([, name, type]) => [name, type === "integer" ? count : name === "state" ? ui.text("ui_review_state_draft") : "Runic"]));
      const value = ui.text(key, argumentsValue);
      assert.ok(value.length > 0 && !value.includes("[[") && !value.includes("undefined") && !value.includes("{$"), `${locale}/${key}/${count}: unresolved message`);
    }
  }
  assert.equal(ui.text("ui_a11y_toggle_sidebar"), locale === "en" ? "Toggle sidebar" : "Seitenleiste umschalten");
  assert.equal(ui.text("ui_loading"), locale === "en" ? "Loading" : "Wird geladen");
}
assert.doesNotMatch(page, /length === 1\s*\? ui\.text/, "UI count grammar must be chosen by MF2, not JavaScript.");
console.log("PASS: real editor TOML messages format all plural stress counts in English and German through the reactive UI adapter.");

const { notice, displayNotice } = await import(`data:text/javascript;base64,${Buffer.from(adapterJs).toString("base64")}`);
const stored = notice("ui_count_review_marked", { count: 2, state: "approved" });
locale = "en";
assert.match(displayNotice(stored, ui), /2 visible messages marked approved/);
locale = "de";
assert.match(displayNotice(stored, ui), /2 sichtbare Nachrichten.*freigegeben/);
const backendNotice = { code: "ui_backend_document_conflict", args: [{ name: "path", value: "de.toml" }] };
assert.match(displayNotice(backendNotice, ui), /de\.toml.*Datenträger/);
locale = "en";
assert.match(displayNotice(backendNotice, ui), /de\.toml.*changed on disk/);
const external = { code: "ui_backend_external_error", args: [], detail: "IO-E42" };
assert.equal(displayNotice(external, ui), "The operation could not be completed. Technical details: IO-E42");
locale = "de";
assert.equal(displayNotice(external, ui), "Der Vorgang konnte nicht abgeschlossen werden. Technische Details: IO-E42");
assert.doesNotMatch(page, /(?:operationMessage|reviewMessage|diagnosticMessage|projectError|clientError)\s*=\s*ui\.text\(/,
  "Persistent notices must retain message identity until render.");
