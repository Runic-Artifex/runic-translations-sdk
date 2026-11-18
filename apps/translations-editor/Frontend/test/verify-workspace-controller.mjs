// The open workspace: loading and installing a snapshot into the stores, opening another project with the unsaved-work
// confirmation, the recent projects, the one save's notices, and the recovery of a transaction and of earlier work.
import assert from "node:assert/strict";
import { deferred, settle } from "./svelte-modules.mjs";

const { EditorFeedback } = await import("../src/lib/state/feedback.svelte.ts");
const { WorkspaceController, readRecentProjects, recentProjectsStateKey } = await import("../src/lib/state/workspace-controller.svelte.ts");
const { RecoveryController } = await import("../src/lib/state/recovery-controller.svelte.ts");

const snapshotOf = (root = "/work", catalogId = "product") => ({
  root,
  catalog: catalogId === null ? undefined : { id: catalogId, schemaVersion: 1, defaultLocale: "en", locales: [{ tag: "en" }], layers: [] },
  catalogs: [],
  documents: [{ path: "en.rmf2", locale: "en", isManifest: false, entries: [{ key: "greeting" }] }, { path: "fr/welcome.mf2", locale: "fr", isManifest: false }],
  diagnostics: [],
  success: true,
});

function createWorkspace(options = {}) {
  const calls = [];
  const values = new Map(options.stored ?? []);
  let unsaved = options.unsaved ?? false;
  let confirmAnswer = options.confirm ?? true;
  const bridge = {
    load: options.load ?? (async () => { calls.push("load"); return snapshotOf(); }),
    openWorkspace: options.openWorkspace ?? (async (request) => { calls.push(`open ${request.directory} ${request.catalogId ?? ""}`.trim()); return { ok: true, snapshot: snapshotOf(request.directory) }; }),
    pickWorkspace: options.pickWorkspace ?? (async () => ({ ok: true, directory: "/picked" })),
  };
  const retained = [];
  const drafts = {
    nextWorkspace: () => calls.push("drafts.next"),
    reset: () => calls.push("drafts.reset"),
    prune: () => calls.push("drafts.prune"),
  };
  const messageDrafts = {
    reset: () => calls.push("starters.reset"),
    retain: (keep) => { calls.push("starters.retain"); retained.push(keep); },
  };
  const review = { dirty: options.reviewDirty ?? false, reset: () => calls.push("review.reset"), install: () => calls.push("review.install") };
  const diagnostics = { clear: () => calls.push("diagnostics.clear") };
  const save = { saving: false, saveAll: options.saveAll ?? (async () => ({ kind: "nothing" })) };
  const selection = {
    install: (next, reset) => calls.push(`selection.install ${next.root} ${reset}`),
    rememberCatalogView: () => calls.push("selection.remember"),
    configureEditor: () => calls.push("selection.configure"),
  };
  const unsavedWork = {
    get hasUnsavedWork() { return unsaved; },
    confirmDiscard: (key) => { calls.push(`confirm ${key}`); return !unsaved || confirmAnswer; },
    discard: () => { calls.push("discard"); unsaved = false; },
  };
  const feedback = new EditorFeedback();
  const reloaded = [];
  const workspace = new WorkspaceController({
    bridge, drafts, messageDrafts, review, diagnostics, save, selection, unsaved: unsavedWork, feedback,
    historyBusy: () => options.historyBusy ?? false,
    reloaded: (kind) => reloaded.push(kind),
    storage: { get: (key) => values.get(key) ?? null, set: (key, value) => values.set(key, value) },
    now: () => new Date("2026-11-18T10:00:00Z"),
  });
  return { workspace, calls, values, retained, reloaded, feedback, setConfirm: (value) => { confirmAnswer = value; }, setUnsaved: (value) => { unsaved = value; } };
}

// Loading installs the snapshot into every store, places the selection and remembers the project.
{
  const { workspace, calls, values, reloaded, feedback } = createWorkspace();
  feedback.clientError = "old";
  assert.equal(workspace.loading, true, "The page starts loading.");
  workspace.projectReloadPending = true;
  await workspace.load(false);
  assert.equal(workspace.loading, false);
  assert.equal(workspace.snapshot.root, "/work");
  assert.equal(feedback.clientError, undefined);
  assert.equal(workspace.projectReloadPending, false);
  assert.deepEqual(reloaded, ["load"]);
  assert.deepEqual(calls, ["load", "drafts.next", "drafts.reset", "starters.reset", "review.reset", "diagnostics.clear", "selection.install /work true"]);
  assert.deepEqual(JSON.parse(values.get(recentProjectsStateKey)), [{ root: "/work", catalogId: "product", openedAt: "2026-11-18T10:00:00.000Z" }]);

  // Installing a saved workspace keeps the unsaved work that still applies: starters of messages the files now have go.
  calls.length = 0;
  workspace.install(snapshotOf(), false);
  assert.deepEqual(calls, ["drafts.next", "drafts.prune", "starters.retain", "review.install", "diagnostics.clear", "selection.install /work false"]);
  assert.equal(workspace.savedHasMessage("greeting", "en"), true);
  assert.equal(workspace.savedHasMessage("welcome", "fr"), true, "An MF2 file is the message its name says.");
  assert.equal(workspace.savedHasMessage("farewell", "en"), false);
  assert.equal(workspace.recentProjects.length, 1, "A project is listed once.");
}

