import assert from "node:assert/strict";
import { glob, readFile } from "node:fs/promises";
import { parse } from "svelte/compiler";

const [english, german] = await Promise.all([
  readCatalog("../../EditorResources/editor.en.json"),
  readCatalog("../../EditorResources/editor.de.json"),
]);

const sources = [];
for await (const source of glob("src/{lib,routes}/**/*.svelte", {
  cwd: new URL("..", import.meta.url),
  exclude: ["src/lib/components/ui/**"],
})) sources.push(source);

const keys = new Set();
const uncataloged = [];
for (const source of sources) {
  const markup = await readFile(new URL(`../${source}`, import.meta.url), "utf8");
  for (const match of markup.matchAll(/ui\.text\("(Ui\.[A-Za-z0-9.]+)"\)/g)) keys.add(match[1]);
  visit(parse(markup).html, (node, parent) => {
    if (node.type === "Text" && parent?.type !== "Attribute" && isVisibleCopy(node.data, parent)) uncataloged.push(`${source}: ${node.data.trim()}`);
    if (node.type === "Attribute" && ["aria-label", "title", "placeholder", "alt"].includes(node.name) &&
      node.value?.some((value) => value.type === "Text" && isVisibleCopy(value.data, node))) uncataloged.push(`${source}: ${node.name}`);
  });
}

for (const key of keys) {
  assert.equal(typeof get(english.resources, key), "string", `English catalog omits ${key}.`);
  assert.equal(typeof get(german.resources, key), "string", `German catalog omits ${key}.`);
}
assert.deepEqual(uncataloged, [], `Found uncataloged visible or accessible copy:\n${uncataloged.join("\n")}`);
console.log(`PASS: ${keys.size} UI catalog keys are present in both locales and all static editor copy is catalog-backed.`);

function isVisibleCopy(value, parent) {
  const copy = value.trim();
  if (!/[A-Za-z]{2,}/.test(copy)) return false;
  // Paths, locale tags, enum values, units, and identifiers are editor data, not UI copy.
  if (/^[A-Za-z0-9_.:/-]+(?:\s*·)?$/.test(copy)) return false;
  if (parent?.name === "placeholder" && /[/.]|^[a-z]{2}(?:-[A-Z]{2})?$/.test(copy)) return false;
  return true;
}
function get(value, key) { return key.split(".").reduce((current, part) => current?.[part], value); }
async function readCatalog(path) { return JSON.parse(await readFile(new URL(path, import.meta.url), "utf8")); }
function visit(value, action, parent) { if (value === null || typeof value !== "object") return; if (typeof value.type === "string") action(value, parent); for (const child of Object.values(value)) Array.isArray(child) ? child.forEach((item) => visit(item, action, value)) : visit(child, action, value); }
