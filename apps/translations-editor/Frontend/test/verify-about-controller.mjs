// The About controller: build information read once, the diagnostic bundle actions, and the local state inspector.
import assert from "node:assert/strict";
import { deferred } from "./svelte-modules.mjs";

const { AboutController } = await import("../src/lib/state/about-controller.svelte.ts");

const BUNDLE = "/work/diagnostics/bundle.zip";
const about = {
  product: "Runic Translations Editor", version: "0.9.0", updateChannel: "stable", runtime: ".NET 10",
  runtimeIdentifier: "linux-x64", operatingSystem: "Linux", architecture: "x64",
};
const summaryOf = (entries) => ({ entries, bytes: entries * 10, preferenceEntries: entries, recentProjectEntries: 0, draftEntries: 0, recovered: false });

// Every collaborator records its calls in `log`; each behaviour can be overridden per scenario.
function createController(options = {}) {
  const log = [];
  const local = { entries: options.entries ?? 0 };
  const bridge = {
    async about() { log.push("about"); return options.about ? options.about() : about; },
    async createDiagnosticBundle() { log.push("create"); return options.create ? options.create() : { ok: true, path: BUNDLE }; },
    async revealDiagnosticBundle(path) { log.push(`reveal ${path}`); return options.reveal ? options.reveal(path) : { ok: true }; },
    async deleteDiagnosticBundle(path) { log.push(`delete ${path}`); return options.remove ? options.remove(path) : { ok: true }; },
  };
  const localState = {
    inspect() { log.push("inspect"); return summaryOf(local.entries); },
    async clear() {
      log.push("clear");
      if (options.clear) return options.clear();
      const removed = local.entries;
      local.entries = 0;
      return removed;
    },
  };
  const controller = new AboutController({
    bridge,
    localState,
    localStateCleared: () => log.push("cleared"),
    confirm: (key) => { log.push(`confirm ${key}`); return options.confirm ? options.confirm(key) : true; },
    writeClipboard: async (text) => { log.push(`copy ${text}`); if (options.copy) throw options.copy; },
    historyBusy: () => options.historyBusy ?? false,
  });
  return { controller, log, local };
}

// show(): opens the dialog, inspects the local state, and reads the build information once.
{
  const { controller, log } = createController({ entries: 3 });
  await controller.show();
  assert.equal(controller.open, true);
  assert.deepEqual(controller.info, about);
  assert.equal(controller.localStateSummary.entries, 3);
  assert.equal(controller.busy, false);
  assert.deepEqual(log, ["inspect", "about"]);

  await controller.show();
  assert.deepEqual(log, ["inspect", "about", "inspect"], "A second show inspects again but does not read the build information again.");
}

// A show while the build information is still loading does not start a second read.
{
  const reply = deferred();
  const { controller, log } = createController({ about: () => reply.promise });
  const first = controller.show();
  assert.equal(controller.busy, true);
  await controller.show();
  reply.resolve(about);
  await first;
  assert.deepEqual(log.filter((entry) => entry === "about"), ["about"]);
  assert.equal(controller.busy, false);
  assert.deepEqual(controller.info, about);
}

// A failing read of the build information is reported, and the next show reads it again.
{
  let attempts = 0;
  const { controller } = createController({
    about: () => {
      attempts += 1;
      if (attempts === 1) throw new Error("host went away");
      return about;
    },
  });
  await controller.show();
  assert.equal(controller.info, undefined);
  assert.equal(controller.busy, false);
  assert.deepEqual(controller.diagnosticMessage, { code: "ui_backend_external_error", args: [], detail: "host went away" });

  await controller.show();
  assert.deepEqual(controller.info, about);
  assert.equal(controller.diagnosticMessage, undefined, "Opening the dialog again clears the previous diagnostic message.");
}

// While history is busy, show() does nothing: the dialog stays closed and nothing is read.
{
  const { controller, log } = createController({ historyBusy: true });
  await controller.show();
  assert.equal(controller.open, false);
  assert.deepEqual(log, []);
}

// clearLocalState(): with nothing stored there is no confirmation and no clear.
{
  const { controller, log } = createController({ entries: 0 });
  await controller.show();
  log.length = 0;
  await controller.clearLocalState();
  assert.deepEqual(log, []);
}

// clearLocalState(): a declined confirmation leaves the state alone.
{
  const { controller, log, local } = createController({ entries: 2, confirm: () => false });
  await controller.show();
  log.length = 0;
  await controller.clearLocalState();
  assert.deepEqual(log, ["confirm ui_feedback_clear_state_confirm"]);
  assert.equal(local.entries, 2);
  assert.equal(controller.localStateMessage, undefined);
}

// clearLocalState(): a confirmed clear tells the page, refreshes the summary and reports the removed count.
{
  const { controller, log, local } = createController({ entries: 2 });
  await controller.show();
  log.length = 0;
  await controller.clearLocalState();
  assert.deepEqual(log, ["confirm ui_feedback_clear_state_confirm", "clear", "cleared", "inspect"]);
  assert.equal(local.entries, 0);
  assert.equal(controller.localStateSummary.entries, 0);
  assert.equal(controller.localStateMessage.code, "ui_count_state_removed");
  assert.deepEqual(controller.localStateMessage.args, [{ name: "count", number: 2 }]);
}

// clearLocalState(): a failing clear reports the error, and the page is not told the state was cleared.
{
  const { controller, log } = createController({ entries: 2, clear: async () => { throw new Error("disk locked"); } });
  await controller.show();
  log.length = 0;
  await controller.clearLocalState();
  assert.equal(log.includes("cleared"), false);
  assert.equal(controller.localStateMessage.code, "ui_backend_external_error");
  assert.equal(controller.localStateMessage.detail, "disk locked");
}