// Unsaved review changes stay on top of the data they started from; a failed load is reported.
{
  const { workspace, calls, feedback } = createWorkspace({ reviewDirty: true, load: async () => { throw new Error("host gone"); } });
  workspace.install(snapshotOf(), true);
  assert.ok(!calls.includes("review.reset") && !calls.includes("review.install"));
  await workspace.load(false);
  assert.equal(feedback.clientError.code, "ui_backend_external_error");
  assert.equal(feedback.clientError.detail, "host gone");
  assert.equal(workspace.loading, false);
}

// Reloading over unsaved work needs the user's confirmation, and drops the work only then.
{
  const { workspace, calls, setConfirm } = createWorkspace({ unsaved: true });
  await workspace.load(false);
  assert.ok(!calls.includes("load"), "A reload that did not ask leaves unsaved work alone.");
  setConfirm(false);
  await workspace.load(true);
  assert.deepEqual(calls, ["confirm ui_confirm_reload_workspace"]);
  setConfirm(true);
  await workspace.load(true);
  assert.deepEqual(calls.slice(1, 4), ["confirm ui_confirm_reload_workspace", "discard", "load"]);
}

// Opening another workspace remembers this catalog's view first; an empty directory is refused.
{
  const { workspace, calls, reloaded, feedback } = createWorkspace();
  workspace.openDirectory = "  ";
  await workspace.open();
  assert.equal(feedback.clientError.code, "ui_feedback_workspace_directory_required");
  await workspace.pick();
  assert.equal(workspace.openDirectory, "/picked");
  workspace.openDialogOpen = true;
  await workspace.open();
  assert.equal(workspace.snapshot.root, "/picked");
  assert.equal(workspace.openDialogOpen, false);
  assert.deepEqual(reloaded, ["open"]);
  assert.deepEqual(calls.filter((call) => call.startsWith("open") || call === "selection.remember"), ["selection.remember", "selection.remember", "open /picked"]);
  // A catalog of the open root, and a recent project's directory.
  await workspace.open("other");
  assert.ok(calls.includes("open /picked other"));
  await workspace.open("product", "/recent");
  assert.equal(workspace.snapshot.root, "/recent");
  assert.deepEqual(workspace.recentProjects.map((project) => project.root), ["/recent", "/picked"]);

  const pending = deferred();
  const busy = createWorkspace({ openWorkspace: () => pending.promise });
  busy.workspace.openDirectory = "/slow";
  const first = busy.workspace.open();
  assert.equal(busy.workspace.openingWorkspace, true);
  await busy.workspace.open();
  pending.resolve({ ok: false, message: { code: "ui_backend_workspace_missing", args: [] } });
  await first;
  assert.equal(busy.feedback.clientError.code, "ui_backend_workspace_missing");
  assert.equal(busy.workspace.openingWorkspace, false);
}

// The open dialog starts from the open workspace's root.
{
  const { workspace } = createWorkspace();
  workspace.showOpenDialog();
  assert.equal(workspace.openDialogOpen, false, "Without a workspace there is nothing to start from.");
  workspace.install(snapshotOf("/root"), true);
  workspace.showOpenDialog();
  assert.equal(workspace.openDirectory, "/root");
  assert.equal(workspace.openDialogOpen, true);
}

// The one save reports its outcome; written documents show the selection again.
for (const [outcome, expected] of [
  [{ kind: "saved", saved: ["en.rmf2"], newer: false, message: { code: "app_saved", args: [] } }, { operation: "app_saved", configured: true }],
  [{ kind: "saved", saved: [], newer: true, message: { code: "app_saved", args: [] } }, { operation: "ui_feedback_newer_draft", configured: false }],
  [{ kind: "refused", saved: [], path: "en.rmf2", message: { code: "ui_backend_document_conflict", args: [] } }, { error: "ui_backend_document_conflict", configured: false }],
  [{ kind: "review-failed", saved: ["en.rmf2"], newer: true, message: { code: "ui_feedback_review_failed", args: [] } }, { error: "ui_feedback_review_failed", operation: "ui_feedback_newer_draft", configured: true }],
  [{ kind: "failed", saved: [], error: new Error("disk full") }, { error: "ui_backend_external_error", configured: false }],
  [{ kind: "invalid", path: "en.rmf2", result: { success: false, diagnostics: [] } }, { configured: false }],
]) {
  const { workspace, calls, feedback } = createWorkspace({ saveAll: async () => outcome });
  feedback.operationMessage = { code: "old", args: [] };
  await workspace.saveAll();
  assert.equal(feedback.operationMessage?.code, expected.operation, `${outcome.kind}: operation notice`);
  assert.equal(feedback.clientError?.code, expected.error, `${outcome.kind}: error notice`);
  assert.equal(calls.includes("selection.configure"), expected.configured, `${outcome.kind}: selection shown again`);
}
{
  const { workspace, calls } = createWorkspace({ historyBusy: true, saveAll: async () => { calls.push("save"); return { kind: "nothing" }; } });
  await workspace.saveAll();
  assert.ok(!calls.includes("save"), "A save waits while a saved change is undone.");
}

