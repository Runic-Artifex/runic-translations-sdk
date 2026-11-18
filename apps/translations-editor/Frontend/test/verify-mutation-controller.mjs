// The structural-change dialog: its defaults per operation, the preview and confirmed commit, the selection after a rename
// or duplicate, the debounced CLDR description of a typed locale, and the failures a commit can report.
import assert from "node:assert/strict";
import { deferred, settle } from "./svelte-modules.mjs";

const { MutationController } = await import("../src/lib/state/mutation-controller.svelte.ts");
const { EditorFeedback } = await import("../src/lib/state/feedback.svelte.ts");

// A .rmf2 document's messages are read from its entries, so each `key = value` line of the content becomes one entry.
const documentOf = (path, locale, content) => ({
  path, locale, layer: "base", isManifest: false, isMalformed: false, revision: `rev-${path}`, content,
  entries: content.split("\n").flatMap((line) => {
    const match = /^([A-Za-z][A-Za-z0-9]*) = (.*)$/.exec(line);
    return match ? [{ key: match[1], path, content: match[2], valueStartByte: 0, valueLengthBytes: match[2].length }] : [];
  }),
});
// A catalog with English as the default, German and French as additional languages, and two messages.
const workspace = (contents = { en: "greeting = Hi\nfarewell = Bye\n", de: "greeting = Hallo\nfarewell = Tschuess\n" }, locales = ["en", "de", "fr"]) => ({
  root: "/work",
  catalog: {
    id: "product", schemaVersion: 1, defaultLocale: "en", locales: locales.map((tag) => ({ tag })), layers: [],
  },
  catalogs: [],
  documents: Object.entries(contents).map(([locale, content]) => documentOf(`${locale}.rmf2`, locale, content)),
  review: { path: "product.review.json", revision: "review-1", entries: [], terminology: [] },
  diagnostics: [],
  success: true,
});

// A controller over fake collaborators. `host` answers the bridge and the confirmation; `calls` records the order of
// collaborator calls; `timers` holds the debounce timers the controller scheduled.
function fixture(options = {}) {
  const calls = [];
  const timers = [];
  const host = {
    current: options.snapshot ?? workspace(),
    historyBusy: false,
    confirmResult: true,
    hasUnsavedWork: false,
    describe: async (tag) => ({ canonical: tag, suggested: `${tag}-suggested` }),
    preview: async () => ({ ok: true, confirmationToken: "token-1", requiresIrreversibleConfirmation: false, files: [] }),
    apply: async () => ({ ok: true, snapshot: workspace(), message: undefined }),
  };
  const selection = {
    selectedKey: "greeting",
    selectedLocale: "de",
    configureEditor() { calls.push("configureEditor"); },
  };
  const feedback = new EditorFeedback();
  const controller = new MutationController({
    bridge: {
      describeLocale: (tag) => { calls.push(`describe ${tag}`); return host.describe(tag); },
      previewMutation: (request) => { calls.push("previewMutation"); return host.preview(request); },
      applyMutation: (request) => { calls.push("applyMutation"); return host.apply(request); },
    },
    snapshot: () => host.current,
    selection,
    unsaved: {
      get hasUnsavedWork() { return host.hasUnsavedWork; },
      confirmDiscard(key) { calls.push(`confirm ${key}`); return host.confirmResult; },
      discard() { calls.push("discard"); },
    },
    feedback,
    load: async () => { calls.push("load"); },
    install(next) { calls.push("install"); host.current = next; },
    historyBusy: () => host.historyBusy,
    setTimer: (callback, delay) => { timers.push({ callback, delay, cleared: false }); return timers.length - 1; },
    clearTimer: (handle) => { timers[handle].cleared = true; },
  });
  return { controller, host, calls, timers, selection, feedback };
}

// prepare() with no catalog, a refused confirmation, or history being busy opens nothing and discards nothing.
{
  const fx = fixture({ snapshot: undefined });
  fx.host.current = undefined;
  assert.equal(fx.controller.prepare("add-locale"), false);
  assert.equal(fx.controller.open, false);
  assert.deepEqual(fx.calls, []);

  const refused = fixture();
  refused.host.hasUnsavedWork = true;
  refused.host.confirmResult = false;
  assert.equal(refused.controller.prepare("create-key"), false);
  assert.equal(refused.controller.open, false);
  assert.deepEqual(refused.calls, ["confirm ui_confirm_change_workspace"]);

  const busy = fixture();
  busy.host.historyBusy = true;
  assert.equal(busy.controller.prepare("add-locale"), false);
  assert.deepEqual(busy.calls, []);
}

