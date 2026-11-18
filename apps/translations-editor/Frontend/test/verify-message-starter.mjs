// The starter of a missing translation (#225 P2): created empty for the target locale, edited in memory, placed into
// the document's draft as soon as it has text (one save, the recovery copy), and taken out again when it is cleared, so
// the document is back at its saved text. "Copy source" fills it from the base message in one document edit.
import assert from "node:assert/strict";
import "./svelte-modules.mjs";
import { mockExecute } from "../src/lib/mock-bridge.ts";
import { copySourcePlan, nextPlacement, removePlacement, sourcePattern } from "../src/lib/message-starter.ts";

const { DraftStore } = await import("../src/lib/state/draft-store.svelte.ts");
const { MessageDraftStore } = await import("../src/lib/state/message-draft-store.svelte.ts");

// Placements: taking a placed message out gives the text without it, also after another message changed meanwhile.
{
  const without = "files {\n  open = Ouvrir\n}\n";
  const placed = "files {\n  open = Ouvrir\n  selected = Un\n}\n";
  const placement = { without, with: placed };
  assert.equal(removePlacement(placed, placement), without);
  assert.equal(removePlacement(placed.replace("Ouvrir", "Ouvrir tout"), placement), "files {\n  open = Ouvrir tout\n}\n");
  assert.equal(removePlacement(placed + placed, placement), undefined, "An inserted run found twice is not guessed.");
  assert.equal(removePlacement("other\n", placement), undefined);
  assert.equal(removePlacement("x", { without: "ab", with: "xb" }), undefined, "A replacement is not a placement.");
  // The first placement starts it; an update of the placed text keeps `without`; after other edits it is found again.
  assert.deepEqual(nextPlacement(undefined, without, placed), placement);
  const updated = placed.replace("Un", "Une");
  assert.deepEqual(nextPlacement(placement, placed, updated), { without, with: updated });
  const edited = placed.replace("Ouvrir", "Ouvrir tout");
  assert.deepEqual(nextPlacement(placement, edited, edited.replace("Un", "Une")),
    { without: "files {\n  open = Ouvrir tout\n}\n", with: edited.replace("Un", "Une") });
  assert.equal(nextPlacement(placement, "unrelated\n", "unrelated\nx\n"), undefined);
}

// Copy source: a starter variant gets the base pattern with the same keys, else the base's catch-all.
{
  const base = [{ keys: ["0"], pattern: "No files" }, { keys: ["one"], pattern: "One file" }, { keys: ["*"], pattern: "{$count} files" }];
  assert.equal(sourcePattern(["one"], base), "One file");
  assert.equal(sourcePattern(["few"], base), "{$count} files", "A category the base lacks takes the catch-all.");
  assert.equal(sourcePattern(["0"], base), "No files");
  assert.equal(sourcePattern([], [{ keys: [], pattern: "Save" }]), "Save", "A plain message copies its one pattern.");
  assert.equal(sourcePattern(["one", "*"], [{ keys: ["*", "*"], pattern: "Any" }]), "Any");
  assert.equal(sourcePattern(["one"], [{ keys: ["two"], pattern: "x" }]), undefined);
  const starter = [{ id: "0", keys: ["one"], pattern: "" }, { id: "1", keys: ["few"], pattern: "" }, { id: "2", keys: ["*"], pattern: "{$count} files" }];
  assert.deepEqual(copySourcePlan(starter, base), [
    { id: "0", keys: ["one"], pattern: "One file" },
    { id: "1", keys: ["few"], pattern: "{$count} files" },
  ], "A variant that already has the source pattern is left alone.");
}

await mockExecute("LoadWorkspace", {});
const path = "fr.rmf2";
const saved = "files {\n  open = Ouvrir\n}\n";
const snapshot = {
  root: "/work",
  catalog: { id: "product", schemaVersion: 1, defaultLocale: "en", locales: [{ tag: "en" }, { tag: "fr" }], layers: [{ name: "base", priority: 0 }] },
  catalogs: [],
  documents: [{ path, locale: "fr", layer: "base", isManifest: false, isMalformed: false, revision: "rev-1", content: saved, entries: [] }],
  diagnostics: [],
  success: true,
};

// A host that writes a message into its group as the editor host does (insert or replace `name = value`), recording
// each transform request.
function createHost() {
  const transforms = [];
  const transformDocument = async (document, content, key, value) => {
    transforms.push({ document, content, key, value });
    let next = content;
    if (key !== undefined) {
      const name = key.split("_").at(-1);
      const block = value.includes("\n") ? `  ${name} =\n${value.split("\n").map((line) => `    ${line}`).join("\n")}\n` : `  ${name} = ${value}\n`;
      const existing = new RegExp(`  ${name} =.*\\n(?:    .*\\n)*`);
      next = existing.test(content) ? content.replace(existing, block) : content.replace(/\}\n$/, `${block}}\n`);
    }
    return { success: true, content: next, entries: [], diagnostics: [] };
  };
  return {
    transforms,
    bridge: {
      transformDocument,
      applyAuthoringOperation: async () => { throw new Error("A starter must not use the document's authoring operations."); },
      applyDiagnosticFix: async () => { throw new Error("unused"); },
      createMessageDraft: (key, locale) => mockExecute("CreateMessageDraft", { key, locale }),
      applyMessageDraftOperation: (key, locale, text, expectedRevision, operation) =>
        mockExecute("ApplyMessageDraftOperation", { key, locale, text, expectedRevision, operation }),
    },
  };
}

