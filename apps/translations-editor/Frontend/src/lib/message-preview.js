/**
 * Captures the preview identity selected by the caller. Reactive selection may
 * change afterward without retargeting an already scheduled request.
 * @param {string} path
 * @param {string} content
 * @param {string} key
 * @param {string} locale
 */
export function createMessagePreviewRequest(path, content, key, locale) {
  return Object.freeze({ path, content, key, locale });
}

/**
 * Copies sample values into a dictionary with no inherited property names.
 * @param {Record<string, string> | undefined} [source]
 * @returns {Record<string, string>}
 */
export function createPreviewSamples(source) {
  /** @type {Record<string, string>} */
  const samples = Object.create(null);
  if (source !== undefined) {
    for (const [name, value] of Object.entries(source)) samples[name] = value;
  }
  return samples;
}

/**
 * @param {Record<string, string>} samples
 * @param {string} name
 * @param {string} fallback
 */
export function previewSampleOr(samples, name, fallback) {
  return Object.hasOwn(samples, name) ? samples[name] : fallback;
}

/**
 * @param {Record<string, string>} samples
 * @param {string} name
 * @param {string} value
 */
export function withPreviewSample(samples, name, value) {
  const next = createPreviewSamples(samples);
  next[name] = value;
  return next;
}

/**
 * Owns debounce and freshness for preview work without depending on reactive
 * component globals.
 * @param {(callback: () => void, delay: number) => unknown} setTimer
 * @param {(handle: unknown) => void} clearTimer
 */
export function createMessagePreviewScheduler(setTimer, clearTimer) {
  let epoch = 0;
  /** @type {unknown} */
  let handle;
  return Object.freeze({
    /** @param {number} delay @param {(epoch: number) => void} operation */
    schedule(delay, operation) {
      if (handle !== undefined) clearTimer(handle);
      const scheduledEpoch = ++epoch;
      handle = setTimer(() => {
        handle = undefined;
        operation(scheduledEpoch);
      }, delay);
      return scheduledEpoch;
    },
    cancel() {
      if (handle !== undefined) clearTimer(handle);
      handle = undefined;
      epoch += 1;
    },
    /** @param {number} candidate */
    isCurrent(candidate) {
      return candidate === epoch;
    },
  });
}

/**
 * Runs the host request sequence for a captured selection. AST 5 receives a
 * second request carrying prototype-safe samples; AST 2/4 remains local.
 * @param {(path: string, content: string, locale: string, key: string, samplesJson?: string) => Promise<any>} previewMessage
 * @param {{ readonly path: string, readonly content: string, readonly key: string, readonly locale: string }} request
 * @param {Record<string, string>} previousSamples
 * @param {(type: string) => string} defaultSample
 * @param {() => boolean} [isCurrent]
 */
export async function routeMessagePreview(previewMessage, request, previousSamples, defaultSample, isCurrent = () => true) {
  const initial = await previewMessage(request.path, request.content, request.locale, request.key);
  let samples = createPreviewSamples(previousSamples);
  if (!isCurrent() || !initial.success || typeof initial.astJson !== "string" || typeof initial.locale !== "string") {
    return { initial, ast: undefined, samples, rendered: undefined };
  }
  const ast = JSON.parse(initial.astJson);
  samples = createPreviewSamples();
  const inputs = ast.astVersion === 5 ? ast.inputs : Object.entries(ast.inputs)
    .map(([name, descriptor]) => ({ name, type: descriptor.type }));
  for (const input of inputs) {
    samples[input.name] = previewSampleOr(previousSamples, input.name, defaultSample(input.type));
  }
  const rendered = ast.astVersion === 5
    ? await previewMessage(request.path, request.content, request.locale, request.key, JSON.stringify(samples))
    : undefined;
  return { initial, ast, samples, rendered };
}

/**
 * Executes the compiler-normalized locale AST used by the generated ESM dynamic runtime.
 * The result is semantic data only. Callers must never turn markup names into HTML.
 * @param {import("./message-model").MessageArtifact} ast
 * @param {string} locale
 * @param {Record<string, string>} samples
 * @returns {{ kind: "text", value: string } | { kind: "content", nodes: PreviewNode[] }}
 */
