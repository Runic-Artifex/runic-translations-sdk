import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import { performance } from "node:perf_hooks";
import ts from "typescript";

const sourceUrl = new URL("../src/lib/message-virtualization.ts", import.meta.url);
const source = (await readFile(sourceUrl, "utf8"))
  .replace(/import type \{[^}]*\} from "\.\/MessageList\.svelte";\n/, "");
const transpiled = ts.transpileModule(source, {
  compilerOptions: { module: ts.ModuleKind.ESNext, target: ts.ScriptTarget.ES2022 },
  fileName: sourceUrl.pathname,
});
const virtualization = await import(`data:text/javascript;base64,${Buffer.from(transpiled.outputText).toString("base64")}`);
const item = (key, path, status = {}) => ({
  key, path, preview: key, missing: false, structured: false, stale: false, needsReview: false, ...status,
});
const describe = (rows) => rows.map((row) => `${"  ".repeat(row.level - 1)}${row.kind === "group" ? `${row.label}/ ${row.counts.messages}${row.expanded ? "" : " (collapsed)"}` : row.label}`);

// Tree building: groups are path prefixes, leaves are messages labelled with their last segment, sorted by segment.
{
  const items = [
    item("files_selected", ["files", "selected"]),
    item("common_save", ["common", "save"], { missing: true }),
    item("title", ["title"]),
    item("common_cancel", ["common", "cancel"], { stale: true }),
    item("files_dialog_open", ["files", "dialog", "open"], { needsReview: true, missing: true }),
    // A message and a group of the same name: the message sorts first; both stay.
    item("files", ["files"]),
  ];
  const tree = virtualization.buildMessageTree(items);
  const rows = virtualization.visibleMessageRows(tree, new Set());
  assert.deepEqual(describe(rows), [
    "common/ 2", "  cancel", "  save",
    "files", "files/ 2", "  dialog/ 1", "    open", "  selected",
    "title",
  ], "The tree groups messages by their path prefixes.");
  assert.equal(tree.groupCount, 3);
  const common = rows.find((row) => row.id === "g:common");
  assert.deepEqual(common.counts, { messages: 2, missing: 1, stale: 1, needsReview: 0 }, "A group counts the work below it.");
  assert.deepEqual(rows.find((row) => row.id === "g:files").counts, { messages: 2, missing: 1, stale: 0, needsReview: 1 });
  const open = rows.find((row) => row.kind === "message" && row.key === "files_dialog_open");
  assert.equal(open.label, "open");
  assert.equal(open.dottedPath, "files.dialog.open");
  assert.equal(open.parentId, "g:files.dialog");
  assert.deepEqual([open.level, open.setSize, open.posInSet], [3, 1, 1], "Rows carry their ARIA level and position.");
  assert.deepEqual(rows.filter((row) => row.level === 1).map((row) => [row.label, row.posInSet, row.setSize]),
    [["common", 1, 4], ["files", 2, 4], ["files", 3, 4], ["title", 4, 4]]);
  assert.deepEqual(virtualization.messageAncestorIds(["files", "dialog", "open"]), ["files", "files.dialog"]);
  assert.deepEqual(virtualization.messageAncestorIds(["title"]), []);
}

// A flat catalog has no groups and lists its messages as today; a message without a path falls back to its key.
{
  const rows = virtualization.virtualMessageTree([item("b", ["b"]), item("a", ["a"]), item("c", undefined)]);
  assert.deepEqual(rows.map((row) => [row.kind, row.label, row.level]), [["message", "a", 1], ["message", "b", 1], ["message", "c", 1]]);
}

// Messages from several files or mounts under one prefix form one group.
{
  const fromFirstFile = [item("shop_cart_title", ["shop", "cart", "title"])];
  const fromMountedFile = [item("shop_cart_empty", ["shop", "cart", "empty"]), item("shop_checkout", ["shop", "checkout"])];
  const rows = virtualization.virtualMessageTree([...fromFirstFile, ...fromMountedFile]);
  assert.deepEqual(describe(rows), ["shop/ 3", "  cart/ 2", "    empty", "    title", "  checkout"]);
}

