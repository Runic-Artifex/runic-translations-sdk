// The interchange dialog: XLIFF and review-file export and import, and the catalog import (.resx or i18next) whose dry
// run is applied once by its token. A dry run for other inputs must never be applied, and a token is spent before the
// host answers.
import assert from "node:assert/strict";
import { deferred, settle } from "./svelte-modules.mjs";

const { InterchangeController } = await import("../src/lib/state/interchange-controller.svelte.ts");
const { EditorFeedback } = await import("../src/lib/state/feedback.svelte.ts");

const snapshotOf = (review = { path: "product.review.json", revision: "review-1", entries: [], terminology: [] }) => ({
  root: "/work",
  catalog: { id: "product", schemaVersion: 1, defaultLocale: "en", locales: [{ tag: "en" }], layers: [] },
  catalogs: [],
  documents: [],
  review,
  history: [{ id: "old" }],
  diagnostics: [],
  success: true,
});

// A controller over fake collaborators. `calls` records every bridge call with its arguments, in order.
function fixture() {
  const calls = [];
  const bridge = {
    exportXliff: async (directory) => { calls.push(["exportXliff", directory]); return { ok: true, documents: [], losses: [], lossless: true }; },
    previewXliffImport: async (path) => { calls.push(["previewXliffImport", path]); return { ok: true, confirmationToken: "xliff-token", requiresIrreversibleConfirmation: false, changes: [] }; },
    applyXliffImport: async (token) => { calls.push(["applyXliffImport", token]); return { ok: true, message: undefined, snapshot: undefined }; },
    previewCatalogImport: async (request) => { calls.push(["previewCatalogImport", request]); return { ok: true, confirmationToken: "catalog-token" }; },
    applyCatalogImport: async (token) => { calls.push(["applyCatalogImport", token]); return { ok: true, snapshot: snapshotOf() }; },
    exportReviewJson: async (path) => { calls.push(["exportReviewJson", path]); return { ok: true, entryCount: 0 }; },
    previewReviewJsonImport: async (path) => { calls.push(["previewReviewJsonImport", path]); return { ok: true, confirmationToken: "review-token", requiresIrreversibleConfirmation: false, changes: [] }; },
    applyReviewJsonImport: async (token) => { calls.push(["applyReviewJsonImport", token]); return { ok: true, review: { path: "product.review.json", revision: "review-2", entries: [], terminology: [] }, history: [{ id: "new" }] }; },
  };
  const workspace = {
    snapshot: snapshotOf(),
    installed: [],
    install(next, resetSelection) { this.installed.push([next, resetSelection]); this.snapshot = next; },
  };
  const review = { installed: [], install(next) { this.installed.push(next); } };
  const unsaved = { prompts: [], confirmResult: true, confirmDiscard(key) { this.prompts.push(key); return this.confirmResult; } };
  const feedback = new EditorFeedback();
  const controller = new InterchangeController({ bridge, workspace, review, unsaved, feedback });
  return { controller, bridge, calls, workspace, review, unsaved, feedback };
}

// Each import keeps its own path, and the export a blank directory as undefined.
{
  const fx = fixture();
  const { controller, calls } = fx;
  controller.xliffDirectory = "  ./out  ";
  await controller.exportXliff();
  controller.xliffDirectory = "   ";
  await controller.exportXliff();
  assert.deepEqual(calls.filter((call) => call[0] === "exportXliff"), [["exportXliff", "./out"], ["exportXliff", undefined]]);
  assert.equal(controller.busy, false);
  assert.equal(controller.xliffExport.ok, true);

  controller.xliffImportPath = "  in.xlf  ";
  await controller.previewXliffImport();
  assert.deepEqual(calls.at(-1), ["previewXliffImport", "in.xlf"]);
  assert.equal(controller.xliffPreview.confirmationToken, "xliff-token");

  controller.xliffImportPath = "   ";
  await controller.previewXliffImport();
  assert.equal(calls.filter((call) => call[0] === "previewXliffImport").length, 1, "A blank path previews nothing.");
}

