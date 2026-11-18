// The new-project wizard: its defaults, each step's validation, the review step's plan, and creating the project with an
// optional catalog import that opens its dry run.
import assert from "node:assert/strict";
import { deferred, settle } from "./svelte-modules.mjs";

const { ProjectWizardController, projectWizardSteps } = await import("../src/lib/state/project-wizard.svelte.ts");
const { EditorFeedback } = await import("../src/lib/state/feedback.svelte.ts");

// A wizard over fake collaborators; `host` decides what the bridge and the unsaved-work confirmation answer, and `calls`
// records every collaborator call in order.
function fixture() {
  const calls = [];
  const installed = [];
  const host = {
    historyBusy: false,
    confirmResult: true,
    hasUnsavedWork: false,
    previewProject: async () => ({ ok: true, files: [] }),
    createProject: async () => ({ ok: true, snapshot: { root: "/work/product" } }),
  };
  const unsaved = {
    get hasUnsavedWork() { return host.hasUnsavedWork; },
    confirmDiscard(key) { calls.push(`confirm ${key}`); return host.confirmResult; },
    discard() { calls.push("discard"); },
  };
  const interchange = {
    startCatalogImport(from, source) { calls.push(`startCatalogImport ${from} ${source}`); },
    async previewCatalogImport() { calls.push("previewCatalogImport"); },
  };
  const feedback = new EditorFeedback();
  const wizard = new ProjectWizardController({
    bridge: {
      previewProject: async (request) => { calls.push("previewProject"); return host.previewProject(request); },
      createProject: async (request) => { calls.push("createProject"); return host.createProject(request); },
    },
    unsaved,
    feedback,
    install(next) { calls.push("install"); installed.push(next); },
    interchange,
    historyBusy: () => host.historyBusy,
  });
  return { wizard, host, calls, installed, feedback };
}

// Opens the wizard with answers that pass every step, then advances to the review step with an ok plan.
async function atReview(fx) {
  fx.wizard.start();
  fx.wizard.directory = "/work/product";
  for (let i = 0; i < 4; i += 1) await fx.wizard.advance();
  assert.equal(fx.wizard.step, projectWizardSteps);
  fx.calls.length = 0;
}

// start() opens on step 1 with the default answers, after clearing whatever the last session left behind.
{
  const fx = fixture();
  const { wizard } = fx;
  wizard.step = 3;
  wizard.importFrom = "resx";
  wizard.importSource = "old.resx";
  wizard.directory = "/old";
  wizard.catalog = "old";
  wizard.defaultLocale = "de";
  wizard.locales = [{ id: 9, tag: "fr", fallback: "de" }];
  wizard.namespace = "Old.Ns";
  wizard.className = "Old";
  wizard.includeStarter = false;
  wizard.plan = { ok: true, files: [] };
  wizard.error = "stale";
  wizard.busy = true;
  wizard.start();
  assert.equal(wizard.open, true);
  assert.deepEqual(
    [wizard.step, wizard.importFrom, wizard.importSource, wizard.directory, wizard.catalog, wizard.defaultLocale,
      wizard.locales, wizard.namespace, wizard.className, wizard.includeStarter, wizard.plan, wizard.error, wizard.busy],
    [1, "", "", "", "product", "en", [], "Customer.Product", "ProductText", true, undefined, undefined, false],
  );
}

// While history is busy, the wizard does not open or change.
{
  const fx = fixture();
  fx.host.historyBusy = true;
  fx.wizard.start();
  assert.equal(fx.wizard.open, false);
  fx.wizard.directory = "/work";
  await fx.wizard.advance();
  assert.equal(fx.wizard.step, 1);
  assert.deepEqual(fx.calls, []);
}

// Step 1 needs a directory and a catalog id; a blank value names the missing answer and keeps the step.
{
  const fx = fixture();
  const { wizard } = fx;
  wizard.start();
  assert.equal(wizard.validateStep(), false);
  assert.deepEqual(wizard.error, { code: "ui_feedback_project_directory_required", args: [] });
  wizard.directory = "/work";
  wizard.catalog = "   ";
  assert.equal(wizard.validateStep(), false);
  assert.equal(wizard.error.code, "ui_feedback_project_directory_required");
  wizard.catalog = " product ";
  assert.equal(wizard.validateStep(), true);
  assert.equal(wizard.error, undefined);
}

