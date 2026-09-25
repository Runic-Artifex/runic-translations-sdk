import { readFile, readdir } from "node:fs/promises";
import { extname } from "node:path";

const build = new URL("../build/", import.meta.url);
const generated = await readFile(new URL("../src/generated/editor.ts", import.meta.url), "utf8");
if (!generated.includes("export interface EditorView") || !generated.includes("readonly workspace: EditorWorkspacePageReference;") || !generated.includes("readonly interchange: EditorInterchangePageReference;"))
  throw new Error("The generated Views Editor client is missing its routed feature contracts.");
const generatedDocument = await readFile(new URL("../src/generated/editorDocument.ts", import.meta.url), "utf8");
if (!generatedDocument.includes("save(argument: string): Promise<EditorDocumentState>"))
  throw new Error("The generated document client is missing its save command.");
const index = await readFile(new URL("index.html", build), "utf8");
for (const script of ['src="/webui.js"', 'src="/runic-cswebui.js"'])
  if (!index.includes(script)) throw new Error(`The production shell omitted ${script}.`);
if (!index.includes("/_app/immutable/")) throw new Error("The SvelteKit client entry was not emitted.");

const scripts = [];
await collect(build, scripts);
const bundled = (await Promise.all(scripts.map((file) => readFile(file, "utf8")))).join("\n");
for (const text of ["Translations", "\\u00DCbersetzungen", "LoadWorkspace", "SaveReview", "RecoverTransaction", "Undo", "Redo", "About", "CreateDiagnosticBundle", "Translate the message", "Create new variable", "Message source", "Preview", "Editor settings", "Runic Gold", "Fjord", "Ember", "Resize Languages and Messages", "Quality report", "About & diagnostics", "Terminology", "schema", "Saved the earlier draft; your newer edit is still open.", "Recovery completed; reload required", "Discard unsaved document drafts, repair text, and workflow/terminology changes", "Local editor state", "Clear local state"]) {
  if (!bundled.includes(text)) throw new Error(`The production client omitted '${text}'.`);
}
if (bundled.includes("node:fs") || bundled.includes("Runic.Translations.Compiler.dll")) {
  throw new Error("Server/compiler implementation details leaked into the browser bundle.");
}

console.log(`PASS: static SvelteKit client contains the Views bootstrap and generated editor client (${scripts.length} scripts).`);

async function collect(directory, result) {
  for (const entry of await readdir(directory, { withFileTypes: true })) {
    const location = new URL(entry.name + (entry.isDirectory() ? "/" : ""), directory);
    if (entry.isDirectory()) await collect(location, result);
    else if (extname(entry.name) === ".js") result.push(location);
  }
}
