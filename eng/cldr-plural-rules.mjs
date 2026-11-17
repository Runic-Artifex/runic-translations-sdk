// Parses CLDR plural rules (UTS #35, Part 3, "Language Plural Rules") and emits
// equivalent C# and JavaScript code over the CLDR operands of a visible decimal.
// eng/generate-cldr.mjs uses it to generate the runtime selectors.

export const pluralCategories = ["zero", "one", "two", "few", "many", "other"];
const operands = new Set(["n", "i", "v", "w", "f", "t", "c", "e"]);

/** Splits a CLDR rule string into its condition AST and sample lists. */
export function parsePluralRule(text) {
  if (typeof text !== "string") throw new Error("A CLDR plural rule must be a string.");
  const at = text.indexOf("@");
  const condition = (at < 0 ? text : text.slice(0, at)).trim();
  const samples = { integer: [], decimal: [] };
  if (at >= 0) {
    for (const part of text.slice(at + 1).split("@")) {
      const match = /^(integer|decimal)\s+(.*)$/s.exec(part.trim());
      if (!match) throw new Error(`Malformed CLDR sample list '${part}'.`);
      samples[match[1]].push(
        ...match[2].split(",").map((item) => item.trim()).filter((item) => item && item !== "…"),
      );
    }
  }
  return { condition: condition ? parseCondition(condition) : null, samples };
}

function parseCondition(text) {
  const tokens = text.match(/\.\.|!=|=|%|,|[a-z]+|\d+|\S/g) ?? [];
  let position = 0;
  const peek = () => tokens[position];
  const take = (expected) => {
    const token = tokens[position++];
    if (token === undefined || (expected !== undefined && token !== expected))
      throw new Error(`Malformed CLDR plural rule '${text}' at token ${position}.`);
    return token;
  };
  const integer = () => {
    const token = take();
    if (!/^\d+$/.test(token)) throw new Error(`Expected a number in CLDR plural rule '${text}'.`);
    return Number(token);
  };
  const relation = () => {
    const operand = take();
    if (!operands.has(operand)) throw new Error(`Unknown CLDR plural operand '${operand}'.`);
    let modulus = null;
    if (peek() === "%") { take("%"); modulus = integer(); if (modulus === 0) throw new Error("Zero CLDR modulus."); }
    const operator = take();
    if (operator !== "=" && operator !== "!=") throw new Error(`Unsupported CLDR plural operator '${operator}'.`);
    const ranges = [];
    do {
      const low = integer();
      const high = peek() === ".." ? (take(".."), integer()) : low;
      if (high < low) throw new Error(`Empty CLDR range in '${text}'.`);
      ranges.push([low, high]);
    } while (peek() === "," && take(","));
    return { operand: operand === "c" ? "e" : operand, modulus, negated: operator === "!=", ranges };
  };
  const or = [];
  do {
    const and = [relation()];
    while (peek() === "and") { take("and"); and.push(relation()); }
    or.push(and);
  } while (peek() === "or" && take("or"));
  if (position !== tokens.length) throw new Error(`Trailing tokens in CLDR plural rule '${text}'.`);
  return or;
}

/** Parses an ordered category map and checks that it is a complete CLDR rule set. */
export function parseRuleSet(rules, label) {
  if (!rules || typeof rules !== "object") throw new Error(`${label} has no plural rules.`);
  const names = Object.keys(rules);
  if (names.at(-1) !== "other") throw new Error(`${label} must end with the 'other' category.`);
  let previous = -1;
  return names.map((category) => {
    const index = pluralCategories.indexOf(category);
    if (index <= previous) throw new Error(`${label} has an unknown or misordered category '${category}'.`);
    previous = index;
    const parsed = parsePluralRule(rules[category]);
    if ((category === "other") !== (parsed.condition === null))
      throw new Error(`${label}/${category}: only 'other' may be unconditional.`);
    return { category, source: rules[category].split("@")[0].trim(), ...parsed };
  });
}

