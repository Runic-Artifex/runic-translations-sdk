import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import { compile } from "svelte/compiler";
import ts from "typescript";
import { readPageSourceFiles } from "./page-sources.mjs";
import { readUiMessages } from "./ui-messages.mjs";

// The keyboard module's one import, the shortcut platform, is inlined.
const shortcutLabels = await loadModule(new URL("../src/lib/shortcut-labels.ts", import.meta.url), true);
const keyboard = await loadModule(new URL("../src/lib/editor-keyboard.ts", import.meta.url), false, { "./shortcut-labels": shortcutLabels });
for (const [event, textEditingTarget, expected, draftField = false] of [
  [key("z", { ctrlKey: true }), false, "undo"],
  [key("z", { ctrlKey: true, shiftKey: true }), false, "redo"],
  [key("y", { metaKey: true }), false, "redo"],
  [key("s", { ctrlKey: true }), true, "save"],
  [key("k", { metaKey: true }), true, "toggle-command-search"],
  [key("p", { altKey: true }), false, "toggle-pseudo-localization"],
  [key("r", { altKey: true }), false, "toggle-right-to-left"],
  [key("b", { altKey: true }), false, "toggle-artifact-preview"],
  [key("z", { ctrlKey: true }), true, undefined],
  [key("y", { ctrlKey: true }), true, undefined],
  [key("p", { altKey: true }), true, undefined],
  // A draft field (composer pattern, message source, file view) has the editor's undo history (#225 P4).
  [key("z", { ctrlKey: true }), true, "undo", true],
  [key("Z", { ctrlKey: true, shiftKey: true }), true, "redo", true],
  [key("y", { ctrlKey: true }), true, "redo", true],
  [key("z", { metaKey: true }), true, "undo", true],
  [key("z", { metaKey: true, shiftKey: true }), true, "redo", true],
  // AltGr (Ctrl+Alt) types characters on some layouts; it is never undo.
  [key("z", { ctrlKey: true, altKey: true }), true, undefined, true],
  [key("z", { ctrlKey: true, altKey: true }), false, undefined],
  [key("p", { altKey: true }), true, undefined, true],
  [key("s", { ctrlKey: true }), true, "save", true],
]) {
  assert.equal(keyboard.editorShortcut(event, textEditingTarget, draftField), expected,
    `Keyboard route for ${event.key} did not preserve the owned editor command boundary.`);
}

// The message steps (D074.1): Alt+↓/↑ and Ctrl+Enter (⌘Enter) outside text fields and in draft fields; never in another
// text field, a widget with its own use for the keys (select, combobox, listbox, menu, dialog), during an input method
// composition, with Shift, or with AltGr (Ctrl+Alt on Windows; elsewhere AltGr does not set Alt). On macOS ⌥↑/⌥↓ move
// the caret by paragraph, so there they step only outside text fields.
for (const [event, textEditingTarget, draftField, context, expected] of [
  [key("ArrowDown", { altKey: true }), false, false, { platform: "other" }, "next-message"],
  [key("ArrowUp", { altKey: true }), false, false, { platform: "other" }, "previous-message"],
  [key("ArrowDown", { altKey: true }), true, true, { platform: "other" }, "next-message"],
  [key("ArrowUp", { altKey: true }), true, true, { platform: "other" }, "previous-message"],
  [key("ArrowDown", { altKey: true }), false, false, { platform: "mac" }, "next-message"],
  [key("ArrowDown", { altKey: true }), true, true, { platform: "mac" }, undefined],
  [key("ArrowDown", { altKey: true }), true, false, { platform: "other" }, undefined],
  [key("ArrowDown", { altKey: true }), false, false, { platform: "other", widgetTarget: true }, undefined],
  [key("ArrowDown", { altKey: true, shiftKey: true }), false, false, { platform: "other" }, undefined],
  [key("ArrowDown", { altKey: true, ctrlKey: true }), false, false, { platform: "other" }, undefined],
  [key("ArrowDown", { altKey: true, ctrlKey: true }), true, true, { platform: "other" }, undefined],
  [key("ArrowDown", { altKey: true, metaKey: true }), false, false, { platform: "mac" }, undefined],
  [key("ArrowDown"), false, false, { platform: "other" }, undefined],
  [{ ...key("ArrowDown", { altKey: true }), isComposing: true }, true, true, { platform: "other" }, undefined],
  [key("Enter", { ctrlKey: true }), false, false, { platform: "other" }, "save-and-next"],
  [key("Enter", { ctrlKey: true }), true, true, { platform: "other" }, "save-and-next"],
  [key("Enter", { metaKey: true }), true, true, { platform: "mac" }, "save-and-next"],
  [key("Enter", { ctrlKey: true }), true, false, { platform: "other" }, undefined],
  [key("Enter", { ctrlKey: true }), false, false, { platform: "other", widgetTarget: true }, undefined],
  [key("Enter", { ctrlKey: true, altKey: true }), true, true, { platform: "other" }, undefined],
  [key("Enter", { ctrlKey: true, shiftKey: true }), true, true, { platform: "other" }, undefined],
  [{ ...key("Enter", { ctrlKey: true }), isComposing: true }, true, true, { platform: "other" }, undefined],
  [key("Enter"), true, true, { platform: "other" }, undefined],
  // AltGr (Ctrl+Alt) still types characters; the existing Alt letters stay outside text fields only.
  [key("p", { altKey: true, ctrlKey: true }), false, false, { platform: "other" }, undefined],
  [key("b", { altKey: true }), false, false, { platform: "other" }, "toggle-artifact-preview"],
]) {
  assert.equal(keyboard.editorShortcut(event, textEditingTarget, draftField, context), expected,
    `Message step route for ${JSON.stringify(event)} in ${JSON.stringify({ textEditingTarget, draftField, ...context })}.`);
}

