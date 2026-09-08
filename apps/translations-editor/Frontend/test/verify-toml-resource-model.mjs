import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import ts from "typescript";

const source = await readFile(new URL("../src/lib/resource-model.ts", import.meta.url), "utf8");
const compiled = ts.transpileModule(source, { compilerOptions: { module: ts.ModuleKind.ESNext, target: ts.ScriptTarget.ES2022 } });
const { buildRows } = await import(`data:text/javascript;base64,${Buffer.from(compiled.outputText).toString("base64")}`);
const entry = (key, content) => ({ key, content, valueStartByte: 10, valueLengthBytes: 8 });
const document = (locale, entries) => ({ path: `${locale}.toml`, locale, layer: "base", revision: `revision-${locale}`,
  content: "# physical source\n", isManifest: false, isMalformed: false, entries });
const en = document("en", [entry("title", "Title"), entry("save", "Save")]);
const de = document("de", [entry("title", "Titel")]);
const manifest = { path: "runic.json", content: JSON.stringify({ sourceLayout: "locale-toml" }), revision: "config",
  isManifest: true, isMalformed: false };
const snapshot = { catalog: { defaultLocale: "en", locales: [{ tag: "en" }, { tag: "de", fallback: "en" }, { tag: "fr", fallback: "en" }],
  layers: [{ name: "base", priority: 0 }] }, documents: [manifest, en, de] };
let rows = buildRows(snapshot, {});
assert.equal(rows.length, 2);
assert.strictEqual(rows.find(row => row.key === "save").cells.en.document, en);
assert.strictEqual(rows.find(row => row.key === "title").cells.en.document, en);
assert.strictEqual(rows.find(row => row.key === "save").cells.de.document, de, "Missing entries must target their existing physical locale file");
assert.equal(rows.find(row => row.key === "save").cells.de.inheritedFrom, "en");
assert.equal(rows.find(row => row.key === "save").cells.fr.document.path, "fr.toml", "Missing locale files use a real physical TOML path");
const draft = "# physical source\ntitle = 'Entwurf'\nsave = 'Speichern'\n";
rows = buildRows(snapshot, { "de.toml": draft }, { "de.toml": { content: draft, entries: [entry("title", "Entwurf"), entry("save", "Speichern")] } });
assert.equal(rows.find(row => row.key === "save").cells.de.entry.value, "Speichern");
assert.equal(rows.find(row => row.key === "title").cells.de.document.revision, "revision-de", "Logical drafts retain the physical file revision");
rows = buildRows(snapshot, { "de.toml": "newer raw text" }, { "de.toml": { content: draft, entries: [entry("title", "stale")] } });
assert.notEqual(rows.find(row => row.key === "title").cells.de.entry.value, "stale", "A stale parse must not describe a newer raw draft");

const mixedCase = { ...de, path: "DE.TOML" };
const mixedRows = buildRows({ ...snapshot, documents: [manifest, en, mixedCase] }, {});
assert.equal(mixedRows.find(row => row.key === "title").cells.de.entry.value, "Titel", "Supported case variants must expose logical entries");
assert.equal(mixedRows.find(row => row.key === "save").cells.de.document.path, "DE.TOML", "Missing logical entries retain the actual physical filename");
console.log("PASS: TOML physical document identity, logical entries, missing entries/locales, shared revision, and stale parse isolation.");