// Filtering applies to messages: the list passes only matches, so a group appears only with a match below it and
// counts only its matches.
{
  const all = [
    item("common_cancel", ["common", "cancel"], { missing: true }),
    item("common_save", ["common", "save"]),
    item("files_selected", ["files", "selected"]),
    item("files_dialog_open", ["files", "dialog", "open"], { missing: true }),
  ];
  const rows = virtualization.virtualMessageTree(all.filter((candidate) => candidate.missing));
  assert.deepEqual(describe(rows), ["common/ 1", "  cancel", "files/ 1", "  dialog/ 1", "    open"],
    "Only groups with a matching message are shown, with their ancestors.");
}

// Collapse state: the translator's own state, a search's transient state, and reveal.
{
  const tree = virtualization.buildMessageTree([
    item("common_cancel", ["common", "cancel"]),
    item("files_selected", ["files", "selected"]),
    item("files_dialog_open", ["files", "dialog", "open"]),
  ]);
  let state = virtualization.emptyMessageTreeExpansion;
  state = virtualization.setGroupExpanded(state, "files", false, false);
  assert.deepEqual(describe(virtualization.visibleMessageRows(tree, virtualization.collapsedGroups(state, false))),
    ["common/ 1", "  cancel", "files/ 2 (collapsed)"], "Collapsing a group hides its rows.");
  assert.equal(virtualization.setGroupExpanded(state, "files", false, false), state, "An unchanged state is kept as is.");
  // While searching, every group with matches starts expanded; collapsing then changes only the search's state.
  assert.equal(virtualization.collapsedGroups(state, true).size, 0);
  const searching = virtualization.setGroupExpanded(state, "common", false, true);
  assert.deepEqual([...searching.transient], ["common"]);
  assert.deepEqual([...searching.collapsed], ["files"], "Collapsing during a search keeps the translator's own state.");
  assert.deepEqual([...virtualization.collapsedGroups(searching, false)], ["files"], "The own state is back after the search.");
  // Revealing a message expands its ancestors in both states.
  const revealed = virtualization.revealGroups({ collapsed: new Set(["files", "files.dialog", "common"]), transient: new Set(["files"]) },
    virtualization.messageAncestorIds(["files", "dialog", "open"]));
  assert.deepEqual([...revealed.collapsed], ["common"]);
  assert.deepEqual([...revealed.transient], []);
  assert.equal(virtualization.revealGroups(revealed, ["files"]), revealed);
}

// The recorded state: per catalog, most recent first, bounded, and robust against a damaged record.
{
  let raw = null;
  raw = virtualization.writeCollapsedGroups(raw, "/work\nA", new Set(["files", "common"]));
  raw = virtualization.writeCollapsedGroups(raw, "/work\nB", new Set(["shop"]));
  assert.deepEqual([...virtualization.readCollapsedGroups(raw, "/work\nA")], ["common", "files"]);
  assert.deepEqual([...virtualization.readCollapsedGroups(raw, "/work\nB")], ["shop"]);
  assert.deepEqual(JSON.parse(raw).catalogs.map((entry) => entry.catalog), ["/work\nB", "/work\nA"]);
  raw = virtualization.writeCollapsedGroups(raw, "/work\nA", new Set());
  assert.deepEqual(JSON.parse(raw).catalogs.map((entry) => entry.catalog), ["/work\nB"], "An all-expanded catalog records nothing.");
  for (let index = 0; index < 20; index += 1) raw = virtualization.writeCollapsedGroups(raw, `/work\n${index}`, new Set(["g"]));
  assert.equal(JSON.parse(raw).catalogs.length, virtualization.messageTreeStateLimits.catalogs, "Only the most recent catalogs are kept.");
  const many = new Set(Array.from({ length: 5_000 }, (_, index) => `group${index}`));
  assert.equal(JSON.parse(virtualization.writeCollapsedGroups(null, "x", many)).catalogs[0].collapsed.length,
    virtualization.messageTreeStateLimits.collapsedGroups);
  for (const damaged of ["{", "[]", '{"version":2,"catalogs":[]}', '{"version":1,"catalogs":[{"catalog":1}]}'])
    assert.equal(virtualization.readCollapsedGroups(damaged, "x").size, 0, `A damaged record (${damaged}) reads as none.`);
}

