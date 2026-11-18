// What changed in the source of a stale review state (#225 P20): the word diff of the recorded and the current base
// text, the review store's recorded text of a stale entry, and the workflow panel's comparison, rendered by Svelte's
// server renderer.
import assert from "node:assert/strict";
import { html, text } from "./svelte-server-components.mjs";
import { mockExecute } from "../src/lib/mock-bridge.ts";
import { sourceDiff } from "../src/lib/source-diff.ts";

const { ReviewStore } = await import("../src/lib/state/review-store.svelte.ts");
const { default: ReviewWorkflow } = await import("../src/lib/ReviewWorkflow.svelte");

// A part list as compact strings: "=text", "-removed", "+added"; placeholders in braces as they are written.
const compact = (parts) => parts.map((part) => ({ same: "=", removed: "-", added: "+" })[part.change] + (part.kind === "text" ? part.text : part.syntax));

// Words: a changed word is removed from the recorded text and added to the current one; the rest is shared.
const words = sourceDiff("Save all changes", "Save the changes");
assert.deepEqual(compact(words.before), ["=Save ", "-all", "= changes"]);
assert.deepEqual(compact(words.after), ["=Save ", "+the", "= changes"]);
assert.equal(words.changed, true);
assert.equal(words.syntax, false);

// Placeholders are single parts, kept as chips; a renamed variable is a removed and an added chip, never split.
const placeholders = sourceDiff("Welcome back, {$name}", "Welcome, {$user}!");
assert.deepEqual(compact(placeholders.before), ["=Welcome", "- back", "=, ", "-{$name}"]);
assert.deepEqual(compact(placeholders.after), ["=Welcome, ", "+{$user}", "+!"]);
assert.deepEqual(placeholders.before.filter((part) => part.kind !== "text").map((part) => part.kind), ["variable"]);
assert.equal(sourceDiff("{$count :number} items", "{$count :number} items").before[0].kind, "placeholder", "A placeholder with a function is a chip too.");

// Escapes and quoted literals read as the source panel shows them.
const escaped = sourceDiff("Use \\{braces\\}", "Use {|{|}braces\\}");
assert.equal(escaped.changed, false, "The texts are compared as displayed, escapes resolved.");
assert.deepEqual(compact(escaped.after), ["=Use {braces}"]);

// White space alone between two changed words belongs to the change: one replacement, not two.
assert.deepEqual(compact(sourceDiff("Delete this file", "Remove that file").after), ["+Remove that", "= file"]);

// Scripts without spaces between words compare per character.
assert.deepEqual(compact(sourceDiff("保存する", "保存した").after), ["=保存", "+した"]);

// The same text: the fingerprint changed through the inputs (a placeholder's type); nothing is marked.
const same = sourceDiff("{$count} files", "{$count} files");
assert.equal(same.changed, false);
assert.ok([...same.before, ...same.after].every((part) => part.change === "same"));

// A message with declarations or selectors is compared as its MF2 source, in both texts.
const selector = sourceDiff(
  ".input {$count :integer}\n.match $count\none {{One file}}\n* {{{$count} files}}",
  ".input {$count :integer}\n.match $count\none {{One file selected}}\n* {{{$count} files selected}}",
);
assert.equal(selector.syntax, true);
assert.equal(selector.before.every((part) => part.kind === "text"), true, "MF2 source is compared as text.");
assert.deepEqual(selector.after.filter((part) => part.change === "added").map((part) => part.text), [" selected", " selected"]);
assert.equal(sourceDiff("One file", ".input {$count :integer}\n.match $count\n* {{{$count} files}}").syntax, true,
  "A plain text that became a selector message is compared as MF2 source too.");

// A long text beyond the word table is still compared: the common start and end, and the middle as a whole.
const long = (word) => `Start ${Array.from({ length: 700 }, (_, index) => `${word}${index}`).join(" ")} end`;
const large = sourceDiff(long("a"), long("b"));
assert.deepEqual([large.before[0], large.before.at(-1)].map((part) => [part.change, part.text]), [["same", "Start "], ["same", " end"]]);
assert.ok(large.before.slice(1, -1).every((part) => part.change === "removed"));