// Unsaved work is discarded, after the confirmation, when the dialog opens.
{
  const fx = fixture();
  fx.host.hasUnsavedWork = true;
  assert.equal(fx.controller.prepare("add-locale"), true);
  assert.deepEqual(fx.calls, ["confirm ui_confirm_change_workspace", "discard"]);
  assert.equal(fx.controller.open, true);
}

// The defaults of each operation: remove-locale and set-fallback pick the selected locale unless it is the default, then
// the first non-default; duplicate-key targets the key with "Copy"; create-key starts with no target.
{
  const fx = fixture();
  const { controller, selection } = fx;

  selection.selectedLocale = "de";
  controller.prepare("remove-locale");
  assert.equal(controller.locale, "de");

  selection.selectedLocale = "en";
  controller.prepare("set-fallback");
  assert.equal(controller.locale, "de", "The default locale is not a candidate: the first non-default is.");

  selection.selectedLocale = "en";
  controller.prepare("remove-locale");
  assert.equal(controller.locale, "de");

  controller.prepare("add-locale");
  assert.equal(controller.locale, "", "A new locale has no existing tag to pick.");

  controller.prepare("duplicate-key");
  assert.equal(controller.targetKey, "greetingCopy");
  assert.equal(controller.sourceKey, "greeting");

  controller.prepare("create-key");
  assert.equal(controller.targetKey, "");

  controller.prepare("rename-key");
  assert.equal(controller.targetKey, "greeting", "A rename starts from the selected key.");

  assert.deepEqual(
    [controller.fallback, controller.replacementFallback, controller.copyFrom],
    ["en", "en", "en"],
    "Fallbacks and the copy source start from the default locale.",
  );
  assert.equal(controller.initialValue, "");
  assert.equal(controller.open, true);
}

// With the selected locale the default and no other language in the catalog, there is no locale to pick.
{
  const fx = fixture({ snapshot: workspace({ en: "greeting = Hi\n" }, ["en"]) });
  fx.selection.selectedLocale = "en";
  fx.controller.prepare("remove-locale");
  assert.equal(fx.controller.locale, "");
}

// prepare() clears the last dialog's preview, error and confirmation.
{
  const fx = fixture();
  fx.controller.prepare("remove-locale");
  fx.controller.preview = { ok: true, confirmationToken: "old", requiresIrreversibleConfirmation: true };
  fx.controller.error = { code: "x", args: [] };
  fx.controller.irreversibleConfirmed = true;
  fx.controller.busy = true;
  fx.controller.prepare("remove-locale");
  assert.deepEqual(
    [fx.controller.preview, fx.controller.error, fx.controller.irreversibleConfirmed, fx.controller.busy],
    [undefined, undefined, false, false],
  );
}

// request() trims the fields, and carries the confirmation token only once the user confirmed the irreversible change.
{
  const fx = fixture();
  const { controller } = fx;
  controller.prepare("remove-locale");
  controller.locale = "  de  ";
  controller.fallback = "   ";
  controller.preview = { ok: true, confirmationToken: "token-1", requiresIrreversibleConfirmation: true };
  assert.deepEqual(controller.request(), {
    kind: "remove-locale",
    locale: "de",
    fallback: undefined,
    replacementFallback: "en",
    copyFromLocale: "en",
    sourceKey: "greeting",
    targetKey: "greeting",
    initialValue: "",
    confirmationToken: undefined,
  });
  controller.irreversibleConfirmed = true;
  assert.equal(controller.request().confirmationToken, "token-1");
}

// invalidate() drops the preview and the error, and the irreversible confirmation with them.
{
  const fx = fixture();
  const { controller } = fx;
  controller.prepare("remove-locale");
  controller.preview = { ok: true, confirmationToken: "t", requiresIrreversibleConfirmation: true };
  controller.error = { code: "x", args: [] };
  controller.irreversibleConfirmed = true;
  controller.invalidate();
  assert.deepEqual([controller.preview, controller.error, controller.irreversibleConfirmed], [undefined, undefined, false]);
}

