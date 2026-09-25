// RMF2 execution-v2 ESM runtime. Configuration constants are emitted above.
const maximumCoefficient = 79228162514264337593543950335n;
const minimumInt64 = -9223372036854775808n;
const maximumInt64 = 9223372036854775807n;
const localeSet = new Set(locales);
const limits = Object.freeze({ maximumDocumentBytes: 8388608, maximumDepth: 64, maximumMessages: 50000, maximumPatternBytes: 65536, maximumArgumentsPerMessage: 32 });
const validatedMessages = new WeakSet();
const decodedArtifacts = new WeakSet();
let localeResolver = () => typeof document === "undefined" ? baseLocale : (document.documentElement.lang || baseLocale);

export function freezeTrusted(value) {
  if (Array.isArray(value)) return Object.freeze(value.map(freezeTrusted));
  if (isRecord(value)) return freezeOwnRecord(Object.keys(value).map(key => [key, freezeTrusted(value[key])]));
  return value;
}

export function freezeOwnRecord(entries) {
  const result = Object.create(null);
  for (const [name, value] of entries) Object.defineProperty(result, name, { value, enumerable: true, configurable: false, writable: false });
  return Object.freeze(result);
}

export function configureLocaleResolver(resolver) {
  if (typeof resolver !== "function") throw new TypeError("The locale resolver must be a function.");
  const previous = localeResolver; localeResolver = resolver;
  return () => { if (localeResolver === resolver) localeResolver = previous; };
}

export function getLocale() { return resolveLocale(localeResolver()); }

export function createLocaleSource(options = {}) {
  if (!isRecord(options)) throw new TypeError("Locale source options must be an object.");
  let current = resolveLocale(options.initialLocale ?? baseLocale); const listeners = new Set();
  return Object.freeze({
    getLocale: () => current,
    subscribe(listener) { if (typeof listener !== "function") throw new TypeError("A locale listener must be a function."); listeners.add(listener); let active = true; return () => { if (active) { active = false; listeners.delete(listener); } }; },
    setLocale(locale) { const next = resolveLocale(locale); if (next === current) return; current = next; for (const listener of [...listeners]) listener(next); },
  });
}

export function resolveLocale(requested) {
  if (typeof requested !== "string" || !requested) throw new RangeError("A non-empty locale is required.");
  const canonical = canonicalLocale(requested); if (canonical === null) throw new RangeError(`Invalid locale '${requested}'.`);
  if (localeSet.has(canonical)) return canonical;
  if (unsupportedLocalePolicy === "ParentsThenDefault") {
    let parent = canonical; while (parent.includes("-")) { parent = parent.slice(0, parent.lastIndexOf("-")); if (localeSet.has(parent)) return parent; }
    return baseLocale;
  }
  if (unsupportedLocalePolicy === "Default") return baseLocale;
  throw new RangeError(`Unsupported locale '${requested}'.`);
}

export function decimal(value) { return parseDecimal(value); }

function parseDecimal(value) {
  if (typeof value !== "string" || value.length > 4096) throw new TypeError("A portable decimal string is required.");
  const match = /^(-?)(0|[1-9][0-9]*)(?:\.([0-9]+))?(?:[eE]([+-]?[0-9]+))?$/.exec(value);
  if (!match) throw new TypeError("Invalid portable decimal.");
  let exponent = 0;
  if (match[4] !== undefined) {
    if (!/^[+-]?(?:0|[1-9][0-9]*)$/.test(match[4])) throw new TypeError("Invalid portable decimal exponent.");
    const exponentValue = BigInt(match[4]);
    if (exponentValue < -2147483648n || exponentValue > 2147483647n) throw new RangeError("Portable decimal exponent is outside Int32.");
    exponent = Number(exponentValue);
  }
  let digits = (match[2] + (match[3] ?? "")).replace(/^0+/, "");
  if (!digits) return Object.freeze({ coefficient: 0n, scale: 0, negative: false });
  let scale = (match[3]?.length ?? 0) - exponent;
  while (digits.endsWith("0")) { digits = digits.slice(0, -1); scale--; }
  if (scale < 0) {
    if (digits.length - scale > 29) throw new RangeError("Portable decimal coefficient overflow.");
    digits += "0".repeat(-scale); scale = 0;
  }
  if (scale > 28 || digits.length > 29) throw new RangeError("Portable decimal scale or coefficient overflow.");
  const coefficient = BigInt(digits);
  if (coefficient > maximumCoefficient) throw new RangeError("Portable decimal coefficient overflow.");
  return Object.freeze({ coefficient, scale, negative: match[1] === "-" });
}

function exactDecimal(value) {
  if (typeof value === "string") return parseDecimal(value);
  if (!isRecord(value) || !exactKeys(value, ["coefficient", "scale", "negative"]) || typeof value.coefficient !== "bigint" ||
      !Number.isInteger(value.scale) || value.scale < 0 || value.scale > 28 || typeof value.negative !== "boolean" ||
      value.coefficient < 0n || value.coefficient > maximumCoefficient) throw new TypeError("An exact RMF2 decimal is required.");
  let coefficient = value.coefficient, scale = value.scale;
  if (coefficient === 0n) return Object.freeze({ coefficient: 0n, scale: 0, negative: false });
  while (scale > 0 && coefficient % 10n === 0n) { coefficient /= 10n; scale--; }
  return Object.freeze({ coefficient, scale, negative: value.negative });
}

function decimalCanonical(value) {
  const digits = value.coefficient.toString();
  const unsigned = value.scale === 0 ? digits : value.scale >= digits.length
    ? `0.${"0".repeat(value.scale - digits.length)}${digits}`
    : `${digits.slice(0, digits.length - value.scale)}.${digits.slice(digits.length - value.scale)}`;
  return value.negative && value.coefficient !== 0n ? `-${unsigned}` : unsigned;
}

function decimalEquals(left, right) { return left.coefficient === right.coefficient && left.scale === right.scale && left.negative === right.negative; }
function decimalAbsolute(value) { return value.negative ? Object.freeze({ coefficient: value.coefficient, scale: value.scale, negative: false }) : value; }
function decimalIntegral(value) { return value.scale === 0; }
function decimalIntegerPart(value) { return value.scale === 0 ? value.coefficient : value.coefficient / (10n ** BigInt(value.scale)); }
function decimalModulo(value, divisor) { if (value.scale !== 0) return null; return value.coefficient % BigInt(divisor); }

function coerceInput(value, type, name) {
  switch (type) {
    case "string": if (typeof value === "string") return value; break;
    case "boolean": if (typeof value === "boolean") return value; break;
    case "int64": {
      const integer = typeof value === "bigint" ? value : typeof value === "number" && Number.isSafeInteger(value) ? BigInt(value) : null;
      if (integer !== null && integer >= minimumInt64 && integer <= maximumInt64) return integer; break;
    }
    case "decimal": return exactDecimal(value);
    case "date": if (typeof value === "string" && validDate(value)) return value; break;
    case "time": if (typeof value === "string" && validTime(value)) return value; break;
    case "datetime": if (typeof value === "string" && validDateTime(value)) return value; break;
    case "guid": if (typeof value === "string" && /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(value)) return value.toLowerCase(); break;
  }
  throw new TypeError(`Input '${name}' does not contain its declared ${type} carrier.`);
}

function validateInputs(inputs, contract) {
  if (!isRecord(inputs)) throw new TypeError("Message inputs must be an object.");
  const expected = contract.inputs.map(item => item.name).sort(); const actual = Object.keys(inputs).sort();
  if (actual.length !== expected.length || actual.some((name, index) => name !== expected[index])) throw new TypeError(`Message inputs must contain exactly: ${expected.join(", ")}.`);
  return freezeOwnRecord(contract.inputs.map(item => [item.name, Object.freeze({ carrier: coerceInput(inputs[item.name], item.type, item.name), type: item.type, format: Object.freeze({ function: defaultFunction(item.type), options: freezeOwnRecord([]) }) })]));
}

