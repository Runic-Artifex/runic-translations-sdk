import { readFile, readdir } from "node:fs/promises";
import { extname } from "node:path";

const build = new URL("../build/", import.meta.url);
const index = await readFile(new URL("index.html", build), "utf8");
if (!index.includes('src="webui.js"')) throw new Error("The production shell omitted the CsWebUi bridge.");
if (!index.includes("/_app/immutable/")) throw new Error("The SvelteKit client entry was not emitted.");

const scripts = [];
await collect(build, scripts);
const bundled = (await Promise.all(scripts.map((file) => readFile(file, "utf8")))).join("\n");
for (const text of ["Translations", "Übersetzungen", "runicEditorSave", "runicEditorPreviewMessage", "Structured message composer", "Compiler-backed preview", "schema v"]) {
  if (!bundled.includes(text)) throw new Error(`The production client omitted '${text}'.`);
}
if (bundled.includes("node:fs") || bundled.includes("RunicTextResources.Compiler.dll")) {
  throw new Error("Server/compiler implementation details leaked into the browser bundle.");
}

console.log(`PASS: static SvelteKit client contains the CsWebUi bridge and generated Runic ESM (${scripts.length} scripts).`);

async function collect(directory, result) {
  for (const entry of await readdir(directory, { withFileTypes: true })) {
    const location = new URL(entry.name + (entry.isDirectory() ? "/" : ""), directory);
    if (entry.isDirectory()) await collect(location, result);
    else if (extname(entry.name) === ".js") result.push(location);
  }
}