// changeKind() switches the operation with its defaults and invalidates what was previewed.
{
  const fx = fixture();
  const { controller } = fx;
  controller.prepare("add-locale");
  controller.preview = { ok: true, confirmationToken: "t", requiresIrreversibleConfirmation: false };
  controller.error = { code: "x", args: [] };
  controller.fallback = "de";
  controller.replacementFallback = "de";
  controller.changeKind("remove-locale");
  assert.equal(controller.kind, "remove-locale");
  assert.equal(controller.locale, "de", "The first non-default language is the default pick.");
  assert.equal(controller.fallback, "en");
  assert.equal(controller.replacementFallback, "en");
  assert.deepEqual([controller.preview, controller.error, controller.irreversibleConfirmed], [undefined, undefined, false]);

  controller.changeKind("add-locale");
  assert.equal(controller.locale, "", "Adding a locale starts with no tag.");
}

// describeLocale() describes a typed locale after a pause; an empty tag clears the description and schedules nothing.
{
  const fx = fixture();
  const { controller, timers, host } = fx;
  controller.prepare("add-locale");
  controller.locale = "fr";
  controller.describeLocale();
  assert.deepEqual(timers.map((timer) => timer.delay), [200]);
  assert.equal(controller.localeDescription, undefined, "Nothing is described before the pause.");

  const description = { canonical: "fr", suggested: "fr-FR" };
  host.describe = async (tag) => ({ canonical: tag, suggested: "fr-FR" });
  timers[0].callback();
  await settle();
  assert.deepEqual(controller.localeDescription, description);

  controller.locale = "   ";
  controller.describeLocale();
  assert.equal(controller.localeDescription, undefined, "An empty tag clears the description.");
  assert.equal(timers.length, 1, "An empty tag schedules nothing.");
}

// Typing again within the pause replaces the timer: only the last tag is described.
{
  const fx = fixture();
  const { controller, timers, calls } = fx;
  controller.prepare("add-locale");
  controller.locale = "f";
  controller.describeLocale();
  controller.locale = "fr";
  controller.describeLocale();
  assert.equal(timers[0].cleared, true, "The earlier timer was cleared.");
  timers[1].callback();
  await settle();
  assert.deepEqual(calls.filter((call) => call.startsWith("describe")), ["describe fr"]);
}

// An older description that arrives after a newer tag is typed does not overwrite the newer one.
{
  const fx = fixture();
  const { controller, timers, host } = fx;
  const answers = { fr: deferred(), de: deferred() };
  host.describe = (tag) => answers[tag].promise;
  controller.prepare("add-locale");
  controller.locale = "fr";
  controller.describeLocale();
  timers[0].callback();
  controller.locale = "de";
  controller.describeLocale();
  timers[1].callback();
  answers.de.resolve({ canonical: "de", suggested: "de-DE" });
  await settle();
  assert.equal(controller.localeDescription.canonical, "de");
  answers.fr.resolve({ canonical: "fr", suggested: "fr-FR" });
  await settle();
  assert.equal(controller.localeDescription.canonical, "de", "The older answer for fr is ignored.");
}

// A failing description leaves no description.
{
  const fx = fixture();
  const { controller, timers, host } = fx;
  host.describe = async () => { throw new Error("no CLDR data"); };
  controller.prepare("add-locale");
  controller.locale = "xx";
  controller.describeLocale();
  timers[0].callback();
  await settle();
  assert.equal(controller.localeDescription, undefined);
}

// useSuggestedLocale() takes the CLDR suggestion and describes it.
{
  const fx = fixture();
  const { controller, timers } = fx;
  controller.prepare("add-locale");
  controller.locale = "de-ch-1996";
  controller.localeDescription = { canonical: "de-CH-1996", suggested: "de-CH" };
  controller.useSuggestedLocale();
  assert.equal(controller.locale, "de-CH");
  assert.equal(controller.localeDescription.suggested, "de-CH", "The dialog keeps the description until it is recomputed.");
  assert.equal(timers.length, 1);
}

// previewChange() stores the host's preview; a refused preview shows its message, else the generic notice.
{
  const fx = fixture();
  const { controller, host } = fx;
  controller.prepare("remove-locale");
  await controller.previewChange();
  assert.equal(controller.preview.ok, true);
  assert.equal(controller.error, undefined);
  assert.equal(controller.busy, false);

  host.preview = async () => ({ ok: false, message: { code: "ui_backend_locale_in_use", args: [] } });
  await controller.previewChange();
  assert.deepEqual(controller.error, { code: "ui_backend_locale_in_use", args: [] });
  assert.equal(controller.preview.ok, false);

  host.preview = async () => ({ ok: false });
  await controller.previewChange();
  assert.equal(controller.error.code, "ui_feedback_change_invalid");
}