function defaultFunction(type) { return ({ string: "string", int64: "integer", decimal: "number", boolean: "runic:boolean", date: "date", time: "time", datetime: "datetime", guid: "runic:uuid" })[type]; }
function functionType(name) { return ({ string: "string", integer: "int64", number: "decimal", "runic:boolean": "boolean", date: "date", time: "time", datetime: "datetime", "runic:uuid": "guid", "runic:relative-time": "decimal" })[name]; }
function optionType(fn, name) {
  if (["string", "runic:boolean", "integer", "number"].includes(fn) && name === "select") return "string";
  if (fn === "integer" && name === "useGrouping") return "string";
  if (["number", "date", "time", "datetime", "runic:uuid"].includes(fn) && name === "style") return "string";
  if (fn === "number" && ["minimumFractionDigits", "maximumFractionDigits"].includes(name)) return "int64";
  if (fn === "runic:relative-time" && ["unit", "numeric"].includes(name)) return "string";
  return null;
}

function literal(value, type) {
  if (value.kind === "number-literal") {
    const number = parseDecimal(value.value); if (decimalCanonical(number) !== value.canonical) throw new TypeError("Numeric canonical field mismatch.");
    if (type === "decimal") return number;
    if (type === "int64" && number.scale === 0) { const integer = number.negative ? -number.coefficient : number.coefficient; if (integer >= minimumInt64 && integer <= maximumInt64) return integer; }
  }
  if (value.kind === "string-literal") {
    if (type === "string") return value.value;
    if (type === "boolean" && ["true", "false"].includes(value.value)) return value.value === "true";
    if (type === "date" && validDate(value.value)) return value.value;
    if (type === "time" && validTime(value.value)) return value.value;
    if (type === "datetime" && validDateTime(value.value)) return value.value;
    if (type === "guid" && /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(value.value)) return value.value.toLowerCase();
  }
  throw new TypeError("Literal does not have its declared v5 carrier.");
}

function contextFor(wrapper, inputs, contract) {
  const inputValues = Object.assign(Object.create(null), validateInputs(inputs, contract));
  const locals = Object.create(null);
  const resolve = (value, literalType) => {
    if (value.kind === "input") { if (!Object.hasOwn(inputValues, value.value)) throw new TypeError("Unbound input."); return inputValues[value.value]; }
    if (value.kind === "local") { if (!Object.hasOwn(locals, value.value)) throw new TypeError("Unbound local."); return locals[value.value]; }
    const carrier = literal(value, literalType); return Object.freeze({ carrier, type: literalType, format: Object.freeze({ function: defaultFunction(literalType), options: freezeOwnRecord([]) }) });
  };
  const expression = expressionValue => {
    const inherited = resolve(expressionValue.operand, expressionValue.valueType);
    if (inherited.type !== expressionValue.valueType) throw new TypeError("Expression carrier mismatch.");
    if (expressionValue.function === undefined) return inherited;
    const entries = expressionValue.options.map(option => {
      const type = optionType(expressionValue.function, option.name); if (!type) throw new TypeError("Unknown formatter option.");
      return [option.name, resolve(option.value, type).carrier];
    });
    const options = freezeOwnRecord(entries); validateResolvedOptions(expressionValue.function, options);
    return Object.freeze({ carrier: inherited.carrier, type: inherited.type, format: Object.freeze({ function: expressionValue.function, options }) });
  };
  for (const declaration of wrapper.ast.declarations) {
    const resolved = expression(declaration.expression);
    if (declaration.kind === "input") inputValues[declaration.name] = resolved; else locals[declaration.name] = resolved;
  }
  return Object.freeze({ resolve, expression });
}

function validateResolvedOptions(fn, options) {
  const allowed = {
    string: { select: ["exact"] }, "runic:boolean": { select: ["exact"] }, integer: { select: ["plural", "ordinal", "exact"], useGrouping: ["always", "never"] },
    number: { select: ["plural", "ordinal", "exact"], style: ["decimal", "percent"] }, date: { style: ["iso", "short", "long"] },
    time: { style: ["iso", "short", "long"] }, datetime: { style: ["iso", "short", "long"] }, "runic:uuid": { style: ["d", "n", "b", "p"] },
    "runic:relative-time": { unit: ["second", "minute", "hour", "day", "week", "month", "year"], numeric: ["always", "auto"] },
  }[fn];
  if (!allowed) throw new TypeError("Unknown formatter.");
  for (const name of Object.keys(options)) {
    if (!Object.hasOwn(allowed, name) && !(fn === "number" && ["minimumFractionDigits", "maximumFractionDigits"].includes(name))) throw new TypeError("Unknown formatter option.");
    if (fn === "number" && ["minimumFractionDigits", "maximumFractionDigits"].includes(name)) { if (typeof options[name] !== "bigint" || options[name] < 0n || options[name] > 6n) throw new RangeError("Fraction digits are outside 0..6."); }
    else if (!allowed[name].includes(options[name])) throw new RangeError("Invalid formatter option value.");
  }
  if (fn === "number") {
    const style = options.style ?? "decimal", minimum = options.minimumFractionDigits ?? 0n, maximum = options.maximumFractionDigits ?? (style === "percent" ? 4n : 6n);
    if (minimum > maximum || maximum > (style === "percent" ? 4n : 6n)) throw new RangeError("Invalid number precision.");
  }
}

function formatResolved(value, locale) {
  const fn = value.format.function, options = value.format.options;
  switch (fn) {
    case "string": return value.carrier;
    case "runic:boolean": return value.carrier ? "true" : "false";
    case "integer": return options.useGrouping === "always" ? groupInteger(value.carrier, locale) : value.carrier.toString();
    case "number": return formatDecimal(value.type === "int64" ? decimalFromInteger(value.carrier) : value.carrier, locale, options);
    case "date": return formatTemporal(value.carrier, fn, options.style ?? "iso", locale);
    case "time": return formatTemporal(value.carrier, fn, options.style ?? "iso", locale);
    case "datetime": return formatTemporal(value.carrier, fn, options.style ?? "iso", locale);
    case "runic:uuid": { const canonical = value.carrier; const style = options.style ?? "d"; if (style === "d") return canonical; if (style === "n") return canonical.replaceAll("-", ""); if (style === "b") return `{${canonical}}`; return `(${canonical})`; }
    case "runic:relative-time": return formatRelative(value.type === "int64" ? decimalFromInteger(value.carrier) : value.carrier, options.unit ?? "day", options.numeric ?? "always", locale);
    default: throw new TypeError("Unknown formatter.");
  }
}

function decimalFromInteger(value) { return Object.freeze({ coefficient: value < 0n ? -value : value, scale: 0, negative: value < 0n }); }

function formatDecimal(value, locale, options) {
  const percent = (options.style ?? "decimal") === "percent";
  const minimum = Number(options.minimumFractionDigits ?? 0n), maximum = Number(options.maximumFractionDigits ?? (percent ? 4n : 6n));
  let coefficient = value.coefficient, scale = value.scale - (percent ? 2 : 0);
  if (scale < 0) { coefficient *= 10n ** BigInt(-scale); scale = 0; }
  if (scale > maximum) { const divisor = 10n ** BigInt(scale - maximum); const quotient = coefficient / divisor, remainder = coefficient % divisor; coefficient = quotient + (remainder * 2n >= divisor ? 1n : 0n); scale = maximum; }
  let digits = coefficient.toString();
  let whole = scale === 0 ? digits : scale >= digits.length ? "0" : digits.slice(0, digits.length - scale);
  let fraction = scale === 0 ? "" : (scale >= digits.length ? "0".repeat(scale - digits.length) + digits : digits.slice(digits.length - scale));
  while (fraction.length > minimum && fraction.endsWith("0")) fraction = fraction.slice(0, -1);
  while (fraction.length < minimum) fraction += "0";
  const { decimal: separator } = numberSymbols(locale);
  return `${value.negative && coefficient !== 0n ? "-" : ""}${whole}${fraction ? separator + fraction : ""}${percent ? "%" : ""}`;
}

function numberSymbols(locale) {
  const parts = new Intl.NumberFormat(locale, { useGrouping: false, minimumFractionDigits: 1, maximumFractionDigits: 1 }).formatToParts(1.1);
  return { decimal: parts.find(part => part.type === "decimal")?.value ?? "." };
}
function groupInteger(value, locale) { return new Intl.NumberFormat(locale, { useGrouping: true, maximumFractionDigits: 0 }).format(value); }

