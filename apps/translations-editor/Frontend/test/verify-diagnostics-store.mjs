// The diagnostics store keeps validation results per request and document; the panel still lists the result that
// finished last, or the workspace's diagnostics before any, and the selectors scope and order that list.
import assert from "node:assert/strict";
import "./svelte-modules.mjs";

const { DiagnosticsStore } = await import("../src/lib/state/diagnostics-store.svelte.ts");

const diagnostic = (id, severity, extra = {}) => ({
  id, severity, message: id, path: "de.rmf2", line: 1, column: 1, endLine: 1, endColumn: 2, ...extra,
});
let snapshot = {
  diagnostics: [
    diagnostic("RTR0090", "warning", { key: "common_save", locale: "de" }),
    diagnostic("RTR0010", "error", { key: "common_cancel", locale: "fr" }),
  ],
};
const store = new DiagnosticsStore(() => snapshot);

// Before any request the workspace's diagnostics are shown.
assert.equal(store.latest, undefined);
assert.deepEqual(store.displayed.map((item) => item.id), ["RTR0090", "RTR0010"]);
assert.deepEqual([store.errorCount, store.warningCount], [1, 1]);

// The result that finished last is shown, whichever request produced it.
const transform = { success: true, diagnostics: [diagnostic("W1", "warning", { key: "common_save", locale: "de" })] };
const validation = {
  success: false,
  diagnostics: [
    diagnostic("W2", "warning", { key: "common_save", locale: "de" }),
    diagnostic("E1", "error", { key: "common_save", locale: "de" }),
    diagnostic("W3", "warning", { key: "common_save", locale: "fr" }),
    diagnostic("E2", "error"),
  ],
};
store.record("transform", "de.rmf2", transform);
assert.equal(store.latest, transform);
store.record("validation", "fr.rmf2", validation);
assert.equal(store.latest, validation, "The panel shows the latest result, as the single slot did.");
assert.deepEqual(store.displayed.map((item) => item.id), ["W2", "E1", "W3", "E2"], "The displayed list keeps the reported order.");
assert.deepEqual([store.errorCount, store.warningCount], [2, 2]);
assert.equal(store.result("transform", "de.rmf2"), transform, "Earlier results stay addressable by request and document.");
assert.equal(store.result("transform", "fr.rmf2"), undefined);
store.record("transform", "de.rmf2", { success: true, diagnostics: [] });
assert.deepEqual(store.displayed, [], "A newer result of the same request replaces the shown one.");
store.record("validation", "fr.rmf2", validation);

// Selectors for later scoping: one message's diagnostics, and errors first.
assert.deepEqual(store.forMessage("common_save", "de").map((item) => item.id), ["W2", "E1"]);
assert.deepEqual(store.forMessage("common_save", "fr").map((item) => item.id), ["W3"]);
assert.deepEqual(store.forMessage("missing", "de"), []);
assert.deepEqual(store.sortedErrorsFirst().map((item) => item.id), ["E1", "E2", "W2", "W3"], "Errors come first; the order is otherwise kept.");
assert.deepEqual(store.displayed.map((item) => item.id), ["W2", "E1", "W3", "E2"], "Sorting does not reorder the displayed list.");

// Clearing (a new selection or snapshot) returns to the workspace's diagnostics, which follow the snapshot.
store.clear();
assert.equal(store.latest, undefined);
assert.equal(store.result("validation", "fr.rmf2"), undefined);
assert.deepEqual(store.displayed.map((item) => item.id), ["RTR0090", "RTR0010"]);
assert.deepEqual(store.forMessage("common_cancel", "fr").map((item) => item.id), ["RTR0010"]);
assert.deepEqual(store.sortedErrorsFirst().map((item) => item.id), ["RTR0010", "RTR0090"]);
snapshot = undefined;
const empty = new DiagnosticsStore(() => snapshot);
assert.deepEqual(empty.displayed, [], "Without a workspace nothing is listed.");

// A save of review data requests the saved workspace's diagnostics again (real-window check R5): they replace the
// snapshot's and the results of earlier requests, which still reported RTR0090, until the next snapshot.
{
  const { RuneValue } = await import("./rune-value.svelte.ts");
  const first = { diagnostics: [diagnostic("RTR0090", "warning", { key: "common_save", locale: "de" }), diagnostic("W9", "warning")] };
  const open = new RuneValue(first);
  const refreshing = new DiagnosticsStore(() => open.value);
  refreshing.record("transform", "de.rmf2", { success: true, diagnostics: [diagnostic("RTR0090", "warning", { key: "common_save", locale: "de" })] });
  refreshing.refreshWorkspace(first, [diagnostic("W9", "warning")]);
  assert.equal(refreshing.latest, undefined, "The results of earlier requests still show.");
  assert.deepEqual(refreshing.displayed.map((item) => item.id), ["W9"], "The stale-review warning stays after the review save.");
  // A selection change clears the results: the refreshed workspace diagnostics stay.
  refreshing.clear();
  assert.deepEqual(refreshing.displayed.map((item) => item.id), ["W9"]);
  // A refresh for a snapshot that is no longer open changes nothing; a new snapshot brings its own diagnostics.
  const second = { diagnostics: [diagnostic("E7", "error")] };
  open.value = second;
  assert.deepEqual(refreshing.displayed.map((item) => item.id), ["E7"]);
  refreshing.refreshWorkspace(first, []);
  assert.deepEqual(refreshing.displayed.map((item) => item.id), ["E7"], "A refresh of an earlier snapshot replaced the open one's.");
}

console.log("PASS: the diagnostics store keeps results per request and document, shows the latest, and scopes and orders it.");