// A host error while previewing is shown as the error; the preview stays as it was.
{
  const fx = fixture();
  const { controller, host } = fx;
  controller.prepare("remove-locale");
  host.preview = async () => { throw new Error("offline"); };
  await controller.previewChange();
  assert.equal(controller.error.code, "ui_backend_external_error");
  assert.equal(controller.error.detail, "offline");
  assert.equal(controller.busy, false);
}

// A second preview while one is pending does nothing, and history being busy blocks it.
{
  const fx = fixture();
  const { controller, host, calls } = fx;
  controller.prepare("remove-locale");
  const pending = deferred();
  host.preview = () => pending.promise;
  const first = controller.previewChange();
  await settle();
  assert.equal(controller.busy, true);
  await controller.previewChange();
  assert.equal(calls.filter((call) => call === "previewMutation").length, 1);
  pending.resolve({ ok: true, confirmationToken: "t", requiresIrreversibleConfirmation: false });
  await first;
  assert.equal(controller.busy, false);

  host.historyBusy = true;
  await controller.previewChange();
  assert.equal(calls.filter((call) => call === "previewMutation").length, 1);
}

// apply() needs an ok preview, and an irreversible change needs the user's confirmation first.
{
  const fx = fixture();
  const { controller, host, calls } = fx;
  controller.prepare("remove-locale");
  await controller.apply();
  assert.equal(calls.includes("applyMutation"), false, "Without a preview nothing is applied.");

  host.preview = async () => ({ ok: false, message: { code: "ui_backend_locale_in_use", args: [] } });
  await controller.previewChange();
  await controller.apply();
  assert.equal(calls.includes("applyMutation"), false, "A refused preview cannot be applied.");

  host.preview = async () => ({ ok: true, confirmationToken: "token-1", requiresIrreversibleConfirmation: true, files: [] });
  await controller.previewChange();
  await controller.apply();
  assert.equal(calls.includes("applyMutation"), false, "The irreversible change waits for confirmation.");

  controller.irreversibleConfirmed = true;
  await controller.apply();
  assert.equal(calls.filter((call) => call === "applyMutation").length, 1);
}

// The commit carries the confirmation token only after confirmation, and the host receives the request built from the fields.
{
  const fx = fixture();
  const { controller, host } = fx;
  const sent = [];
  host.apply = async (request) => { sent.push(request); return { ok: true, snapshot: workspace() }; };
  host.preview = async () => ({ ok: true, confirmationToken: "token-9", requiresIrreversibleConfirmation: true, files: [] });
  controller.prepare("remove-locale");
  controller.locale = "fr";
  await controller.previewChange();
  controller.irreversibleConfirmed = true;
  await controller.apply();
  assert.equal(sent[0].confirmationToken, "token-9");
  assert.equal(sent[0].locale, "fr");
}

// A successful commit installs the new workspace, selects the renamed message, closes the dialog and reports the change.
{
  const fx = fixture();
  const { controller, host, selection, feedback, calls } = fx;
  const renamed = workspace({ en: "welcome = Hi\nfarewell = Bye\n", de: "welcome = Hallo\nfarewell = Tschuess\n" });
  host.apply = async () => ({ ok: true, snapshot: renamed });
  controller.prepare("rename-key");
  controller.targetKey = "welcome";
  await controller.previewChange();
  await controller.apply();
  assert.equal(host.current, renamed);
  assert.equal(selection.selectedKey, "welcome", "A renamed message is selected under its new key.");
  assert.equal(controller.open, false);
  assert.equal(controller.busy, false);
  assert.equal(controller.error, undefined);
  assert.deepEqual(feedback.operationMessage, { code: "ui_feedback_workspace_updated", args: [] });
  assert.equal(calls.at(-1), "configureEditor");
  assert.equal(calls.at(-2), "install");
}

// A duplicated message is selected under its copy's key.
{
  const fx = fixture();
  const { controller, host, selection } = fx;
  host.apply = async () => ({ ok: true, snapshot: workspace({ en: "greeting = Hi\ngreetingCopy = Hi\n", de: "greeting = Hallo\n" }) });
  controller.prepare("duplicate-key");
  await controller.previewChange();
  await controller.apply();
  assert.equal(selection.selectedKey, "greetingCopy");
}

