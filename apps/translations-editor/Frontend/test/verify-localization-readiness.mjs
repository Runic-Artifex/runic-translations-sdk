import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
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