// Every operand is a nonnegative integer. n is the only fractional operand. CLDR
// equality and range relations on n match only integral values, so `n % m = x`
// holds exactly when the visible fraction value t is zero and `i % m = x`.
const targets = {
  // C#: I, F and T are UInt128; V, W and E are int.
  cs: {
    equals: "==",
    differs: "!=",
    name: (operand) => ({ n: "o.I", i: "o.I", v: "o.V", w: "o.W", f: "o.F", t: "o.T", e: "o.E" })[operand],
    literal: (operand, value) => (["v", "w", "e"].includes(operand) ? String(value) : `${value}UL`),
    integral: "o.T == 0UL",
  },
  // JavaScript: every operand is a BigInt.
  js: {
    equals: "===",
    differs: "!==",
    name: (operand) => `o.${operand === "n" ? "i" : operand}`,
    literal: (_, value) => `${value}n`,
    integral: "o.t === 0n",
  },
};

// Returns a boolean expression that can be joined with `&&` without parentheses.
function relationCode(relation, target) {
  const { operand, modulus, negated, ranges } = relation;
  const { equals, differs, name, literal, integral } = targets[target];
  const value = modulus === null ? name(operand) : `(${name(operand)} % ${literal(operand, modulus)})`;
  // A single value on an integer operand negates by operator; anything else as !(...).
  if (ranges.length === 1 && ranges[0][0] === ranges[0][1] && operand !== "n")
    return `${value} ${negated ? differs : equals} ${literal(operand, ranges[0][0])}`;
  const tests = ranges.map(([low, high]) => low === high
    ? `${value} ${equals} ${literal(operand, low)}`
    : `${value} >= ${literal(operand, low)} && ${value} <= ${literal(operand, high)}`);
  let code = tests.length === 1 ? tests[0] : `(${tests.map((test) => (test.includes("&&") ? `(${test})` : test)).join(" || ")})`;
  if (operand === "n") code = `${integral} && ${code}`;
  return negated ? `!(${code})` : code;
}

function conditionCode(condition, target) {
  const alternatives = condition.map((and) => and.map((relation) => relationCode(relation, target)).join(" && "));
  return alternatives.length === 1 ? alternatives[0] : alternatives.map((item) => (item.includes("&&") ? `(${item})` : item)).join(" || ");
}

/** Emits a C# method body selecting a category from `in PluralOperands o`. */
export function csharpRuleBody(ruleSet, indent) {
  const lines = [];
  for (const rule of ruleSet) {
    if (rule.condition === null) lines.push(`${indent}return ${JSON.stringify(rule.category)};`);
    else lines.push(`${indent}if (${conditionCode(rule.condition, "cs")}) return ${JSON.stringify(rule.category)}; // ${rule.source}`);
  }
  return lines.join("\n");
}

/** Emits a JavaScript arrow function selecting a category from BigInt operands. */
export function javascriptRuleFunction(ruleSet) {
  let body = "";
  for (const rule of ruleSet)
    body += rule.condition === null
      ? `return ${JSON.stringify(rule.category)};`
      : `if (${conditionCode(rule.condition, "js")}) return ${JSON.stringify(rule.category)}; `;
  return `(o) => { ${body} }`;
}

/**
 * Expands CLDR sample notation into visible decimals. `0.0~1.5` steps by the
 * last visible digit, and `1.1c6` is the compact form of 1100000 with e = 6.
 */
export function expandSamples(samples) {
  const result = [];
  for (const sample of samples) {
    const [first, last] = sample.split("~");
    if (last === undefined) { result.push(sample); continue; }
    const start = sampleOperands(first), end = sampleOperands(last);
    if (start.e !== 0n || end.e !== 0n || start.v !== end.v) throw new Error(`Unsupported CLDR sample range '${sample}'.`);
    const scale = 10n ** start.v;
    for (let value = start.i * scale + start.f; value <= end.i * scale + end.f; value++) {
      const whole = value / scale, fraction = value % scale;
      result.push(start.v === 0n ? `${whole}` : `${whole}.${fraction.toString().padStart(Number(start.v), "0")}`);
    }
  }
  return result;
}

/** Computes CLDR operands for one expanded sample, as BigInt values. */
export function sampleOperands(sample) {
  const match = /^(\d+)(?:\.(\d+))?(?:c(\d+))?$/.exec(sample);
  if (!match) throw new Error(`Malformed CLDR sample '${sample}'.`);
  const fraction = match[2] ?? "", exponent = Number(match[3] ?? 0);
  let coefficient = BigInt(match[1] + fraction), scale = fraction.length - exponent;
  if (scale < 0) { coefficient *= 10n ** BigInt(-scale); scale = 0; }
  return visibleOperands(coefficient, scale, BigInt(exponent));
}