function selectPlural(value, locale, ordinal) {
  const decimalValue = decimalAbsolute(value); const language = locale.toLowerCase().split("-")[0]; const rules = generatedLocaleData.plural[language];
  if (!rules) throw new RangeError(`Plural selection is not supported for locale '${locale}'.`);
  const rule = rules[ordinal ? 1 : 0], integral = decimalIntegral(decimalValue), integer = decimalIntegerPart(decimalValue);
  const equals = expected => decimalValue.scale === 0 && decimalValue.coefficient === BigInt(expected);
  const modulo = divisor => decimalModulo(decimalValue, divisor);
  if (rule === "english" && integral) { const mod100 = modulo(100), mod10 = modulo(10); if (mod10 === 1n && mod100 !== 11n) return "one"; if (mod10 === 2n && mod100 !== 12n) return "two"; if (mod10 === 3n && mod100 !== 13n) return "few"; }
  else if (rule === "italian") return [8,11,80,800].some(equals) ? "many" : "other";
  else if (rule === "swedish") { const mod100 = modulo(100), mod10 = modulo(10); return (mod10 === 1n || mod10 === 2n) && mod100 !== 11n && mod100 !== 12n ? "one" : "other"; }
  else if (rule === "one" || rule === "integer-one") return equals(1) ? "one" : "other";
  else if (rule === "danish") return equals(1) || (!integral && (integer === 0n || integer === 1n)) ? "one" : "other";
  else if (rule === "one-and-million") { if (equals(1)) return "one"; return integral && !equals(0) && modulo(1000000) === 0n ? "many" : "other"; }
  else if (rule === "french") { if (integral && !equals(0) && modulo(1000000) === 0n) return "many"; return integer === 0n || integer === 1n ? "one" : "other"; }
  return "other";
}

function formatRelative(value, unit, numeric, locale) {
  const language = locale.toLowerCase().split("-")[0], data = generatedLocaleData.relativeTime[language];
  if (!data) throw new RangeError(`Relative-time formatting is not supported for locale '${locale}'.`);
  const signed = value.negative ? -value.coefficient : value.coefficient;
  if (numeric === "auto" && unit === "day" && value.scale === 0 && signed >= -1n && signed <= 1n) return data.autoDay[Number(signed + 1n)];
  const absolute = decimalAbsolute(value), forms = data.units[unit], noun = forms[selectPlural(absolute, locale, false) === "one" ? 0 : 1];
  return (value.negative ? data.past : data.future).replace("{0}", decimalCanonical(absolute)).replace("{unit}", noun);
}

function formatTemporal(value, type, style, locale) {
  if (style === "iso") return value;
  const date = type === "date" ? new Date(`${value}T00:00:00Z`) : type === "time" ? new Date(`1970-01-01T${value}Z`) : new Date(value);
  if (type === "date") return new Intl.DateTimeFormat(locale, { dateStyle: style, timeZone: "UTC" }).format(date);
  if (type === "time") return new Intl.DateTimeFormat(locale, { timeStyle: style, timeZone: "UTC" }).format(date);
  return new Intl.DateTimeFormat(locale, { dateStyle: style, timeStyle: style, timeZone: "UTC" }).format(date);
}

export function formatCompiledMessage(key, wrapper, inputs, requestedLocale) {
  const contract = messageContracts[key]; if (!contract || !wrapper) throw new RangeError(`Unknown message '${key}'.`);
  if (!validatedMessages.has(wrapper)) throw new TypeError("A validated v5 message is required.");
  const context = contextFor(wrapper, inputs, contract), locale = wrapper.contentLocale;
  const selections = wrapper.ast.selectors.map(selector => {
    const resolved = context.resolve(selector.value, selector.type); const canonical = canonicalCarrier(resolved.carrier, selector.type);
    return { resolved, canonical, category: selector.function === "exact" ? null : selectPlural(selector.type === "int64" ? decimalFromInteger(resolved.carrier) : resolved.carrier, locale, selector.function === "ordinal") };
  });
  let selected = null, selectedRanks = null, selectedIndex = -1;
  for (let variantIndex = 0; variantIndex < wrapper.ast.variants.length; variantIndex++) {
    const variant = wrapper.ast.variants[variantIndex], ranks = []; let matches = true;
    for (let index = 0; index < selections.length; index++) {
      const keyValue = variant.keys[index], selector = wrapper.ast.selectors[index], value = selections[index]; let rank;
      if (keyValue.kind === "wildcard") rank = 0;
      else if (["int64", "decimal"].includes(selector.type)) rank = keyValue.canonical !== undefined ? (keyValue.canonical === value.canonical ? 2 : -1) : (keyValue.value === value.category ? 1 : -1);
      else rank = keyValue.value.normalize("NFC") === value.canonical.normalize("NFC") ? 1 : -1;
      if (rank < 0) { matches = false; break; } ranks.push(rank);
    }
    if (matches && (selectedRanks === null || compareRanks(ranks, selectedRanks) > 0)) { selected = variant; selectedRanks = ranks; selectedIndex = variantIndex; }
  }
  if (!selected) throw new RangeError("Message has no matching variant.");
  const built = buildNodes(selected.nodes, context, locale, `v${selectedIndex}.`);
  if (!contract.structured) { if (built.some(node => node.kind !== "text")) throw new TypeError("Structured content cannot be formatted as text."); return built.map(node => node.value).join(""); }
  return Object.freeze({ kind: "localized-content", key, locale, nodes: Object.freeze(built) });
}

function buildNodes(nodes, context, locale, path) {
  const roots = [], stack = [{ children: roots }];
  for (let index = 0; index < nodes.length; index++) {
    const node = nodes[index], parent = stack.at(-1).children;
    if (node.kind === "text") parent.push({ kind: "text", value: node.value, annotations: [] });
    else if (node.kind === "expression") parent.push({ kind: "text", value: formatResolved(context.expression(node.expression), locale), annotations: annotations(node.expression.annotations) });
    else if (node.markupKind === "open") { const element = { kind: "element", name: node.name, attributes: markupOptions(node.options, context), annotations: annotations(node.annotations), closingAnnotations: [], standalone: false, occurrence: path + index, children: [] }; parent.push(element); stack.push(element); }
    else if (node.markupKind === "standalone") parent.push({ kind: "element", name: node.name, attributes: markupOptions(node.options, context), annotations: annotations(node.annotations), closingAnnotations: [], standalone: true, occurrence: path + index, children: [] });
    else { if (stack.length === 1 || stack.at(-1).name !== node.name) throw new TypeError("Unbalanced markup."); stack.at(-1).closingAnnotations = annotations(node.annotations); stack.pop(); }
  }
  if (stack.length !== 1) throw new TypeError("Unbalanced markup.");
  return roots.map(freezeContentNode);
}

function markupOptions(options, context) { return freezeOwnRecord(options.map(option => { const type = option.value.kind === "number-literal" ? "decimal" : "string"; const resolved = context.resolve(option.value, type); return [option.name, canonicalCarrier(resolved.carrier, resolved.type)]; })); }
function annotations(values) { return Object.freeze(values.map(item => Object.freeze(item.value === undefined ? { name: item.name } : { name: item.name, value: item.value.kind === "number-literal" ? parseDecimal(item.value.canonical) : item.value.value }))); }
function freezeContentNode(node) { node.annotations = Object.freeze(node.annotations); if (node.kind === "element") { node.closingAnnotations = Object.freeze(node.closingAnnotations); node.children = Object.freeze(node.children.map(freezeContentNode)); } return Object.freeze(node); }
function compareRanks(left, right) { for (let index = 0; index < left.length; index++) if (left[index] !== right[index]) return left[index] - right[index]; return 0; }
function canonicalCarrier(value, type) { if (type === "decimal") return decimalCanonical(value); if (type === "int64") return value.toString(); if (type === "boolean") return value ? "true" : "false"; return value; }

export function freezeGeneratedMessage(key, value) {
  const contract = messageContracts[key]; const error = validateMessageWrapper(value, contract, null, key);
  if (error) throw new TypeError(`Invalid generated RMF2 v5 message: ${error}.`);
  const result=cloneFreeze(value);validatedMessages.add(result);return result;
}

