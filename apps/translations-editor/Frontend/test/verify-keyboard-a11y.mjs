import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import { compile } from "svelte/compiler";
import ts from "typescript";
import { readUiMessages } from "./ui-messages.mjs";

const keyboard = await loadModule(new URL("../src/lib/editor-keyboard.ts", import.meta.url));
for (const [event, textEditingTarget, expected] of [
  [key("z", { ctrlKey: true }), false, "undo"],
  [key("z", { ctrlKey: true, shiftKey: true }), false, "redo"],
  [key("y", { metaKey: true }), false, "redo"],
  [key("s", { ctrlKey: true }), true, "save"],
  [key("k", { metaKey: true }), true, "toggle-command-search"],
  [key("p", { altKey: true }), false, "toggle-pseudo-localization"],
  [key("r", { altKey: true }), false, "toggle-right-to-left"],
  [key("b", { altKey: true }), false, "toggle-artifact-preview"],
  [key("z", { ctrlKey: true }), true, undefined],
  [key("p", { altKey: true }), true, undefined],
]) {
  assert.equal(keyboard.editorShortcut(event, textEditingTarget), expected,
    `Keyboard route for ${event.key} did not preserve the owned editor command boundary.`);
}

const componentEntries = await Promise.all([
  "CommandPalette.svelte",
  "EditorToolbar.svelte",
  "MessageList.svelte",
  "MessageToolbar.svelte",
].map(async (name) => [name, await readFile(new URL(`../src/lib/${name}`, import.meta.url), "utf8")]));
const [english, german] = await Promise.all([
  readUiMessages("en"),
  readUiMessages("de"),
]);
const sources = Object.fromEntries([
  ...componentEntries,
  ["+page.svelte", await readFile(new URL("../src/routes/+page.svelte", import.meta.url), "utf8")],
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
for (const key of ["ui_toolbar_undo_saved_change", "ui_toolbar_redo_saved_change"]) {
  assert.match(editorToolbar, new RegExp(`aria-label=\\{[^}]*${key}`), `${key} is unnamed.`);
  assert.equal(typeof english[key], "string", `English project omits ${key}.`);
  assert.equal(typeof german[key], "string", `German project omits ${key}.`);
}
assert.match(editorToolbar, /aria-label=\{saving \? savingLabel/, "Save control is unnamed.");
assertOrder(editorToolbar, "Undo2Icon", "Redo2Icon", "SaveIcon",
  "Editor toolbar focus order must remain undo, redo, then save.");

const page = sources["+page.svelte"];
assert.match(page, /<svelte:window onkeydown=\{handleKeyboard\}/,
  "Owned keyboard dispatch is not installed on the Editor window.");
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

async function loadModule(url) {
  const source = await readFile(url, "utf8");
  const transpiled = ts.transpileModule(source, {
    compilerOptions: { module: ts.ModuleKind.ESNext, target: ts.ScriptTarget.ES2022 },
    fileName: url.pathname,
  });
  return import(`data:text/javascript;base64,${Buffer.from(transpiled.outputText).toString("base64")}`);
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
