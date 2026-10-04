import { test } from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { resolve } from "node:path";
import { root } from "./run.mjs";
import {
  expandSamples,
  javascriptRuleFunction,
  parsePluralRule,
  parseRuleSet,
  sampleOperands,
} from "./cldr-plural-rules.mjs";

const subset = JSON.parse(
  readFileSync(resolve(root, "specs/translations/cldr/runic-subset-48.2.json"), "utf8"),
);

test("generated ESM plural rules select every pinned CLDR sample", () => {
  let samples = 0;
  for (const locale of subset.locales)
    for (const type of ["cardinal", "ordinal"]) {
      const ruleSet = parseRuleSet(locale[type], `${locale.tag} ${type}`);
      // The emitted source is evaluated exactly as the generated ESM runtime embeds it.
      const select = new Function(`return ${javascriptRuleFunction(ruleSet)};`)();
      for (const rule of ruleSet)
        for (const sample of expandSamples([...rule.samples.integer, ...rule.samples.decimal])) {
          assert.equal(select(Object.freeze(sampleOperands(sample))), rule.category, `${locale.tag} ${type} ${sample}`);
          samples++;
        }
    }
  assert.ok(samples > 500, `only ${samples} samples were checked`);
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