// Scrolling a row into view moves as little as possible.
{
  const height = virtualization.messageVirtualRowHeight;
  assert.equal(virtualization.scrollTopForRow(20, 0, 480), 20 * height + height - 480);
  assert.equal(virtualization.scrollTopForRow(3, 0, 480), 0);
  assert.equal(virtualization.scrollTopForRow(2, 500, 480), 2 * height);
  // After a collapse shrinks the list below the scroll position, the window stays valid.
  const window = virtualization.virtualMessageWindow(5, 10_000, 480);
  assert.ok(window.start <= window.end && window.end === 5);
}

verifyScale("10,000", 10_000, 250);
verifyScale("50,000", 50_000, 900);
verifyTree("3,000-message tree", 3_000, 60);

function scaleItems(count) {
  return Array.from({ length: count }, (_, index) => {
    const path = [`Group${index % 100}`, `Section${index % 31}`, `Message${String(index).padStart(5, "0")}`];
    return item(path.join("_"), path);
  });
}

function verifyScale(name, count, buildBudget) {
  const items = scaleItems(count);
  const started = performance.now();
  const rows = virtualization.virtualMessageTree(items);
  const buildElapsed = performance.now() - started;
  const interactionStarted = performance.now();
  let maximumWindow = 0;
  for (let step = 0; step < 100; step += 1) {
    const window = virtualization.virtualMessageWindow(rows.length, step * 9_847, 480);
    maximumWindow = Math.max(maximumWindow, window.end - window.start);
    assert(window.offset === window.start * virtualization.messageVirtualRowHeight,
      `${name} virtual window offset drifted from its row index.`);
  }
  const interactionElapsed = performance.now() - interactionStarted;
  const maximumAllowed = Math.ceil(480 / virtualization.messageVirtualRowHeight) + virtualization.messageVirtualOverscan * 2;
  assert(rows.length > count, `${name} tree flattening lost hierarchy rows.`);
  assert(maximumWindow <= maximumAllowed, `${name} rendered ${maximumWindow} rows instead of a bounded viewport window.`);
  assert(buildElapsed <= buildBudget, `${name} virtual tree build took ${buildElapsed.toFixed(1)}ms (budget ${buildBudget}ms).`);
  assert(interactionElapsed <= 30, `${name} 100 scroll-window interactions took ${interactionElapsed.toFixed(1)}ms (budget 30ms).`);
  console.log(`PASS: ${name} virtual tree built in ${buildElapsed.toFixed(1)}ms; 100 viewport windows in ${interactionElapsed.toFixed(1)}ms; at most ${maximumWindow}/${rows.length} rows rendered.`);
}

// A few thousand messages in nested groups: building, collapsing and expanding stay fast and the window stays bounded.
function verifyTree(name, count, toggleBudget) {
  const items = scaleItems(count);
  const tree = virtualization.buildMessageTree(items);
  const groups = [...new Set(items.map((candidate) => candidate.path[0]))];
  const toggleStarted = performance.now();
  let state = virtualization.emptyMessageTreeExpansion;
  let collapsedRows = 0;
  for (const group of groups.slice(0, 20)) {
    state = virtualization.setGroupExpanded(state, group, false, false);
    collapsedRows = virtualization.visibleMessageRows(tree, state.collapsed).length;
  }
  for (const group of groups.slice(0, 20)) state = virtualization.setGroupExpanded(state, group, true, false);
  const expandedRows = virtualization.visibleMessageRows(tree, state.collapsed).length;
  const toggleElapsed = (performance.now() - toggleStarted) / 40;
  const allCollapsed = virtualization.visibleMessageRows(tree, new Set(groups));
  assert.equal(allCollapsed.length, groups.length, `${name}: collapsing every top-level group leaves one row per group.`);
  assert(collapsedRows < expandedRows, `${name}: collapsed groups still render their rows.`);
  const window = virtualization.virtualMessageWindow(expandedRows, expandedRows * virtualization.messageVirtualRowHeight / 2, 480);
  assert(window.end - window.start <= Math.ceil(480 / virtualization.messageVirtualRowHeight) + virtualization.messageVirtualOverscan * 2,
    `${name}: the expanded tree renders more than a viewport window.`);
  assert(toggleElapsed <= toggleBudget, `${name}: a collapse or expand took ${toggleElapsed.toFixed(1)}ms (budget ${toggleBudget}ms).`);
  console.log(`PASS: ${name} (${expandedRows} rows expanded, ${allCollapsed.length} with every group collapsed); collapse/expand with re-flattening ${toggleElapsed.toFixed(2)}ms each.`);
}
