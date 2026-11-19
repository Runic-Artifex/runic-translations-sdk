// The side-by-side list (D074.1): the rows of the selected locale in tree order under namespace headers, which rows are
// edited in place, their states, the source text's columns, the stored choice of the view, and the list's grid
// semantics, keyboard and one edit model.
import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import "./svelte-modules.mjs";

const { buildListRows, inlineVariant, listMessageRows, readWorkspaceLayout, workspaceLayoutKey } = await import("../src/lib/list-view.ts");
const { PreferencesController } = await import("../src/lib/state/preferences-controller.svelte.ts");
const { ListRowHeights, listRowAt, listRowEstimates, listRowOffsets, listSlices, listWindow, listOverscanRows } = await import("../src/lib/list-virtualization.ts");

const pattern = (text) => ({ supported: true, selectors: [], variants: [{ id: "v0", keys: [], pattern: text }] });
const plural = (one, other) => ({
  supported: true, selectors: ["count"],
  variants: [{ id: "v0", keys: ["one"], pattern: one }, { id: "v1", keys: ["*"], pattern: other }],
});
const entry = (value, authoring, extra = {}) => ({ key: "", path: [], value, tags: [], structured: authoring?.selectors.length > 0, authoring, ...extra });
const row = (key, path, cells, extra = {}) => ({ key, path, tags: [], cells, structured: false, ...extra });

// Inline: a supported message without selectors and with one pattern; plural forms and unsupported text are not.
assert.deepEqual(inlineVariant(pattern("Cancel")), { id: "v0", keys: [], pattern: "Cancel" });
assert.equal(inlineVariant(plural("One", "{$count}")), undefined);
assert.equal(inlineVariant({ supported: false, selectors: [], variants: [{ id: "v0", keys: [], pattern: "x" }] }), undefined);
assert.equal(inlineVariant(undefined), undefined);

const rows = [
  row("zeta", ["zeta"], { de: { entry: entry("Letzte", pattern("Letzte")) } }),
  row("files_selected", ["files", "selected"], {
    de: { entry: entry("…", plural("Eine Datei", "{$count} Dateien")) },
    en: { entry: entry("…", plural("One file", "{$count} files")) },
  }, { structured: true, structure: "plural" }),
  row("files_deleted", ["files", "deleted"], { de: { entry: entry("…", plural("Eine gelöscht", "{$count} gelöscht")) } },
    { structured: true, structure: "plural" }),
  row("common_save", ["common", "save"], {
    de: { entry: entry("Speichern {$name}", pattern("Speichern {$name}")) },
    en: { entry: entry("Save {$name}", pattern("Save {$name}")) },
  }, { description: "The save button." }),
  row("common_cancel", ["common", "cancel"], { de: { entry: entry("Abbrechen", pattern("Abbrechen")) } }),
];
const reviews = { common_save: { state: "approved", stale: true } };
const review = (key, translated) => reviews[key] ?? { state: translated ? "translated" : "draft", stale: false };

// English, a translation: tree order (common, files, then zeta at the top level), one header per namespace with its
// counts, the source beside the translation, and the states with the brand's shapes.
const english = buildListRows(rows, { locale: "en", baseLocale: "de", sourceText: false, review });
assert.deepEqual(english.map((item) => item.kind === "group" ? `# ${item.group} ${item.messages}/${item.missing}` : item.dottedPath), [
  "# common 2/1", "common.cancel", "common.save", "# files 2/1", "files.deleted", "files.selected", "#  1/1", "zeta",
]);
const byKey = Object.fromEntries(listMessageRows(english).map((item) => [item.key, item]));
assert.deepEqual(byKey.common_cancel.state, { name: "missing", mark: "missing" }, "A missing translation is a hollow square.");
assert.equal(byKey.common_cancel.missing, true);
assert.equal(byKey.common_cancel.text, undefined, "A missing translation has no text.");
assert.equal(byKey.common_cancel.inline, true, "A missing translation of one pattern is edited in place on its starter.");
assert.deepEqual(byKey.common_cancel.source, { kind: "blocks", selectors: [], blocks: [{ id: "v0", keys: [], segments: [{ kind: "text", text: "Abbrechen" }] }] },
  "The source text is shown beside a missing translation.");