export function decodeLocaleArtifactV5(value) {
  const captured = snapshotJson(value, limits.maximumDepth, limits.maximumDocumentBytes);
  if (!captured.ok) return failure(`RTR0023/${captured.reason}`);
  const snapshot = captured.value;
  const envelope = validateEnvelope(snapshot); if (envelope) return failure(envelope);
  const entries = [];
  for (const [key, message] of Object.entries(snapshot.messages)) {
    if (!Object.hasOwn(messageContracts, key)) return failure("RTR0023/unknown-key");
    const error = validateMessageWrapper(message, messageContracts[key], snapshot.locale, key); if (error) return failure(`RTR0023/${error}`);
    validatedMessages.add(message); entries.push([key,message]);
  }
  const artifact=Object.freeze({ artifactVersion: 5, messageGrammarVersion: 5, profile, catalog, locale: snapshot.locale, contractFingerprint, messages: freezeOwnRecord(entries), markupContract: rmf2Contract });decodedArtifacts.add(artifact);
  return Object.freeze({ ok: true, value: artifact });
}

export async function decodeLocalePackV5(content, expectedLocale, integrityVerifier) {
  if (!(content instanceof Uint8Array) || content.byteLength === 0) return failure("RTR0023/malformed");
  if (content.byteLength > limits.maximumDocumentBytes) return failure("RTR0023/limit-exceeded");
  const snapshot = new Uint8Array(content);
  if (integrityVerifier) { try { if (!await integrityVerifier(new Uint8Array(snapshot))) return failure("RTR0023/integrity-rejected"); } catch { return failure("RTR0023/integrity-rejected"); } }
  if (!withinJsonDepth(snapshot, limits.maximumDepth)) return failure("RTR0023/limit-exceeded");
  let value; try { const text = new TextDecoder("utf-8", { fatal: true }).decode(snapshot); if (hasDuplicateJsonProperties(text)) return failure("RTR0023/malformed"); value = JSON.parse(text); } catch { return failure("RTR0023/malformed"); }
  const result = decodeLocaleArtifactV5(value); if (!result.ok) return result;
  return result.value.locale === expectedLocale ? result : failure("RTR0023/locale-mismatch");
}

export function formatDynamicMessageV5(artifact, key, inputs = Object.create(null), options) {
  if (!artifact || !decodedArtifacts.has(artifact)) throw new TypeError("A validated v5 locale artifact is required.");
  if (!Object.hasOwn(artifact.messages, key)) throw new RangeError(`Locale artifact has no message '${key}'.`);
  const requested = resolveLocale(options?.locale ?? artifact.locale); if (requested !== artifact.locale) throw new RangeError("A dynamic locale artifact can only format its own locale.");
  return formatCompiledMessage(key, artifact.messages[key], inputs, requested);
}

export function decodeWireValue(value, type) {
  try {
    if (type === "decimal") return success(parseDecimal(value));
    if (type === "int64") { if (typeof value !== "string" || !/^-?(?:0|[1-9][0-9]*)$/.test(value)) return failure(); const parsed = BigInt(value); if (parsed < minimumInt64 || parsed > maximumInt64) return failure(); return success(parsed); }
    if (type === "boolean") return typeof value === "boolean" ? success(value) : failure();
    if (typeof value !== "string" || value.length > 16384) return failure();
    return success(coerceInput(value, type, "wire"));
  } catch { return failure(); }
}

function validateEnvelope(value) {
  const members=["artifactVersion","messageGrammarVersion","profile","catalog","locale","contractFingerprint","messages","markupContract"];
  if (!isRecord(value)) return "RTR0023/malformed";
  if (Object.keys(value).some(name=>!members.includes(name))) return "RTR0023/unknown-member";
  if (!members.every(name=>Object.hasOwn(value,name))) return "RTR0023/malformed";
  if (value.artifactVersion !== 5) return "RTR0023/artifact-version-mismatch";
  if (value.messageGrammarVersion !== 5 || value.profile !== profile) return "RTR0023/message-grammar-version-mismatch";
  if (value.catalog !== catalog) return "RTR0023/catalog-mismatch"; if (value.contractFingerprint !== contractFingerprint) return "RTR0023/contract-fingerprint-mismatch";
  if (typeof value.locale !== "string" || !localeSet.has(value.locale) || canonicalLocale(value.locale) !== value.locale) return "RTR0023/locale-mismatch";
  if (!isRecord(value.messages)) return "RTR0023/malformed"; if (Object.keys(value.messages).length > limits.maximumMessages) return "RTR0023/limit-exceeded";
  if (JSON.stringify(value.markupContract) !== JSON.stringify(rmf2Contract)) return "RTR0023/argument-contract-mismatch";
  return null;
}

