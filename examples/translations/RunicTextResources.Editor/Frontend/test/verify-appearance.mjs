import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";

const stored = new Map();
const classes = new Set();
let systemDark = false;

globalThis.localStorage = {
  getItem: (key) => stored.get(key) ?? null,
  setItem: (key, value) => stored.set(key, String(value)),
  removeItem: (key) => stored.delete(key),
  clear: () => stored.clear(),
};

globalThis.document = {
  documentElement: {
    classList: {
      toggle(name, force) {
        if (force) classes.add(name);
        else classes.delete(name);
        return force;
      },
      contains: (name) => classes.has(name),
    },
    dataset: {},
    style: {},
  },
};

globalThis.matchMedia = () => ({ matches: systemDark });

const {
  applyAppearance,
  readAppearance,
  saveAppearance,
  themeModes,
  themePalettes,
} = await import("../src/lib/appearance.ts");

assert.deepEqual(readAppearance(), { mode: "dark", palette: "runic" });

for (const palette of themePalettes) {
  for (const mode of themeModes) {
    systemDark = false;
    saveAppearance(mode, palette);
    assert.deepEqual(readAppearance(), { mode, palette });
    assert.equal(document.documentElement.dataset.theme, palette);
    assert.equal(document.documentElement.style.colorScheme, mode === "dark" ? "dark" : "light");
    assert.equal(document.documentElement.classList.contains("dark"), mode === "dark");

    if (mode === "system") {
      systemDark = true;
      applyAppearance(mode, palette);
      assert.equal(document.documentElement.classList.contains("dark"), true);
      assert.equal(document.documentElement.style.colorScheme, "dark");
    }
  }
}

stored.set("runic-text-resources.theme-mode", "sepia");
stored.set("runic-text-resources.theme-palette", "unknown");
assert.deepEqual(readAppearance(), { mode: "dark", palette: "runic" });

const css = await readFile(new URL("../src/routes/layout.css", import.meta.url), "utf8");
const requiredTokens = [
  "background",
  "foreground",
  "card",
  "card-foreground",
  "popover",
  "popover-foreground",
  "primary",
  "primary-foreground",
  "secondary",
  "secondary-foreground",
  "muted",
  "muted-foreground",
  "accent",
  "accent-foreground",
  "border",
  "input",
  "ring",
  "chart-1",
  "chart-2",
  "chart-3",
  "chart-4",
  "chart-5",
  "sidebar",
  "sidebar-foreground",
  "sidebar-primary",
  "sidebar-primary-foreground",
  "sidebar-accent",
  "sidebar-accent-foreground",
  "sidebar-border",
  "sidebar-ring",
];

const selectors = [
  ":root",
  ".dark",
  ...themePalettes.filter((palette) => palette !== "runic").flatMap((palette) => [
    `:root:not(.dark)[data-theme="${palette}"]`,
    `.dark[data-theme="${palette}"]`,
  ]),
];

for (const selector of selectors) {
  const block = cssBlock(css, selector);
  for (const token of requiredTokens) {
    assert.match(block, new RegExp(`--${token}:`), `${selector} does not define --${token}`);
  }
}

console.log(`PASS: ${themeModes.length * themePalettes.length} appearance combinations persist and every palette defines the complete semantic token contract.`);

function cssBlock(source, selector) {
  const start = source.indexOf(`${selector} {`);
  assert.notEqual(start, -1, `Missing theme selector ${selector}`);
  const bodyStart = source.indexOf("{", start) + 1;
  const end = source.indexOf("}", bodyStart);
  assert.notEqual(end, -1, `Unclosed theme selector ${selector}`);
  return source.slice(bodyStart, end);
}
