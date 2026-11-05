import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import ts from "typescript";

const stored = new Map();
const read = (key) => stored.get(key) ?? null;
const write = (key, value) => stored.set(key, String(value));
globalThis.localStorage = {
  getItem: (key) => stored.get(key) ?? null,
  setItem: (key, value) => stored.set(key, String(value)),
  removeItem: (key) => stored.delete(key),
  clear: () => stored.clear(),
};

async function tsModuleUrl(path, imports = {}) {
  const sourceUrl = new URL(path, import.meta.url);
  let source = await readFile(sourceUrl, "utf8");
  for (const [specifier, url] of Object.entries(imports)) {
    source = source.replaceAll(JSON.stringify(specifier), JSON.stringify(url));
  }
  const transpiled = ts.transpileModule(source, {
    compilerOptions: { module: ts.ModuleKind.ESNext, target: ts.ScriptTarget.ES2022 },
    fileName: sourceUrl.pathname,
  });
  return `data:text/javascript;base64,${Buffer.from(transpiled.outputText).toString("base64")}`;
}

async function importTs(path, imports = {}) {
  return import(await tsModuleUrl(path, imports));
}

const simulation = await importTs("../src/lib/simulation.ts");
const semanticReviewUrl = await tsModuleUrl("../src/lib/semantic-review.ts");
const reviewModel = await importTs("../src/lib/review-model.ts", { "./semantic-review": semanticReviewUrl });
const paletteModule = await importTs("../src/lib/command-palette.ts");

// --- Pseudo-localization comes from the SDK's generator on the preview host ---

// The editor renders en-XA/ar-XB through the host (Rmf2Preview with pseudo-locales), so it shows exactly what a
// --pseudo-locales build produces; the frontend keeps no transform of its own.
assert.equal(simulation.pseudoLocalizeText, undefined, "The frontend must not carry its own pseudo-localization algorithm.");
assert.equal(simulation.simulatePreviewResult, undefined, "Preview results must not be transformed in the frontend.");
assert.deepEqual([...simulation.pseudoLocales], ["en-XA", "ar-XB"], "The SDK's pseudo-locales, accented first.");

// --- Persistence mirrors the appearance module contract ---

assert.deepEqual(simulation.readUiSimulation(read), { pseudoLocalization: false, direction: "ltr" },
  "Defaults must be simulation off and LTR.");
simulation.saveUiSimulation(true, "rtl", write);
assert.deepEqual(simulation.readUiSimulation(read), { pseudoLocalization: true, direction: "rtl" },
  "Simulation settings must persist like appearance settings.");
stored.set("runic-translations.ui-direction", "up");
stored.set("runic-translations.pseudo-localization", "yes");
assert.deepEqual(simulation.readUiSimulation(read), { pseudoLocalization: false, direction: "ltr" },
  "Corrupt stored values must fall back to defaults.");
assert.deepEqual(simulation.readUiSimulation(), { pseudoLocalization: false, direction: "ltr" },
  "An absent native-state reader must fall back to defaults.");

// --- Bidi/mixed-direction quality findings ---

const bidiEntries = [
  { key: "B.Clean", locale: "de", text: "Nur Deutsch" },
  { key: "A.Controls", locale: "de", text: "hidden\u{200F}mark" },
  { key: "C.Mixed", locale: "de", text: "settings \u{0645}\u{0646} menu" },
];
const ltrFindings = reviewModel.bidiIssues(bidiEntries, "ltr");
assert.deepEqual(ltrFindings.map((issue) => issue.key), ["A.Controls"],
  "Invisible bidi controls are flagged regardless of simulation direction.");
assert.equal(ltrFindings[0].kind, "bidi", "Findings must use the shared bidi issue kind.");
const rtlFindings = reviewModel.bidiIssues(bidiEntries, "rtl");
assert.deepEqual(rtlFindings.map((issue) => issue.key), ["A.Controls", "C.Mixed"],
  "Mixed strong-direction runs surface only under RTL simulation; output stays key-ordered.");
