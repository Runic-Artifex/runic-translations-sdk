// The library catalogs dialog: opening and selecting a library, the busy guard, the notice and conflict of a refused write,
// the locale choice after a reload, the project change a result announces, and each write passing the loaded catalog.
import assert from "node:assert/strict";
import { deferred, settle } from "./svelte-modules.mjs";

const { LibraryController } = await import("../src/lib/state/library-controller.svelte.ts");

const libraryView = (catalog = "shared", locales = ["en", "fr-CA"]) => ({
  catalog,
  locales: locales.map((tag) => ({ tag })),
  messages: [],
  revision: `${catalog}-1`,
});

// A controller over fake bridge methods. `calls` records each bridge call; `answers` supplies the result per method.
function fixture() {
  const calls = [];
  const answers = {};
  const respond = (name) => async (...args) => {
    calls.push([name, ...args]);
    return typeof answers[name] === "function" ? answers[name](...args) : answers[name] ?? { ok: true, library: libraryView() };
  };
  const bridge = {
    loadLibrary: respond("loadLibrary"),
    overrideLibraryMessage: respond("overrideLibraryMessage"),
    saveLibraryOverride: respond("saveLibraryOverride"),
    revertLibraryOverride: respond("revertLibraryOverride"),
    keepLibraryOverride: respond("keepLibraryOverride"),
    addLibraryLocale: respond("addLibraryLocale"),
  };
  const host = { unsaved: false, projectChanges: [] };
  const controller = new LibraryController({
    bridge,
    hasUnsavedWork: () => host.unsaved,
    projectChanged: async (deferred) => { host.projectChanges.push(deferred); },
  });
  return { controller, calls, answers, host };
}

// openLibrary() opens the dialog and loads the library; the first locale is chosen when none was.
{
  const fx = fixture();
  const { controller, calls } = fx;
  await controller.openLibrary("shared");
  assert.equal(controller.open, true);
  assert.deepEqual(calls, [["loadLibrary", "shared"]]);
  assert.equal(controller.view.catalog, "shared");
  assert.equal(controller.locale, "en");
  assert.equal(controller.busy, false);
}

// A canonical tag the backend wrote is chosen; a locale that still exists is kept; a vanished one falls back to the first.
{
  const fx = fixture();
  const { controller, answers } = fx;
  answers.loadLibrary = () => ({ ok: true, locale: "fr-CA", library: libraryView("shared", ["en", "fr-CA"]) });
  await controller.openLibrary("shared");
  assert.equal(controller.locale, "fr-CA");

  await controller.run(() => Promise.resolve({ ok: true, library: libraryView("shared", ["en", "fr-CA", "de"]) }));
  assert.equal(controller.locale, "fr-CA", "The current locale is kept while it still exists.");

  await controller.run(() => Promise.resolve({ ok: true, library: libraryView("shared", ["en", "de"]) }));
  assert.equal(controller.locale, "en", "A vanished locale falls back to the first one.");
}

// select() loads the chosen library in the background.
{
  const fx = fixture();
  const { controller, calls, answers } = fx;
  answers.loadLibrary = () => ({ ok: true, library: libraryView("other", ["de"]) });
  controller.select("other");
  await settle();
  assert.deepEqual(calls, [["loadLibrary", "other"]]);
  assert.equal(controller.view.catalog, "other");
  assert.equal(controller.locale, "de", "The first locale of the new library is chosen.");
}

// A busy dialog refuses a second operation; the first one finishes alone.
{
  const fx = fixture();
  const { controller, answers, calls } = fx;
  const pending = deferred();
  answers.loadLibrary = () => pending.promise;
  const first = controller.openLibrary("shared");
  await settle();
  assert.equal(controller.busy, true);
  assert.equal(await controller.run(() => Promise.resolve({ ok: true })), false);
  assert.equal(calls.length, 1);
  pending.resolve({ ok: true, library: libraryView() });
  await first;
  assert.equal(controller.busy, false);
}