const componentEntries = await Promise.all([
  "CommandPalette.svelte",
  "EditorToolbar.svelte",
  "InlineMessageEditor.svelte",
  "MessageComposer.svelte",
  "MessageList.svelte",
  "MessageToolbar.svelte",
  "PatternEditor.svelte",
  "TranslationEditor.svelte",
].map(async (name) => [name, await readFile(new URL(`../src/lib/${name}`, import.meta.url), "utf8")]));
const [english, german] = await Promise.all([
  readUiMessages("en"),
  readUiMessages("de"),
]);
// The page: its composition root, screens, dialogs and controllers, each compiled on its own and read as one.
const pageFiles = await readPageSourceFiles();
const sources = Object.fromEntries([
  ...componentEntries,
  ...pageFiles.filter(([path]) => path.endsWith(".svelte")),
]);

for (const [name, source] of Object.entries(sources)) {
  const result = compile(source, { generate: "server" });
  const accessibilityWarnings = result.warnings.filter((warning) => warning.code.startsWith("a11y_"));
  assert.deepEqual(accessibilityWarnings, [], `${name} has compiler-reported accessibility violations.`);
}

const commandPalette = sources["CommandPalette.svelte"];
assert.match(commandPalette, /bind:ref=\{searchInput\}/, "The command-search input is not an owned focus target.");
assert.match(commandPalette, /onOpenAutoFocus=\{focusSearch\}/, "Opening the command palette does not own its focus route.");
assert.match(commandPalette, /function focusSearch[\s\S]*searchInput\?\.focus\(\)/,
  "Opening the command palette does not focus command search.");
assert.match(commandPalette, /aria-label=\{ui\.text\("ui_command_palette_search_aria_label"\)\}/,
  "The command-search input is not named through the UI catalog.");
assert.equal(english.ui_command_palette_search_aria_label, "Search commands",
  "The English command-search accessibility label changed unexpectedly.");
assert.equal(german.ui_command_palette_search_aria_label, "Befehle suchen",
  "The German command-search accessibility label is missing or incorrect.");
assert.match(commandPalette, /role="listbox"[\s\S]*aria-activedescendant=/,
  "Command search does not expose its active result to assistive technology.");
assert.match(commandPalette, /role="option"[\s\S]*aria-selected=/,
  "Command results do not expose selected state.");
assertOrder(commandPalette, 'aria-label={ui.text("ui_command_palette_search_aria_label")}', 'role="listbox"',
  "Command-search focus order no longer reaches results after its search field.");

const toolbar = sources["MessageToolbar.svelte"];
assert.match(toolbar, /<label class="sr-only" for="message-search">\{ui\.text\("ui_message_toolbar_search_messages"\)\}<\/label>/,
  "Message search is not named through the UI catalog.");
assert.equal(english.ui_message_toolbar_search_messages, "Search messages",
  "The English message-search label changed unexpectedly.");