// Step 2 needs every language tag, and no two tags may match case-insensitively (after trimming).
{
  const fx = fixture();
  const { wizard } = fx;
  wizard.start();
  wizard.step = 2;
  wizard.defaultLocale = " ";
  assert.equal(wizard.validateStep(), false);
  assert.equal(wizard.error.code, "ui_feedback_locale_required");

  wizard.defaultLocale = "en";
  wizard.addLocale();
  assert.equal(wizard.validateStep(), false, "A new locale row with no tag is incomplete.");
  assert.equal(wizard.error.code, "ui_feedback_locale_required");

  wizard.locales[0].tag = "EN";
  assert.equal(wizard.validateStep(), false);
  assert.equal(wizard.error.code, "ui_feedback_locale_duplicate", "The duplicate check ignores case.");

  wizard.locales[0].tag = " de-CH ";
  assert.equal(wizard.validateStep(), true, "Tags are compared after trimming.");
  assert.equal(wizard.error, undefined);

  wizard.locales[0].tag = " EN-us ";
  wizard.defaultLocale = "en-US";
  assert.equal(wizard.validateStep(), false);
  assert.equal(wizard.error.code, "ui_feedback_locale_duplicate");
}

// Step 3 needs the namespace and the class name.
{
  const fx = fixture();
  const { wizard } = fx;
  wizard.start();
  wizard.step = 3;
  wizard.namespace = "";
  assert.equal(wizard.validateStep(), false);
  assert.equal(wizard.error.code, "ui_feedback_namespace_required");
  wizard.namespace = "Customer.Product";
  wizard.className = "  ";
  assert.equal(wizard.validateStep(), false);
  assert.equal(wizard.error.code, "ui_feedback_namespace_required");
  wizard.className = "ProductText";
  assert.equal(wizard.validateStep(), true);
}

// Step 4 needs an import source only when an import format is chosen.
{
  const fx = fixture();
  const { wizard } = fx;
  wizard.start();
  wizard.step = 4;
  assert.equal(wizard.validateStep(), true, "No import chosen: no source is needed.");
  wizard.importFrom = "i18next";
  wizard.importSource = "   ";
  assert.equal(wizard.validateStep(), false);
  assert.equal(wizard.error.code, "ui_backend_import_source_required");
  wizard.importSource = "./strings.json";
  assert.equal(wizard.validateStep(), true);
}

// advance() moves through steps 1 to 4 without asking the host; the step after the import asks for the plan and
// shows the review step.
{
  const fx = fixture();
  const { wizard } = fx;
  wizard.start();
  wizard.directory = "/work/product";
  await wizard.advance();
  await wizard.advance();
  await wizard.advance();
  assert.equal(wizard.step, 4);
  assert.deepEqual(fx.calls, [], "Steps 1 to 3 ask the host for nothing.");
  await wizard.advance();
  assert.deepEqual(fx.calls, ["previewProject"]);
  assert.equal(wizard.step, projectWizardSteps);
  assert.equal(wizard.busy, false);
  assert.deepEqual(wizard.plan, { ok: true, files: [] });
  assert.equal(wizard.error, undefined);
}

// The host's plan request is the one the request() builder describes.
{
  const fx = fixture();
  const { wizard } = fx;
  let seen;
  fx.host.previewProject = async (request) => { seen = request; return { ok: true }; };
  wizard.start();
  wizard.directory = "/work/product";
  for (let i = 0; i < 4; i += 1) await wizard.advance();
  assert.equal(seen.directory, "/work/product");
  assert.equal(seen.catalogId, "product");
  assert.deepEqual(seen.additionalLocales, []);
}

// A plan the host refuses stays on the import step and shows the host's message; without one, a generic notice.
{
  const fx = fixture();
  const { wizard } = fx;
  wizard.start();
  wizard.directory = "/work/product";
  for (let i = 0; i < 3; i += 1) await wizard.advance();
  fx.host.previewProject = async () => ({ ok: false, message: { code: "ui_backend_project_exists", args: [] } });
  await wizard.advance();
  assert.equal(wizard.step, 4);
  assert.deepEqual(wizard.error, { code: "ui_backend_project_exists", args: [] });
  assert.equal(wizard.busy, false);

  fx.host.previewProject = async () => ({ ok: false });
  await wizard.advance();
  assert.equal(wizard.error.code, "ui_feedback_project_invalid");
}