function validateMessageWrapper(wrapper, contract, artifactLocale, key) {
  if (!contract || !isRecord(wrapper)) return "malformed-pattern";
  const wrapperMembers=["contentLocale","ast"];
  if (Object.keys(wrapper).some(name=>!wrapperMembers.includes(name))) return "unknown-member";
  if (!wrapperMembers.every(name=>Object.hasOwn(wrapper,name)) || typeof wrapper.contentLocale !== "string" || !localeSet.has(wrapper.contentLocale)) return "malformed-pattern";
  if (artifactLocale !== null && rmf2Contract.messages?.[key]?.contentLocales?.[artifactLocale] !== wrapper.contentLocale) return "argument-contract-mismatch";
  const ast = wrapper.ast, astMembers=["astVersion","profile","inputs","declarations","selectors","variants"];
  if (!isRecord(ast)) return "malformed-pattern";
  if (Object.keys(ast).some(name=>!astMembers.includes(name))) return "unknown-member";
  if (!astMembers.every(name=>Object.hasOwn(ast,name))) return "malformed-pattern";
  if (ast.astVersion !== 5) return "artifact-version-mismatch";
  if (ast.profile !== profile) return "message-grammar-version-mismatch";
  if (!Array.isArray(ast.inputs) || ast.inputs.length !== contract.inputs.length || ast.inputs.length > limits.maximumArgumentsPerMessage) return "argument-contract-mismatch";
  for (let index = 0; index < ast.inputs.length; index++) { const input = ast.inputs[index], expected = contract.inputs[index], inputMembers=["name","type"]; if (!isRecord(input)) return "malformed-pattern"; if(Object.keys(input).some(name=>!inputMembers.includes(name)))return "unknown-member"; if(!inputMembers.every(name=>Object.hasOwn(input,name)))return "malformed-pattern"; if(input.name !== expected.name || input.type !== expected.type || !validName(input.name)) return "argument-contract-mismatch"; }
  if (!Array.isArray(ast.declarations) || ast.declarations.length > 256 || !Array.isArray(ast.selectors) || ast.selectors.length > 16 || !Array.isArray(ast.variants) || ast.variants.length < 1 || ast.variants.length > 256) return "limit-exceeded";
  const explicitInputs = new Set(ast.declarations.filter(declaration => isRecord(declaration) && declaration.kind === "input" && typeof declaration.name === "string").map(declaration => declaration.name));
  const symbols = Object.create(null); for (const input of ast.inputs) symbols[input.name] = { type: input.type, selection: selectionForType(input.type), explicitDependencies: explicitInputs.has(input.name) };
  const bound = new Set(), referenced = new Set();
  for (const declaration of ast.declarations) {
    const memberError = closedMemberError(declaration,["kind","name","expression"]); if(memberError)return memberError;
    if (!["input","local"].includes(declaration.kind) || !validName(declaration.name) || bound.has(declaration.name) || referenced.has(declaration.name)) return "malformed-pattern";
    const error = validateExpression(declaration.expression, symbols, referenced); if (error) return error;
    if (declaration.kind === "input") { if (declaration.expression.operand.kind !== "input" || declaration.expression.operand.value !== declaration.name || !Object.hasOwn(symbols,declaration.name)) return "argument-contract-mismatch"; }
    else if (Object.hasOwn(symbols,declaration.name)) return "argument-contract-mismatch";
    const symbol = expressionSymbol(declaration.expression, symbols);
    if (declaration.kind === "input" && declaration.expression.function !== undefined && functionType(declaration.expression.function) !== declaration.expression.valueType) return "argument-contract-mismatch";
    symbols[declaration.name] = symbol;
    bound.add(declaration.name); referenceValue(declaration.expression.operand, referenced); for (const option of declaration.expression.options) referenceValue(option.value, referenced);
  }
  const selectorNames = new Set();
  for (const selector of ast.selectors) {
    const memberError=closedMemberError(selector,["value","type","function"]);if(memberError)return memberError;
    const valueError=validateValue(selector.value);if(valueError)return valueError;
    if (!["exact","plural","ordinal"].includes(selector.function) || selectorNames.has(selector.value.value)) return "malformed-pattern";
    if (!["input","local"].includes(selector.value.kind) || !Object.hasOwn(symbols,selector.value.value) || symbols[selector.value.value].type !== selector.type || symbols[selector.value.value].selection !== selector.function) return "argument-contract-mismatch";
    selectorNames.add(selector.value.value);
  }
  const vectors = new Set(); let fallback = false;
  for (const variant of ast.variants) {
    const memberError=closedMemberError(variant,["keys","nodes"]);if(memberError)return memberError;
    if (!Array.isArray(variant.keys) || variant.keys.length !== ast.selectors.length || !Array.isArray(variant.nodes)) return "malformed-pattern";
    const vector = []; let all = true;
    for (let index = 0; index < variant.keys.length; index++) { const validated = validateKey(variant.keys[index], ast.selectors[index]); if (validated.error) return validated.error; vector.push(validated.value); all &&= validated.value === "*"; }
    const encoded = JSON.stringify(vector); if (vectors.has(encoded)) return "malformed-pattern"; vectors.add(encoded); fallback ||= all;
    const stack = [], slotCounts = Object.create(null), slotRequirements = rmf2Contract.messages?.[key]?.slots ?? Object.create(null); let totalNodes = 0, textBytes = 0;
    for (const node of variant.nodes) {
      if (++totalNodes > 4096) return "limit-exceeded";
      if (!isRecord(node) || typeof node.kind !== "string") return "malformed-pattern";
      if (node.kind === "text") { const memberError=closedMemberError(node,["kind","value"]);if(memberError)return memberError;if (typeof node.value !== "string") return "malformed-pattern"; textBytes += new TextEncoder().encode(node.value).length; if (textBytes > limits.maximumPatternBytes) return "limit-exceeded"; }
      else if (node.kind === "expression") { const memberError=closedMemberError(node,["kind","expression"]);if(memberError)return memberError;const error = validateExpression(node.expression,symbols); if (error) return error; }
      else if (node.kind === "markup") {
        const memberError=closedMemberError(node,["kind","name","markupKind","options","annotations"]);if(memberError)return memberError==="unknown-member"?memberError:"argument-contract-mismatch";
        const annotationError=validateAnnotations(node.annotations,"argument-contract-mismatch");if(annotationError)return annotationError;
        if (!validName(node.name,true) || !["open","close","standalone"].includes(node.markupKind) || !Object.hasOwn(rmf2Contract.contracts,node.name)) return "argument-contract-mismatch";
        const markupError = validateMarkupNode(node,symbols,slotRequirements,slotCounts,stack); if (markupError) return markupError;
        if (node.markupKind === "open") { stack.push(node.name); if (stack.length > 16) return "limit-exceeded"; } else if (node.markupKind === "close" && stack.pop() !== node.name) return "malformed-pattern";
      }
      else return "malformed-pattern";
    }
    if (stack.length) return "malformed-pattern";
    for (const [slot,bounds] of Object.entries(slotRequirements)) if ((slotCounts[slot] ?? 0) < bounds.min || (slotCounts[slot] ?? 0) > bounds.max) return "argument-contract-mismatch";
  }
  return fallback ? null : "malformed-pattern";
}

function validateExpression(expression, symbols, references) {
  const memberError=closedMemberError(expression,["operand","valueType","function","options","annotations"],["operand","valueType","options","annotations"]);if(memberError)return memberError;
  if(Object.hasOwn(expression,"function")&&expression.function===undefined)return "malformed-pattern";
  const valueError=validateValue(expression.operand);if(valueError)return valueError;
  const annotationError=validateAnnotations(expression.annotations,"malformed-pattern");if(annotationError)return annotationError;
  if (!["string","int64","decimal","boolean","date","time","datetime","guid"].includes(expression.valueType)) return "malformed-pattern";
  if (!Array.isArray(expression.options)) return "malformed-pattern";
  if (["input","local"].includes(expression.operand.kind)) { if (!Object.hasOwn(symbols,expression.operand.value) || symbols[expression.operand.value].type !== expression.valueType) return "argument-contract-mismatch"; }
  else { try { literal(expression.operand, expression.valueType); } catch { return "malformed-pattern"; } }
  if (expression.function === undefined) return expression.options.length === 0 ? null : "malformed-pattern";
  const accepted = functionType(expression.function); if (!accepted || !(accepted === expression.valueType || (expression.valueType === "int64" && accepted === "decimal"))) return "argument-contract-mismatch";
  const optionError=validateOptions(expression.options,symbols,expression.function);if(optionError)return optionError;
  if (references) for (const option of expression.options) referenceValue(option.value,references);
  return null;
}

