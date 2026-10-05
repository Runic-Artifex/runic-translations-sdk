import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import ts from "typescript";

const source = await readFile(new URL("../src/lib/message-composer.ts", import.meta.url), "utf8");
const js = ts.transpileModule(source.replace('export * from "./message-model";', ""), {
  compilerOptions: { module: ts.ModuleKind.ESNext, target: ts.ScriptTarget.ES2022 },
}).outputText;
const { parseMf2Slots, serializeMf2Slots, mf2VariableSyntax } = await import(`data:text/javascript;base64,${Buffer.from(js).toString("base64")}`);

const examples = [
  "Hello {$name}!",
  "{$count} files selected",
  "العربية {$name} — 日本語",
  "Escaped \\{$name} and \\} text",
  "Quoted {|literal {$name} text|} then {$name}",
  "{$count :integer} formatted; {$name} simple",
  "{#strong}Hello {$name}{/strong}",
  "Spacing { $name } and {$name}",
  "Unclosed {$name",
  "Unicode {$名前} and hyphen {$item-count}",
];
for (const source of examples) assert.equal(serializeMf2Slots(parseMf2Slots(source)), source, `Slot display changed MF2 source: ${source}`);
assert.deepEqual(parseMf2Slots("Hello {$name}!"), [
  { text: "Hello ", token: "name", syntax: "{$name}" }, { text: "!" },
]);
assert.equal(parseMf2Slots("Escaped \\{$name}").some(slot => slot.token !== undefined), false);
assert.equal(parseMf2Slots("{|literal {$name}|}").some(slot => slot.token !== undefined), false);
assert.equal(parseMf2Slots("{$count :integer}").some(slot => slot.token !== undefined), false,
  "A formatter expression must remain intact instead of becoming a plain variable chip.");
assert.equal(mf2VariableSyntax("count"), "{$count}");
assert.equal(parseMf2Slots("{$名前}")[0].token, "名前");
assert.equal(parseMf2Slots("{$item-count}")[0].token, "item-count");
assert.equal(serializeMf2Slots([{ text: "", token: "count" }, { text: " files" }]), "{$count} files");
console.log("PASS: MF2 variable chips preserve escapes, expressions, whitespace, Unicode and source bytes.");
