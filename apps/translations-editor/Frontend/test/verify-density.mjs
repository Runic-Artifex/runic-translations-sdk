// Density (compact or comfortable) replaces the colour palettes in the editor settings (D074): stored with the other
// preferences, applied as `data-density` on the root, and driving spacing, the tree's rows and the interface text.
import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";

globalThis.document = { documentElement: { dataset: {} } };
const root = document.documentElement;

const { applyDensity, densities, densityKey, messageRowHeight, readAppearance } = await import("../src/lib/appearance.ts");
const { scrollTopForRow, virtualMessageWindow } = await import("../src/lib/message-virtualization.ts");

// Storage: one key, comfortable by default, unknown values fall back.
assert.equal(densityKey, "runic-translations.density");
assert.deepEqual([...densities], ["comfortable", "compact"]);
const stored = new Map();
const read = (key) => stored.get(key) ?? null;
assert.equal(readAppearance(read).density, "comfortable", "Comfortable is the default.");
stored.set(densityKey, "compact");
assert.equal(readAppearance(read).density, "compact");
stored.set(densityKey, "spacious");
assert.equal(readAppearance(read).density, "comfortable");

// The key is editor-owned on both sides of the native record, which stays within its 64 entries.
const localState = await readFile(new URL("../src/lib/local-state.ts", import.meta.url), "utf8");
const nativeStore = await readFile(new URL("../../EditorLocalStateStore.cs", import.meta.url), "utf8");
assert.match(localState, /"runic-translations\.density",/, "The frontend does not own the density key.");
assert.match(nativeStore, /"runic-translations\.density",/, "The native store's allow-list does not accept the density key.");
const exactKeys = /ExactKeys = new\(StringComparer\.Ordinal\)\s*\{([\s\S]*?)\};/.exec(nativeStore)[1];
assert.doesNotMatch(exactKeys, /theme-palette/, "The native store no longer accepts a palette.");
assert.match(nativeStore, /RetiredKeys = new\(StringComparer\.Ordinal\)\s*\{\s*"runic-translations\.theme-palette",/,
  "A palette stored by an earlier version must be dropped on load, not quarantine the whole record.");
assert.match(nativeStore, /if \(RetiredKeys\.Contains\(stateKey\)\) continue;/);

// The attribute on the root.
for (const density of densities) {
  applyDensity(density);
  assert.equal(root.dataset.density, density);
}
const html = await readFile(new URL("../src/app.html", import.meta.url), "utf8");
assert.match(html, /<html[^>]*data-density="comfortable"/, "The first paint uses the default density.");

// The stylesheet derives spacing, interface text and message text from it.
const css = await readFile(new URL("../src/routes/layout.css", import.meta.url), "utf8");
const comfortable = block(css, ":root {");
const compact = block(css, ':root[data-density="compact"] {');
assert.match(comfortable, /--ui-text: 0\.875rem;/, "Comfortable interface text is 14px.");
assert.match(comfortable, /--message-text: 1rem;/, "Message text is larger than the interface text.");
assert.match(comfortable, /--title-text: 1\.5rem;/);
for (const variable of ["--spacing", "--ui-text", "--ui-text-xs", "--message-text", "--title-text"]) {
  assert.match(compact, new RegExp(`${variable}: `), `Compact does not set ${variable}.`);
}
assert.match(css, /--text-sm: var\(--ui-text\);/, "The components' interface text does not follow the density.");

// The message tree's rows: the virtualized tree positions rows with the density's height.
assert.equal(messageRowHeight("comfortable"), 32);
assert.equal(messageRowHeight("compact"), 28);
assert.equal(scrollTopForRow(10, 0, 140, 28), 10 * 28 + 28 - 140);
assert.equal(virtualMessageWindow(100, 28 * 20, 280, 28).offset, (20 - 8) * 28);
const list = await readFile(new URL("../src/lib/MessageList.svelte", import.meta.url), "utf8");
const sidebar = await readFile(new URL("../src/lib/screens/WorkspaceSidebar.svelte", import.meta.url), "utf8");
assert.match(sidebar, /rowHeight=\{messageRowHeight\(preferences\.density\)\}/, "The tree's row height does not follow the density.");
assert.doesNotMatch(list, /messageVirtualRowHeight\}px/, "A tree row still uses the fixed row height.");

// The settings menu: Density with its two options, and no palette.
const footer = await readFile(new URL("../src/lib/EditorSettingsFooter.svelte", import.meta.url), "utf8");
assert.match(footer, /ui\.text\("ui_settings_density"\)/);
assert.match(footer, /<DropdownMenu\.RadioGroup value=\{density\} onValueChange=\{\(value\) => ondensitychange\(value as Density\)\}/);
assert.match(footer, /<DropdownMenu\.RadioItem value="comfortable">/);
assert.match(footer, /<DropdownMenu\.RadioItem value="compact">/);
assert.doesNotMatch(footer, /palette|runic_gold|color_theme/i, "The settings menu still offers a palette.");
assert.match(sidebar, /ondensitychange=\{\(density\) => preferences\.changeDensity\(density\)\}/);
for (const locale of ["en", "de"]) {
  const resource = await readFile(new URL(`../../EditorResources/${locale}.rmf2`, import.meta.url), "utf8");
  for (const key of ["density", "comfortable", "compact"]) {
    assert.match(resource, new RegExp(`\\n    ${key} = \\S`), `${locale}.rmf2 lacks ui.settings.${key}.`);
  }
  for (const key of ["color_theme", "runic_gold", "moss", "fjord", "ember"]) {
    assert.doesNotMatch(resource, new RegExp(`\\n    ${key} = `), `${locale}.rmf2 still has the palette string ${key}.`);
  }
}

console.log("PASS: density is stored, set as data-density on the root, drives spacing, rows and text, and replaces the palette in the settings menu.");

function block(source, selector) {
  const start = source.indexOf(selector);
  assert.notEqual(start, -1, `Missing ${selector}`);
  const end = source.indexOf("}", start);
  return source.slice(start + selector.length, end);
}
