/**
 * Executes the compiler-normalized locale AST used by the generated ESM dynamic runtime.
 * The result is semantic data only. Callers must never turn markup names into HTML.
 * @param {import("./message-composer").MessageArtifact} ast
 * @param {string} locale
 * @param {Record<string, string>} samples
 * @returns {{ kind: "text", value: string } | { kind: "content", nodes: PreviewNode[] }}
 */
export function executeMessagePreview(ast, locale, samples) {
  /** @type {Record<string, unknown>} */
  const inputs = {};
  for (const [name, descriptor] of Object.entries(ast.inputs)) {
    if (!(name in samples)) throw new TypeError(`Enter a sample value for '${name}'.`);
    inputs[name] = parseSample(name, descriptor.type, samples[name]);
  }
  const selected = ast.selectors.map((selector) => {
    const value = inputs[selector.input];
    if (selector.function === "plural") return new Intl.PluralRules(locale, { type: "cardinal" }).select(Number(value));
    if (selector.function === "ordinal") return new Intl.PluralRules(locale, { type: "ordinal" }).select(Number(value));
    return String(value);
  });
  const variant = ast.variants.find((candidate) => ast.selectors.every((selector, index) =>
    candidate.matches[selector.name] === "*" || candidate.matches[selector.name] === selected[index]));
  if (variant === undefined) throw new RangeError("No variant matches these sample values.");
  const nodes = contentNodes(variant.nodes, ast.inputs, inputs, locale);
  return hasMarkup(nodes)
    ? { kind: "content", nodes }
    : { kind: "text", value: flattenPreview(nodes) };
}

/** @param {PreviewNode[]} nodes @returns {string} */
export function flattenPreview(nodes) {
  return nodes.map((node) => node.kind === "text" ? node.value : flattenPreview(node.children)).join("");
}

/**
 * @param {import("./message-composer").ArtifactNode[]} nodes
 * @param {Record<string, import("./message-composer").ArtifactInput>} descriptors
 * @param {Record<string, unknown>} inputs
 * @param {string} locale
 * @returns {PreviewNode[]}
 */
function contentNodes(nodes, descriptors, inputs, locale) {
  return nodes.map((node) => {
    if (node.kind === "markup") {
      return {
        kind: "element",
        name: node.name,
        attributes: { ...node.attributes },
        children: contentNodes(node.children, descriptors, inputs, locale),
      };
    }
    const value = node.kind === "text"
      ? node.value
      : node.kind === "input"
        ? formatInput(inputs, node.input, descriptors[node.input], locale)
        : node.function === "relativeTime"
          ? formatRelativeTime(inputs[node.input], node.unit ?? "day", node.numeric ?? "auto", locale, node.input)
          : formatInput(inputs, node.input, { ...descriptors[node.input], format: node.format }, locale);
    return { kind: "text", value };
  });
}

/** @param {PreviewNode[]} nodes */
function hasMarkup(nodes) {
  return nodes.some((node) => node.kind === "element");
}

/** @param {string} name @param {string} type @param {string} value */
function parseSample(name, type, value) {
  if (type === "int") {
    try { return BigInt(value); } catch { throw new TypeError(`Sample '${name}' must be an integer.`); }
  }
  if (type === "number") {
    const parsed = Number(value);
    if (!Number.isFinite(parsed)) throw new TypeError(`Sample '${name}' must be a finite number.`);
    return parsed;
  }
  if (type === "bool") {
    if (value === "true") return true;
    if (value === "false") return false;
    throw new TypeError(`Sample '${name}' must be true or false.`);
  }
  return value;
}

/** @param {Record<string, unknown>} inputs @param {string} name @param {import("./message-composer").ArtifactInput} descriptor @param {string} locale */
function formatInput(inputs, name, descriptor, locale) {
  const value = inputs[name];
  const format = descriptor.format;
  switch (descriptor.type) {
    case "string": if (typeof value !== "string") invalid(name, "a string"); return value;
    case "bool": if (typeof value !== "boolean") invalid(name, "true or false"); return value ? "true" : "false";
    case "int": return formatInteger(value, format, locale, name);
    case "number": return formatNumber(value, format, locale, name);
    case "date": return formatDate(value, format, locale, name);
    case "time": return formatTime(value, format, locale, name);
    case "datetime": return formatDateTime(value, format, locale, name);
    case "guid": return formatGuid(value, format, name);
  }
}

/** @param {unknown} value @param {string} format @param {string} locale @param {string} name */
function formatInteger(value, format, locale, name) {
  if (typeof value !== "bigint") invalid(name, "an integer");
  if (format === "plain") return value.toString();
  if (format === "grouped") return new Intl.NumberFormat(locale, { maximumFractionDigits: 0 }).format(value);
  throw new TypeError(`Unsupported integer format '${format}'.`);
}

