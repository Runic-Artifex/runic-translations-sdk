// The editor's chrome (#225 B7): shortcut notation per platform (P10), coverage in whole percent (P16), the window
// title (P14), localized review states and the editor's stale hint (P20, P23), and the remembered selection (P24).
import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import { dirname, resolve } from "node:path";
import { pathToFileURL } from "node:url";
import ts from "typescript";
import { detectShortcutPlatform, shortcutLabel } from "../src/lib/shortcut-labels.ts";
import { buildEditorCommandPalette } from "../src/lib/command-palette.ts";
import { formatCoverage } from "../src/lib/coverage-format.ts";
import { editorWindowTitle } from "../src/lib/window-title.ts";
import { catalogSelectionLimits, catalogSelectionStateKey, readCatalogSelection, writeCatalogSelection } from "../src/lib/catalog-selection.ts";
import { readPageSources } from "./page-sources.mjs";
import { qualityIssues } from "../src/lib/review-model.ts";

// P10: ⌘ on macOS, Ctrl elsewhere, from one helper.
assert.equal(detectShortcutPlatform({ platform: "MacIntel" }), "mac");
assert.equal(detectShortcutPlatform({ userAgentData: { platform: "macOS" }, platform: "" }), "mac");
assert.equal(detectShortcutPlatform({ userAgent: "Mozilla/5.0 (iPad; CPU OS 18_0 like Mac OS X)" }), "mac");
assert.equal(detectShortcutPlatform({ platform: "Linux x86_64", userAgent: "Mozilla/5.0 (X11; Linux x86_64)" }), "other");
assert.equal(detectShortcutPlatform({ userAgentData: { platform: "Windows" }, platform: "Win32" }), "other");
assert.equal(detectShortcutPlatform(undefined), "other");
assert.equal(shortcutLabel("Mod+S", "mac"), "⌘S");
assert.equal(shortcutLabel("Mod+S", "other"), "Ctrl+S");
assert.equal(shortcutLabel("Mod+Shift+Z", "mac"), "⇧⌘Z", "macOS orders the modifier symbols as Apple does.");
assert.equal(shortcutLabel("Shift+Mod+Z", "other"), "Ctrl+Shift+Z");
assert.equal(shortcutLabel("Alt+P", "mac"), "⌥P");
assert.equal(shortcutLabel("Alt+P", "other"), "Alt+P");
const noop = () => {};
const paletteActions = new Proxy({}, { get: () => noop });
const paletteState = {
  locales: [], selectedLocale: "en", editorMode: "translation", uiLocale: "en", uiLocales: [], themeMode: "system",
  workspaceReady: true, searchAvailable: true, unsaved: true, canUndo: true, canRedo: true, reviewEditable: true,
  reviewDirty: false, pseudoLocalization: false, uiDirection: "ltr", artifactPreviewOpen: false,
};
const keybindings = (platform) => Object.fromEntries(buildEditorCommandPalette(paletteActions, { ...paletteState, shortcutPlatform: platform })
  .filter((command) => command.keybinding !== undefined).map((command) => [command.id, command.keybinding]));
assert.deepEqual(keybindings("mac"), {
  "document.save": "⌘S", "document.undo": "⌘Z", "document.redo": "⇧⌘Z",
  "view.toggle-pseudo-localization": "⌥P", "view.toggle-rtl-simulation": "⌥R", "view.toggle-artifact-preview": "⌥B",
});
assert.deepEqual(keybindings("other"), {
  "document.save": "Ctrl+S", "document.undo": "Ctrl+Z", "document.redo": "Ctrl+Shift+Z",
  "view.toggle-pseudo-localization": "Alt+P", "view.toggle-rtl-simulation": "Alt+R", "view.toggle-artifact-preview": "Alt+B",
});
const toolbar = await readFile(new URL("../src/lib/EditorToolbar.svelte", import.meta.url), "utf8");
assert.doesNotMatch(toolbar, /⌘ S/, "The toolbar's save hint is not hard-coded to macOS.");
assert.match(toolbar, /shortcutLabel\("Mod\+S"\)/, "The toolbar's save hint uses the platform's modifier.");

