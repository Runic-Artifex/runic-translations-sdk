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

console.log("PASS: mounted direct MF2 rows retain compiler keys and synthesize missing locales beside the canonical source root.");