/** @param {unknown} value @param {string} format @param {string} locale @param {string} name */
function formatNumber(value, format, locale, name) {
  if (typeof value !== "number" || !Number.isFinite(value)) invalid(name, "a finite number");
  if (format === "plain") return expandExponent(String(value));
  if (format === "grouped") return new Intl.NumberFormat(locale, { maximumFractionDigits: 20 }).format(value);
  const fixed = /^fixed([0-6])$/.exec(format);
  if (fixed !== null) return new Intl.NumberFormat(locale, { minimumFractionDigits: Number(fixed[1]), maximumFractionDigits: Number(fixed[1]), useGrouping: false }).format(value);
  const percent = /^percent([0-4])$/.exec(format);
  if (percent !== null) return new Intl.NumberFormat(locale, { style: "percent", minimumFractionDigits: Number(percent[1]), maximumFractionDigits: Number(percent[1]) }).format(value);
  throw new TypeError(`Unsupported number format '${format}'.`);
}

/** @param {unknown} value @param {string} format @param {string} locale @param {string} name */
function formatDate(value, format, locale, name) {
  if (typeof value !== "string" || !/^\d{4}-\d{2}-\d{2}$/.test(value)) invalid(name, "an ISO date");
  if (format === "iso") return value;
  const date = new Date(`${value}T00:00:00Z`);
  if (Number.isNaN(date.valueOf()) || date.toISOString().slice(0, 10) !== value) invalid(name, "an ISO date");
  if (!["short", "medium", "long"].includes(format)) throw new TypeError(`Unsupported date format '${format}'.`);
  return new Intl.DateTimeFormat(locale, { dateStyle: /** @type {"short"|"medium"|"long"} */ (format), timeZone: "UTC" }).format(date);
}

/** @param {unknown} value @param {string} format @param {string} locale @param {string} name */
function formatTime(value, format, locale, name) {
  if (typeof value !== "string" || !/^\d{2}:\d{2}:\d{2}(?:\.\d{1,7})?$/.test(value)) invalid(name, "an ISO time");
  if (format === "iso") return value;
  const date = new Date(`1970-01-01T${value}Z`);
  if (Number.isNaN(date.valueOf())) invalid(name, "an ISO time");
  if (!["short", "medium"].includes(format)) throw new TypeError(`Unsupported time format '${format}'.`);
  return new Intl.DateTimeFormat(locale, { timeStyle: /** @type {"short"|"medium"} */ (format), timeZone: "UTC" }).format(date);
}

/** @param {unknown} value @param {string} format @param {string} locale @param {string} name */
function formatDateTime(value, format, locale, name) {
  if (typeof value !== "string") invalid(name, "an ISO instant");
  const date = new Date(value);
  if (Number.isNaN(date.valueOf())) invalid(name, "an ISO instant");
  if (format === "iso") return date.toISOString().replace(/\.(\d{3})Z$/, (_, digits) => `.${digits}0000Z`);
  if (!["short", "medium", "long"].includes(format)) throw new TypeError(`Unsupported datetime format '${format}'.`);
  const style = /** @type {"short"|"medium"|"long"} */ (format);
  return new Intl.DateTimeFormat(locale, { dateStyle: style, timeStyle: style, timeZone: "UTC" }).format(date);
}

/** @param {unknown} value @param {string} format @param {string} name */
function formatGuid(value, format, name) {
  if (typeof value !== "string" || !/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(value)) invalid(name, "a canonical UUID");
  const canonical = value.toLowerCase();
  if (format.toLowerCase() === "d") return canonical;
  if (format.toLowerCase() === "n") return canonical.replaceAll("-", "");
  throw new TypeError(`Unsupported UUID format '${format}'.`);
}

/** @param {unknown} value @param {string} unit @param {string} numeric @param {string} locale @param {string} name */
function formatRelativeTime(value, unit, numeric, locale, name) {
  const number = typeof value === "bigint" ? Number(value) : value;
  if (typeof number !== "number" || !Number.isFinite(number)) invalid(name, "a number");
  return new Intl.RelativeTimeFormat(locale, { numeric: /** @type {"always"|"auto"} */ (numeric) }).format(number, /** @type {Intl.RelativeTimeFormatUnit} */ (unit));
}

/** @param {string} value */
function expandExponent(value) {
  if (!/[eE]/.test(value)) return value;
  const [coefficient, exponentText] = value.toLowerCase().split("e");
  const exponent = Number(exponentText);
  const negative = coefficient.startsWith("-");
  const unsigned = negative ? coefficient.slice(1) : coefficient;
  const point = unsigned.indexOf(".");
  const digits = unsigned.replace(".", "");
  const decimal = (point < 0 ? unsigned.length : point) + exponent;
  const result = decimal <= 0 ? `0.${"0".repeat(-decimal)}${digits}` : decimal >= digits.length ? digits + "0".repeat(decimal - digits.length) : `${digits.slice(0, decimal)}.${digits.slice(decimal)}`;
  return negative ? `-${result}` : result;
}

/** @param {string} name @param {string} expected @returns {never} */
function invalid(name, expected) { throw new TypeError(`Input '${name}' must be ${expected}.`); }

/** @typedef {{ kind: "text", value: string } | { kind: "element", name: string, attributes: Record<string, string>, children: PreviewNode[] }} PreviewNode */
