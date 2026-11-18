// The length limit at the field (#225 P21): with `@maxLength` the character counter shows the text against the limit
// ("16 / 24"), measured as RTR0098 measures it, the composer shows each variant's length, and a text over the limit is
// marked and explained next to the field. Without a limit the counter stays as it was. Also: the workflow badge of a
// translation that does not exist yet, and the composer's "Copy source" action of a starter (#225 P2).
import assert from "node:assert/strict";
import { html, text } from "./svelte-server-components.mjs";
import { lengthLimit, messageLength, patternLength } from "../src/lib/message-starter.ts";

const { default: TranslationEditor } = await import("../src/lib/TranslationEditor.svelte");
const { default: ReviewWorkflow } = await import("../src/lib/ReviewWorkflow.svelte");

// What RTR0098 counts: literal text only (escapes resolved), no placeholders, formatted values or markup; NFC code points.
assert.equal(patternLength("Sort by {$column}"), 8);
assert.equal(patternLength("{$count} files selected"), 15);
assert.equal(patternLength("A \\{brace\\}"), 9, "An escaped brace is one character of text.");
assert.equal(patternLength("{|literal|} {$n :integer} {#b}bold{/b}"), 6, "Literal expressions, formatted values and markup are not text.");
assert.equal(patternLength("é"), 1, "Text is counted in NFC.");
assert.equal(patternLength("🙂!"), 2, "A surrogate pair is one character.");
assert.equal(patternLength("{$unterminated"), 14, "An unterminated brace stays text.");
assert.equal(messageLength({ supported: true, variants: [{ pattern: "One file" }, { pattern: "{$count} files selected" }] }, ""), 15,
  "A message is as long as its longest variant.");
assert.equal(messageLength(undefined, "{{Sixteen chars!!!}}"), 16, "Without a projection a quoted pattern is counted without its braces.");
assert.equal(lengthLimit(16, undefined), undefined);
assert.deepEqual(lengthLimit(16, 24), { length: 16, limit: 24, over: false });
assert.deepEqual(lengthLimit(25, 24), { length: 25, limit: 24, over: true });

const authoring = (patterns) => ({
  revision: "r", supported: true, inputs: [{ name: "count", type: "int64", function: "integer", declared: true }],
  selectors: patterns.length > 1 ? ["count"] : [], selectorFunctions: patterns.length > 1 ? ["plural"] : [],
  variants: patterns.map(([keys, pattern], index) => ({ id: String(index), keys, pattern, startByte: 0, lengthBytes: 0 })),
  pluralCategories: ["one", "other"], selectorPluralCategories: patterns.length > 1 ? [["one", "other"]] : [], cldrVersion: "48", empty: false,
});
const props = (extra) => ({
  identity: "doc\u0000common_save\u0000de", mode: "translation", locale: "de", label: "German",
  value: "Änderungen sichern", resourceValue: "Änderungen sichern", authoring: authoring([[[], "Änderungen sichern"]]),
  sourceLocale: "en", sourceLocaleName: "English", sourceEntry: { key: "common_save", value: "Save changes", tags: [], structured: false },
  missing: false, invalid: false, disabled: false, onresourcechange() {}, onrawchange() {}, onformatraw() {}, onoperation() {},
  ...extra,
});
const counter = (markup) => /<span[^>]*data-testid="character-count"[^>]*>([^<]*)<\/span>/.exec(markup);

// Without a limit: "18 characters", as before.
const plain = html(TranslationEditor, props());
assert.equal(counter(plain)[1].trim(), "[ui_count_characters count=18]");
assert.doesNotMatch(plain, /data-testid="length-limit"|data-testid="variant-character-count"/);

// With a limit: "18 / 24", not marked, no warning.
const within = html(TranslationEditor, props({ maxLength: 24 }));
assert.equal(counter(within)[1].trim(), "[ui_count_characters_limit count=18 limit=24]");
assert.match(counter(within)[0], /data-over-limit="false"/);
assert.match(counter(within)[0], /title="\[ui_count_characters_limit_title count=18 limit=24\]"/);
assert.doesNotMatch(within, /data-testid="length-limit"/);