/** Operands of coefficient / 10^scale, keeping visible trailing zeros. */
export function visibleOperands(coefficient, scale, exponent = 0n) {
  const divisor = 10n ** BigInt(scale);
  const f = coefficient % divisor;
  let t = f, w = BigInt(scale);
  while (w > 0n && t % 10n === 0n) { t /= 10n; w--; }
  if (t === 0n) w = 0n;
  return { i: coefficient / divisor, v: BigInt(scale), w, f, t, e: exponent };
}

// The bytecode of a rule set, which the .NET runtime interprets (GeneratedLocaleData.ApplyPluralRule) instead of
// compiling a method per rule set: about 1.5 KB of data for every pinned rule set, where the compiled selectors took
// 32 KB of NativeAOT code. A rule set is
//   count  (category orCount (andCount relation…)…)…   for each category but 'other', which is the fallback;
// a relation is a header byte (operand code | 8 when negated | 16 with a modulus), the modulus, a range count and
// each range's low and high value. Counts are bytes; the modulus and the range bounds are unsigned LEB128.
export const operandCodes = { n: 0, i: 1, v: 2, w: 3, f: 4, t: 5, e: 6 };

/** Encodes a parsed rule set (parseRuleSet) as plural rule bytecode. */
export function encodeRuleSet(ruleSet) {
  const bytes = [];
  const count = (value) => {
    if (!Number.isInteger(value) || value < 0 || value > 255) throw new Error(`A plural rule count ${value} does not fit a byte.`);
    bytes.push(value);
  };
  const varint = (value) => {
    if (!Number.isSafeInteger(value) || value < 0) throw new Error(`A plural rule value ${value} is not a nonnegative integer.`);
    do {
      const low = value % 128;
      value = Math.floor(value / 128);
      bytes.push(value > 0 ? low | 128 : low);
    } while (value > 0);
  };
  const conditional = ruleSet.filter((rule) => rule.condition !== null);
  if (ruleSet.at(-1).condition !== null || conditional.length !== ruleSet.length - 1)
    throw new Error("Only the last category of a rule set may be unconditional.");
  count(conditional.length);
  for (const rule of conditional) {
    count(pluralCategories.indexOf(rule.category));
    count(rule.condition.length);
    for (const and of rule.condition) {
      count(and.length);
      for (const { operand, modulus, negated, ranges } of and) {
        bytes.push(operandCodes[operand] | (negated ? 8 : 0) | (modulus === null ? 0 : 16));
        if (modulus !== null) varint(modulus);
        count(ranges.length);
        for (const [low, high] of ranges) { varint(low); varint(high); }
      }
    }
  }
  return bytes;
}

/**
 * Selects a category with plural rule bytecode over BigInt operands, as GeneratedLocaleData.ApplyPluralRule does in
 * .NET; the tests compare it with the compiled selectors.
 */
export function selectEncoded(bytes, operands) {
  let position = 0;
  const varint = () => {
    let value = 0n, shift = 0n, byte;
    do { byte = bytes[position++]; value |= BigInt(byte & 127) << shift; shift += 7n; } while (byte & 128);
    return value;
  };
  const names = ["n", "i", "v", "w", "f", "t", "e"];
  for (let categories = bytes[position++]; categories > 0; categories--) {
    const category = bytes[position++];
    let any = false;
    for (let alternatives = bytes[position++]; alternatives > 0; alternatives--) {
      let all = true;
      for (let relations = bytes[position++]; relations > 0; relations--) {
        const header = bytes[position++], operand = names[header & 7];
        let value = operands[operand === "n" ? "i" : operand];
        if (header & 16) value %= varint();
        let match = false;
        for (let ranges = bytes[position++]; ranges > 0; ranges--) {
          const low = varint(), high = varint();
          if (value >= low && value <= high) match = true;
        }
        if (operand === "n" && operands.t !== 0n) match = false;
        if (match === Boolean(header & 8)) all = false;
      }
      if (all) any = true;
    }
    if (any) return pluralCategories[category];
  }
  return "other";
}