// A host error during the plan request is shown as the error, and the wizard stays on the import step.
{
  const fx = fixture();
  const { wizard } = fx;
  wizard.start();
  wizard.directory = "/work/product";
  for (let i = 0; i < 3; i += 1) await wizard.advance();
  fx.host.previewProject = async () => { throw new Error("host offline"); };
  await wizard.advance();
  assert.equal(wizard.step, 4);
  assert.equal(wizard.error.code, "ui_backend_external_error");
  assert.equal(wizard.error.detail, "host offline");
  assert.equal(wizard.busy, false);
}

// A failing step blocks advance() before the host is asked; the plan request is busy until the host answers.
{
  const fx = fixture();
  const { wizard } = fx;
  wizard.start();
  await wizard.advance();
  assert.equal(wizard.step, 1, "The directory is missing, so the wizard stays on step 1.");
  assert.equal(wizard.error.code, "ui_feedback_project_directory_required");
  assert.deepEqual(fx.calls, []);

  wizard.directory = "/work/product";
  for (let i = 0; i < 3; i += 1) await wizard.advance();
  const plan = deferred();
  fx.host.previewProject = () => plan.promise;
  const pending = wizard.advance();
  await settle();
  assert.equal(wizard.busy, true);
  plan.resolve({ ok: true, files: [] });
  await pending;
  assert.equal(wizard.busy, false);
  assert.equal(wizard.step, projectWizardSteps);
}

// History being busy blocks the advance from the import step: the host is not asked.
{
  const fx = fixture();
  const { wizard } = fx;
  wizard.start();
  wizard.directory = "/work/product";
  for (let i = 0; i < 3; i += 1) await wizard.advance();
  fx.host.historyBusy = true;
  await wizard.advance();
  assert.equal(wizard.step, 4);
  assert.deepEqual(fx.calls, []);
}

// request() trims the answers, maps an empty fallback to undefined, and carries the starter choice.
{
  const fx = fixture();
  const { wizard } = fx;
  wizard.start();
  wizard.directory = "  /work/product  ";
  wizard.catalog = " product ";
  wizard.defaultLocale = " en ";
  wizard.locales = [
    { id: 1, tag: " fr ", fallback: "   " },
    { id: 2, tag: "de", fallback: " en " },
  ];
  wizard.namespace = " Customer.Product ";
  wizard.className = " ProductText ";
  wizard.includeStarter = false;
  assert.deepEqual(wizard.request(), {
    directory: "/work/product",
    catalogId: "product",
    defaultLocale: "en",
    additionalLocales: [{ tag: "fr", fallback: undefined }, { tag: "de", fallback: "en" }],
    codeNamespace: "Customer.Product",
    className: "ProductText",
    includeStarterMessage: false,
  });
}

// create() does nothing without a plan the host accepted.
{
  const fx = fixture();
  const { wizard } = fx;
  wizard.start();
  wizard.directory = "/work/product";
  await wizard.create();
  assert.deepEqual(fx.calls, [], "Without a plan nothing is confirmed or created.");
  assert.equal(wizard.open, true);

  for (let i = 0; i < 3; i += 1) await wizard.advance();
  fx.host.previewProject = async () => ({ ok: false, message: { code: "ui_backend_project_exists", args: [] } });
  await wizard.advance();
  fx.calls.length = 0;
  await wizard.create();
  assert.deepEqual(fx.calls, [], "A refused plan cannot be created.");
}

// Refused confirmation: nothing is discarded, created or installed, and the wizard stays open.
{
  const fx = fixture();
  await atReview(fx);
  fx.host.hasUnsavedWork = true;
  fx.host.confirmResult = false;
  await fx.wizard.create();
  assert.deepEqual(fx.calls, ["confirm ui_confirm_create_project"]);
  assert.equal(fx.wizard.open, true);
  assert.equal(fx.wizard.busy, false);
  assert.deepEqual(fx.installed, []);
}

// Created: unsaved work is discarded after confirmation, the snapshot is installed, the success message is shown, and the
// wizard closes. Without an import, no import is started.
{
  const fx = fixture();
  await atReview(fx);
  fx.host.hasUnsavedWork = true;
  await fx.wizard.create();
  assert.deepEqual(fx.calls, ["confirm ui_confirm_create_project", "discard", "createProject", "install"]);
  assert.deepEqual(fx.installed, [{ root: "/work/product" }]);
  assert.equal(fx.wizard.open, false);
  assert.equal(fx.wizard.busy, false);
  assert.deepEqual(fx.wizard.error, undefined);
  assert.deepEqual(fx.feedback.operationMessage, { code: "ui_feedback_project_created", args: [] });
}