function validateOptions(options, symbols, fn) {
  if (!Array.isArray(options) || options.length > 256) return "argument-contract-mismatch"; const names = new Set(), staticEntries=[]; let dynamic=false;
  for (const option of options) { const memberError=closedMemberError(option,["name","value"]);if(memberError)return memberError==="unknown-member"?memberError:"argument-contract-mismatch";const valueError=validateValue(option.value);if(valueError)return ["unknown-member","malformed"].includes(valueError)?valueError:"argument-contract-mismatch";if(!validName(option.name)||names.has(option.name))return "argument-contract-mismatch";names.add(option.name); if (fn) { const type = optionType(fn,option.name); if (!type) return "argument-contract-mismatch"; if (["input","local"].includes(option.value.kind)) { if (option.name === "select" || !Object.hasOwn(symbols,option.value.value) || symbols[option.value.value].type !== type || !symbols[option.value.value].explicitDependencies) return "argument-contract-mismatch"; dynamic=true; } else { try { staticEntries.push([option.name,literal(option.value,type)]); } catch { return "argument-contract-mismatch"; } } } }
  if(fn){try{const resolved=freezeOwnRecord(staticEntries);validateResolvedOptions(fn,resolved);if(dynamic&&fn==="number"){const style=resolved.style,min=resolved.minimumFractionDigits??0n,max=resolved.maximumFractionDigits;if(style==="percent"&&min>4n||max!==undefined&&min>max)return "argument-contract-mismatch";}}catch{return "argument-contract-mismatch";}}
  return null;
}
function validateMarkupNode(node,symbols,requirements,counts,stack) {
  const contract=rmf2Contract.contracts[node.name],functional=["runic:link","runic:action","runic:icon"].includes(node.name);
  if(!Array.isArray(node.options))return "argument-contract-mismatch";
  if(node.markupKind==="close") return node.options.length===0?null:"argument-contract-mismatch";
  if((contract.kind==="standalone")!==(node.markupKind==="standalone"))return "argument-contract-mismatch";
  if(contract.interactive&&stack.some(name=>rmf2Contract.contracts[name]?.interactive))return "argument-contract-mismatch";
  if(node.options.length>256)return "limit-exceeded";
  const values=Object.create(null);
  for(const option of node.options){const memberError=closedMemberError(option,["name","value"]);if(memberError)return memberError==="unknown-member"?memberError:"argument-contract-mismatch";const valueError=validateValue(option.value);if(valueError)return ["unknown-member","malformed"].includes(valueError)?valueError:"argument-contract-mismatch";if(!validName(option.name)||Object.hasOwn(values,option.name))return "argument-contract-mismatch";values[option.name]=option.value;}
  if(functional){const reference=values.ref;if(!reference||reference.kind!=="string-literal"||!Object.hasOwn(requirements,reference.value)||requirements[reference.value].kind!==node.name)return "argument-contract-mismatch";counts[reference.value]=(counts[reference.value]??0)+1;}
  const expected=Object.keys(contract.options);
  if(expected.some(name=>!Object.hasOwn(values,name))||Object.keys(values).some(name=>name!=="ref"&&!Object.hasOwn(contract.options,name)))return "argument-contract-mismatch";
  for(const name of expected){const schema=contract.options[name],value=values[name];if(["input","local"].includes(value.kind)){if(schema.literalOnly||!Object.hasOwn(symbols,value.value)||!markupTypeAccepts(schema.type,symbols[value.value].type))return "argument-contract-mismatch";}else if(!markupLiteral(schema,value))return "argument-contract-mismatch";}
  return null;
}
function markupTypeAccepts(schema,type){return schema==="number"?["int64","decimal"].includes(type):schema==="boolean"?type==="boolean":type==="string";}
function markupLiteral(schema,value){try{if(schema.type==="number")return value.kind==="number-literal"&&decimalCanonical(parseDecimal(value.value))===value.canonical;if(value.kind!=="string-literal")return false;if(schema.type==="boolean")return ["true","false"].includes(value.value);if(schema.type==="enum")return schema.values.includes(value.value);return true;}catch{return false;}}
function validateAnnotations(values,invalid) { if (!Array.isArray(values) || values.length > 256) return invalid; const names = new Set();for(const annotation of values){const memberError=closedMemberError(annotation,["name","value"],["name"]);if(memberError)return memberError==="unknown-member"?memberError:invalid;if(!validName(annotation.name,true)||names.has(annotation.name))return invalid;names.add(annotation.name);if(Object.hasOwn(annotation,"value")){if(annotation.value===undefined)return invalid;const valueError=validateValue(annotation.value);if(valueError)return ["unknown-member","malformed"].includes(valueError)?valueError:invalid;if(["input","local"].includes(annotation.value.kind))return invalid;}}return null; }
function validateValue(value) { if (!isRecord(value)||typeof value.kind!=="string") return "malformed";const numeric=value.kind==="number-literal";const memberError=closedMemberError(value,["kind","value",...(numeric?["canonical"]:[]) ]);if(memberError)return memberError;if(!["input","local","string-literal","number-literal"].includes(value.kind)||typeof value.value!=="string"||numeric&&typeof value.canonical!=="string")return "malformed-pattern";if(["input","local"].includes(value.kind)&&!validName(value.value))return "malformed-pattern";if(numeric){try{if(decimalCanonical(parseDecimal(value.value))!==value.canonical)return "malformed-pattern";}catch{return "malformed-pattern";}}return null; }
function validateKey(key,selector) { if(!isRecord(key)||typeof key.kind!=="string")return {error:"malformed-pattern"};if(key.kind==="wildcard"){const memberError=closedMemberError(key,["kind"]);return memberError?{error:memberError}:{value:"*"};}const memberError=closedMemberError(key,["kind","value","canonical"],["kind","value"]);if(memberError)return {error:memberError};if(key.kind!=="literal"||typeof key.value!=="string"||Object.hasOwn(key,"canonical")&&key.canonical===undefined)return {error:"malformed-pattern"};if(["decimal","int64"].includes(selector.type)){try{const value=parseDecimal(key.value),canonical=decimalCanonical(value);if(key.canonical!==canonical||selector.type==="int64"&&(value.scale!==0||(value.negative?-value.coefficient:value.coefficient)<minimumInt64||(value.negative?-value.coefficient:value.coefficient)>maximumInt64))return {error:"malformed-pattern"};return {value:`=${canonical}`};}catch{if(key.canonical!==undefined||selector.function==="exact"||!["zero","one","two","few","many","other"].includes(key.value))return {error:"malformed-pattern"};return {value:`=${key.value}`};}}if(key.canonical!==undefined||selector.type==="boolean"&&!["true","false"].includes(key.value))return {error:"malformed-pattern"};return {value:`=${key.value.normalize("NFC")}`};}
function expressionSymbol(expression,symbols) { const inherited=["input","local"].includes(expression.operand.kind)?symbols[expression.operand.value]:{type:expression.valueType,selection:selectionForType(expression.valueType),explicitDependencies:true}; if(expression.function===undefined)return inherited; const selected=expression.options.find(option=>option.name==="select"); let explicitDependencies=inherited.explicitDependencies;for(const option of expression.options)if(["input","local"].includes(option.value.kind))explicitDependencies&&=symbols[option.value.value].explicitDependencies;return {type:expression.valueType,selection:selected?.value?.value??selectionForFunction(expression.function),explicitDependencies}; }
function selectionForType(type) { return ["string","boolean"].includes(type)?"exact":["int64","decimal"].includes(type)?"plural":"none"; }
function selectionForFunction(fn) { return ["string","runic:boolean"].includes(fn)?"exact":["integer","number","runic:relative-time"].includes(fn)?"plural":"none"; }
function referenceValue(value,set) { if (["input","local"].includes(value.kind)) set.add(value.value); }

