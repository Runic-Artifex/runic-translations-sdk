// The composer of a content-editing project leaves the message's structure to developers (#225, real-window check R6):
// no Add plural forms, no adding or removing cases, no advanced structure (inputs, formatters, selection rules), and
// no Message source button for the message or its cases. The message source stays reachable as a small "Show source" link in the composer's footer,
// for fixing a broken text. A translation project keeps all of it. Rendered by Svelte's server renderer.
import assert from "node:assert/strict";
import { html, text } from "./svelte-server-components.mjs";

const { default: MessageComposer } = await import("../src/lib/MessageComposer.svelte");

const authoring = (patterns, selectors = []) => ({
  revision: "r", supported: true, inputs: [{ name: "count", type: "int64", function: "integer", declared: true }],
  selectors, selectorFunctions: selectors.map(() => "plural"),
  variants: patterns.map(([keys, pattern], index) => ({ id: String(index), keys, pattern, startByte: 0, lengthBytes: 0 })),
  pluralCategories: ["one", "other"], selectorPluralCategories: selectors.map(() => ["one", "other"]), cldrVersion: "48", empty: false,
});
const composerProps = (extra) => ({
  value: "Save", locale: "en", authoring: authoring([[[], "Save"]]), onchange() {}, onoperation() {}, ...extra,
});
const pluralValue = ".input {$count :integer}\n.match $count\none {{One note}}\n* {{{$count} notes}}";
const pluralAuthoring = authoring([[["one"], "One note"], [["*"], "{$count} notes"]], ["count"]);

// A translation project: plural forms, the advanced structure and the message source as a primary action.
const translation = html(MessageComposer, composerProps());
assert.match(translation, /\[ui_composer_add_plural_forms\]/);
assert.match(translation, /\[ui_composer_advanced_structure\]/);
assert.match(translation, /<button[^>]*aria-pressed="false"[^>]*>[\s\S]*?\[ui_composer_message_source\]/, "The message source is a primary action.");
assert.doesNotMatch(translation, /composer-show-source/);
const pluralTranslation = html(MessageComposer, composerProps({ value: pluralValue, authoring: pluralAuthoring }));
assert.equal([...pluralTranslation.matchAll(/\[ui_composer_message_source\]/g)].length, 3, "The message and each case offer their source.");
assert.match(pluralTranslation, /\[ui_composer_exact_or_custom_match\]/, "A translation project cannot add a case.");
assert.match(pluralTranslation, /aria-label="\[ui_composer_remove\] count: one"/, "A translation project cannot remove a case.");

// A content-editing project: none of them, and a "Show source" link in the footer.
const content = html(MessageComposer, composerProps({ structure: false }));
assert.doesNotMatch(content, /\[ui_composer_add_plural_forms\]/, "A content-editing project offers Add plural forms.");
assert.doesNotMatch(content, /\[ui_composer_advanced_structure\]|\[ui_composer_inputs\]|\[ui_composer_selection_rules\]/,
  "A content-editing project shows the advanced structure.");
assert.doesNotMatch(content, /\[ui_composer_message_source\]/, "A content-editing project shows the Message source button.");
assert.match(content, /<footer[^>]*>\s*<button[^>]*data-testid="composer-show-source"[^>]*>\s*\[ui_composer_show_source\]/,
  "The message source is not reachable from the composer's footer.");
// A plural keeps its cases (the text of each), without a Message source button per case.
const plural = html(MessageComposer, composerProps({ value: pluralValue, authoring: pluralAuthoring, structure: false }));
assert.equal([...plural.matchAll(/data-slot="card-title"/g)].length, 2, "A content-editing project lost a case of the text.");
assert.doesNotMatch(plural, /\[ui_composer_message_source\]|\[ui_composer_advanced_structure\]/);
assert.doesNotMatch(plural, /\[ui_composer_exact_or_custom_match\]|\[ui_composer_remove\]/, "A content-editing project adds or removes cases.");
assert.match(text(plural), /\[ui_composer_show_source\]/);
// A message without a visual projection still shows its source.
assert.match(html(MessageComposer, composerProps({ authoring: { supported: false, reason: "complex" }, structure: false })), /<textarea/);

console.log("PASS: a content-editing project's composer leaves plural forms, the advanced structure and the message source button to developers, and keeps the source behind a Show source link.");