export function executeMessagePreview(ast, locale, samples) {
  if (ast.astVersion === 5) {
    throw new TypeError("RMF2 execution-v2 previews must be rendered by the compiler host.");
  }
  if (ast.astVersion !== 2 && ast.astVersion !== 4) {
    throw new TypeError(`Unsupported message preview AST version '${ast.astVersion}'.`);
  }
  locale = ast.contentLocale ?? locale;
  /** @type {Record<string, unknown>} */
  const inputs = Object.create(null);
  for (const [name, descriptor] of Object.entries(ast.inputs)) {
    if (!Object.hasOwn(samples, name)) throw new TypeError(`Enter a sample value for '${name}'.`);
    inputs[name] = parseSample(name, descriptor.type, samples[name]);
  }
  const selected = ast.selectors.map((selector) => {
    const value = inputs[selector.input];
    if (selector.function === "plural") return new Intl.PluralRules(locale, { type: "cardinal" }).select(Number(value));
    if (selector.function === "ordinal") return new Intl.PluralRules(locale, { type: "ordinal" }).select(Number(value));
    return String(value);
  });
  const candidates = ast.variants.map((variant, order) => ({ variant, order, ranks: ast.selectors.map((selector, index) => {
    const key = variant.matches[selector.name];
    if (key === "*") return 2;
    if (key === String(inputs[selector.input])) return 0;
    return key === selected[index] ? 1 : 3;
  }) })).filter(candidate => candidate.ranks.every(rank => rank < 3));
  candidates.sort((left, right) => {
    for (let index = 0; index < left.ranks.length; index++) if (left.ranks[index] !== right.ranks[index]) return left.ranks[index] - right.ranks[index];
    return left.order - right.order;
  });
  const variant = candidates[0]?.variant;
  if (variant === undefined) throw new RangeError("No variant matches these sample values.");
  const nodes = contentNodes(variant.nodes, ast.inputs, inputs, locale);
  return hasMarkup(nodes)
    ? { kind: "content", nodes }
    : { kind: "text", value: flattenPreview(nodes) };
}

/**
 * Converts the compiler host's JSON preview runs to the same inert semantic
 * result consumed by InlinePreview. Markup names and options remain data.
 * @param {string} renderedJson
 * @returns {{ kind: "text", value: string } | { kind: "content", nodes: PreviewNode[] }}
 */
export function parseRenderedMessagePreview(renderedJson) {
  const document = JSON.parse(renderedJson);
  if (!isRecord(document) || typeof document.key !== "string" ||
      typeof document.locale !== "string" || !Array.isArray(document.runs) ||
      !hasExactKeys(document, ["key", "locale", "runs"])) {
    throw new TypeError("The compiler host returned an invalid rendered message preview.");
  }
  const nodes = document.runs.map(renderedRun);
  return hasMarkup(nodes)
    ? { kind: "content", nodes }
    : { kind: "text", value: flattenPreview(nodes) };
}

/** @param {unknown} value @returns {PreviewNode} */
function renderedRun(value) {
  if (!isRecord(value)) invalidRenderedRun();
  if (typeof value.text === "string") {
    const textOnly = hasExactKeys(value, ["text"]);
    const fullRun = hasExactKeys(value, ["name", "text", "options", "children"]);
    if (!textOnly && !fullRun) invalidRenderedRun();
    if (fullRun && (typeof value.name !== "string" || !isStringRecord(value.options) ||
        !Array.isArray(value.children) || value.children.length !== 0)) {
      invalidRenderedRun();
    }
    return { kind: "text", value: value.text };
  }
  if (value.text !== null || typeof value.name !== "string" || value.name.length === 0 ||
      !isStringRecord(value.options) || !Array.isArray(value.children) ||
      !hasExactKeys(value, ["name", "text", "options", "children"])) {
    invalidRenderedRun();
  }
  return {
    kind: "element",
    name: value.name,
    attributes: { ...value.options },
    children: value.children.map(renderedRun),
  };
}

/** @param {unknown} value @returns {value is Record<string, unknown>} */
function isRecord(value) {
  return value !== null && typeof value === "object" && !Array.isArray(value);
}

/** @param {unknown} value @returns {value is Record<string, string>} */
function isStringRecord(value) {
  return isRecord(value) && Object.values(value).every((item) => typeof item === "string");
}

/** @param {Record<string, unknown>} value @param {string[]} expected */
function hasExactKeys(value, expected) {
  const keys = Object.keys(value);
  return keys.length === expected.length && keys.every((key) => expected.includes(key));
}

/** @returns {never} */
function invalidRenderedRun() {
  throw new TypeError("The compiler host returned an invalid rendered message run.");
}

/** @param {PreviewNode[]} nodes @returns {string} */
export function flattenPreview(nodes) {
  return nodes.map((node) => node.kind === "text" ? node.value : flattenPreview(node.children)).join("");
}

/**
 * @param {import("./message-model").ArtifactNode[]} nodes
 * @param {Record<string, import("./message-model").ArtifactInput>} descriptors
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
        attributes: Object.fromEntries(Object.entries(node.attributes).map(([key, value]) => [key, node.variableOptions?.includes(key) ? String(inputs[value]) : value])),
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

/** @param {Record<string, unknown>} inputs @param {string} name @param {import("./message-model").ArtifactInput} descriptor @param {string} locale */
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