// P16: whole percent in the interface locale's format; never 100 % while something is missing, never 0 % while
// something is covered.
const coverage = (percent, missing, total, locale = "en") => formatCoverage({ percent, missing, total }, locale).text;
assert.equal(coverage(53.84, 6, 13), "54%");
assert.equal(coverage(53.84, 6, 13, "de"), "54 %", "German puts a no-break space before the percent sign.");
assert.equal(coverage(99.6, 1, 250), "99%", "Rounding up never claims completeness.");
assert.equal(coverage(0.3, 299, 300), "1%", "Rounding down never claims emptiness.");
assert.equal(coverage(100, 0, 12), "100%");
assert.equal(coverage(0, 12, 12), "0%");
assert.equal(coverage(100, 0, 0), "100%", "An empty catalog is complete.");
assert.equal(coverage(99.995), "99%", "Without counts, the percentage itself decides.");
assert.equal(coverage(0.004), "1%");
assert.equal(formatCoverage({ percent: 53.84, missing: 6, total: 13 }, "de").exact, "53,84 %", "The tooltip keeps the exact value.");
assert.equal(formatCoverage({ percent: 50, missing: 1, total: 2 }, "not a locale!").text, "50%", "An unusable locale falls back to English.");
const switcher = await readFile(new URL("../src/lib/LocaleSwitcher.svelte", import.meta.url), "utf8");
assert.doesNotMatch(switcher, /\{locale\.percent\}%/, "The sidebar formats coverage through formatCoverage.");

// The real generated UI catalog through the editor's locale-reactive adapter.
const manifestPath = process.env.RUNIC_TRANSLATIONS_MANIFEST;
assert.ok(manifestPath, "Generate the editor ESM module and provide RUNIC_TRANSLATIONS_MANIFEST.");
const manifest = JSON.parse(await readFile(manifestPath, "utf8"));
const messageUrl = pathToFileURL(resolve(dirname(manifestPath), manifest.entrypoints.messages)).href;
const adapterSource = (await readFile(new URL("../src/lib/ui-text.ts", import.meta.url), "utf8"))
  .replace('import { createContext } from "svelte";', 'const createContext = () => [() => undefined, () => undefined];')
  .replace('"virtual:runic-translations/editor"', JSON.stringify(messageUrl));
const adapterJs = ts.transpileModule(adapterSource, { compilerOptions: { module: ts.ModuleKind.ESNext, target: ts.ScriptTarget.ES2022 } }).outputText;
const { createUiText } = await import(`data:text/javascript;base64,${Buffer.from(adapterJs).toString("base64")}`);
let uiLocale = "en";
const ui = createUiText(() => uiLocale);

// P14: "<catalog> – Runic Translations Editor", marked while unsaved; the product name without a catalog.
assert.equal(editorWindowTitle(ui, "customer-product", false), "customer-product – Runic Translations Editor");
assert.equal(editorWindowTitle(ui, "customer-product", true), "• customer-product – Runic Translations Editor");
assert.equal(editorWindowTitle(ui, undefined, true), "Runic Translations Editor", "The welcome screen shows the product name.");
uiLocale = "de";
assert.equal(editorWindowTitle(ui, "customer-product", false), "customer-product – Runic Translations Editor", "The product name is not translated.");
const page = await readPageSources();
assert.match(page, /<title>\{windowTitle\}<\/title>/, "The page title is the window title.");
assert.match(page, /editorWindowTitle\(ui, [^\n]*hasUnsavedWork\)/, "The title marks every kind of unsaved work.");

