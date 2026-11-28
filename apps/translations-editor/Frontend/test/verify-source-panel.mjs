// The translate view's read-only source panel (#225 P1), rendered by Svelte's server renderer: the base locale's text
// of the selected message above the target editor, with placeholder chips, one block per variant, the base locale's
// language and direction, and a heading; never for the base locale itself.
import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import { compile } from "svelte/compiler";
import { html, text } from "./svelte-server-components.mjs";

const { default: SourceTextPanel } = await import("../src/lib/SourceTextPanel.svelte");
const { default: TranslationEditor } = await import("../src/lib/TranslationEditor.svelte");

// A simple message: one paragraph in the base locale, variables as chips.
const simple = html(SourceTextPanel, {
  locale: "en", localeName: "English",
  entry: { value: "Sort by {$column}", authoring: { supported: true, selectors: [], variants: [{ id: "0", keys: [], pattern: "Sort by {$column}" }] } },
});
assert.match(simple, /<section[^>]*aria-labelledby="([^"]+)"[^>]*data-testid="source-text"/, "The panel is a labelled region.");
const headingId = /aria-labelledby="([^"]+)"/.exec(simple)[1];
assert.match(simple, new RegExp(`<h3[^>]*id="${headingId}"[^>]*>\\[ui_source_panel_title language=English\\]</h3>`),
  "The region is labelled by a heading naming the base locale.");
assert.match(simple, /<p[^>]*lang="en"[^>]*dir="ltr"[^>]*>Sort by <span[^>]*data-placeholder="variable"[^>]*>\{\$column\}<\/span><\/p>/,
  "The base text carries the base locale's language and direction, with the variable as a chip.");
assert.doesNotMatch(simple, /<textarea|contenteditable|<input/, "The source text is read-only.");

// A plural message: one block per variant, titled with the variant keys as the composer titles them.
const plural = html(SourceTextPanel, {
  locale: "en", localeName: "English",
  entry: { value: "", authoring: { supported: true, selectors: ["count"], variants: [
    { id: "a", keys: ["one"], pattern: "One file selected" },
    { id: "b", keys: ["*"], pattern: "{$count} files selected" },
  ] } },
});
const blocks = [...plural.matchAll(/<dt[^>]*>([^<]*)<\/dt>\s*<dd([^>]*)>(.*?)<\/dd>/g)].map(([, title, attributes, body]) => ({ title, attributes, body: text(body) }));
assert.deepEqual(blocks.map(({ title, body }) => [title, body]), [
  ["count: one", "One file selected"],
  ["count: [ui_composer_fallback]", "{$count} files selected"],
]);
assert.ok(blocks.every(({ attributes }) => /lang="en"/.test(attributes) && /dir="ltr"/.test(attributes)));

// A right-to-left base locale keeps its direction; a message the base locale lacks says so.
assert.match(html(SourceTextPanel, { locale: "ar", localeName: "Arabic", entry: { value: "مرحبا" } }), /lang="ar"[^>]*dir="rtl"[^>]*>مرحبا</);
assert.match(html(SourceTextPanel, { locale: "en", localeName: "English", entry: undefined }), /\[ui_source_panel_missing\]/);

// Placed above the target editor for another locale, in the translation view only, and never for the base locale.
const editorProps = (locale, mode = "translation") => ({
  identity: `doc\u0000common_save\u0000${locale}`, mode, locale, label: locale, value: "Speichern", resourceValue: "Speichern",
  sourceLocale: "en", sourceLocaleName: "English", sourceEntry: { key: "common_save", value: "Save", tags: [], structured: false },
  missing: false, invalid: false, disabled: false, onresourcechange() {}, onrawchange() {}, onformatraw() {},
});
const german = html(TranslationEditor, editorProps("de"));
assert.ok(german.indexOf('data-testid="source-text"') >= 0, "The German translate view shows the English source text.");
assert.ok(german.indexOf('data-testid="source-text"') < german.indexOf('lang="de"'), "The source panel is above the target editor.");
assert.match(text(german), /\[ui_source_panel_title language=English\].*Save/);
assert.doesNotMatch(html(TranslationEditor, editorProps("en")), /data-testid="source-text"/, "The base locale has no source panel.");
assert.doesNotMatch(html(TranslationEditor, editorProps("de", "raw")), /data-testid="source-text"/, "The file view shows the file only.");

// The webview's checker never reads MF2 source (P11): it is off everywhere, and the editor's own checker (D074.10) reads
// only the words of the text, the source boxes' as a whole message or a pattern, the visual segments' in the target
// language.
const [composer, inline, patternEditor, spelled] = await Promise.all(["MessageComposer", "InlineMessageEditor", "PatternEditor", "SpelledTextarea"]
  .map((name) => readFile(new URL(`../src/lib/${name}.svelte`, import.meta.url), "utf8")));
assert.match(composer, /<SpelledTextarea value=\{source\}[^>]*kind="message"/, "The composer's MF2 source box is not checked as a message.");
assert.match(patternEditor, /<SpelledTextarea value=\{visiblePattern\}[^>]*kind="pattern"/, "A variant's MF2 source box is not checked as a pattern.");
assert.match(spelled, /spellcheck=\{false\}/, "The webview spell-checks the MF2 source boxes.");
assert.match(inline, /spellcheck="false"\s+lang=\{locale\}/, "The webview spell-checks the visual text segments.");
// The pattern box reads as one text field (P22): a text cursor, and a click on its empty area focuses the last segment.
assert.match(inline, /class="[^"]*cursor-text[^"]*"\s+role="group"[\s\S]*?onmousedown=\{focusEnd\}/);
assert.match(inline, /function focusEnd[\s\S]*?event\.target !== event\.currentTarget[\s\S]*?editingSlot = last;[\s\S]*?focusSlot\(last, slots\[last\]\.text\.length\)/);
for (const [name, source] of [["SourceTextPanel.svelte", await readFile(new URL("../src/lib/SourceTextPanel.svelte", import.meta.url), "utf8")], ["InlineMessageEditor.svelte", inline]]) {
  const warnings = compile(source, { generate: "server" }).warnings.filter((warning) => warning.code.startsWith("a11y_"));
  assert.deepEqual(warnings, [], `${name} has compiler-reported accessibility violations.`);
}
console.log("PASS: the source panel renders the base text with chips per variant, in the base locale's language and direction, above the editor of every other locale.");