function validName(value, qualified = false) {
  if (typeof value !== "string" || !value || value !== value.normalize("NFC")) return false;
  let first=true,colon=false; for(const character of value){const code=character.codePointAt(0);if(code===58&&qualified&&!colon&&!first){colon=true;first=true;continue;}const start=(code>=65&&code<=90)||(code>=97&&code<=122)||code===43||code===95||(code>=0xa1&&code<=0x61b)||(code>=0x61d&&code<=0x167f)||(code>=0x1681&&code<=0x1fff)||(code>=0x200b&&code<=0x200d)||(code>=0x2010&&code<=0x2027)||(code>=0x2030&&code<=0x205e)||(code>=0x2060&&code<=0x2065)||(code>=0x206a&&code<=0x2fff)||(code>=0x3001&&code<=0xd7ff)||(code>=0xe000&&code<=0xfdcf)||(code>=0xfdf0&&code<=0xfffd)||(code>=0x10000&&code<=0x10fffd&&(code&0xffff)<=0xfffd);if(!start&&(first||!((code>=48&&code<=57)||code===45||code===46)))return false;first=false;}return !first;
}
function validDate(value) { if (!/^\d{4}-\d{2}-\d{2}$/.test(value) || value.startsWith("0000-")) return false; const date = new Date(`${value}T00:00:00Z`); return !Number.isNaN(date.valueOf()) && date.toISOString().slice(0,10) === value; }
function validTime(value) { if (!/^\d{2}:\d{2}:\d{2}$/.test(value)) return false; return Number(value.slice(0,2)) < 24 && Number(value.slice(3,5)) < 60 && Number(value.slice(6,8)) < 60; }
function validDateTime(value) { if (!/^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}Z$/.test(value) || value.startsWith("0000-")) return false; const date = new Date(value); return !Number.isNaN(date.valueOf()) && date.toISOString().replace(".000Z","Z") === value; }
function canonicalLocale(value) {
  if (typeof value!=="string"||!value||value.startsWith("-")||value.endsWith("-")) return null;
  const parts=value.split("-");
  if(parts[0].length<2||parts[0].length>8||!/^[A-Za-z]+$/.test(parts[0])) return null;
  const result=[parts[0].toLowerCase()]; let index=1;
  if(parts[0].length<=3) for(let count=0;count<3&&index<parts.length&&/^[A-Za-z]{3}$/.test(parts[index]);count++,index++) result.push(parts[index].toLowerCase());
  if(index<parts.length&&/^[A-Za-z]{4}$/.test(parts[index])) { const part=parts[index++]; result.push(part[0].toUpperCase()+part.slice(1).toLowerCase()); }
  if(index<parts.length&&(/^[A-Za-z]{2}$/.test(parts[index])||/^\d{3}$/.test(parts[index]))) result.push(parts[index++].toUpperCase());
  const variants=new Set();
  while(index<parts.length&&(/^[A-Za-z0-9]{5,8}$/.test(parts[index])||/^\d[A-Za-z0-9]{3}$/.test(parts[index]))) { const part=parts[index++].toLowerCase(); if(variants.has(part))return null; variants.add(part); result.push(part); }
  const singletons=new Set();
  while(index<parts.length&&/^[0-9A-WY-Za-wy-z]$/.test(parts[index])) { const singleton=parts[index++].toLowerCase(); if(singletons.has(singleton))return null; singletons.add(singleton); result.push(singleton); const first=index; while(index<parts.length&&/^[A-Za-z0-9]{2,8}$/.test(parts[index]))result.push(parts[index++].toLowerCase()); if(index===first)return null; }
  if(index<parts.length&&/^[xX]$/.test(parts[index])) { result.push("x"); index++; const first=index; while(index<parts.length&&/^[A-Za-z0-9]{1,8}$/.test(parts[index]))result.push(parts[index++].toLowerCase()); if(index===first)return null; }
  return index===parts.length?result.join("-"):null;
}
function isRecord(value) { return typeof value === "object" && value !== null && !Array.isArray(value); }
function closedMemberError(value,allowed,required=allowed){if(!isRecord(value))return "malformed-pattern";if(Object.keys(value).some(name=>!allowed.includes(name)))return "unknown-member";return required.every(name=>Object.hasOwn(value,name))?null:"malformed-pattern";}
function exactKeys(value, names) { if (!isRecord(value)) return false; const actual = Object.keys(value).sort(), expected = [...names].sort(); return actual.length === expected.length && actual.every((name,index) => name === expected[index] && Object.hasOwn(value,name)); }
function cloneFreeze(value) { if (Array.isArray(value)) return Object.freeze(value.map(cloneFreeze)); if (isRecord(value)) return freezeOwnRecord(Object.keys(value).map(key => [key,cloneFreeze(value[key])])); return value; }
function snapshotJson(value, maximumDepth, maximumBytes) {
  const active = new WeakSet(), state = { bytes: 0, reason: null };
  const consume = count => { state.bytes += count; if (!state.reason && state.bytes > maximumBytes) state.reason = "limit-exceeded"; };
  const visit = (current, depth) => {
    if (state.reason) return undefined;
    if (current === null) { consume(4); return null; }
    if (typeof current === "string") { consume(jsonStringByteLength(current, maximumBytes - state.bytes)); return current; }
    if (typeof current === "boolean") { consume(current ? 4 : 5); return current; }
    if (typeof current === "number") { if (!Number.isFinite(current)) { state.reason = "malformed"; return undefined; } consume(Object.is(current,-0) ? 1 : String(current).length); return Object.is(current,-0) ? 0 : current; }
    if (typeof current !== "object") { state.reason = "malformed"; return undefined; }
    if (depth >= maximumDepth) { state.reason = "limit-exceeded"; return undefined; }
    if (active.has(current)) { state.reason = "malformed"; return undefined; }
    active.add(current);
    let keys;
    try { keys = Reflect.ownKeys(current); } catch { state.reason = "malformed"; active.delete(current); return undefined; }
    const nextDepth = depth + 1;
    if (Array.isArray(current)) {
      let lengthDescriptor; try { lengthDescriptor = Object.getOwnPropertyDescriptor(current,"length"); } catch { state.reason = "malformed"; }
      const length = lengthDescriptor?.value;
      if (!state.reason && (!Number.isSafeInteger(length) || length < 0)) state.reason = "malformed";
      else if (!state.reason && length > Math.floor((maximumBytes - 1) / 2)) state.reason = "limit-exceeded";
      else if (!state.reason && (keys.length !== length + 1 || keys[length] !== "length")) state.reason = "malformed";
      const result = state.reason ? undefined : new Array(length); consume(2);
      for (let index = 0; !state.reason && index < length; index++) {
        if (keys[index] !== String(index)) { state.reason = "malformed"; break; }
        let descriptor; try { descriptor = Object.getOwnPropertyDescriptor(current, keys[index]); } catch { state.reason = "malformed"; break; }
        if (!descriptor?.enumerable || !Object.hasOwn(descriptor,"value")) { state.reason = "malformed"; break; }
        if (index) consume(1);
        result[index] = visit(descriptor.value, nextDepth);
      }
      active.delete(current); return state.reason ? undefined : Object.freeze(result);
    }
    consume(2); const result = Object.create(null);
    for (let index = 0; !state.reason && index < keys.length; index++) {
      const key = keys[index];
      if (typeof key !== "string") { state.reason = "malformed"; break; }
      let descriptor; try { descriptor = Object.getOwnPropertyDescriptor(current,key); } catch { state.reason = "malformed"; break; }
      if (!descriptor?.enumerable || !Object.hasOwn(descriptor,"value")) { state.reason = "malformed"; break; }
      if (index) consume(1); consume(jsonStringByteLength(key, maximumBytes - state.bytes)); consume(1);
      const captured = visit(descriptor.value, nextDepth);
      if (!state.reason) Object.defineProperty(result,key,{value:captured,enumerable:true,configurable:false,writable:false});
    }
    active.delete(current); return state.reason ? undefined : Object.freeze(result);
  };
  const snapshot = visit(value,0);
  return state.reason ? Object.freeze({ok:false,reason:state.reason}) : Object.freeze({ok:true,value:snapshot});
}
function jsonStringByteLength(value, remaining) {
  let bytes = 2;
  for (let index = 0; index < value.length && bytes <= remaining; index++) {
    const code = value.charCodeAt(index);
    if (code === 34 || code === 92 || code === 8 || code === 9 || code === 10 || code === 12 || code === 13) bytes += 2;
    else if (code < 32 || code >= 0xd800 && code <= 0xdfff && !(code <= 0xdbff && index + 1 < value.length && value.charCodeAt(index + 1) >= 0xdc00 && value.charCodeAt(index + 1) <= 0xdfff)) bytes += 6;
    else if (code < 0x80) bytes += 1;
    else if (code < 0x800) bytes += 2;
    else if (code >= 0xd800 && code <= 0xdbff) { bytes += 4; index++; }
    else bytes += 3;
  }
  return bytes;
}
function success(value) { return Object.freeze({ ok: true, value }); }
function failure(reason) { return Object.freeze({ ok: false, reason }); }
function withinJsonDepth(bytes, maximum) { let depth = 0, quoted = false, escaped = false; for (const byte of bytes) { if (quoted) { if (escaped) escaped = false; else if (byte === 92) escaped = true; else if (byte === 34) quoted = false; } else if (byte === 34) quoted = true; else if (byte === 123 || byte === 91) { if (++depth > maximum) return false; } else if (byte === 125 || byte === 93) --depth; } return true; }
function hasDuplicateJsonProperties(text) { const scopes = []; for (let index = 0; index < text.length; index++) { const character = text[index]; if (character === "{") scopes.push({ object: true, keys: new Set() }); else if (character === "[") scopes.push({ object: false }); else if (character === "}" || character === "]") scopes.pop(); else if (character === "\"") { const start = index++; let escaped = false; for (; index < text.length; index++) { if (escaped) escaped = false; else if (text[index] === "\\") escaped = true; else if (text[index] === "\"") break; } if (index >= text.length) return false; let next = index + 1; while (/\s/.test(text[next] ?? "")) next++; if (text[next] !== ":") continue; const scope = scopes.at(-1); if (!scope?.object) continue; const key = JSON.parse(text.slice(start,index+1)); if (scope.keys.has(key)) return true; scope.keys.add(key); } } return false; }