// Recent projects come from the native state; an unreadable record is none, and at most eight are kept.
{
  assert.deepEqual(readRecentProjects("{not json"), []);
  assert.deepEqual(readRecentProjects('{"root":"/w"}'), []);
  assert.deepEqual(readRecentProjects(JSON.stringify([{ root: "/w", catalogId: "c", openedAt: "x" }, { root: 1 }])), [{ root: "/w", catalogId: "c", openedAt: "x" }]);
  const many = Array.from({ length: 12 }, (_, index) => ({ root: `/w${index}`, catalogId: "c", openedAt: "x" }));
  assert.equal(readRecentProjects(JSON.stringify(many)).length, 8);
  const { workspace } = createWorkspace({ stored: [[recentProjectsStateKey, JSON.stringify(many)]] });
  workspace.loadRecentProjects();
  assert.equal(workspace.recentProjects.length, 8);
  workspace.install(snapshotOf("/new"), true);
  assert.equal(workspace.recentProjects[0].root, "/new");
  assert.equal(workspace.recentProjects.length, 8);
  workspace.install(snapshotOf("/scan", null), true);
  assert.equal(workspace.recentProjects[0].root, "/new", "A workspace without a catalog is not a recent project.");
}

// Recovery: a transaction completed with its snapshot, or one that leaves the workspace to be reloaded.
{
  const results = [
    { ok: false, message: { code: "ui_backend_recovery_refused", args: [] } },
    { ok: true, snapshot: snapshotOf("/recovered") },
    { ok: true },
  ];
  const requests = [];
  const { workspace, feedback } = createWorkspace();
  workspace.install(snapshotOf("/work"), true);
  const recovery = new RecoveryController({
    bridge: { recoverTransaction: async (mode) => { requests.push(mode); return results.shift(); } },
    workspace,
    drafts: { recovered: {}, drafts: {}, restoreRecovered() {}, discardRecovered() {}, parse: async () => false },
    review: { recovered: undefined, restoreRecovered() {}, discardRecovered() {} },
    selection: { configureEditor() {}, selectedDocumentPath: "" },
    feedback,
    historyBusy: () => false,
  });
  await recovery.recoverTransaction("rollback");
  assert.equal(feedback.clientError.code, "ui_backend_recovery_refused");
  await recovery.recoverTransaction("complete");
  assert.equal(workspace.snapshot.root, "/recovered");
  assert.equal(feedback.operationMessage.code, "ui_feedback_recovery_completed");
  await recovery.recoverTransaction("rollback");
  assert.equal(recovery.reloadRequired, true, "Without a snapshot the page asks for a reload.");
  assert.equal(recovery.reloadMessage.code, "ui_feedback_recovery_reload");
  assert.equal(workspace.openDirectory, "/recovered", "The open dialog starts from the recovered root.");
  assert.equal(workspace.snapshot.root, "/work", "The reload ran (the fake host loads /work).");
  assert.deepEqual(requests, ["rollback", "complete", "rollback"]);
  recovery.reloaded();
  assert.equal(recovery.reloadRequired, false);
  assert.equal(recovery.busy, false);
}

// Recovered work: restored drafts are shown, and parsed again; a failed parse is reported.
{
  const calls = [];
  const feedback = new EditorFeedback();
  const drafts = {
    recovered: { "en.rmf2": { content: "x", baseRevision: "rev-1" } },
    drafts: {},
    restoreRecovered() { calls.push("drafts.restore"); this.drafts = { "en.rmf2": "x", "de.rmf2": "y" }; },
    discardRecovered() { calls.push("drafts.discard"); },
    parse: async (path) => { calls.push(`parse ${path}`); if (path === "de.rmf2") throw new Error("bad"); return true; },
  };
  const review = { recovered: undefined, restoreRecovered() { calls.push("review.restore"); }, discardRecovered() { calls.push("review.discard"); } };
  const recovery = new RecoveryController({
    bridge: {}, workspace: {}, drafts, review, feedback, historyBusy: () => false,
    selection: { configureEditor: () => calls.push("configure"), selectedDocumentPath: "en.rmf2" },
  });
  assert.equal(recovery.hasRecoveredWork, true);
  recovery.restoreRecoveredWork();
  await settle();
  assert.deepEqual(calls, ["drafts.restore", "review.restore", "configure", "parse en.rmf2", "parse de.rmf2", "configure"]);
  assert.equal(feedback.clientError.detail, "bad");
  recovery.discardRecoveredWork();
  assert.deepEqual(calls.slice(-2), ["drafts.discard", "review.discard"]);
}

console.log("PASS: the workspace controller installs snapshots into the stores, confirms dropping unsaved work, opens and remembers projects, reports the save's outcome; recovery completes transactions and restores earlier work.");