function createStores() {
  const host = createHost();
  const values = new Map();
  const storage = { values, get: (key) => values.get(key) ?? null, set: (key, value) => values.set(key, value), remove: (key) => values.delete(key) };
  const drafts = new DraftStore({ bridge: host.bridge, snapshot: () => snapshot, storage, wait: () => Promise.resolve() });
  const starters = new MessageDraftStore({ bridge: host.bridge, drafts, wait: () => Promise.resolve() });
  return { host, storage, drafts, starters };
}

const target = { path, saved, key: "files_selected", locale: "fr" };
const record = () => { const list = []; return { list, push: (outcome) => list.push(outcome) }; };

// Create: the empty starter of the target locale (French: one, many, *), nothing in the document.
{
  const { host, drafts, starters } = createStores();
  const opening = starters.open(target);
  assert.equal(starters.isLoading(target), true, "A starter being created is loading.");
  const opened = await opening;
  assert.equal(opened.kind, "applied");
  assert.equal(starters.isLoading(target), false);
  const starter = starters.get(target);
  assert.equal(starter.text, ".input {$count :integer select=plural}\n.match $count\none {{}}\nmany {{}}\n* {{}}");
  assert.equal(starter.initial, starter.text);
  assert.deepEqual(starter.projection.variants.map((variant) => [variant.keys, variant.pattern]), [[["one"], ""], [["many"], ""], [["*"], ""]]);
  assert.equal(starter.projection.empty, true);
  assert.equal(starter.placed, false);
  assert.equal(drafts.isDirty(path), false, "An empty starter is not a change of the document.");
  assert.equal(host.transforms.length, 0);
  assert.equal(await starters.open(target), undefined, "An existing starter is kept, not created again.");
  assert.equal(starters.get({ path, key: "files_selected", locale: "en" }), undefined, "Starters are kept per locale.");

  // Operate: text in one variant makes it non-empty, and the full message is placed into the document's draft.
  const outcomes = record();
  const placements = record();
  await starters.apply(target, { kind: "set-pattern", variantId: "0", pattern: "Un fichier" }, outcomes.push, placements.push);
  assert.equal(outcomes.list.at(-1).kind, "applied");
  assert.equal(starters.get(target).projection.empty, false);
  assert.equal(starters.get(target).placed, true);
  await drafts.settledAll();
  assert.equal(placements.list.at(-1).kind, "applied");
  const typed = starters.get(target).text;
  assert.match(typed, /^one \{\{Un fichier\}\}$/m);
  assert.deepEqual(host.transforms.map(({ key, value }) => [key, value]), [["files_selected", typed]],
    "The document receives the key and the full message text through the transform path.");
  assert.equal(drafts.drafts[path], `files {\n  open = Ouvrir\n  selected =\n${typed.split("\n").map((line) => `    ${line}`).join("\n")}\n}\n`,
    "The message is inserted into its existing group.");
  assert.deepEqual(drafts.dirtyPaths, [path], "The placed starter is a normal unsaved document draft.");

  // More text updates the placed message; switching to another message and back keeps the starter.
  await starters.apply(target, { kind: "set-pattern", variantId: "2", pattern: "{$count} fichiers" }, outcomes.push, placements.push);
  await drafts.settledAll();
  assert.match(drafts.drafts[path], /    \* \{\{\{\$count\} fichiers\}\}\n/);
  assert.equal((drafts.drafts[path].match(/selected =/g) ?? []).length, 1, "An update replaces the placed message.");
  assert.equal(starters.get({ path, key: "files_selected", locale: "fr" }).text, starters.get(target).text);

  // Clear: back to empty, the message is taken out again and the document is clean (content-based dirty).
  await starters.apply(target, { kind: "set-pattern", variantId: "0", pattern: "" }, outcomes.push, placements.push);
  await starters.apply(target, { kind: "set-pattern", variantId: "2", pattern: " " }, outcomes.push, placements.push);
  await drafts.settledAll();
  assert.equal(starters.get(target).projection.empty, true);
  assert.equal(starters.get(target).placed, false);
  assert.equal(drafts.isDirty(path), false, "A starter cleared back to empty leaves the saved text.");
  assert.deepEqual(drafts.dirtyPaths, []);

  // A refused operation keeps the starter and reports why.
  const before = starters.get(target).text;
  await starters.apply(target, { kind: "remove-variant", variantId: "2" }, outcomes.push);
  assert.equal(outcomes.list.at(-1).kind, "rejected");
  assert.equal(starters.get(target).text, before);
}

