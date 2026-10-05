import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import ts from "typescript";

const sourceUrl = new URL("../src/lib/resource-model.ts", import.meta.url);
const source = await readFile(sourceUrl, "utf8");
const transpiled = ts.transpileModule(source, {
  compilerOptions: { module: ts.ModuleKind.ESNext, target: ts.ScriptTarget.ES2022 },
  fileName: sourceUrl.pathname,
});
const model = await import(`data:text/javascript;base64,${Buffer.from(transpiled.outputText).toString("base64")}`);

const snapshot = {
  root: "/workspace",
  catalog: {
    id: "mounted-direct",
    schemaVersion: 1,
    defaultLocale: "en",
    locales: [{ tag: "en" }, { tag: "de", fallback: "en" }],
    layers: [{ name: "base", priority: 0 }],
  },
  catalogs: [],
  documents: [
    {
      path: "translations/runic.json",
      content: "{}\n",
      revision: "manifest",
      isManifest: true,
      isMalformed: false,
    },
    {
      path: "feature/en/greeting.mf2",
      content: "Hello\n",
      revision: "source",
      isManifest: false,
      isMalformed: false,
      locale: "en",
      layer: "base",
      entries: [{ key: "shop_greeting", content: "Hello\n", valueStartByte: 0, valueLengthBytes: 6 }],
    },
  ],
  diagnostics: [],
  success: true,
};

const rows = model.buildRows(snapshot, {});
assert.equal(rows.length, 1);
assert.equal(rows[0].key, "shop_greeting", "The frontend discarded the compiler's mounted direct key.");
assert.equal(rows[0].cells.en.document.path, "feature/en/greeting.mf2");
assert.equal(rows[0].cells.de.document.path, "feature/de/greeting.mf2",
  "A missing direct locale must be synthesized beside the canonical sourceRoot, not beside runic.json.");
assert.equal(rows[0].cells.de.document.revision, "new-mf2-document");

const uppercaseGrouped = structuredClone(snapshot);
uppercaseGrouped.documents[1] = {
  ...uppercaseGrouped.documents[1],
  path: "feature/en.RMF2",
  content: "greeting = Hello\n",
  entries: [{ key: "shop_greeting", content: "Hello", valueStartByte: 11, valueLengthBytes: 5 }],
};
const groupedRows = model.buildRows(uppercaseGrouped, {});
assert.equal(groupedRows.length, 1);
assert.equal(groupedRows[0].cells.de.document.path, "feature/de.rmf2",
  "Uppercase grouped RMF2 sources must synthesize another grouped locale, not a direct MF2 file.");

uppercaseGrouped.documents.push({
  path: "feature/de.RMF2",
  content: "other = Andere\n",
  revision: "target",
  isManifest: false,
  isMalformed: false,
  locale: "de",
  layer: "base",
  entries: [{ key: "shop_other", content: "Andere", valueStartByte: 8, valueLengthBytes: 6 }],
});
const existingGroupedRows = model.buildRows(uppercaseGrouped, {});
const greetingRow = existingGroupedRows.find((row) => row.key === "shop_greeting");
assert.equal(greetingRow.cells.de.document.path, "feature/de.RMF2",
  "A grouped locale missing the key must reuse its existing document with the original path casing.");

const mountedExtra = structuredClone(snapshot);
mountedExtra.catalog.locales.push({ tag: "fr", fallback: "en" });
mountedExtra.documents[1] = {
  ...mountedExtra.documents[1],
  path: "feature/fr/extra.mf2",
  locale: "fr",
  content: "Supplément\n",
  entries: [{ key: "shop_extra", content: "Supplément\n", valueStartByte: 0, valueLengthBytes: 12 }],
};
const extraRows = model.buildRows(mountedExtra, {});
assert.equal(extraRows.length, 1);
assert.equal(extraRows[0].cells.en.document.path, "feature/en/extra.mf2");
assert.equal(extraRows[0].cells.de.document.path, "feature/de/extra.mf2",
  "A mounted extra key must use its existing non-default document as the physical template.");

console.log("PASS: mounted direct, extra-key, and uppercase grouped RMF2 rows synthesize or reuse canonical locale documents.");

const compilerMetadata = {
  authoring: { revision: "projection", supported: true, inputs: [], selectors: [], selectorFunctions: [], selectorPluralCategories: [], variants: [{ id: "0", keys: [], pattern: "Hello", startByte: 0, lengthBytes: 5 }], pluralCategories: ["one", "other"], cldrVersion: "48.2" },
  context: { comments: ["Shown in the shop"], tags: ["checkout"], examples: [{ count: "2" }] },
  semantic: { text: ["Hello"], placeholders: [], slots: [], supported: true, hasBoundaryWhitespace: false },
};
const withMetadata = structuredClone(snapshot);
Object.assign(withMetadata.documents[1].entries[0], compilerMetadata);
const metadataRow = model.buildRows(withMetadata, {})[0];
assert.deepEqual(metadataRow.cells.en.entry.authoring, compilerMetadata.authoring);
assert.deepEqual(metadataRow.cells.en.entry.semantic, compilerMetadata.semantic);
assert.deepEqual(metadataRow.cells.en.entry.context, compilerMetadata.context);
assert.equal(metadataRow.description, "Shown in the shop");
assert.deepEqual(metadataRow.tags, ["checkout"]);
const editedContent = "Edited\n";
const staleRow = model.buildRows(withMetadata, { "feature/en/greeting.mf2": editedContent })[0];
assert.equal(staleRow.cells.en.entry.authoring, undefined, "A stale projection cannot authorize editing the new source.");
assert.equal(staleRow.cells.en.entry.semantic, undefined, "QA must not consume stale compiler semantics.");
const freshMetadata = { ...compilerMetadata, semantic: { ...compilerMetadata.semantic, text: ["Edited"] } };
const parsedRow = model.buildRows(withMetadata, { "feature/en/greeting.mf2": editedContent }, {
  "feature/en/greeting.mf2": { content: editedContent, entries: [{ ...withMetadata.documents[1].entries[0], content: editedContent, ...freshMetadata }] },
})[0];
assert.deepEqual(parsedRow.cells.en.entry.semantic.text, ["Edited"]);
const groupedMetadata = structuredClone(uppercaseGrouped);
Object.assign(groupedMetadata.documents[1].entries[0], compilerMetadata);
assert.deepEqual(model.buildRows(groupedMetadata, {})[0].cells.en.entry.context, compilerMetadata.context);
console.log("PASS: compiler authoring, semantic and translator context survive direct/grouped rows and reject stale drafts.");