// Over the limit: marked as a warning, with the compiler's RTR0098 text next to the field, or the editor's own text
// before the compiler has reported it.
const rtr0098 = "The 'de' text of 'common.save' is 18 characters long, more than its @maxLength of 12; placeholder values are not counted.";
const over = html(TranslationEditor, props({ maxLength: 12, lengthWarning: rtr0098 }));
assert.match(counter(over)[0], /data-over-limit="true"/);
assert.match(counter(over)[0], /text-warning/);
assert.equal(counter(over)[1].trim(), "[ui_count_characters_limit count=18 limit=12]");
const warning = /<p[^>]*data-testid="length-limit"[^>]*>([^<]*)<\/p>/.exec(over);
assert.equal(warning?.[1].trim(), rtr0098);
assert.match(warning[0], /role="status"/);
assert.ok(over.indexOf('data-testid="length-limit"') < over.indexOf("[ui_composer_translate_message]"), "The warning is at the field, above the composer.");
assert.match(text(html(TranslationEditor, props({ maxLength: 12 }))), /\[ui_translation_editor_over_limit limit=12\]/);

// A plural: the counter measures the longest variant; each variant shows its own length against the limit.
const pluralProps = props({
  value: ".input {$count :integer}\n.match $count\none {{Eine Datei}}\n* {{{$count} Dateien wurden gelöscht}}", resourceValue: undefined,
  authoring: authoring([[["one"], "Eine Datei"], [["*"], "{$count} Dateien wurden gelöscht"]]), maxLength: 24,
});
pluralProps.resourceValue = pluralProps.value;
const plural = html(TranslationEditor, pluralProps);
assert.equal(counter(plural)[1].trim(), "[ui_count_characters_limit count=24 limit=24]");
const variants = [...plural.matchAll(/<span[^>]*data-testid="variant-character-count"[^>]*data-over-limit="(true|false)"[^>]*>([^<]*)<\/span>/g)]
  .map(([, isOver, label]) => [label.trim(), isOver]);
assert.deepEqual(variants, [["[ui_count_characters_limit count=10 limit=24]", "false"], ["[ui_count_characters_limit count=24 limit=24]", "false"]]);
assert.doesNotMatch(html(TranslationEditor, { ...pluralProps, maxLength: undefined }), /variant-character-count/, "Without a limit no variant shows a length.");

// The file view counts the file, without the limit.
assert.equal(counter(html(TranslationEditor, props({ mode: "raw", maxLength: 3, value: "common {\n}\n" })))[1].trim(), "[ui_count_characters count=11]");

// A starter being prepared shows that instead of the composer; a starter offers "Copy source", a translation does not.
const loading = html(TranslationEditor, props({ starterLoading: true, missing: true }));
assert.match(text(loading), /\[ui_translation_editor_starter_loading\]/);
assert.doesNotMatch(loading, /\[ui_composer_translate_message\]/);
assert.match(html(TranslationEditor, props({ oncopysource() {} })), /<button[^>]*title="\[ui_composer_copy_source_title\]"[^>]*>[\s\S]*?\[ui_composer_copy_source\]/);
assert.doesNotMatch(plain, /ui_composer_copy_source/);

// The workflow badge of a translation that does not exist yet says so, not "Draft".
const workflow = (notTranslated) => html(ReviewWorkflow, {
  state: "draft", notTranslated, dirty: false, message: "", disabled: false, stale: false, terminologyCount: 0, qualityCount: 0,
  note: "", qualityIssues: [], suggestions: [], onstatechange() {}, onnotechange() {}, ondiscard() {}, onterminology() {},
  onreport() {}, onqualityfilter() {}, onsuggestion() {},
});
// The badge's text follows its state mark (a hollow square for a missing translation; a draft has none).
const badgeMatch = (markup) => /data-testid="workflow-state"[^>]*>((?:<!--[^]*?-->|<span[^>]*><\/span>)*)([^<]*)</.exec(markup);
const badge = (markup) => badgeMatch(markup)?.[2].trim();
assert.equal(badge(workflow(true)), "[ui_resource_not_translated]");
assert.match(badgeMatch(workflow(true))[1], /data-state-mark="missing"/);
assert.equal(badge(workflow(false)), "[ui_workflow_draft]");
assert.doesNotMatch(badgeMatch(workflow(false))[1], /data-state-mark/);
console.log("PASS: the counter shows the text against @maxLength as RTR0098 counts it, per variant too, and explains a text over the limit at the field.");