assert.equal(german.ui_message_toolbar_search_messages, "Nachrichten suchen",
  "The German message-search label is missing or incorrect.");
assert.match(toolbar, /id="message-search"/, "Message search label is not bound to its input.");
assert.match(toolbar, /aria-label=\{`\$\{filterLabel\}:/, "Message filter trigger is unnamed.");
assertOrder(toolbar, 'id="message-search"', 'aria-label={`${filterLabel}:',
  "Message search must precede its keyboard-reachable filter control.");

const messages = sources["MessageList.svelte"];
assert.match(messages, /<nav[\s\S]*aria-label=\{labels\.messages\}/, "Message navigation has no catalog-backed landmark name.");
assert.match(messages, /aria-label=\{labels\.bulkActions\}/, "Message bulk actions are unnamed.");
assert.match(messages, /aria-label=\{labels\.addMessage\}/, "Add-message control is unnamed.");
// The message list is an ARIA tree (#225 M1): virtualized rows are flat tree items that state their level, position
// and expansion, with one roving tab stop and the tree keyboard model.
assert.match(messages, /role="tree"\s+aria-label=\{labels\.messages\}/, "The message tree is unnamed.");
for (const attribute of ["aria-expanded={row.expanded}", "aria-level={row.level}", "aria-setsize={row.setSize}",
  "aria-posinset={row.posInSet}", "aria-selected={selectedKey === row.key}", "tabindex={index === focusIndex ? 0 : -1}",
  "onkeydown={handleTreeKeydown}"]) {
  assert.ok(messages.includes(attribute), `Message tree items omit ${attribute}.`);
}
assert.equal(messages.match(/role="treeitem"/g)?.length, 2, "Group and message rows must both be tree items.");
const treeKeyboard = messages.slice(messages.indexOf("function handleTreeKeydown"), messages.indexOf("function groupCountText"));
for (const key of ["ArrowDown", "ArrowUp", "Home", "End", "ArrowRight", "ArrowLeft", "Enter", " "]) {
  assert.ok(treeKeyboard.includes(`case "${key}":`), `The message tree does not handle ${JSON.stringify(key)}.`);
}
assert.match(treeKeyboard, /row\.kind === "group" && !row\.expanded\) setExpanded\(row, true\)/, "Right does not expand a collapsed group.");
assert.match(treeKeyboard, /row\.kind === "group" && row\.expanded\) setExpanded\(row, false\)/, "Left does not collapse an expanded group.");
assert.match(treeKeyboard, /row\.parentId !== undefined\) void focusRow\(rowIndex\.get\(row\.parentId\)/, "Left does not move to the parent group.");
assert.match(treeKeyboard, /direction === "rtl"/, "Left and Right do not follow a right-to-left interface.");
assert.match(messages, /if \(focusIndex < viewport\.start[\s\S]*if \(focusIndex >= viewport\.end/,
  "The virtualized tree can lose its only tab stop when the focus row scrolls out of view.");
for (const key of ["app_messages", "app_message_bulk_actions", "app_add_message"]) {
  assert.equal(typeof english[key], "string", `English project omits ${key}.`);
  assert.equal(typeof german[key], "string", `German project omits ${key}.`);
}

const editorToolbar = sources["EditorToolbar.svelte"];
assert.match(editorToolbar, /aria-label=\{undoLabel\} title=\{`\$\{undoLabel\} \(\$\{shortcutLabel\("Mod\+Z"\)\}\)`\}/, "Undo is unnamed or lacks its platform shortcut.");
assert.match(editorToolbar, /aria-label=\{redoLabel\} title=\{`\$\{redoLabel\} \(\$\{shortcutLabel\("Mod\+Shift\+Z"\)\}\)`\}/, "Redo is unnamed or lacks its platform shortcut.");
// Undo names the unsaved step it takes back, else the saved change (#225 P4); the palette shows the same names.
for (const [key, en, de] of [
  ["ui_toolbar_undo_saved_change", "Undo saved change", "Gespeicherte Änderung rückgängig machen"],
  ["ui_toolbar_redo_saved_change", "Redo saved change", "Gespeicherte Änderung wiederholen"],
  ["ui_toolbar_undo_saved_change_named", "Undo saved change: {$change}", "Gespeicherte Änderung rückgängig machen: {$change}"],
  ["ui_toolbar_redo_saved_change_named", "Redo saved change: {$change}", "Gespeicherte Änderung wiederholen: {$change}"],
  ["ui_toolbar_undo_unsaved", "Undo: {$change} in {$message}", "Rückgängig: {$change} in {$message}"],
  ["ui_toolbar_redo_unsaved", "Redo: {$change} in {$message}", "Wiederholen: {$change} in {$message}"],
  ["ui_history_change_edit", "edit", "Bearbeitung"],
  ["ui_history_change_structure", "structure change", "Strukturänderung"],
  ["ui_history_change_quick_fix", "quick fix", "Schnellkorrektur"],
  ["ui_history_change_copy_source", "Copy source", "Ausgangstext übernehmen"],
  ["ui_history_change_file_edit", "file edit", "Dateibearbeitung"],
  ["ui_history_change_recovery", "draft recovery", "Entwurfswiederherstellung"],
]) {
  assert.match(english[key] ?? "", new RegExp(`${en.replace(/[.*+?^${}()|[\]\\]/g, "\\$&")}`), `English ${key} changed or is missing.`);
  assert.match(german[key] ?? "", new RegExp(`${de.replace(/[.*+?^${}()|[\]\\]/g, "\\$&")}`), `German ${key} changed or is missing.`);
}
assert.match(editorToolbar, /aria-label=\{saving \? savingLabel/, "Save control is unnamed.");
assertOrder(editorToolbar, "Undo2Icon", "Redo2Icon", "SaveIcon",
  "Editor toolbar focus order must remain undo, redo, then save.");

const page = pageFiles.map(([, text]) => text).join("\n");
assert.match(page, /<svelte:window onkeydown=\{handleKeyboard\}/,
  "Owned keyboard dispatch is not installed on the Editor window.");
// Ctrl+Z in a draft field undoes the unsaved edit, not the field's browser history, and so does the browser's own undo.
// A content-editing project (D074.2) hides Next and Previous, so their keys are not bound there.
assert.match(page, /editorShortcut\(event, isTextEditingTarget\(event\.target\), draftField\(event\.target\) !== undefined,\s*\{ widgetTarget: stepWidgetTarget\(event\.target\), messageSteps: !view\.contentProject \}\)/,
  "Keyboard dispatch does not tell draft fields from other text fields, widgets from the rest, or whether Next is shown.");
assert.match(page, /function stepWidgetTarget[\s\S]*?closest\('select, \[role="combobox"\], \[role="listbox"\], \[role="menu"\], \[role="menubar"\], \[role="dialog"\], \[role="alertdialog"\]'\)/,
  "The message steps do not leave selects, comboboxes, listboxes, menus and dialogs alone.");
assert.match(page, /shortcut === "save-and-next"[\s\S]*?saveAndNext\(\)[\s\S]*?shortcut === "next-message"\) stepMessage\("next"\)[\s\S]*?shortcut === "previous-message"\) stepMessage\("previous"\)/,
  "Ctrl+Enter and Alt+↓/↑ are not dispatched to Save and next and the message steps.");
assert.match(page, /onbeforeinput=\{handleBeforeInput\}/, "The browser's own undo of a draft field is not routed to the editor.");
assert.match(page, /inputType === "historyUndo"[\s\S]*undo\(\)[\s\S]*inputType === "historyRedo"[\s\S]*redo\(\)/,
  "historyUndo and historyRedo of a draft field are not the editor's undo and redo.");
// The field's pending input reaches the draft before the step is taken back; then the field keeps focus and caret.
assert.match(page, /async #stepHistory[\s\S]*?await drafts\.settledAll\(\)[\s\S]*?drafts\.undo\(path\)[\s\S]*?restoreCaret\(field, shown\)/,
  "Undo does not flush the field's pending input first or does not restore the caret.");
assert.match(page, /onundo=\{undo\}/, "The toolbar's Undo does not use the unsaved-edit history.");
assert.match(page, /onredo=\{redo\}/, "The toolbar's Redo does not use the unsaved-edit history.");
assert.match(page, /undoTitle: undoLabel,\s*redoTitle: redoLabel/, "The palette's Undo and Redo are not named as the toolbar's.");
for (const [name, pattern] of [
  ["TranslationEditor.svelte", /id="translation-value"\s*data-draft-field/],
  ["MessageComposer.svelte", /<Textarea value=\{source\}[^>]*data-draft-field/],
  ["PatternEditor.svelte", /<Textarea value=\{visiblePattern\}[^>]*data-draft-field/],
  ["InlineMessageEditor.svelte", /<textarea[^>]*data-draft-field/],
]) assert.match(sources[name], pattern, `${name} does not mark its draft field.`);
// The composer's fields show the draft again after an undo: text typed before it belongs to an older revision.
assert.match(sources["TranslationEditor.svelte"], /<MessageComposer[^>]*\{revision\}/, "The composer does not receive the history revision.");
assert.match(sources["MessageComposer.svelte"], /<PatternEditor[^>]*\{revision\}/, "The pattern editor does not receive the history revision.");
assert.match(sources["MessageComposer.svelte"], /pendingSource\.revision === revision/, "Typed message source outlives an undo.");
assert.match(sources["PatternEditor.svelte"], /pendingPattern\.revision === revision/, "A typed pattern outlives an undo.");
assert.match(page, /<main class="recovery-shell">[\s\S]*<h1>\{ui\.text\("ui_page_recovery_title"\)\}<\/h1>/,
  "Recovery view has no named primary heading.");
assertOrder(page, 'ui.text("ui_page_recovery_restore_before")', 'ui.text("ui_page_recovery_complete")',
  "Recovery focus order must offer rollback before complete.");
assert.match(page, /<section class="grid gap-3 rounded-xl border p-4" aria-labelledby="local-state-title">/,
  "The user-owned local state has no inspectable, named privacy boundary.");
assert.match(page, /ui\.text\("ui_page_about_clear_local_state"\)/, "The user-owned local state cannot be cleared.");
assert.match(page, /ui\.text\("ui_page_about_local_state_description"\)/, "The local-state inspector does not state its privacy boundary.");

const css = await readFile(new URL("../src/routes/layout.css", import.meta.url), "utf8");
const forcedColors = cssBlock(css, "@media (forced-colors: active)");
for (const token of ["--background: Canvas", "--foreground: CanvasText", "--primary: Highlight", "--primary-foreground: HighlightText", "--ring: Highlight"]) {
  assert.match(forcedColors, new RegExp(token.replace(/[.*+?^${}()|[\]\\]/g, "\\$&")), `Forced-colors mode omits ${token}.`);
}
assert.match(forcedColors, /:focus-visible\s*\{[\s\S]*outline: 2px solid Highlight;[\s\S]*outline-offset: 2px;/,
  "Forced-colors mode does not preserve a visible focus indicator.");

const reducedMotion = cssBlock(css, "@media (prefers-reduced-motion: reduce)");
for (const declaration of ["animation-duration: 0.01ms !important", "transition-duration: 0.01ms !important", "scroll-behavior: auto !important"]) {
  assert.match(reducedMotion, new RegExp(declaration.replace(/[.*+?^${}()|[\]\\]/g, "\\$&")),
    `Reduced-motion mode omits ${declaration}.`);
}

console.log("PASS: headless Editor keyboard and accessibility semantics cover command search, recovery focus, labels, landmarks, and forced-colors focus." );

async function loadModule(url, raw = false, imports = {}) {
  let source = await readFile(url, "utf8");
  const transpiled = ts.transpileModule(source, {
    compilerOptions: { module: ts.ModuleKind.ESNext, target: ts.ScriptTarget.ES2022 },
    fileName: url.pathname,
  });
  let output = transpiled.outputText;
  for (const [specifier, module] of Object.entries(imports)) output = output.replaceAll(JSON.stringify(specifier), JSON.stringify(module));
  const dataUrl = `data:text/javascript;base64,${Buffer.from(output).toString("base64")}`;
  return raw ? dataUrl : import(dataUrl);
}

function key(value, modifiers = {}) {
  return { altKey: false, ctrlKey: false, key: value, metaKey: false, shiftKey: false, ...modifiers };
}

function assertOrder(source, first, second, message) {
  assert(source.indexOf(first) !== -1 && source.indexOf(second) !== -1 && source.indexOf(first) < source.indexOf(second), message);
}

function cssBlock(source, selector) {
  const start = source.indexOf(selector);
  assert.notEqual(start, -1, `Missing ${selector}.`);
  const bodyStart = source.indexOf("{", start) + 1;
  let depth = 1;
  for (let index = bodyStart; index < source.length; index++) {
    if (source[index] === "{") depth++;
    if (source[index] === "}" && --depth === 0) return source.slice(bodyStart, index);
  }
  throw new Error(`${selector} is unclosed.`);
}