// Without unsaved work there is nothing to discard.
{
  const fx = fixture();
  await atReview(fx);
  await fx.wizard.create();
  assert.deepEqual(fx.calls, ["confirm ui_confirm_create_project", "createProject", "install"]);
}

// With an import chosen, the created project's import dry run opens in the interchange dialog, after the snapshot is
// installed and the wizard closes; the source is trimmed.
{
  const fx = fixture();
  fx.wizard.start();
  fx.wizard.directory = "/work/product";
  fx.wizard.importFrom = "i18next";
  fx.wizard.importSource = "  ./strings.json  ";
  for (let i = 0; i < 4; i += 1) await fx.wizard.advance();
  fx.calls.length = 0;
  await fx.wizard.create();
  assert.deepEqual(fx.calls, [
    "confirm ui_confirm_create_project",
    "createProject",
    "install",
    "startCatalogImport i18next ./strings.json",
    "previewCatalogImport",
  ]);
  assert.equal(fx.wizard.open, false);
  assert.equal(fx.wizard.busy, false);
}

// A refused creation shows the host's message and stays open; without one, the generic notice; a missing snapshot is the
// same failure.
{
  const fx = fixture();
  await atReview(fx);
  fx.host.createProject = async () => ({ ok: false, message: { code: "ui_backend_directory_not_empty", args: [] } });
  await fx.wizard.create();
  assert.deepEqual(fx.wizard.error, { code: "ui_backend_directory_not_empty", args: [] });
  assert.equal(fx.wizard.open, true);
  assert.deepEqual(fx.installed, []);
  assert.equal(fx.wizard.busy, false);

  fx.host.createProject = async () => ({ ok: false });
  await fx.wizard.create();
  assert.equal(fx.wizard.error.code, "ui_feedback_project_failed");

  fx.host.createProject = async () => ({ ok: true, snapshot: undefined });
  await fx.wizard.create();
  assert.equal(fx.wizard.error.code, "ui_feedback_project_failed");
  assert.equal(fx.wizard.open, true);
  assert.deepEqual(fx.installed, []);
}

// A host error during creation is shown as the error and the wizard stays open.
{
  const fx = fixture();
  await atReview(fx);
  fx.host.createProject = async () => { throw new Error("disk full"); };
  await fx.wizard.create();
  assert.equal(fx.wizard.error.code, "ui_backend_external_error");
  assert.equal(fx.wizard.error.detail, "disk full");
  assert.equal(fx.wizard.open, true);
  assert.equal(fx.wizard.busy, false);
}

// Creation is busy until the host answers; a second create() meanwhile does nothing, and history being busy blocks it.
{
  const fx = fixture();
  await atReview(fx);
  const created = deferred();
  fx.host.createProject = () => created.promise;
  const pending = fx.wizard.create();
  await settle();
  assert.equal(fx.wizard.busy, true);
  await fx.wizard.create();
  assert.equal(fx.calls.filter((call) => call === "createProject").length, 1, "The second create() is ignored.");
  created.resolve({ ok: true, snapshot: { root: "/work/product" } });
  await pending;
  assert.equal(fx.wizard.busy, false);
  assert.deepEqual(fx.installed, [{ root: "/work/product" }]);
}

{
  const fx = fixture();
  await atReview(fx);
  fx.host.historyBusy = true;
  await fx.wizard.create();
  assert.deepEqual(fx.calls, []);
  assert.equal(fx.wizard.open, true);
}

// The wizard closes on close() only when nothing is pending.
{
  const fx = fixture();
  fx.wizard.start();
  fx.wizard.close();
  assert.equal(fx.wizard.open, false);
  fx.wizard.start();
  fx.wizard.busy = true;
  fx.wizard.close();
  assert.equal(fx.wizard.open, true, "A pending request keeps the wizard open.");
}

// back() returns to the previous step and clears the error.
{
  const fx = fixture();
  fx.wizard.start();
  fx.wizard.step = 3;
  fx.wizard.error = { code: "x", args: [] };
  fx.wizard.back();
  assert.deepEqual([fx.wizard.step, fx.wizard.error], [2, undefined]);
}

console.log("PASS: the project wizard resets its defaults, validates each step with its notice, plans and creates the project, and opens an import's dry run.");