// P23: the review states and badges come from the UI catalog, and follow a change of the interface language.
const german = { draft: "Entwurf", translated: "Übersetzt", needs_review: "Prüfung erforderlich", approved: "Freigegeben", source_changed: "Quelle geändert" };
for (const [state, label] of Object.entries(german)) assert.equal(ui.text(`ui_workflow_${state}`), label);
assert.equal(ui.text("ui_resource_not_translated"), "Nicht übersetzt");
assert.equal(ui.text("app_stale"), "Veraltet");
const workflow = await readFile(new URL("../src/lib/ReviewWorkflow.svelte", import.meta.url), "utf8");
assert.match(workflow, /const states: [^\n]*= \$derived\(\[/, "The workflow's state labels are derived, so they follow the interface language.");

// P20: the editor's hint for a stale state instead of the command-line instruction; the diagnostic stays as it is.
const staleDiagnostic = { id: "RTR0090", severity: "warning", message: "… record it with runic-translations review mark --key common_save --locale de --state <state>.", key: "common_save", locale: "de" };
const rows = [{ key: "common_save", cells: { en: { entry: { value: "Save" } }, de: { entry: { value: "Sichern" } } } }];
for (uiLocale of ["en", "de"]) {
  const [issue] = qualityIssues(rows, "en", "de", [staleDiagnostic], [], ui);
  assert.equal(issue.code, "RTR0090");
  assert.equal(issue.message, ui.text("ui_quality_stale"));
  assert.doesNotMatch(issue.message, /runic-translations|review mark/, `${uiLocale}: the hint names the editor's actions, not the CLI.`);
  assert.equal(issue.diagnostic, staleDiagnostic, "The diagnostic itself is unchanged.");
  assert.match(ui.text("ui_workflow_stale_explanation", { state: ui.text("ui_workflow_approved") }), uiLocale === "en" ? /“Approved”/ : /„Freigegeben“/);
}
assert.equal(qualityIssues(rows, "en", "de", [staleDiagnostic], [])[0].message, staleDiagnostic.message, "Without the UI context the diagnostic text is kept.");
uiLocale = "en";
assert.equal(ui.text("ui_workflow_confirm_translation"), "Confirm translation");
assert.equal(ui.text("ui_workflow_mark_needs_review"), "Mark needs review");

// P24: one bounded local-state entry holds the last selection of the most recent catalogs.
assert.equal(catalogSelectionStateKey, "runic-translations:catalog-selection:1");
let record = writeCatalogSelection(null, "/w\nrunic.json", { locale: "de", key: "common_save" });
assert.deepEqual(readCatalogSelection(record, "/w\nrunic.json"), { locale: "de", key: "common_save" });
assert.equal(readCatalogSelection(record, "/other\nrunic.json"), undefined);
record = writeCatalogSelection(record, "/other\nrunic.json", { locale: "fr", key: "dashboard_welcome" });
record = writeCatalogSelection(record, "/w\nrunic.json", { locale: "en", key: "common_cancel" });
assert.deepEqual(JSON.parse(record).catalogs.map((entry) => entry.catalog), ["/w\nrunic.json", "/other\nrunic.json"], "The latest catalog comes first, once.");
assert.deepEqual(readCatalogSelection(record, "/w\nrunic.json"), { locale: "en", key: "common_cancel" });
for (let index = 0; index < 40; index++) record = writeCatalogSelection(record, `/w${index}\nrunic.json`, { locale: "en", key: "k" });
assert.equal(JSON.parse(record).catalogs.length, catalogSelectionLimits.catalogs, "Only the most recent catalogs are kept.");
assert.equal(readCatalogSelection(record, "/w39\nrunic.json")?.key, "k");
assert.equal(readCatalogSelection(record, "/w0\nrunic.json"), undefined);
assert.equal(readCatalogSelection("{not json", "/w\nrunic.json"), undefined, "An unreadable record restores nothing.");
assert.equal(readCatalogSelection('{"version":2,"catalogs":[]}', "/w\nrunic.json"), undefined);
assert.equal(readCatalogSelection(JSON.stringify({ version: 1, catalogs: [{ catalog: "/w", locale: 1, key: "x" }] }), "/w"), undefined, "Invalid entries are skipped.");
const oversized = writeCatalogSelection(null, "/w", { locale: "en", key: "x".repeat(catalogSelectionLimits.valueLength + 1) });
assert.equal(readCatalogSelection(oversized, "/w"), undefined, "An oversized selection is not stored.");
const [localStateTs, localStateCs] = await Promise.all([
  readFile(new URL("../src/lib/local-state.ts", import.meta.url), "utf8"),
  readFile(new URL("../../EditorLocalStateStore.cs", import.meta.url), "utf8"),
]);
assert.ok(localStateTs.includes(`"${catalogSelectionStateKey}"`) && localStateCs.includes(`"${catalogSelectionStateKey}"`),
  "The selection record is allow-listed in both local-state stores.");
assert.match(page, /readCatalogSelection\(this\.#options\.storage\.get\(catalogSelectionStateKey\), catalogViewKey\(next\)\)/, "Opening a catalog restores its selection.");
assert.match(page, /storage\.set\(catalogSelectionStateKey, writeCatalogSelection\(/, "A selection change is remembered.");
assert.match(page, /\$effect\(\(\) => selection\.persistSelection\(\)\)/, "The selection is remembered as it changes.");

console.log("PASS: shortcuts show the platform's modifier, coverage is whole percent in the UI locale, the window title names the catalog and unsaved work, review states are localized with the editor's stale hint, and the selection of each catalog is remembered.");