// The recovery copy holds the placed starter; clearing it after another edit of the same document keeps that edit.
{
  const { drafts, starters, storage } = createStores();
  await starters.open(target);
  await starters.apply(target, { kind: "set-pattern", variantId: "0", pattern: "Un fichier" }, () => {});
  await drafts.settledAll();
  const stored = JSON.parse([...storage.values.values()][0]);
  assert.match(stored.documents[path].content, /one \{\{Un fichier\}\}/, "The recovery copy holds the starter's text.");
  await drafts.transform(path, saved, "files_open", "Ouvrir tout", () => {});
  await starters.apply(target, { kind: "set-pattern", variantId: "0", pattern: "" }, () => {});
  await drafts.settledAll();
  assert.equal(drafts.drafts[path], "files {\n  open = Ouvrir tout\n}\n", "Clearing the starter takes out only its message.");
}

// A pattern edit superseded before it ran is never placed, and nothing has to be taken out.
{
  const { host, drafts, starters } = createStores();
  await starters.open(target);
  const first = starters.apply(target, { kind: "set-pattern", variantId: "0", pattern: "U" }, () => {});
  const second = starters.apply(target, { kind: "set-pattern", variantId: "0", pattern: "" }, () => {});
  await Promise.all([first, second]);
  await drafts.settledAll();
  assert.equal(host.transforms.length, 0, "Only the latest edit of a variant runs.");
  assert.equal(drafts.isDirty(path), false);
}

// Copy source: each variant gets the base pattern with its keys or the catch-all; placeholders stay expressions; one
// change of the starter and one edit of the document.
{
  const { host, drafts, starters } = createStores();
  await starters.open(target);
  const outcomes = record();
  await starters.copySource(target, [{ keys: ["one"], pattern: "One file selected" }, { keys: ["*"], pattern: "{$count} files selected" }], outcomes.push);
  await drafts.settledAll();
  assert.equal(outcomes.list.length, 1);
  assert.equal(outcomes.list[0].kind, "applied");
  assert.equal(starters.get(target).text,
    ".input {$count :integer select=plural}\n.match $count\none {{One file selected}}\nmany {{{$count} files selected}}\n* {{{$count} files selected}}");
  assert.deepEqual(starters.get(target).projection.inputs.map((input) => input.name), ["count"], "The placeholder stays a variable.");
  assert.equal(host.transforms.length, 1, "Copy source is one edit of the document.");
  assert.equal(drafts.isDirty(path), true);
  // The untouched starter typed back into its MF2 source takes the copy out again, and the composer is empty again.
  starters.edit(target, starters.get(target).initial);
  await drafts.settledAll();
  assert.equal(drafts.isDirty(path), false);
  assert.equal(starters.get(target).projection.empty, true);
  assert.equal(starters.get(target).projection.revision, starters.get(target).initialProjection.revision);
  await starters.apply(target, { kind: "set-pattern", variantId: "0", pattern: "Un" }, outcomes.push);
  assert.equal(outcomes.list.at(-1).kind, "applied", "The composer edits the restored starter.");
}

// Typed MF2 source: placed unless it is empty or the untouched starter; once placed, the document owns the message.
{
  const { drafts, starters } = createStores();
  await starters.open(target);
  const initial = starters.get(target).initial;
  starters.edit(target, initial);
  await drafts.settledAll();
  assert.equal(drafts.isDirty(path), false, "The untouched starter text is not placed.");
  starters.edit(target, ".input {$count :integer select=plural}\n.match $count\none {{Un}}\nmany {{}}\n* {{}}");
  await drafts.settledAll();
  assert.match(drafts.drafts[path], /one \{\{Un\}\}/);
  assert.equal(starters.get(target), undefined, "A placed source edit hands the message over to the document.");
}

// Reset (discarded drafts, another workspace) forgets every starter, and an answer for a forgotten one is dropped.
{
  const { drafts, starters } = createStores();
  await starters.open(target);
  const outcomes = record();
  const pending = starters.apply(target, { kind: "set-pattern", variantId: "0", pattern: "Un" }, outcomes.push);
  starters.reset();
  await pending;
  await drafts.settledAll();
  assert.equal(starters.get(target), undefined);
  assert.deepEqual(outcomes.list, []);
  assert.equal(drafts.isDirty(path), false);
  await starters.open(target);
  starters.retain(({ key }) => key !== "files_selected");
  assert.equal(starters.get(target), undefined, "A starter whose message was saved is retired.");
}
console.log("PASS: a missing translation's starter is created empty for its locale, placed into the document once it has text, taken out when cleared, and filled from the source in one edit.");