export function linkBinding({ href }) { if (typeof href !== "string" || !href.trim() || !["http:","https:","mailto:","tel:"].includes(new URL(href,"https://runic.invalid/").protocol)) throw new TypeError("Invalid application link destination."); return Object.freeze({ kind:"runic:link",href }); }
export function actionBinding({ onActivate }) { if (typeof onActivate !== "function") throw new TypeError("An action requires an application callback."); return Object.freeze({ kind:"runic:action",onActivate }); }
export function iconBinding({ asset,decorative,accessibleName }) { if (asset == null || typeof decorative !== "boolean" || !decorative && typeof accessibleName !== "function") throw new TypeError("A meaningful icon requires a localized accessibleName function."); return Object.freeze({ kind:"runic:icon",asset,decorative,accessibleName }); }
export function enumOption(values,defaultValue){if(!Array.isArray(values)||!values.length||values.some(value=>typeof value!=="string")||defaultValue!==undefined&&!values.includes(defaultValue))throw new TypeError("Invalid enum option.");return Object.freeze({type:"enum",values:Object.freeze([...values]),...(defaultValue===undefined?{}:{default:defaultValue})});}
export function createInlineRenderer(factory, bindings = []) {
  return createInlineRendererCore(factory, bindings, false);
}
function createInlineRendererCore(factory, bindings = [], allowUnboundCustom = false) {
  if (!factory || typeof factory.text !== "function" || typeof factory.element !== "function") throw new TypeError("An inline renderer requires text and element factories.");
  const custom = new Map();
  for (const binding of bindings) {
    if (!isRecord(binding) || !isRecord(binding.contract) || typeof binding.render !== "function") throw new TypeError("Invalid markup renderer binding.");
    const suppliedContract = binding.contract, declared = rmf2Contract.contracts[suppliedContract.name];
    const children = declared?.children ?? (declared?.kind === "standalone" ? "none" : "inline");
    if (!declared || suppliedContract.name.startsWith("runic:") || custom.has(suppliedContract.name) || declared.kind !== suppliedContract.kind || children !== suppliedContract.children || declared.plainText !== suppliedContract.plainText || declared.interactive !== suppliedContract.interactive) throw new TypeError("Incompatible or duplicate renderer contract.");
    const suppliedOptions = suppliedContract.options ?? Object.create(null);
    if (!isRecord(suppliedOptions) || Object.keys(declared.options).length !== Object.keys(suppliedOptions).length || Object.entries(declared.options).some(([name,schema]) => {
      const actual = suppliedOptions[name];
      return !isRecord(actual) || actual.type !== schema.type || JSON.stringify(actual.values ?? []) !== JSON.stringify(schema.values) || (actual.default ?? null) !== schema.default || !!actual.literalOnly !== schema.literalOnly;
    })) throw new TypeError("Renderer option contract mismatch.");
    custom.set(suppliedContract.name,binding.render);
  }
  function render(content,{slots=Object.create(null)}={}) {
    const requirements=rmf2Contract.messages[content.key]?.slots;if(content.kind!=="localized-content"||!requirements||!isRecord(slots))throw new TypeError("Unknown RMF2 content contract.");
    const validatedSlots=Object.create(null);for (const [reference,requirement] of Object.entries(requirements)) validatedSlots[reference]=validateSlotBinding(reference,requirement.kind,slots[reference]);
    const counts=Object.create(null);let count=0;
    function visit(nodes,interactive=false,depth=0){if(depth>16)throw new RangeError("Inline nesting exceeds 16 levels.");return nodes.map(node=>{if(++count>4096)throw new RangeError("Inline node limit exceeded.");if(node.kind==="text")return node;const contract=rmf2Contract.contracts[node.name];if(node.kind!=="element"||!contract||(contract.kind==="standalone")!==node.standalone||interactive&&contract.interactive)throw new TypeError("Unknown or invalid inline markup.");let binding;for(const[name,value]of Object.entries(node.attributes)){if(name==="ref"&&["runic:link","runic:action","runic:icon"].includes(node.name)){binding=validatedSlots[value];if(requirements[value]?.kind!==node.name||!binding)throw new TypeError(`Invalid slot '${value}'.`);counts[value]=(counts[value]??0)+1;}else if(!contract.options[name]||!renderOption(contract.options[name],value))throw new TypeError(`Invalid option '${name}'.`);}if(["runic:link","runic:action","runic:icon"].includes(node.name)&&!binding)throw new TypeError("Missing functional slot ref.");for(const name of Object.keys(contract.options))if(!Object.hasOwn(node.attributes,name))throw new TypeError(`Missing option '${name}'.`);const element={name:node.name,children:visit(node.children,interactive||contract.interactive,depth+1),options:node.attributes,binding,occurrence:`${content.key}:${node.occurrence}`,locale:content.locale,standalone:node.standalone};if(!custom.has(node.name)&&!node.name.startsWith("runic:")&&!allowUnboundCustom)throw new TypeError(`No renderer linked for '${node.name}'.`);return element;});}
    const output=visit(content.nodes);for(const[slot,bounds]of Object.entries(requirements))if((counts[slot]??0)<bounds.min||(counts[slot]??0)>bounds.max)throw new TypeError(`Slot multiplicity mismatch for '${slot}'.`);
    function materialize(node){if(node.kind==="text")return factory.text(node.value);const contract=rmf2Contract.contracts[node.name];if(!custom.has(node.name)&&!node.name.startsWith("runic:")&&["omit","lineBreak"].includes(contract?.plainText))return contract.plainText==="lineBreak"?"\n":"";const element={...node,children:node.children.map(materialize)};return custom.has(node.name)?custom.get(node.name)(element):factory.element(element);}
    return Object.freeze(output.map(materialize));
  }
  return Object.freeze({ render, extend(extra) { return createInlineRendererCore(factory,[...bindings,...extra],allowUnboundCustom); } });
}
function validateSlotBinding(reference,kind,binding){if(binding?.kind!==kind)throw new TypeError(`Missing or incompatible slot '${reference}'.`);return kind==="runic:link"?linkBinding(binding):kind==="runic:action"?actionBinding(binding):iconBinding(binding);}
export function defineMarkup(contract) { if (!isRecord(contract) || !validName(contract.name,true) || contract.name.startsWith("runic:") || !["paired","standalone"].includes(contract.kind) || contract.children !== (contract.kind === "standalone" ? "none" : "inline") || typeof contract.interactive !== "boolean" || !["children","lineBreak","alternateText","explicit","omit"].includes(contract.plainText) || contract.options !== undefined && !isRecord(contract.options)) throw new TypeError("Invalid markup contract."); return cloneFreeze(contract); }
export function bindMarkup(contract,render) { if (typeof render !== "function") throw new TypeError("A markup renderer is required."); return Object.freeze({contract,render}); }
export function toPlainText(content,{slots=Object.create(null),allowActionLabels=false,annotateLinkDestinations=false,custom=[]}={}) { const renderer=createInlineRendererCore({text:value=>value,element({name,children,binding,locale}) { if (name==="runic:br") return "\n"; if (name==="runic:action"&&!allowActionLabels) throw new TypeError("Action labels require explicit projection policy."); if (name==="runic:icon") { if (binding?.decorative) return ""; const label=binding?.accessibleName?.(locale); if (typeof label!=="string"||!label.trim()) throw new TypeError("Meaningful icon alternate text is empty."); return label; } const contract=rmf2Contract.contracts[name]; if(!name.startsWith("runic:")&&contract?.plainText==="lineBreak")return "\n";if(!name.startsWith("runic:")&&contract?.plainText==="omit")return "";if(!name.startsWith("runic:")&&["explicit","alternateText"].includes(contract?.plainText))throw new TypeError("Custom markup requires an explicit plain-text adapter.");const text=children.join(""); return name==="runic:link"&&annotateLinkDestinations?`${text} (${binding.href})`:text; }},custom,true); return renderer.render(content,{slots}).join(""); }
export function createDomInlineRenderer(document,custom=[]) { return createInlineRenderer({text:value=>document.createTextNode(value),element({name,children,binding,locale}) { if(name==="runic:icon"){const node=typeof binding.asset==="function"?binding.asset(document):binding.asset?.cloneNode?.(true);if(!node||typeof node.setAttribute!=="function")throw new TypeError("The application icon asset must create a DOM element.");if(binding.decorative){node.setAttribute("aria-hidden","true");node.removeAttribute("aria-label");}else{const label=binding.accessibleName(locale);if(typeof label!=="string"||!label.trim())throw new TypeError("Meaningful icon alternate text is empty.");node.setAttribute("role","img");node.setAttribute("aria-label",label);}return node;}const tags={"runic:strong":"strong","runic:em":"em","runic:bold":"span","runic:italic":"span","runic:code":"code","runic:br":"br","runic:link":"a","runic:action":"button"};const node=document.createElement(tags[name]);if(name==="runic:bold")node.style.fontWeight="bold";if(name==="runic:italic")node.style.fontStyle="italic";if(name==="runic:link")node.href=binding.href;if(name==="runic:action"){node.type="button";node.addEventListener("click",binding.onActivate);}node.append(...children);return node;}},custom); }
function renderOption(schema,value){if(typeof value!=="string")return false;if(schema.type==="enum")return schema.values.includes(value);if(schema.type==="boolean")return value==="true"||value==="false";if(schema.type==="number"){try{parseDecimal(value);return true;}catch{return false;}}return true;}
