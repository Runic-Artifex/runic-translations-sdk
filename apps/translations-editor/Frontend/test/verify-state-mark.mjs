// State marks (D074): a hollow square for missing, filled for done, a diagonal for outdated, a hollow square in the
// warning colour for needs review, on the tree, the workflow badge and the language list.
import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";

const { localeStateMark, messageStateMark, reviewStateMark } = await import("../src/lib/state-mark.ts");

const item = (extra) => ({ missing: false, stale: false, needsReview: false, ...extra });
assert.equal(messageStateMark(item({})), "done");
assert.equal(messageStateMark(item({ missing: true, stale: true })), "missing", "A missing translation is missing first.");
assert.equal(messageStateMark(item({ stale: true, needsReview: true })), "outdated");
assert.equal(messageStateMark(item({ needsReview: true })), "review");

assert.equal(reviewStateMark("draft", true, false), "missing");
assert.equal(reviewStateMark("approved", false, true), "outdated");
assert.equal(reviewStateMark("needs-review", false, false), "review");
assert.equal(reviewStateMark("approved", false, false), "done");
assert.equal(reviewStateMark("translated", false, false), "done");
assert.equal(reviewStateMark("draft", false, false), undefined, "A draft is named by its badge, without a mark.");

assert.equal(localeStateMark({ isSource: true, missing: 3, percent: 50 }), undefined);
assert.equal(localeStateMark({ isSource: false, missing: 2, percent: 80 }), "missing");
assert.equal(localeStateMark({ isSource: false, missing: 0, stale: 1, percent: 100 }), "outdated");
assert.equal(localeStateMark({ isSource: false, missing: 0, percent: 100 }), "done");
assert.equal(localeStateMark({ isSource: false, percent: 99.5 }), "missing");

// Each mark has its shape in the stylesheet, so it reads without colour; the colour comes from the brand's state
// tokens, never from paint.
const css = await readFile(new URL("../src/routes/layout.css", import.meta.url), "utf8");
assert.match(css, /\.state-mark \{[^}]*border: 1\.5px solid var\(--state-mark-ink, var\(--rune-ink-soft\)\);/, "Missing is not a hollow square.");
assert.match(css, /\.state-mark\[data-state-mark="done"\] \{[^}]*background: var\(--state-mark-ink, var\(--rune-done\)\);/, "Done is not filled.");
assert.match(css, /\.state-mark\[data-state-mark="outdated"\] \{[^}]*linear-gradient\(135deg/, "Outdated has no diagonal.");
assert.match(css, /\.state-mark\[data-state-mark="review"\] \{[^}]*border-color: var\(--state-mark-ink, var\(--rune-warning\)\);/);
for (const [rule] of css.matchAll(/\.state-mark[^{]*\{[^}]*\}/g)) assert.doesNotMatch(rule, /paint/, "A state mark uses paint.");

const list = await readFile(new URL("../src/lib/MessageList.svelte", import.meta.url), "utf8");
const workflow = await readFile(new URL("../src/lib/ReviewWorkflow.svelte", import.meta.url), "utf8");
const locales = await readFile(new URL("../src/lib/LocaleSwitcher.svelte", import.meta.url), "utf8");
assert.match(list, /<StateMark state=\{messageStateMark\(row\.item\)\} \/>/, "The tree's messages carry no state mark.");
assert.match(list, /selectedKey === row\.key && "tree-current"/, "The selected message is not marked as the current position.");
assert.match(workflow, /data-testid="workflow-state">\{#if mark !== undefined && !stale\}<StateMark state=\{mark\} \/>/);
assert.match(workflow, /<StateMark state="outdated" \/>\{ui\.text\("ui_workflow_source_changed"\)\}/);
assert.match(locales, /<StateMark state=\{mark\} \/>/, "The language list carries no state mark.");

console.log("PASS: state marks map messages, review states and languages to the four brand shapes.");
