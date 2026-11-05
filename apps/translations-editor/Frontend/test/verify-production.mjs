import { readFile, readdir } from "node:fs/promises";
import { extname } from "node:path";
import { gzipSync } from "node:zlib";

const build = new URL("../build/", import.meta.url);
const generated = await readFile(new URL("../src/generated/editor.ts", import.meta.url), "utf8");
if (!generated.includes("export interface EditorState") || !generated.includes("readonly workspace: EditorWorkspacePageReference;") || !generated.includes("readonly interchange: EditorInterchangePageReference;"))
  throw new Error("The generated Views Editor client is missing its routed feature contracts.");
const generatedDocument = await readFile(new URL("../src/generated/editorDocument.ts", import.meta.url), "utf8");
const generatedTypes = await readFile(new URL("../src/generated/types.ts", import.meta.url), "utf8");
const saveArgument = "editorDocumentSaveRequest: EditorDocumentSaveRequest";
if (!/export interface EditorDocumentSaveRequest \{\s*readonly content: string;\s*readonly revision: string;\s*\}/.test(generatedTypes) ||
    !generatedDocument.includes(`save(${saveArgument}): Promise<EditorDocumentState>`) ||
    !generatedDocument.includes(`startSave(${saveArgument}, options?: BridgeOperationStartOptions): Promise<EditorDocumentSaveOperation>`))
  throw new Error("The generated document client is missing its revision-aware save command or result operation.");
const index = await readFile(new URL("index.html", build), "utf8");
for (const script of ['src="/webui.js"', 'src="/runic-views.js"'])
  if (!index.includes(script)) throw new Error(`The production shell omitted ${script}.`);
if (!index.includes("/_app/immutable/")) throw new Error("The SvelteKit client entry was not emitted.");

const scripts = [];
await collect(build, scripts);
// Generated ESM writes a plain message as its text, so German text is unescaped (#85).
const bundled = (await Promise.all(scripts.map((file) => readFile(file, "utf8")))).join("\n");
for (const text of ["Translations", "Übersetzungen", "LoadWorkspace", "SaveReview", "RecoverTransaction", "Undo", "Redo", "About", "CreateDiagnosticBundle", "Translate the message", "Create new variable", "Message source", "Preview", "Editor settings", "Runic Gold", "Fjord", "Ember", "Resize Languages and Messages", "Quality report", "About & diagnostics", "Terminology", "schema", "Saved the earlier draft; your newer edit is still open.", "Recovery completed; reload required", "Discard unsaved document drafts, repair text, and workflow/terminology changes", "Local editor state", "Clear local state"]) {
  if (!bundled.includes(text)) throw new Error(`The production client omitted '${text}'.`);
}
if (bundled.includes("node:fs") || bundled.includes("Runic.Translations.Compiler.dll")) {
  throw new Error("Server/compiler implementation details leaked into the browser bundle.");
}

// Size budget (L5-27). The editor reads its UI text through the tree-shakable `m` namespace by key (ui-text.ts), so
// every message is kept, as it should be: about 270 KiB gzip with 0.9's catalog. The budget leaves room for growth
// but catches a regression such as a second copy of the catalog, the full CLDR data or unused Svelte components.
const budget = 360 * 1024;
const compressed = (await Promise.all(scripts.map(async (file) => gzipSync(await readFile(file)).length))).reduce((sum, size) => sum + size, 0);
if (compressed > budget)
  throw new Error(`The editor client is ${Math.round(compressed / 1024)} KiB gzip, over its ${budget / 1024} KiB budget.`);

console.log(`PASS: static SvelteKit client contains the Views bootstrap and generated editor client (${scripts.length} scripts, ${Math.round(compressed / 1024)} KiB gzip of ${budget / 1024} KiB).`);

async function collect(directory, result) {
  for (const entry of await readdir(directory, { withFileTypes: true })) {
    const location = new URL(entry.name + (entry.isDirectory() ? "/" : ""), directory);
    if (entry.isDirectory()) await collect(location, result);
    else if (extname(entry.name) === ".js") result.push(location);
  }
}