// A rename whose target is not in the new workspace keeps the selection; so does any other change.
{
  const fx = fixture();
  const { controller, host, selection } = fx;
  host.apply = async () => ({ ok: true, snapshot: workspace() });
  controller.prepare("rename-key");
  controller.targetKey = "missing";
  await controller.previewChange();
  await controller.apply();
  assert.equal(selection.selectedKey, "greeting");

  const removed = fixture();
  removed.selection.selectedKey = "farewell";
  removed.host.apply = async () => ({ ok: true, snapshot: workspace({ en: "greeting = Hi\n", de: "greeting = Hallo\n" }) });
  removed.controller.prepare("remove-locale");
  removed.controller.locale = "de";
  await removed.controller.previewChange();
  await removed.controller.apply();
  assert.equal(removed.selection.selectedKey, "farewell", "Only a rename or duplicate moves the selection.");
}

// A warning the host sends with a committed change replaces the generic message.
{
  const fx = fixture();
  const { controller, host, feedback } = fx;
  host.apply = async () => ({ ok: true, snapshot: workspace(), message: { code: "ui_backend_review_entries_kept", args: [] } });
  controller.prepare("remove-locale");
  controller.locale = "de";
  await controller.previewChange();
  await controller.apply();
  assert.deepEqual(feedback.operationMessage, { code: "ui_backend_review_entries_kept", args: [] });
}

// Without a returned snapshot the workspace is reloaded instead; nothing is installed and the selection is kept.
{
  const fx = fixture();
  const { controller, host, calls, feedback, selection } = fx;
  host.apply = async () => ({ ok: true, snapshot: undefined });
  controller.prepare("remove-locale");
  controller.locale = "de";
  await controller.previewChange();
  await controller.apply();
  assert.deepEqual(calls.filter((call) => call === "load" || call === "install" || call === "configureEditor"), ["load"]);
  assert.equal(controller.open, false);
  assert.deepEqual(feedback.operationMessage, { code: "ui_feedback_workspace_updated", args: [] });
  assert.equal(selection.selectedKey, "greeting");

  host.apply = async () => ({ ok: true, snapshot: undefined, message: { code: "ui_backend_reloaded", args: [] } });
  await controller.previewChange();
  controller.open = true;
  await controller.apply();
  assert.deepEqual(feedback.operationMessage, { code: "ui_backend_reloaded", args: [] });
}

// A refused commit shows the host's message and drops the preview; without a message, the generic notice.
{
  const fx = fixture();
  const { controller, host, feedback, calls } = fx;
  host.apply = async () => ({ ok: false, message: { code: "ui_backend_document_conflict", args: [] } });
  controller.prepare("remove-locale");
  controller.locale = "de";
  await controller.previewChange();
  await controller.apply();
  assert.deepEqual(controller.error, { code: "ui_backend_document_conflict", args: [] });
  assert.equal(controller.preview, undefined);
  assert.equal(controller.open, true);
  assert.equal(calls.includes("install"), false);
  assert.equal(feedback.operationMessage, undefined);

  host.apply = async () => ({ ok: false });
  await controller.previewChange();
  await controller.apply();
  assert.equal(controller.error.code, "ui_feedback_change_failed");
}

// A host error during the commit is shown as the error and drops the preview.
{
  const fx = fixture();
  const { controller, host } = fx;
  host.apply = async () => { throw new Error("disk full"); };
  controller.prepare("remove-locale");
  controller.locale = "de";
  await controller.previewChange();
  await controller.apply();
  assert.equal(controller.error.code, "ui_backend_external_error");
  assert.equal(controller.error.detail, "disk full");
  assert.equal(controller.preview, undefined);
  assert.equal(controller.busy, false);
}

// A commit is busy until the host answers; a second apply() meanwhile does nothing.
{
  const fx = fixture();
  const { controller, host, calls } = fx;
  const committed = deferred();
  host.apply = () => committed.promise;
  controller.prepare("remove-locale");
  controller.locale = "de";
  await controller.previewChange();
  const pending = controller.apply();
  await settle();
  assert.equal(controller.busy, true);
  await controller.apply();
  assert.equal(calls.filter((call) => call === "applyMutation").length, 1);
  committed.resolve({ ok: true, snapshot: workspace() });
  await pending;
  assert.equal(controller.busy, false);
}

console.log("PASS: the mutation controller defaults each operation, commits only an ok preview with its confirmation, selects renamed and duplicated messages, and describes typed locales after a pause.");
