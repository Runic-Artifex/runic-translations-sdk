import { readFile } from "node:fs/promises";
import ts from "typescript";

const sourceUrl = new URL("../src/lib/command-palette.ts", import.meta.url);
// The palette's one import, the shortcut notation, is inlined the same way.
const transpileTs = (text, url) => ts.transpileModule(text, {
  compilerOptions: { module: ts.ModuleKind.ESNext, target: ts.ScriptTarget.ES2022 },
  fileName: url.pathname,
}).outputText;
const shortcutLabelsUrl = new URL("../src/lib/shortcut-labels.ts", import.meta.url);
const shortcutLabelsModule = `data:text/javascript;base64,${Buffer.from(transpileTs(await readFile(shortcutLabelsUrl, "utf8"), shortcutLabelsUrl)).toString("base64")}`;
const source = (await readFile(sourceUrl, "utf8")).replaceAll('"./shortcut-labels"', JSON.stringify(shortcutLabelsModule));
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
for (const name of ["load", "save", "undo", "redo", "about", "createDiagnosticBundle"]) {
  assert(palette.paletteBridgeOperations.includes(name), `Bridge operation '${name}' lost palette coverage.`);
}

const actions = stubActions();
const state = {
  locales: [{ tag: "de", name: "German" }, { tag: "en", name: "English" }, { tag: "fr", name: "French" }],
  selectedLocale: "en",
  editorMode: "translation",
  uiLocale: "en",
  uiLocales: [{ tag: "en", name: "English" }, { tag: "de", name: "Deutsch" }],
  themeMode: "dark",
  workspaceReady: true,
  searchAvailable: true,
  unsaved: true,
  canUndo: true,
  canRedo: false,
  reviewEditable: true,
  reviewDirty: false,
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
  "document.next-message", "document.previous-message", "document.save-and-next",
  "review.discard-workflow", "review.state-approved",
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
assert(!disabled.includes("document.save"), "Unsaved work must stay savable from the palette.");
assert(!ids.includes("review.save-workflow"), "Review changes are saved by the one save, not by a separate entry.");
assert(commands.find((command) => command.id === "view.locale:en").disabled === true,
  "The active locale entry should be disabled.");
assert(commands.find((command) => command.id === "view.locale:fr").disabled !== true,
  "Inactive locales should remain selectable.");

commands.find((command) => command.id === "document.save").run();
assert(actions.calls.at(-1) === "saveAll", "Save entry did not dispatch through the handed-in handler.");
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

// The palette names Undo and Redo as the toolbar does: the unsaved step, else the saved change (#225 P4).
const named = palette.buildEditorCommandPalette(actions, {
  ...state, canRedo: true, undoTitle: "Undo: edit in files.selected", redoTitle: "Redo saved change: Save en.rmf2",
});
assert(named.find((command) => command.id === "document.undo").title === "Undo: edit in files.selected",
  "The palette's Undo is not named after the unsaved step.");
assert(named.find((command) => command.id === "document.redo").title === "Redo saved change: Save en.rmf2",
  "The palette's Redo is not named after the saved change.");
assert(named.find((command) => command.id === "document.redo").disabled !== true, "Redo with a step must be enabled.");
assert(commands.find((command) => command.id === "document.undo").title === "Undo saved change",
  "Without a name the palette's Undo is the saved-change undo.");
commands.find((command) => command.id === "document.undo").run();
assert(actions.calls.at(-1) === "undo", "The palette's Undo did not dispatch through the page's undo.");

// Next missing, Previous missing and Save and next (D074.1): titled after the step kind, with their shortcuts, disabled
// without a message to go to, and dispatched through the page's handlers.
const stepping = palette.buildEditorCommandPalette(actions, {
  ...state, shortcutPlatform: "other", stepKind: "missing", canStepNext: true, canStepPrevious: false, saveAndNextKind: "missing", canSaveAndNext: true,
});
const stepEntry = (commandsList, id) => commandsList.find((command) => command.id === id);
assert(stepEntry(stepping, "document.next-message").title === "Next missing", "Next missing is misnamed.");
assert(stepEntry(stepping, "document.next-message").keybinding === "Alt+↓", "Next missing does not show Alt+↓.");
assert(stepEntry(stepping, "document.next-message").disabled !== true, "Next missing with a target must be enabled.");
assert(stepEntry(stepping, "document.previous-message").title === "Previous missing", "Previous missing is misnamed.");
assert(stepEntry(stepping, "document.previous-message").keybinding === "Alt+↑", "Previous missing does not show Alt+↑.");
assert(stepEntry(stepping, "document.previous-message").disabled === true, "Previous missing without a target must be disabled.");
assert(stepEntry(stepping, "document.save-and-next").title === "Save and next missing", "Save and next is misnamed.");
assert(stepEntry(stepping, "document.save-and-next").keybinding === "Ctrl+Enter", "Save and next does not show Ctrl+Enter.");
const macStepping = palette.buildEditorCommandPalette(actions, { ...state, shortcutPlatform: "mac", stepKind: "stale", saveAndNextKind: "stale" });
assert(stepEntry(macStepping, "document.next-message").title === "Next outdated", "The outdated filter does not rename Next.");
assert(stepEntry(macStepping, "document.next-message").keybinding === "⌥↓", "Next does not show ⌥↓ on macOS.");
assert(stepEntry(macStepping, "document.save-and-next").keybinding === "⌘Enter", "Save and next does not show ⌘Enter on macOS.");
assert(stepEntry(macStepping, "document.save-and-next").title === "Save and next outdated");
assert(stepEntry(macStepping, "document.next-message").disabled === true, "Next without a target must be disabled.");
const reviewStepping = palette.buildEditorCommandPalette(actions, { ...state, stepKind: "needs-review" });
assert(stepEntry(reviewStepping, "document.next-message").title === "Next to review", "The to-review filter does not rename Next.");
assert(stepEntry(reviewStepping, "document.previous-message").title === "Previous to review");
for (const [id, call] of [
  ["document.next-message", "nextMessage"],
  ["document.previous-message", "previousMessage"],
  ["document.save-and-next", "saveAndNext"],
]) {
  stepEntry(stepping, id).run();
  assert(actions.calls.at(-1) === call, `${id} did not dispatch through ${call}.`);
}
const stepUi = { text: (key) => `[${key}]` };
assert(stepEntry(palette.buildEditorCommandPalette(actions, { ...state, stepKind: "needs-review" }, stepUi), "document.next-message").title === "[ui_workbench_next_needs_review]",
  "Next is not named through the interface text.");
assert(palette.filterCommands(stepping, "next missing")[0].id === "document.next-message", "Searching \"next missing\" does not find Next missing first.");

// A content-editing project (D074.2) shows neither Next nor Previous, no review workflow and one language: the palette
// leaves out their commands. Save and next goes to the next text.
const contentCommands = palette.buildEditorCommandPalette(actions, {
  ...state, projectKind: "content", stepKind: "text", saveAndNextKind: "text", canSaveAndNext: true,
});
const contentIds = contentCommands.map((command) => command.id);
for (const hidden of ["document.next-message", "document.previous-message", "view.toggle-languages"]) {
  assert(!contentIds.includes(hidden), `A content-editing project's palette offers ${hidden}.`);
}
assert(contentCommands.every((command) => command.group !== "review"), "A content-editing project's palette offers review commands.");
assert(stepEntry(contentCommands, "document.save-and-next").title === "Save and next text", "Save and next is not named after texts.");
assert(stepEntry(contentCommands, "document.save-and-next").disabled !== true);
for (const kept of ["document.save", "document.undo", "view.mode-raw", "view.toggle-messages", "view.toggle-pseudo-localization"]) {
  assert(contentIds.includes(kept), `A content-editing project's palette lost ${kept}.`);
}
assert(stepEntry(palette.buildEditorCommandPalette(actions, { ...state, projectKind: "translation" }), "review.state-approved") !== undefined,
  "A translation project's palette lost its review commands.");
// The base locale of a translation project holds the source text (#225 R1): no review states to set on its messages;
// the project's terminology, quality report and unsaved review changes stay, and so do Next and Previous (over texts).
const sourceIds = palette.buildEditorCommandPalette(actions, {
  ...state, selectedLocale: "de", projectKind: "translation", textRole: "source", stepKind: "text",
}).map((command) => command.id);
for (const hidden of ["review.state-needs-review", "review.state-approved", "review.state-translated", "review.state-draft",
  "review.mark-visible-needs-review", "review.mark-visible-approved"]) {
  assert(!sourceIds.includes(hidden), `The source text's palette offers ${hidden}.`);
}
for (const kept of ["review.terminology", "review.quality-report", "review.discard-workflow", "document.next-message", "view.toggle-languages"]) {
  assert(sourceIds.includes(kept), `The source text's palette lost ${kept}.`);
}

const blocked = palette.buildEditorCommandPalette(
  actions,
  { ...state, reviewEditable: false },
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
const sourceMatch = palette.filterCommands(commands, "source").map((entry) => entry.id);
assert(sourceMatch[0] === "view.mode-raw",
  "Direct source-mode text should surface raw-mode ahead of incidental matches.");

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
    saveAll: record("saveAll"),
    undo: record("undo"),
    redo: record("redo"),
    nextMessage: record("nextMessage"),
    previousMessage: record("previousMessage"),
    saveAndNext: record("saveAndNext"),
    focusMessageSearch: record("focusMessageSearch"),
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
