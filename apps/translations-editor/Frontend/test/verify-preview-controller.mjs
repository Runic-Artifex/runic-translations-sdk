// The preview controller: debounced compiler previews of the selected message with its samples, the requested
// formatting, the pseudo-locale rendering of the simulation, and a reset that drops a pending preview.
import assert from "node:assert/strict";
import { settle } from "./svelte-modules.mjs";

const { PreviewController, previewFailure } = await import("../src/lib/state/preview-controller.svelte.ts");

const ast = JSON.stringify({ astVersion: 5, profile: "rmf2-execution-v2", inputs: [{ name: "name", type: "string" }], declarations: [], selectors: [], variants: [] });
const rendered = (text, locale = "de") => JSON.stringify({ key: "welcome", locale, runs: [{ text }] });
const requests = [];
const bridge = {
  async previewMessage(path, content, locale, key, samplesJson, requestedLocale, timeZone) {
    requests.push({ locale, samplesJson, requestedLocale, timeZone });
    if (samplesJson === undefined) return { success: true, locale, astJson: ast, diagnostics: [] };
    const samples = JSON.parse(samplesJson);
    return { success: true, locale, renderedJson: rendered(`Hallo ${samples.name}`, locale), diagnostics: [] };
  },
};
const timers = [];
let pseudo = false;
const preview = new PreviewController({
  bridge,
  pseudoLocalization: () => pseudo,
  defaultSample: () => "Sample",
  errorNotice: (error) => `failed: ${error.message}`,
  setTimer: (callback, delay) => { timers.push({ callback, delay }); return timers.length; },
  clearTimer: (handle) => { timers[handle - 1] = undefined; },
});
const fire = () => { const pending = timers.splice(0).filter(Boolean); for (const timer of pending) timer.callback(); return pending; };

// A scheduled preview is busy until the debounced host requests finish; a saved sample is kept.
preview.useSamples({ name: "Ada" });
preview.setFormatting("de-CH", "Europe/Zurich");
preview.schedule("de.rmf2", "welcome = Hallo {$name}\n", "welcome", "de");
assert.equal(preview.busy, true);
assert.equal(preview.request.requestedLocale, "de-CH");
assert.deepEqual(fire().map((timer) => timer.delay), [450]);
await settle();
assert.equal(preview.busy, false);
assert.deepEqual(preview.ast.inputs.map((input) => input.name), ["name"]);
assert.deepEqual({ ...preview.samples }, { name: "Ada" });
assert.deepEqual(preview.result, { kind: "text", value: "Hallo Ada" });
assert.equal(preview.simulatedResult, preview.result);
assert.deepEqual(requests.map((request) => [request.requestedLocale, request.timeZone]), [[undefined, undefined], ["de-CH", "Europe/Zurich"]]);

// A changed sample renders again; with the simulation on, the pseudo-locale is rendered too and shown.
pseudo = true;
preview.pseudoLocale = "en-XA";
preview.updateSample("name", "Grace");
assert.deepEqual(fire().map((timer) => timer.delay), [150]);
await settle();
assert.deepEqual(preview.result, { kind: "text", value: "Hallo Grace" });
assert.deepEqual(preview.pseudoResult, { kind: "text", value: "Hallo Grace" });
assert.equal(requests.at(-1).locale, "en-XA");
assert.equal(requests.at(-1).timeZone, "Europe/Zurich", "Only the time zone carries over to the pseudo-locale.");
assert.equal(preview.simulatedResult, preview.pseudoResult);

// A reset (a new selection) cancels the pending preview and clears the shown one.
preview.schedule("de.rmf2", "welcome = Neu\n", "welcome", "de");
preview.reset();
assert.deepEqual([preview.busy, preview.ast, preview.result, preview.request], [false, undefined, undefined, undefined]);
const before = requests.length;
assert.equal(fire().length, 0, "The reset cancelled the debounce.");
await settle();
assert.equal(requests.length, before, "A cancelled preview sends nothing.");
assert.equal(preview.result, undefined);
preview.updateSample("name", "Linus");
assert.equal(timers.length, 0, "Samples render only after the active request supplied its AST.");

// A failing host is reported as the preview error.
const failing = new PreviewController({
  bridge: { previewMessage: async () => { throw new Error("offline"); } },
  pseudoLocalization: () => false,
  defaultSample: () => "",
  errorNotice: (error) => `failed: ${error.message}`,
  setTimer: (callback) => { callback(); return 0; },
  clearTimer: () => undefined,
});
failing.schedule("de.rmf2", "x", "welcome", "de");
await settle();
assert.equal(failing.error, "failed: offline");
assert.equal(failing.busy, false);

// The preview error is the previewed message's primary diagnostic, not the first of the catalog (issue #225, P5): a
// syntax error in the message is shown, not "Locale 'de' does not translate 'common.delete'".
const unrelated = { id: "RTR0010", severity: "error", message: "Locale 'de' does not translate 'common.delete'.", path: "de.rmf2", line: 1, column: 1, endLine: 1, endColumn: 1, key: "common_delete", locale: "de" };
const own = { id: "RTR0001", severity: "error", message: "Expected '}}'.", path: "de.rmf2", line: 4, column: 3, endLine: 4, endColumn: 9 };
const broken = new PreviewController({
  bridge: { previewMessage: async () => ({ success: false, diagnostics: [unrelated, own], primaryDiagnostic: own }) },
  pseudoLocalization: () => false,
  defaultSample: () => "",
  errorNotice: (error) => `failed: ${error.message}`,
  setTimer: (callback) => { callback(); return 0; },
  clearTimer: () => undefined,
});
broken.schedule("de.rmf2", "x", "welcome", "de");
await settle();
assert.equal(broken.error, own.message, "The preview must show its primary diagnostic.");
assert.equal(previewFailure({ success: false, diagnostics: [{ ...own, severity: "warning" }, unrelated] }), unrelated.message,
  "Without a primary diagnostic the preview shows the first error, not a warning.");
assert.deepEqual(previewFailure({ success: false, diagnostics: [] }), previewFailure(undefined), "Without diagnostics it shows the generic failure.");

console.log("PASS: the preview controller debounces, renders samples and the pseudo-locale, and drops cancelled previews.");