assert.ok(rtlFindings.every((issue) => reviewModel.qualityReportCsv([issue]).startsWith('"key","locale","kind","message"\n')),
  "Bidi findings must serialize through the existing quality CSV report.");
assert.deepEqual(reviewModel.bidiIssues([{ key: "K", locale: "de", text: "\u{05E9}\u{05DC}\u{05D5}\u{05DD}" }], "rtl"),
  [], "Pure RTL text is not a mixed-direction finding.");

// --- Palette registration for the three W03 toggles ---

const calls = [];
const record = (name) => () => calls.push(name);
const commands = paletteModule.buildEditorCommandPalette(
  {
    reloadWorkspace: record("reloadWorkspace"),
    openWorkspaceDialog: record("openWorkspaceDialog"),
    createProject: record("createProject"),
    saveDocument: record("saveDocument"),
    undo: record("undo"),
    redo: record("redo"),
    focusMessageSearch: record("focusMessageSearch"),
    saveReview: record("saveReview"),
    discardReview: record("discardReview"),
    setMessageReviewState: record("setMessageReviewState"),
    markVisibleMessages: record("markVisibleMessages"),
    openTerminology: record("openTerminology"),
    openQualityReport: record("openQualityReport"),
    showAbout: record("showAbout"),
    createDiagnosticBundle: record("createDiagnosticBundle"),
    setEditorMode: record("setEditorMode"),
    selectLocale: record("selectLocale"),
    setUiLocale: record("setUiLocale"),
    setThemeMode: record("setThemeMode"),
    toggleLanguagesSection: record("toggleLanguagesSection"),
    toggleMessagesSection: record("toggleMessagesSection"),
    togglePseudoLocalization: record("togglePseudoLocalization"),
    toggleUiDirection: record("toggleUiDirection"),
    toggleArtifactPreview: record("toggleArtifactPreview"),
  },
  {
    locales: [],
    selectedLocale: "en",
    editorMode: "translation",
    uiLocale: "en",
    uiLocales: [{ tag: "en", name: "English" }, { tag: "de", name: "Deutsch" }],
    themeMode: "dark",
    workspaceReady: true,
    searchAvailable: true,
    documentDirty: false,
    canUndo: false,
    canRedo: false,
    reviewEditable: false,
    reviewDirty: false,
    reviewError: false,
    pseudoLocalization: false,
    uiDirection: "ltr",
    artifactPreviewOpen: false,
  },
);
const w03 = Object.fromEntries(["view.toggle-pseudo-localization", "view.toggle-rtl-simulation", "view.toggle-artifact-preview"]
  .map((id) => [id, commands.find((command) => command.id === id)]));
for (const [id, command] of Object.entries(w03)) {
  assert.ok(command, `Palette registry is missing ${id}.`);
  assert.equal(command.group, "view", `${id} must live in the view group.`);
  assert.equal(typeof command.run, "function", `${id} must be runnable.`);
}
assert.equal(w03["view.toggle-pseudo-localization"].keybinding, "Alt+P");
assert.equal(w03["view.toggle-rtl-simulation"].keybinding, "Alt+R");
assert.equal(w03["view.toggle-artifact-preview"].keybinding, "Alt+B");
w03["view.toggle-pseudo-localization"].run();
w03["view.toggle-rtl-simulation"].run();
w03["view.toggle-artifact-preview"].run();
assert.deepEqual(calls.slice(-3),
  ["togglePseudoLocalization", "toggleUiDirection", "toggleArtifactPreview"],
  "W03 entries must dispatch through their handed-in handlers.");

console.log(`PASS: W03 simulation uses the host's pseudo-locales, persists across sessions, emit bidi findings into the shared quality model, and register Alt+P/Alt+B/Alt+R palette entries (${commands.length} total).`);