// The review store: the recorded text of a stale entry, by its key and source fingerprint.
let snapshot = await mockExecute("LoadWorkspace", {});
snapshot.review = { ...snapshot.review, fingerprints: { common_save: "sha256:save", common_cancel: "sha256:cancel", dashboard_welcome: "sha256:welcome" } };
const review = new ReviewStore({
  bridge: { saveReview: (request) => mockExecute("SaveReview", request) },
  snapshot: () => snapshot,
  errorNotice: (error) => `failed: ${error.message}`,
});
review.install(snapshot);
assert.equal(review.recordedSource("common_save", "de"), "Save all changes", "A stale entry's recorded base text is found.");
assert.equal(review.recordedSource("dashboard_welcome", "en"), undefined, "An entry that is not stale has none.");
assert.equal(review.recordedSource("common_cancel", "de"), undefined, "A message without an entry has none.");
// Confirming the translation records the current source: the entry is no longer stale, so nothing is compared.
review.confirm("common_save", "de", {}, true);
assert.equal(review.index.get("common_save\u0000de").stale, false);
assert.equal(review.recordedSource("common_save", "de"), undefined);
review.discard();
assert.equal(review.recordedSource("common_save", "de"), "Save all changes");
// An entry recorded without its text (by an older tool) has none.
snapshot = { ...snapshot, review: { ...snapshot.review, recordedSources: undefined } };
review.install(snapshot);
assert.equal(review.recordedSource("common_save", "de"), undefined);

// The workflow panel: the recorded text beside the current one, removed and added words marked, chips kept.
const props = (extra) => ({
  state: "approved", dirty: false, message: "", disabled: false, stale: true, terminologyCount: 0, qualityCount: 0,
  note: "", qualityIssues: [], suggestions: [], onstatechange() {}, onconfirm() {}, onnotechange() {}, ondiscard() {},
  onterminology() {}, onreport() {}, onqualityfilter() {}, onsuggestion() {}, ...extra,
});
const compared = html(ReviewWorkflow, props({ sourceDiff: sourceDiff("Welcome back, {$name}", "Welcome, {$name}!"), sourceLocale: "en" }));
assert.match(compared, /\[ui_workflow_stale_diff_explanation state=\[ui_workflow_approved\]\]/, "The explanation points at the comparison.");
assert.doesNotMatch(compared, /ui_workflow_stale_explanation/);
const row = (testId) => new RegExp(`data-testid="${testId}"[^>]*>(.*?)</div>`, "s").exec(compared)?.[1] ?? "";
const before = row("workflow-source-before"), now = row("workflow-source-now");
assert.match(before, /<dt[^>]*>\[ui_workflow_source_before\]<\/dt>/);
assert.match(now, /<dt[^>]*>\[ui_workflow_source_now\]<\/dt>/);
assert.match(before, /<dd[^>]*lang="en"[^>]*dir="ltr"/, "The compared texts carry the base locale's language and direction.");
assert.match(before, /<del[^>]*data-change="removed"[^>]*><span class="sr-only">\[ui_workflow_source_removed\] <\/span> back<\/del>/,
  "A removed word is struck through, named for screen readers.");
assert.match(now, /<ins[^>]*data-change="added"[^>]*><span class="sr-only">\[ui_workflow_source_added\] <\/span>!<\/ins>/);
assert.match(before, /<span[^>]*data-placeholder="variable"[^>]*>\{\$name\}<\/span>/, "An unchanged placeholder is a chip.");
assert.equal(text(now), "[ui_workflow_source_now] Welcome, {$name}[ui_workflow_source_added] !");
assert.doesNotMatch(compared, /workflow-source-same/);
assert.match(compared, /data-testid="workflow-confirm"/, "Confirm translation stays beside the comparison.");

// The same text says why it is stale; a message compared as MF2 source is monospaced.
assert.match(html(ReviewWorkflow, props({ sourceDiff: sourceDiff("{$count} files", "{$count} files") })), /data-testid="workflow-source-same"[^>]*>\[ui_workflow_source_same_text\]/);
assert.match(html(ReviewWorkflow, props({ sourceDiff: selector })), /data-testid="workflow-source-diff"[^>]*data-syntax="true"/);
assert.match(html(ReviewWorkflow, props({ sourceDiff: selector })), /<dd class="[^"]*font-mono/);

// Without a recorded text the panel keeps today's message; a right-to-left base locale keeps its direction.
const plain = html(ReviewWorkflow, props({}));
assert.match(plain, /\[ui_workflow_stale_explanation state=\[ui_workflow_approved\]\]/);
assert.doesNotMatch(plain, /workflow-source-diff/);
assert.match(html(ReviewWorkflow, props({ sourceDiff: sourceDiff("مرحبا", "أهلا"), sourceLocale: "ar" })), /<dd[^>]*lang="ar"[^>]*dir="rtl"/);
// A state that is not stale shows no comparison, even with a diff.
assert.doesNotMatch(html(ReviewWorkflow, props({ stale: false, sourceDiff: words })), /workflow-source-diff/);

console.log("PASS: a stale review state shows its recorded source next to the current one, with the changed words marked.");