// createDiagnosticBundle(): a created bundle is remembered by its path and reported as saved.
{
  const { controller } = createController();
  await controller.createDiagnosticBundle();
  assert.equal(controller.diagnosticBundlePath, BUNDLE);
  assert.equal(controller.diagnosticMessage.code, "ui_feedback_diagnostics_saved");
  assert.equal(controller.diagnosticBusy, false);
}

// createDiagnosticBundle(): a refused bundle forgets the previous path and shows the host's own message.
{
  const hostMessage = { code: "ui_feedback_diagnostics_host_refused", args: [] };
  let refuse = false;
  const { controller } = createController({
    create: () => (refuse ? { ok: false, message: hostMessage } : { ok: true, path: BUNDLE }),
  });
  await controller.createDiagnosticBundle();
  assert.equal(controller.diagnosticBundlePath, BUNDLE);
  refuse = true;
  await controller.createDiagnosticBundle();
  assert.equal(controller.diagnosticBundlePath, undefined);
  assert.deepEqual(controller.diagnosticMessage, hostMessage, "The host's own message is shown as it was sent.");
}

// createDiagnosticBundle(): a refusal without a message uses the generic failure notice.
{
  const { controller } = createController({ create: () => ({ ok: false }) });
  await controller.createDiagnosticBundle();
  assert.equal(controller.diagnosticBundlePath, undefined);
  assert.equal(controller.diagnosticMessage.code, "ui_feedback_diagnostics_failed");
}

// createDiagnosticBundle(): a thrown error is reported as an external error, and the busy flag is released.
{
  const { controller } = createController({ create: () => { throw new Error("no disk"); } });
  await controller.createDiagnosticBundle();
  assert.equal(controller.diagnosticMessage.code, "ui_backend_external_error");
  assert.equal(controller.diagnosticMessage.detail, "no disk");
  assert.equal(controller.diagnosticBusy, false);
}

// createDiagnosticBundle(): nothing happens while history is busy.
{
  const { controller, log } = createController({ historyBusy: true });
  await controller.createDiagnosticBundle();
  assert.deepEqual(log, []);
  assert.equal(controller.diagnosticBundlePath, undefined);
}

// revealDiagnosticBundle(): without a bundle there is nothing to reveal; with one, the stored path is opened.
{
  const { controller, log } = createController();
  await controller.revealDiagnosticBundle();
  assert.deepEqual(log, []);

  await controller.createDiagnosticBundle();
  log.length = 0;
  await controller.revealDiagnosticBundle();
  assert.deepEqual(log, [`reveal ${BUNDLE}`]);
  assert.equal(controller.diagnosticMessage.code, "ui_feedback_diagnostics_opened");
}

// revealDiagnosticBundle(): a refused reveal uses the generic failure notice; the path is kept.
{
  const { controller } = createController({ reveal: () => ({ ok: false }) });
  await controller.createDiagnosticBundle();
  await controller.revealDiagnosticBundle();
  assert.equal(controller.diagnosticMessage.code, "ui_feedback_diagnostics_open_failed");
  assert.equal(controller.diagnosticBundlePath, BUNDLE);
}

// copyDiagnosticBundlePath(): copies the stored path; a clipboard failure has its own notice.
{
  const { controller, log } = createController();
  await controller.copyDiagnosticBundlePath();
  assert.deepEqual(log, []);

  await controller.createDiagnosticBundle();
  log.length = 0;
  await controller.copyDiagnosticBundlePath();
  assert.deepEqual(log, [`copy ${BUNDLE}`]);
  assert.equal(controller.diagnosticMessage.code, "ui_feedback_diagnostics_copied");
}
{
  const { controller } = createController({ copy: new Error("no clipboard") });
  await controller.createDiagnosticBundle();
  await controller.copyDiagnosticBundlePath();
  assert.equal(controller.diagnosticMessage.code, "ui_feedback_diagnostics_copy_failed");
  assert.equal(controller.diagnosticBundlePath, BUNDLE);
}

// deleteDiagnosticBundle(): asks first; a declined delete keeps the bundle.
{
  const { controller, log } = createController({ confirm: () => false });
  await controller.createDiagnosticBundle();
  log.length = 0;
  await controller.deleteDiagnosticBundle();
  assert.deepEqual(log, ["confirm ui_feedback_diagnostics_delete_confirm"]);
  assert.equal(controller.diagnosticBundlePath, BUNDLE);
}

// deleteDiagnosticBundle(): a confirmed delete that succeeds forgets the path.
{
  const { controller, log } = createController();
  await controller.createDiagnosticBundle();
  log.length = 0;
  await controller.deleteDiagnosticBundle();
  assert.deepEqual(log, ["confirm ui_feedback_diagnostics_delete_confirm", `delete ${BUNDLE}`]);
  assert.equal(controller.diagnosticBundlePath, undefined);
  assert.equal(controller.diagnosticMessage.code, "ui_feedback_diagnostics_deleted");
}

// deleteDiagnosticBundle(): a failed delete keeps the path and reports the failure.
{
  const { controller } = createController({ remove: () => ({ ok: false }) });
  await controller.createDiagnosticBundle();
  await controller.deleteDiagnosticBundle();
  assert.equal(controller.diagnosticBundlePath, BUNDLE);
  assert.equal(controller.diagnosticMessage.code, "ui_feedback_diagnostics_delete_failed");
}

console.log("PASS: about controller shows once, inspects and clears local state, and manages the diagnostic bundle");