assert.deepEqual(byKey.common_save.state, { name: "outdated", mark: "outdated" }, "An outdated translation is a diagonal.");
assert.deepEqual(byKey.common_save.text.blocks[0].segments, [{ kind: "text", text: "Save " }, { kind: "variable", name: "name", syntax: "{$name}" }],
  "Placeholders are chips in the list as in the workbench.");
assert.equal(byKey.common_save.note, "The save button.");
assert.deepEqual(byKey.files_selected.state, { name: "translated", mark: "done" }, "A translated message is a filled square.");
assert.equal(byKey.files_selected.inline, false, "Plural forms open in the workbench.");
assert.equal(byKey.files_deleted.inline, false, "A missing plural's starter has plural forms too: it opens in the workbench.");
assert.equal(byKey.files_deleted.structure, "plural");
assert.equal(new Set(english.map((item) => item.id)).size, english.length, "Row ids are unique.");

// Needs review and approved.
reviews.common_save = { state: "needs-review", stale: false };
assert.deepEqual(listMessageRows(buildListRows(rows, { locale: "en", baseLocale: "de", sourceText: false, review }))
  .find((item) => item.key === "common_save").state, { name: "needs-review", mark: "review" });
reviews.common_save = { state: "approved", stale: false };
assert.deepEqual(listMessageRows(buildListRows(rows, { locale: "en", baseLocale: "de", sourceText: false, review }))
  .find((item) => item.key === "common_save").state, { name: "approved", mark: "done" });

// The source text (the base locale, or a content-editing project): no source column, no states, nothing missing.
const german = listMessageRows(buildListRows(rows, { locale: "de", baseLocale: "de", sourceText: true, review }));
assert.ok(german.every((item) => item.source === undefined && item.state === undefined && !item.missing));
assert.equal(german.find((item) => item.key === "common_save").inline, true);
assert.equal(german.find((item) => item.key === "files_selected").inline, false);

// A subset (search, filter) keeps the tree order and its headers.
assert.deepEqual(buildListRows([rows[0], rows[4]], { locale: "en", baseLocale: "de", sourceText: false, review })
  .map((item) => item.kind === "group" ? `# ${item.group}` : item.key), ["# common", "common_cancel", "# ", "zeta"]);

// The unsaved mark: a row says whether its message has unsaved work in the selected locale; none by default.
assert.ok(listMessageRows(english).every((item) => item.unsaved === false), "A row is unsaved without unsaved work.");
const marked = listMessageRows(buildListRows(rows, { locale: "en", baseLocale: "de", sourceText: false, review, unsaved: (key) => key === "common_save" }));
assert.deepEqual(marked.filter((item) => item.unsaved).map((item) => item.key), ["common_save"]);

