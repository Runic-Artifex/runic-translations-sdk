import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import ts from "typescript";

const source = await readFile(new URL("../src/lib/message-composer.ts", import.meta.url), "utf8");
const js = ts.transpileModule(source.replace('export * from "./message-model";', ""), {
  compilerOptions: { module: ts.ModuleKind.ESNext, target: ts.ScriptTarget.ES2022 },
}).outputText;
const { parseMf2Slots, serializeMf2Slots, mf2VariableSyntax, variantTitle, sourceSegments, sourceTextView } = await import(`data:text/javascript;base64,${Buffer.from(js).toString("base64")}`);

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

// The read-only source panel (#225 P1): the base text as display text with placeholder chips, one block per variant,
// titled as the composer titles a variant.
const titleLabels = { defaultTranslation: "Default translation", fallback: "Fallback" };
assert.equal(variantTitle([], [], titleLabels), "Default translation");
assert.equal(variantTitle(["one"], ["count"], titleLabels), "count: one");
assert.equal(variantTitle(["one", "*"], ["count", "kind"], titleLabels), "count: one · kind: Fallback");
assert.deepEqual(sourceSegments("Hello {$name}!"), [
  { kind: "text", text: "Hello " }, { kind: "variable", name: "name", syntax: "{$name}" }, { kind: "text", text: "!" },
]);
assert.deepEqual(sourceSegments(String.raw`Escaped \{ and \} and \\ stay text`), [{ kind: "text", text: String.raw`Escaped { and } and \ stay text` }],
  "Escapes are shown as the characters they stand for.");
assert.deepEqual(sourceSegments("{ $count } and {|literal {$x}|}"), [
  { kind: "variable", name: "count", syntax: "{$count}" }, { kind: "text", text: " and literal {$x}" },
], "A quoted literal is text; a spaced variable is the same chip.");
assert.deepEqual(sourceSegments("{#strong}{$count :integer}{/strong}"), [
  { kind: "placeholder", syntax: "{#strong}" }, { kind: "placeholder", syntax: "{$count :integer}" }, { kind: "placeholder", syntax: "{/strong}" },
], "Markup and formatted values are placeholders, never interpreted text.");
assert.deepEqual(sourceSegments("Unclosed {$name"), [{ kind: "text", text: "Unclosed {$name" }]);
assert.equal(sourceTextView(undefined), undefined, "A message without base text has no source view.");
assert.deepEqual(sourceTextView({ value: "Save", authoring: { supported: true, selectors: [], variants: [{ id: "0", keys: [], pattern: "Save {$n}" }] } }), {
  kind: "blocks", selectors: [], blocks: [{ id: "0", keys: [], segments: [{ kind: "text", text: "Save " }, { kind: "variable", name: "n", syntax: "{$n}" }] }],
});
const pluralSource = sourceTextView({ value: ".input {$count :integer}\n.match $count\none {{One file}}\n* {{{$count} files}}\n", authoring: {
  supported: true, selectors: ["count"], variants: [{ id: "a", keys: ["one"], pattern: "One file" }, { id: "b", keys: ["*"], pattern: "{$count} files" }],
} });
assert.deepEqual(pluralSource.blocks.map(block => variantTitle(block.keys, pluralSource.selectors, titleLabels)), ["count: one", "count: Fallback"]);
assert.deepEqual(pluralSource.blocks[1].segments, [{ kind: "variable", name: "count", syntax: "{$count}" }, { kind: "text", text: " files" }]);
assert.deepEqual(sourceTextView({ value: "{{  Padded {$x} }}" }).blocks[0].segments,
  [{ kind: "text", text: "  Padded " }, { kind: "variable", name: "x", syntax: "{$x}" }, { kind: "text", text: " " }],
  "Without a projection, a quoted simple pattern is shown without its quotes.");
assert.deepEqual(sourceTextView({ value: ".match $x\n* {{Any}}", authoring: { supported: false, selectors: [], variants: [] } }),
  { kind: "source", text: ".match $x\n* {{Any}}" }, "A complex message without a projection is shown as its source.");
console.log("PASS: the source panel shows base text with placeholder chips, one block per variant titled as in the composer.");
