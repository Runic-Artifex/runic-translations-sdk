import { readFile } from "node:fs/promises";
import ts from "typescript";

const sourceUrl = new URL("../src/lib/command-palette.ts", import.meta.url);
const source = await readFile(sourceUrl, "utf8");
const transpiled = ts.transpileModule(source, {
  compilerOptions: { module: ts.ModuleKind.ESNext, target: ts.ScriptTarget.ES2022 },
  fileName: sourceUrl.pathname,
});
const palette = await import(`data:text/javascript;base64,${Buffer.from(transpiled.outputText).toString("base64")}`);

const bridgeSource = await readFile(new URL("../src/lib/editor-bridge.ts", import.meta.url), "utf8");
const interfaceBody = /export interface EditorBridge \{([\s\S]*?)\n\}/.exec(bridgeSource)?.[1];
if (interfaceBody === undefined) throw new Error("EditorBridge façade interface was not found.");
const bridgeMethods = [...interfaceBody.matchAll(/^ {2}(\w+)\(/gm)].map((match) => match[1]);
assert(bridgeMethods.length >= 15, "EditorBridge façade surface collapsed below its contract size.");
for (const operation of palette.paletteBridgeOperations) {
  assert(bridgeMethods.includes(operation),
    `Palette claims bridge operation '${operation}' that the typed façade does not expose.`);
}
for (const name of ["load", "save", "undo", "redo", "saveReview", "about", "createDiagnosticBundle"]) {
  assert(palette.paletteBridgeOperations.includes(name), `Bridge operation '${name}' lost palette coverage.`);
}

const actions = stubActions();
const state = {
  locales: [{ tag: "de", name: "German" }, { tag: "en", name: "English" }, { tag: "fr", name: "French" }],
  selectedLocale: "en",
  editorMode: "translation",
  uiLocale: "en",
  themeMode: "dark",
  workspaceReady: true,
  searchAvailable: true,
  documentDirty: true,
  canUndo: true,
  canRedo: false,
  reviewEditable: true,
  reviewDirty: false,
  reviewError: false,
  pseudoLocalization: false,
  uiDirection: "ltr",
  artifactPreviewOpen: false,
};
const commands = palette.buildEditorCommandPalette(actions, state);

const ids = commands.map((command) => command.id);
assert(new Set(ids).size === ids.length, "Palette registry produced duplicate command ids.");
for (const command of commands) {
  assert(typeof command.title === "string" && command.title.length > 0, `Command ${command.id} has no title.`);
  assert(typeof command.run === "function", `Command ${command.id} is not runnable.`);
}
for (const expected of [
  "workspace.reload", "workspace.open", "workspace.new-project",
  "document.save", "document.undo", "document.redo",
  "review.save-workflow", "review.discard-workflow", "review.state-approved",
  "review.mark-visible-approved", "review.quality-report",
  "view.mode-raw", "view.focus-search", "view.toggle-messages",
  "view.toggle-pseudo-localization", "view.toggle-rtl-simulation", "view.toggle-artifact-preview",
  "help.about", "help.diagnostic-bundle",
]) {
  assert(ids.includes(expected), `Registry is missing required command '${expected}'.`);
}
const keybindings = commands
  .map((command) => command.keybinding)
  .filter((binding) => binding !== undefined);
assert(new Set(keybindings).size === keybindings.length, "Palette keybindings must be unique.");
for (const locale of state.locales) {
  assert(ids.includes(`view.locale:${locale.tag}`), `Registry is missing the language entry for ${locale.tag}.`);
}
const bridgesUsed = commands.map((command) => command.bridge).filter(Boolean);
assert(bridgesUsed.length === palette.paletteBridgeOperations.length,
  "Every covered bridge operation must appear on exactly one palette entry.");

const disabled = commands.filter((command) => command.disabled === true).map((command) => command.id);
assert(disabled.includes("document.redo"), "Redo should inherit canRedo=false as disabled.");
assert(!disabled.includes("document.save"), "A dirty document must stay savable from the palette.");
assert(commands.find((command) => command.id === "view.locale:en").disabled === true,
  "The active locale entry should be disabled.");
assert(commands.find((command) => command.id === "view.locale:fr").disabled !== true,
  "Inactive locales should remain selectable.");

commands.find((command) => command.id === "document.save").run();
assert(actions.calls.at(-1) === "saveDocument", "Save entry did not dispatch through the handed-in handler.");
commands.find((command) => command.id === "review.state-approved").run();
assert(actions.calls.at(-1) === "setMessageReviewState:approved",
  "Review transition did not reuse the page handler.");
for (const [id, call] of [
  ["view.toggle-pseudo-localization", "togglePseudoLocalization"],
  ["view.toggle-rtl-simulation", "toggleUiDirection"],
  ["view.toggle-artifact-preview", "toggleArtifactPreview"],
]) {
  commands.find((command) => command.id === id).run();
  assert(actions.calls.at(-1) === call, `Simulation entry ${id} did not dispatch through ${call}.`);
}
const simulatedIds = ["view.toggle-pseudo-localization", "view.toggle-rtl-simulation", "view.toggle-artifact-preview"];
const simulatedTitles = palette
  .buildEditorCommandPalette(actions, { ...state, pseudoLocalization: true, uiDirection: "rtl", artifactPreviewOpen: true })
  .filter((command) => simulatedIds.includes(command.id))
  .map((command) => command.title);
assert(JSON.stringify(simulatedTitles) === JSON.stringify([
  "Disable pseudo-localization simulation",
  "Simulate right-to-left layout off",
  "Hide compiled artifact preview",
]), "Active simulation entries must offer the off action.");

const blocked = palette.buildEditorCommandPalette(
  actions,
  { ...state, reviewEditable: false, reviewError: true },
).filter((command) => command.disabled === true).map((command) => command.id);
assert(blocked.includes("review.state-approved") && blocked.includes("review.terminology"),
  "Locked review workflow must disable review transitions in the palette.");

const unfiltered = palette.filterCommands(commands, "");
assert(unfiltered.length === commands.length &&
  unfiltered.every((command, index) => command === commands[index]),
  "Empty queries must preserve registry order.");

function command(id, title, keywords) {
  return { id, title, group: "view", keywords, run() {} };
}
const ranked = palette.filterCommands([
  command("substring", "Undo saved change"),
  command("prefix", "Save document"),
  command("keyword", "Approve message", "saves via workflow sidecar"),
], "save");
assert(ranked.map((entry) => entry.id).join("|") === "prefix|substring|keyword",
  "Ranking must prefer title prefixes over later word starts and keyword fuzz.");

const fuzzy = palette.filterCommands([command("approve", "Approve message")], "apmsg");
assert(fuzzy.length === 1 && fuzzy[0].id === "approve",
  "Case-insensitive subsequence matching failed across word boundaries.");
assert(palette.filterCommands(commands, "zzzqx").length === 0,
  "Non-matching queries must produce an empty result set.");
const jsonMatch = palette.filterCommands(commands, "json").map((entry) => entry.id);
assert(jsonMatch[0] === "view.mode-raw",
  "Direct keyword text (json) should surface raw-mode ahead of incidental matches.");

const grouped = palette.groupCommands(commands);
assert(grouped.map((group) => group.id).join(",") ===
  ["workspace", "document", "review", "view", "help"].join(","),
  "Group ordering diverged from the canonical group order.");
let flat = 0;
for (const group of grouped) {
  assert(group.label.length > 0, `Group ${group.id} lost its label.`);
  for (const entry of group.commands) assert(entry === commands[flat++], "Grouping reordered entries.");
}

assert(palette.movePaletteSelection(0, -1, 5) === 4, "ArrowUp at the top must wrap to the last item.");
assert(palette.movePaletteSelection(4, 1, 5) === 0, "ArrowDown at the bottom must wrap to the first item.");
assert(palette.movePaletteSelection(2, 1, 5) === 3 && palette.movePaletteSelection(2, -1, 5) === 1,
  "Arrow navigation drifted off by one.");
assert(palette.movePaletteSelection(9, 1, 0) === 0 && palette.movePaletteSelection(0, -1, 0) === 0,
  "Empty result sets must clamp navigation to index zero.");
assert(palette.movePaletteSelection(0, 7, 5) === 2, "Large deltas must wrap deterministically.");

console.log(`PASS: command palette exposes ${commands.length} actions covering all ${palette.paletteBridgeOperations.length} user-facing bridge operations; filter ranking and wrap-around keyboard navigation are deterministic.`);

function stubActions() {
  const calls = [];
  const record = (name) => () => calls.push(name);
  return {
    calls,
    reloadWorkspace: record("reloadWorkspace"),
    openWorkspaceDialog: record("openWorkspaceDialog"),
    createProject: record("createProject"),
    openInterchange: record("openInterchange"),
    saveDocument: record("saveDocument"),
    undo: record("undo"),
    redo: record("redo"),
    focusMessageSearch: record("focusMessageSearch"),
    saveReview: record("saveReview"),
    discardReview: record("discardReview"),
    setMessageReviewState: (value) => calls.push(`setMessageReviewState:${value}`),
    markVisibleMessages: (value) => calls.push(`markVisibleMessages:${value}`),
    openTerminology: record("openTerminology"),
    openQualityReport: record("openQualityReport"),
    showAbout: record("showAbout"),
    createDiagnosticBundle: record("createDiagnosticBundle"),
    setEditorMode: (mode) => calls.push(`setEditorMode:${mode}`),
    selectLocale: (locale) => calls.push(`selectLocale:${locale}`),
    setUiLocale: (locale) => calls.push(`setUiLocale:${locale}`),
    setThemeMode: (mode) => calls.push(`setThemeMode:${mode}`),
    toggleLanguagesSection: record("toggleLanguagesSection"),
    toggleMessagesSection: record("toggleMessagesSection"),
    togglePseudoLocalization: record("togglePseudoLocalization"),
    toggleUiDirection: record("toggleUiDirection"),
    toggleArtifactPreview: record("toggleArtifactPreview"),
  };
}

function assert(condition, message) {
  if (!condition) throw new Error(message);
}