// Virtualization: heights are measured per row; a row not measured yet counts as the average of its kind.
{
  const heights = new ListRowHeights();
  const many = Array.from({ length: 20_000 }, (_, index) => index % 50 === 0
    ? { id: `g:${index}`, kind: "group" }
    : { id: `m:${index}`, kind: "message" });
  let offsets = listRowOffsets(many, heights);
  assert.equal(offsets.length, many.length + 1);
  assert.equal(offsets[1], listRowEstimates.group);
  assert.equal(offsets[2], listRowEstimates.group + listRowEstimates.message);
  assert.equal(heights.set("m:1", "message", 100), true);
  assert.equal(heights.set("m:1", "message", 100.2), false, "A sub-pixel change is not a new height.");
  assert.equal(heights.estimate("message"), 100, "Unmeasured messages are not estimated by the measured ones.");
  heights.set("m:2", "message", 60);
  assert.equal(heights.estimate("message"), 80);
  assert.equal(heights.height("m:1", "message"), 100);
  heights.set("m:1", "message", 40);
  assert.equal(heights.estimate("message"), 50, "A row measured again replaces its earlier height in the average.");
  offsets = listRowOffsets(many, heights);
  assert.equal(offsets[3] - offsets[2], 60);
  // The window: the rows in view and the overscan on both sides, found by binary search.
  assert.equal(listRowAt(offsets, 0), 0);
  assert.equal(listRowAt(offsets, offsets[500] + 1), 500);
  assert.equal(listRowAt(offsets, offsets.at(-1) + 1_000), many.length - 1, "A position past the end is the last row.");
  const span = listWindow(offsets, offsets[500], offsets[510] - offsets[500]);
  assert.deepEqual(span, { start: 500 - listOverscanRows, end: 510 + listOverscanRows });
  assert.deepEqual(listWindow(offsets, -50, 100), { start: 0, end: listRowAt(offsets, 49) + 1 + listOverscanRows });
  assert.deepEqual(listWindow(listRowOffsets([], heights), 0, 500), { start: 0, end: 0 });
  // The slices: gaps of exactly the left-out rows' height, the window, and a pinned row (the tab stop or the edited
  // row) kept outside it, so the focus stays in the DOM while it scrolls out of view.
  const slices = listSlices(offsets, span, [3, 505, 19_999]);
  assert.deepEqual(slices.map((slice) => slice.kind === "gap" ? "gap" : `${slice.start}-${slice.end}`),
    ["gap", "3-4", "gap", `${span.start}-${span.end}`, "gap", "19999-20000"]);
  assert.equal(slices[0].height, offsets[3]);
  assert.equal(slices[2].height, offsets[span.start] - offsets[4]);
  assert.equal(slices.reduce((total, slice) => total + (slice.kind === "gap" ? slice.height : offsets[slice.end] - offsets[slice.start]), 0), offsets.at(-1),
    "The gaps and rows do not add up to the list's height.");
  assert.deepEqual(listSlices(offsets, { start: 0, end: 10 }, [10]).map((slice) => slice.kind === "gap" ? "gap" : `${slice.start}-${slice.end}`),
    ["0-11", "gap"], "A pinned row next to the window joins it.");
  heights.clear();
  assert.equal(heights.estimate("message"), listRowEstimates.message);
}

// The view is a preference: the workbench by default, the list once chosen, stored in the native editor state.
assert.equal(workspaceLayoutKey, "runic-translations.workspace-layout");
assert.equal(readWorkspaceLayout(null), "workbench");
assert.equal(readWorkspaceLayout("list"), "list");
assert.equal(readWorkspaceLayout("table"), "workbench");
const stored = new Map();
const preferences = new PreferencesController({
  initialLocale: "en", locales: ["en", "de"],
  storage: { get: (key) => stored.get(key) ?? null, set: (key, value) => stored.set(key, value) },
  windowLocale: { request() {} }, preview: () => ({ pseudoLocale: "en-XA" }), refreshPreview() {},
  applyAppearance() {}, applyDensity() {},
});
preferences.restore();
assert.equal(preferences.layout, "workbench");
preferences.changeLayout("list");
assert.equal(stored.get(workspaceLayoutKey), "list");
preferences.layout = "workbench";
preferences.restore();
assert.equal(preferences.layout, "list", "The chosen view is restored.");
const localState = await readFile(new URL("../src/lib/local-state.ts", import.meta.url), "utf8");
const nativeStore = await readFile(new URL("../../EditorLocalStateStore.cs", import.meta.url), "utf8");
assert.match(localState, /"runic-translations\.workspace-layout",/, "The frontend does not own the view key.");
assert.match(nativeStore, /"runic-translations\.workspace-layout",/, "The native store's allow-list does not accept the view key.");