// A thrown export or preview is shown as a failed result, not as an exception.
{
  const fx = fixture();
  const { controller, bridge } = fx;
  bridge.exportXliff = async () => { throw new Error("no folder"); };
  await controller.exportXliff();
  assert.equal(controller.xliffExport.ok, false);
  assert.equal(controller.xliffExport.message.detail, "no folder");
  assert.equal(controller.busy, false);

  bridge.previewXliffImport = async () => { throw new Error("bad file"); };
  controller.xliffImportPath = "in.xlf";
  await controller.previewXliffImport();
  assert.equal(controller.xliffPreview.ok, false);
  assert.equal(controller.xliffPreview.message.detail, "bad file");
  assert.equal(controller.xliffPreview.requiresIrreversibleConfirmation, false);
}

// An XLIFF import is applied only with its preview's token and the user's confirmation; a failed apply keeps the preview.
{
  const fx = fixture();
  const { controller, bridge, calls, unsaved, feedback, workspace } = fx;
  await controller.applyXliffImport();
  assert.equal(calls.some((call) => call[0] === "applyXliffImport"), false, "Without a preview nothing is applied.");

  controller.xliffImportPath = "in.xlf";
  await controller.previewXliffImport();
  unsaved.confirmResult = false;
  await controller.applyXliffImport();
  assert.equal(calls.some((call) => call[0] === "applyXliffImport"), false, "A refused confirmation applies nothing.");
  assert.deepEqual(unsaved.prompts, ["ui_confirm_apply_xliff"]);

  unsaved.confirmResult = true;
  await controller.applyXliffImport();
  assert.deepEqual(calls.at(-1), ["applyXliffImport", "xliff-token"]);
  assert.equal(controller.xliffPreview, undefined, "A successful apply clears the preview.");
  assert.deepEqual(feedback.operationMessage, { code: "ui_feedback_xliff_applied", args: [] });
  assert.equal(workspace.installed.length, 0, "No snapshot is installed when the host returns none.");

  bridge.previewXliffImport = async () => ({ ok: true, confirmationToken: "t2", requiresIrreversibleConfirmation: false, changes: [] });
  controller.xliffImportPath = "in.xlf";
  await controller.previewXliffImport();
  bridge.applyXliffImport = async () => ({ ok: false, message: undefined, snapshot: undefined });
  await controller.applyXliffImport();
  assert.deepEqual(feedback.operationMessage, { code: "ui_feedback_xliff_failed", args: [] });
  assert.notEqual(controller.xliffPreview, undefined, "A failed apply keeps the preview.");
}

// A successful XLIFF apply that returns a snapshot installs it with the selection reset; a thrown apply is reported.
{
  const fx = fixture();
  const { controller, bridge, workspace, feedback } = fx;
  const next = snapshotOf();
  bridge.applyXliffImport = async () => ({ ok: true, message: { code: "ui_backend_xliff_partial", args: [] }, snapshot: next });
  controller.xliffImportPath = "in.xlf";
  await controller.previewXliffImport();
  await controller.applyXliffImport();
  assert.deepEqual(workspace.installed, [[next, true]]);
  assert.deepEqual(feedback.operationMessage, { code: "ui_backend_xliff_partial", args: [] });

  // The successful apply cleared the preview, so the thrown apply needs a fresh dry run.
  controller.xliffImportPath = "in.xlf";
  await controller.previewXliffImport();
  bridge.applyXliffImport = async () => { throw new Error("locked"); };
  await controller.applyXliffImport();
  assert.equal(feedback.operationMessage.code, "ui_backend_external_error");
  assert.equal(feedback.operationMessage.detail, "locked");
  assert.equal(controller.busy, false);
}

