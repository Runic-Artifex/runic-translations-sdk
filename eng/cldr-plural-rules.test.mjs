import { test } from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { resolve } from "node:path";
import { root } from "./run.mjs";
import {
  encodeRuleSet,
  expandSamples,
  javascriptRuleFunction,
  parsePluralRule,
  parseRuleSet,
  sampleOperands,
  selectEncoded,
  visibleOperands,
} from "./cldr-plural-rules.mjs";

const subset = JSON.parse(
  readFileSync(resolve(root, "specs/translations/cldr/runic-subset-48.2.json"), "utf8"),
);

test("generated ESM plural rules select every pinned CLDR sample", () => {
  let samples = 0;
  for (const type of ["cardinal", "ordinal"])
    for (const [tag, rules] of Object.entries(subset.plurals[type])) {
      const ruleSet = parseRuleSet(rules, `${tag} ${type}`);
      // The emitted source is evaluated exactly as the generated ESM runtime embeds it.
      const select = new Function(`return ${javascriptRuleFunction(ruleSet)};`)();
      for (const rule of ruleSet)
        for (const sample of expandSamples([...rule.samples.integer, ...rule.samples.decimal])) {
          assert.equal(select(Object.freeze(sampleOperands(sample))), rule.category, `${tag} ${type} ${sample}`);
          samples++;
        }
    }
  assert.ok(samples > 10000, `only ${samples} samples were checked`);
});

test("plural rule bytecode selects as the generated selectors", () => {
  // The .NET runtime interprets this bytecode (GeneratedLocaleData.ApplyPluralRule); its own tests compare it with the
  // compiled selectors. Here: every pinned rule set over its samples and a sweep of integers, decimals and exponents.
  const sweep = [];
  for (let value = 0n; value <= 2000n; value++) sweep.push(visibleOperands(value, 0));
  for (let value = 0n; value <= 3000n; value++) sweep.push(visibleOperands(value, 1), visibleOperands(value, 2));
  for (let power = 1n; power <= 10n ** 30n; power *= 10n)
    for (const offset of [0n, 1n, 2n, 11n, 1000n, 20000n, 100000n]) sweep.push(visibleOperands(power + offset, 0));
  for (let exponent = 1n; exponent <= 8n; exponent++)
    for (const value of [0n, 1n, 2n, 11n, 100n, 1000000n, 1100000n]) sweep.push(visibleOperands(value, 0, exponent), visibleOperands(value, 1, exponent));
  let checks = 0;
  for (const type of ["cardinal", "ordinal"])
    for (const [tag, rules] of Object.entries(subset.plurals[type])) {
      const ruleSet = parseRuleSet(rules, `${tag} ${type}`);
      const select = new Function(`return ${javascriptRuleFunction(ruleSet)};`)();
      const bytes = encodeRuleSet(ruleSet);
      const samples = ruleSet.flatMap((rule) => expandSamples([...rule.samples.integer, ...rule.samples.decimal]).map(sampleOperands));
      for (const operands of [...samples, ...sweep]) {
        assert.equal(selectEncoded(bytes, operands), select(operands), `${tag} ${type} ${JSON.stringify(operands, (_, value) => String(value))}`);
        checks++;
      }
    }
  assert.ok(checks > 1000000, `only ${checks} operands were checked`);
  assert.deepEqual(encodeRuleSet(parseRuleSet({ one: "n % 1000000 = 0", other: "" }, "test")), [1, 1, 1, 1, 16, 192, 132, 61, 1, 0, 0]);
  assert.throws(() => encodeRuleSet(parseRuleSet({ one: `n = ${"1,".repeat(256)}1`, other: "" }, "test")), /does not fit a byte/);
});

test("CLDR operands follow the visible decimal", () => {
  assert.deepEqual(sampleOperands("1.0"), { i: 1n, v: 1n, w: 0n, f: 0n, t: 0n, e: 0n });
  assert.deepEqual(sampleOperands("1.230"), { i: 1n, v: 3n, w: 2n, f: 230n, t: 23n, e: 0n });
  assert.deepEqual(sampleOperands("1.1c6"), { i: 1100000n, v: 0n, w: 0n, f: 0n, t: 0n, e: 6n });
  assert.deepEqual(sampleOperands("1.0000001c6"), { i: 1000000n, v: 1n, w: 1n, f: 1n, t: 1n, e: 6n });
  assert.deepEqual(expandSamples(["0.8~1.1", "2~4"]), ["0.8", "0.9", "1.0", "1.1", "2", "3", "4"]);
});

test("the rule parser rejects malformed or incomplete CLDR rules", () => {
  assert.throws(() => parsePluralRule("x = 1"), /Unknown CLDR plural operand/);
  assert.throws(() => parsePluralRule("n = 4..2"), /Empty CLDR range/);
  assert.throws(() => parsePluralRule("n = 1 and"), /Malformed/);
  assert.throws(() => parsePluralRule("n < 1"), /Unsupported CLDR plural operator/);
  assert.throws(() => parseRuleSet({ one: "n = 1" }, "test"), /must end with the 'other'/);
  assert.throws(() => parseRuleSet({ few: "n = 3", one: "n = 1", other: "" }, "test"), /misordered/);
  assert.throws(() => parseRuleSet({ one: "", other: "" }, "test"), /only 'other' may be unconditional/);
});