// A refused write shows the host's message, flags the conflict and keeps the view; a refusal without a message is external.
{
  const fx = fixture();
  const { controller, answers } = fx;
  await controller.openLibrary("shared");
  const before = controller.view;
  answers.saveLibraryOverride = () => ({ ok: false, message: { code: "ui_backend_document_conflict", args: [] }, conflict: true });
  const ok = await controller.saveOverride("greeting", "fr-CA", "Salut", "shared-1");
  assert.equal(ok, false);
  assert.deepEqual(controller.notice, { code: "ui_backend_document_conflict", args: [] });
  assert.equal(controller.conflict, true);
  assert.equal(controller.view, before, "A result without a library keeps the loaded view.");

  answers.saveLibraryOverride = () => ({ ok: false });
  await controller.saveOverride("greeting", "fr-CA", "Salut", "shared-1");
  assert.equal(controller.notice.code, "ui_backend_external_error");
  assert.equal(controller.conflict, false);
}

// Each run clears the previous notice and conflict before it starts.
{
  const fx = fixture();
  const { controller, answers } = fx;
  await controller.openLibrary("shared");
  answers.keepLibraryOverride = () => ({ ok: false, conflict: true, message: { code: "ui_backend_document_conflict", args: [] } });
  controller.keep("greeting", "en", "shared-1");
  await settle();
  assert.equal(controller.conflict, true);
  answers.keepLibraryOverride = () => ({ ok: true, library: libraryView() });
  controller.keep("greeting", "en", "shared-1");
  await settle();
  assert.equal(controller.notice, undefined);
  assert.equal(controller.conflict, false);
}

// A thrown operation is shown as an external error and reports failure.
{
  const fx = fixture();
  const { controller, answers } = fx;
  await controller.openLibrary("shared");
  answers.revertLibraryOverride = () => { throw new Error("offline"); };
  const ok = await controller.run(() => Promise.reject(new Error("offline")));
  assert.equal(ok, false);
  assert.equal(controller.notice.code, "ui_backend_external_error");
  assert.equal(controller.notice.detail, "offline");
  assert.equal(controller.busy, false);
}

// A result that says the project changed passes whether unsaved work is pending, so the reload can wait for it.
{
  const fx = fixture();
  const { controller, answers, host } = fx;
  await controller.openLibrary("shared");
  answers.addLibraryLocale = () => ({ ok: true, projectChanged: true, library: libraryView() });
  host.unsaved = true;
  controller.addLocale("de");
  await settle();
  assert.deepEqual(host.projectChanges, [true]);

  host.unsaved = false;
  answers.addLibraryLocale = () => ({ ok: true, projectChanged: true, library: libraryView() });
  controller.addLocale("de");
  await settle();
  assert.deepEqual(host.projectChanges, [true, false]);
}

// Every write names the loaded library's catalog, and the locale or key it was given.
{
  const fx = fixture();
  const { controller, calls } = fx;
  await controller.openLibrary("shared");
  calls.length = 0;
  await controller.override("greeting", "fr-CA", "Salut", "shared-1");
  await controller.saveOverride("greeting", "fr-CA", "Bonjour", "shared-1");
  // The void writes start a run each; they are sequenced so none is dropped by the busy guard.
  controller.revert("greeting", "fr-CA", "shared-1");
  await settle();
  controller.keep("greeting", "fr-CA", "shared-1");
  await settle();
  controller.addLocale("de");
  await settle();
  controller.reload();
  await settle();
  assert.deepEqual(calls, [
    ["overrideLibraryMessage", { catalog: "shared", key: "greeting", locale: "fr-CA", text: "Salut", expectedRevision: "shared-1" }],
    ["saveLibraryOverride", { catalog: "shared", key: "greeting", locale: "fr-CA", text: "Bonjour", expectedRevision: "shared-1" }],
    ["revertLibraryOverride", { catalog: "shared", key: "greeting", locale: "fr-CA", expectedRevision: "shared-1" }],
    ["keepLibraryOverride", { catalog: "shared", key: "greeting", locale: "fr-CA", expectedRevision: "shared-1" }],
    ["addLibraryLocale", "shared", "de"],
    ["loadLibrary", "shared"],
  ]);
}

console.log("PASS: the library controller loads and selects catalogs, refuses a second operation while busy, reports refused writes as conflicts, and passes each write the loaded catalog.");