// The catalog import's dry run sends the trimmed source; the plan applies only while the inputs it was made for are unchanged.
{
  const fx = fixture();
  const { controller, calls } = fx;
  controller.startCatalogImport("i18next", "  ./strings.json  ");
  assert.equal(controller.open, true);
  assert.equal(controller.catalogImportPlan, undefined);

  await controller.previewCatalogImport();
  assert.deepEqual(calls.at(-1), ["previewCatalogImport", { from: "i18next", source: "./strings.json", overwrite: false }]);
  assert.equal(controller.catalogImportPlan.confirmationToken, "catalog-token");

  controller.catalogImportSource = "./strings.json   ";
  assert.notEqual(controller.catalogImportPlan, undefined, "Trailing spaces do not change the inputs.");

  controller.catalogImportOverwrite = true;
  assert.equal(controller.catalogImportPlan, undefined, "Changing overwrite invalidates the dry run.");

  controller.catalogImportOverwrite = false;
  assert.notEqual(controller.catalogImportPlan, undefined, "The dry run is valid again for its original inputs.");

  controller.catalogImportFrom = "resx";
  assert.equal(controller.catalogImportPlan, undefined, "Changing the format invalidates the dry run.");
}

// A dry run made with a different source is not applied: the apply does nothing.
{
  const fx = fixture();
  const { controller, calls } = fx;
  controller.startCatalogImport("resx", "a.resx");
  await controller.previewCatalogImport();
  controller.catalogImportSource = "b.resx";
  await controller.applyCatalogImport();
  assert.equal(calls.some((call) => call[0] === "applyCatalogImport"), false);
}

// The token is spent before the host answers: during the apply the plan is gone, and a second apply does nothing.
{
  const fx = fixture();
  const { controller, bridge, calls } = fx;
  const committed = deferred();
  let seenDuringApply;
  bridge.applyCatalogImport = (token) => {
    calls.push(["applyCatalogImport", token]);
    seenDuringApply = controller.catalogImportPlan;
    return committed.promise;
  };
  controller.startCatalogImport("resx", "a.resx");
  await controller.previewCatalogImport();
  const pending = controller.applyCatalogImport();
  await settle();
  assert.equal(controller.busy, true);
  assert.equal(seenDuringApply, undefined, "The token is cleared before the host answers.");
  await controller.applyCatalogImport();
  assert.equal(calls.filter((call) => call[0] === "applyCatalogImport").length, 1);
  committed.resolve({ ok: true, snapshot: snapshotOf() });
  await pending;
  assert.equal(controller.busy, false);
}

// An applied catalog import installs the new snapshot with the selection reset and reports it; a refused one reports the
// import messages as invalid, and a thrown one as an external error.
{
  const fx = fixture();
  const { controller, bridge, workspace, feedback } = fx;
  const next = snapshotOf();
  bridge.applyCatalogImport = async () => ({ ok: true, snapshot: next });
  controller.startCatalogImport("resx", "a.resx");
  await controller.previewCatalogImport();
  await controller.applyCatalogImport();
  assert.deepEqual(workspace.installed, [[next, true]]);
  assert.deepEqual(feedback.operationMessage, { code: "ui_feedback_catalog_imported", args: [] });

  bridge.applyCatalogImport = async () => ({ ok: false, snapshot: undefined });
  await controller.previewCatalogImport();
  await controller.applyCatalogImport();
  assert.deepEqual(feedback.operationMessage, { code: "ui_backend_import_messages_invalid", args: [] });
  assert.equal(workspace.installed.length, 1, "A refused import installs nothing.");

  bridge.applyCatalogImport = async () => { throw new Error("parse failed"); };
  await controller.previewCatalogImport();
  await controller.applyCatalogImport();
  assert.equal(feedback.operationMessage.detail, "parse failed");
}

// An import the user declined leaves the plan in place and calls nothing.
{
  const fx = fixture();
  const { controller, calls, unsaved } = fx;
  controller.startCatalogImport("resx", "a.resx");
  await controller.previewCatalogImport();
  unsaved.confirmResult = false;
  await controller.applyCatalogImport();
  assert.equal(calls.some((call) => call[0] === "applyCatalogImport"), false);
  assert.deepEqual(unsaved.prompts, ["ui_confirm_apply_catalog_import"]);
  assert.notEqual(controller.catalogImportPlan, undefined);
}

// A thrown dry run reports its error and clears the busy state.
{
  const fx = fixture();
  const { controller, bridge, feedback } = fx;
  bridge.previewCatalogImport = async () => { throw new Error("missing file"); };
  controller.startCatalogImport("resx", "a.resx");
  await controller.previewCatalogImport();
  assert.equal(feedback.operationMessage.detail, "missing file");
  assert.equal(controller.catalogImportPlan, undefined);
  assert.equal(controller.busy, false);
}