// The list's grid: named, described, one tab stop, Up/Down/Home/End/Enter on cells, Escape and Tab in the editor,
// and edits through the one edit model (the selection's starter or message, the composer's set-pattern operation).
const list = await readFile(new URL("../src/lib/screens/MessageListView.svelte", import.meta.url), "utf8");
assert.match(list, /role="grid"\s+aria-label=\{labels\.messages\}\s+aria-describedby=\{helpId\}/, "The list grid is unnamed or undescribed.");
assert.match(list, /tabindex=\{row\.key === focusKey && !editingThis \? 0 : -1\}/, "The list grid has no single tab stop.");
for (const key of ["ArrowDown", "ArrowUp", "Home", "End", "Enter"]) assert.ok(list.includes(`case "${key}":`), `The list does not handle ${key}.`);
assert.match(list, /event\.key === "Escape"[\s\S]*?stopEditing\(row\)/, "Escape does not leave editing.");
assert.match(list, /event\.key !== "Tab"[\s\S]*?moveTo\(target, true, event\.shiftKey\)/, "Tab does not move to the next row's text.");
assert.match(list, /selection\.currentStarter\?\.projection \?\? selection\.currentCell\?\.entry\?\.authoring/,
  "The list does not edit the selection's starter or message.");
assert.match(list, /editing\.applyAuthoringOperation\(\{ kind: "set-pattern", variantId: variant\.id, pattern \}\)/,
  "The list does not edit through the composer's operation.");
assert.match(list, /<PatternEditor\s+compact/, "The list does not reuse the pattern editor.");
assert.match(list, /\{revision\}/, "The list's fields do not follow undo and redo.");
// Virtualized: only the rendered window and the pinned tab stop are rows in the DOM, the grid says how many rows it
// has and where each shown row is, and each shown row is measured.
assert.match(list, /aria-rowcount=\{rows\.length \+ 1\}/, "The grid does not count its rows.");
assert.equal(list.match(/aria-rowindex=\{position \+ 2\}/g)?.length, 2, "A shown row does not say where it is.");
assert.match(list, /class="list-head" role="row" aria-rowindex=\{1\}/);
assert.match(list, /\{#each rendered as item \(item\.id\)\}/, "The list renders every row.");
assert.match(list, /const pinnedIndex = \$derived\(focusKey === undefined \? undefined : rowPosition\.get\(`m:\$\{focusKey\}`\)\)/,
  "The row with the tab stop (and the edited row) can leave the DOM.");
assert.match(list, /\{@attach measureRow\(row\.id, "message"\)\}/, "The list's rows are not measured.");
assert.doesNotMatch(list, /content-visibility/, "The rows' heights are guessed by the browser instead of measured.");
// The unsaved mark: the window title's marker with an accessible name, before the path.
assert.match(list, /role="img" aria-label=\{ui\.text\("ui_list_unsaved"\)\} title=\{ui\.text\("ui_list_unsaved"\)\} data-unsaved-mark>\{unsavedTitleMarker\.trim\(\)\}/,
  "The unsaved mark is not the title's marker or has no name.");
assert.match(list, /\{#if row\.unsaved\}\{@render unsavedMark\(\)\}\{\/if\}/);
assert.match(list, /:global\(:root\[data-density="compact"\]\) \.list-scroller/, "The density does not apply to the list's rows.");
const shell = await readFile(new URL("../src/lib/screens/WorkspaceShell.svelte", import.meta.url), "utf8");
assert.match(shell, /role="group" aria-label=\{ui\.text\("ui_view_label"\)\}/, "The view switch is unnamed.");
assert.match(shell, /aria-pressed=\{layout === option\.value\}/, "The view switch does not say which view is shown.");

console.log("PASS: the list view's rows, states, inline editing, stored view and grid keyboard.");