// The review file: export with a trimmed path, and a dry run; a thrown export or dry run is reported as a failed result.
{
  const fx = fixture();
  const { controller, calls, bridge } = fx;
  controller.reviewExportPath = "  review.json  ";
  await controller.exportReviewJson();
  assert.deepEqual(calls.at(-1), ["exportReviewJson", "review.json"]);

  controller.reviewImportPath = "  in.review.json  ";
  await controller.previewReviewJsonImport();
  assert.deepEqual(calls.at(-1), ["previewReviewJsonImport", "in.review.json"]);
  assert.equal(controller.reviewPreview.confirmationToken, "review-token");

  bridge.exportReviewJson = async () => { throw new Error("denied"); };
  await controller.exportReviewJson();
  assert.equal(controller.reviewExport.ok, false);
  assert.equal(controller.reviewExport.entryCount, 0);

  bridge.previewReviewJsonImport = async () => { throw new Error("corrupt"); };
  await controller.previewReviewJsonImport();
  assert.equal(controller.reviewPreview.ok, false);
  assert.equal(controller.reviewPreview.message.detail, "corrupt");
}

// An applied review import merges the new review and history into the open workspace, hands the review to the store and
// clears the preview.
{
  const fx = fixture();
  const { controller, workspace, review, feedback } = fx;
  const before = workspace.snapshot;
  controller.reviewImportPath = "in.review.json";
  await controller.previewReviewJsonImport();
  await controller.applyReviewJsonImport();
  assert.equal(workspace.snapshot.review.revision, "review-2");
  assert.deepEqual(workspace.snapshot.history, [{ id: "new" }]);
  assert.equal(workspace.snapshot.catalog, before.catalog, "The rest of the snapshot is kept.");
  assert.equal(review.installed.length, 1);
  assert.equal(review.installed[0], workspace.snapshot);
  assert.equal(controller.reviewPreview, undefined);
  assert.deepEqual(feedback.operationMessage, { code: "ui_feedback_review_applied", args: [] });
}

// A failed review import keeps the preview and the workspace; a missing history keeps the old one.
{
  const fx = fixture();
  const { controller, bridge, workspace, review, feedback } = fx;
  const before = workspace.snapshot;
  controller.reviewImportPath = "in.review.json";
  await controller.previewReviewJsonImport();
  bridge.applyReviewJsonImport = async () => ({ ok: false, message: undefined });
  await controller.applyReviewJsonImport();
  assert.equal(workspace.snapshot, before);
  assert.equal(review.installed.length, 0);
  assert.notEqual(controller.reviewPreview, undefined);
  assert.deepEqual(feedback.operationMessage, { code: "ui_feedback_review_failed", args: [] });

  bridge.applyReviewJsonImport = async () => ({ ok: true, review: { path: "p", revision: "r3", entries: [], terminology: [] } });
  await controller.applyReviewJsonImport();
  assert.deepEqual(workspace.snapshot.history, [{ id: "old" }], "An absent history keeps the current one.");
}

// A declined confirmation or a thrown apply of the review import leaves the workspace unchanged.
{
  const fx = fixture();
  const { controller, bridge, workspace, unsaved, feedback, calls } = fx;
  controller.reviewImportPath = "in.review.json";
  await controller.previewReviewJsonImport();
  unsaved.confirmResult = false;
  await controller.applyReviewJsonImport();
  assert.equal(calls.some((call) => call[0] === "applyReviewJsonImport"), false);
  assert.deepEqual(unsaved.prompts, ["ui_confirm_apply_review"]);

  unsaved.confirmResult = true;
  bridge.applyReviewJsonImport = async () => { throw new Error("io"); };
  const before = workspace.snapshot;
  await controller.applyReviewJsonImport();
  assert.equal(workspace.snapshot, before);
  assert.equal(feedback.operationMessage.detail, "io");
  assert.equal(controller.busy, false);
}

console.log("PASS: the interchange controller runs XLIFF and review exports and imports, applies a catalog import only with the dry run made for its current inputs, and spends each token before the host answers.");
