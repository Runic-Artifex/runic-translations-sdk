// Transport versions 1 and 2. The generator prepends the imports and the catalog, contractFingerprint and contracts constants.
// The rules are shared with the .NET TranslationReference, its JSON converter and TranslationReferenceResolver.
const limits = Object.freeze({ arguments: 32, keyLength: 512, argumentLength: 16384, fallbackLength: 65536, listItems: 256, depth: 8, references: 32, totalText: 65536, encoded: 262144, jsonDepth: 64 });
const scalarTypes = new Set(["string", "int64", "decimal", "boolean", "date", "time", "datetime", "guid"]);
const maximumCoefficient = 79228162514264337593543950335n;
const decodedMessages = new WeakSet(), createdReferences = new WeakSet();

// Reads stored or received JSON text. The size is checked before parsing; duplicate members, non-integer numbers and lone
// surrogates are rejected, as the .NET reader rejects them.
export function parseTextReference(json) {
  if (typeof json !== "string") return failure("malformed");
  // A UTF-16 code unit encodes to at least one UTF-8 byte, so the length bounds the work of counting bytes.
  if (json.length > limits.encoded || utf8Length(json) > limits.encoded) return failure("limit-exceeded");
  try {
    const parser = { text: json, index: 0 };
    const value = parseJson(parser, 0);
    whitespace(parser);
    if (parser.index !== json.length) throw new SyntaxError();
    return success(value);
  } catch (error) { return failure(error instanceof LimitError ? error.message : "malformed"); }
}

export function decodeTextReference(value) {
  if (typeof value === "string") { const parsed = parseTextReference(value); if (!parsed.ok) return parsed; value = parsed.value; }
  const parsed = parseReference(value);
  if (!parsed.ok) return parsed;
  const tree = parsed.value;
  if (tree.catalog !== catalog) return failure("catalog-mismatch");
  // Version 1 arguments carry no types, so they need the writer's exact contract. A version 2 reference describes its own
  // argument types: the key's current contract decides, not the whole catalog's fingerprint.
  if (tree.version === 1 && tree.contractFingerprint !== contractFingerprint) return failure("fingerprint-mismatch");
  const bound = bindStrict(tree);
  return bound.ok ? Object.freeze({ ok: true, value: Object.freeze({ version: tree.version, catalog, contractFingerprint: tree.contractFingerprint, key: bound.value.key, arguments: bound.value.arguments, fallbackText: tree.fallbackText }) }) : bound;
}

export function formatTextReference(reference, handlers, options) {
  const text = formatStrict(reference, handlers, options);
  if (text === undefined) throw new RangeError(`No text-reference handler for '${reference.key}'.`);
  return text;
}

// Writes a reference for this catalog. A string input may receive another reference created here, which is stored as a message
// argument; integers accept bigint or safe numbers, decimals a decimal carrier, a canonical string or a finite number. { version: 1 } writes the
// version 1 wire for receivers that do not read version 2 yet; it carries scalar inputs only.
export function createTextReference(key, inputs = {}, options = {}) {
  const version = options?.version ?? 2;
  if (version !== 1 && version !== 2) throw new RangeError("Text references are written as version 1 or 2.");
  const state = { references: 0, text: 0 };
  const body = writeBody(key, inputs, options, version, 1, state);
  const reference = Object.freeze({ version, catalog, contractFingerprint, ...body });
  if (state.text > limits.totalText) throw new RangeError("The reference contains too much text.");
  if (utf8Length(JSON.stringify(reference)) > limits.encoded) throw new RangeError("The encoded reference is larger than the transport limit.");
  if (version === 2) createdReferences.add(reference);
  return reference;
}

function writeBody(key, inputs, options, version, depth, state) {
  if (typeof key !== "string" || !Object.hasOwn(contracts, key)) throw new RangeError(`Unknown message '${key}'.`);
  if (depth > limits.depth || ++state.references > limits.references) throw new RangeError("The reference nests too many messages.");
  if (!isRecord(inputs)) throw new TypeError("Message inputs must be an object.");
  const contract = contracts[key].inputs, names = Object.keys(inputs);
  if (names.length !== contract.length || contract.some(input => !Object.hasOwn(inputs, input.name))) throw new TypeError(`Message inputs must contain exactly: ${contract.map(input => input.name).sort().join(", ")}.`);
  const entries = [];
  for (const input of [...contract].sort((left, right) => left.name < right.name ? -1 : left.name > right.name ? 1 : 0)) {
    const value = inputs[input.name];
    if (input.type === "string" && createdReferences.has(value)) {
      if (version === 1) throw new TypeError(`Input '${input.name}' is a message, which needs transport version 2.`);
      const { key: nestedKey, arguments: nestedArguments, fallbackText } = value;
      const nested = Object.freeze({ key: nestedKey, arguments: nestedArguments, ...(fallbackText === undefined ? {} : { fallbackText }) });
      countNested(nested, depth + 1, state);
      entries.push([input.name, Object.freeze({ type: "message", value: nested })]);
      continue;
    }
    if (version === 1 && input.type === "list") throw new TypeError(`Input '${input.name}' is a list, which needs transport version 2.`);
    const wire = encode(value, input.type, input.name);
    state.text += textLength(wire);
    entries.push([input.name, version === 1 ? wire : Object.freeze({ type: input.type, value: wire })]);
  }
  const fallbackText = options?.fallbackText;
  if (fallbackText !== undefined && (typeof fallbackText !== "string" || fallbackText.length > limits.fallbackLength || !fallbackText.isWellFormed())) throw new TypeError("Invalid fallback text.");
  state.text += fallbackText?.length ?? 0;
  return { key, arguments: freezeOwnRecord(entries), ...(fallbackText === undefined ? {} : { fallbackText }) };
}

function countNested(body, depth, state) {
  if (depth > limits.depth || ++state.references > limits.references) throw new RangeError("The reference nests too many messages.");
  state.text += body.fallbackText?.length ?? 0;
  for (const name of Object.keys(body.arguments)) {
    const argument = body.arguments[name];
    if (argument.type === "message") countNested(argument.value, depth + 1, state); else state.text += textLength(argument.value);
  }
}

function encode(value, type, name) {
  let wire = value;
  if (type === "int64" && (typeof value === "bigint" || Number.isSafeInteger(value))) wire = String(value);
  else if (type === "decimal" && typeof value === "number" && Number.isFinite(value)) wire = encode(decimal(String(value)), type, name);
  else if (type === "decimal" && isRecord(value) && typeof value.coefficient === "bigint" && Number.isInteger(value.scale) && value.scale >= 0) {
    const digits = value.coefficient.toString().padStart(value.scale + 1, "0");
    const unsigned = value.scale === 0 ? digits : `${digits.slice(0, digits.length - value.scale)}.${digits.slice(digits.length - value.scale)}`;
    wire = value.negative === true && /[1-9]/.test(unsigned) ? `-${unsigned}` : unsigned;
  } else if (type === "list" && Array.isArray(value)) {
    const items = Array.from(value); let length = 0;
    if (items.length <= limits.listItems && items.every(item => typeof item === "string" && item.isWellFormed() && (length += item.length) <= limits.argumentLength)) return Object.freeze(items);
    throw new TypeError(`Input '${name}' is not a list of at most ${limits.listItems} strings.`);
  }
  if (type === "list" || (typeof wire === "string" && !wire.isWellFormed()) || !decodeCanonical(wire, type).ok) throw new TypeError(`Input '${name}' is not a canonical ${type} value.`);
  return wire;
}

// The persisted-reference policy. It never throws for a catalog change. Pass the stored JSON text, which is size-checked before
// it is parsed, or an already parsed object.
export function resolveTextReference(value, handlers, options) {
  const diagnostics = [];
  if (typeof value === "string") {
    const json = parseTextReference(value);
    if (!json.ok) return resolution("failed", undefined, [{ path: "$", reason: json.reason }]);
    value = json.value;
  }
  const parsed = parseReference(value);
  if (!parsed.ok) return resolution("failed", undefined, [{ path: "$", reason: parsed.reason }]);
  const tree = parsed.value;
  const fallback = () => resolution(tree.fallbackText === undefined ? "failed" : "fallback", tree.fallbackText, diagnostics);
  if (tree.catalog !== catalog) { diagnostics.push({ path: "$", reason: "catalog-mismatch" }); return fallback(); }
  if (tree.contractFingerprint !== contractFingerprint) {
    if (tree.version === 1) { diagnostics.push({ path: "$", reason: "fingerprint-mismatch" }); return fallback(); }
    diagnostics.push({ path: "$", reason: "contract-changed" });
  }
  const text = translate(tree, "$", handlers, options, diagnostics);
  return text === undefined ? fallback() : resolution("translated", text, diagnostics);
}

// Reads a version 1 or 2 wire value into a detached tree without consulting the receiver's contracts. Structural errors reject
// the whole reference: they come from a faulty writer, never from a catalog change.
function parseReference(value) {
  if (!isRecord(value)) return failure("unsupported-version");
  const state = { references: 0, text: 0, ancestors: new Set() };
  try {
    const root = members(value, ["version", "catalog", "contractFingerprint", "key", "arguments", "fallbackText"]);
    if (root === null) return failure("malformed");
    if (root.version !== 1 && root.version !== 2) return failure("unsupported-version");
    if (!text(root.catalog) || blank(root.catalog) || typeof root.contractFingerprint !== "string" ||
      !/^sha256:[0-9a-f]{64}$/.test(root.contractFingerprint)) return failure("malformed");
    const tree = parseBody(value, root, root.version, 1, state);
    if (tree.ok && state.text > limits.totalText) return failure("limit-exceeded");
    return tree.ok ? Object.freeze({ ok: true, value: Object.freeze({ ...tree.value, version: root.version, catalog: root.catalog, contractFingerprint: root.contractFingerprint }) }) : tree;
  } catch (error) { return failure(error instanceof LimitError ? error.message : "malformed"); }
}

// A reference that contains itself is a cycle; the same message object may still appear in several places of the tree.
function parseBody(original, body, version, depth, state) {
  if (state.ancestors.has(original)) throw new LimitError("cycle");
  if (depth > limits.depth || ++state.references > limits.references) throw new LimitError("limit-exceeded");
  state.ancestors.add(original);
  try { return parseMembers(body, version, depth, state); }
  finally { state.ancestors.delete(original); }
}

function parseMembers(body, version, depth, state) {
  if (!Object.hasOwn(body, "key") || !Object.hasOwn(body, "arguments")) return failure("malformed");
  const key = body.key;
  if (!text(key) || blank(key) || key.length > limits.keyLength) return failure("malformed");
  let fallbackText = Object.hasOwn(body, "fallbackText") ? body.fallbackText : undefined;
  // Version 1 readers have always taken a null fallback as none; version 2 omits the member instead.
  if (version === 1 && fallbackText === null) fallbackText = undefined;
  if (Object.hasOwn(body, "fallbackText") && fallbackText !== undefined && (!text(fallbackText) || fallbackText.length > limits.fallbackLength)) return failure("invalid-fallback");
  if (version !== 1 && Object.hasOwn(body, "fallbackText") && fallbackText === undefined) return failure("invalid-fallback");
  state.text += fallbackText?.length ?? 0;
  const wire = members(body.arguments, null);
  if (wire === null) return failure("invalid-arguments");
  const names = Object.keys(wire).sort();
  if (names.length > limits.arguments) throw new LimitError("limit-exceeded");
  const entries = [];
  for (const name of names) {
    if (!name.isWellFormed() || blank(name)) return failure("invalid-arguments");
    if (version === 1) {
      const raw = wire[name];
      if (typeof raw !== "boolean" && (!text(raw) || raw.length > limits.argumentLength)) return failure(`invalid-argument:${name}`);
      state.text += textLength(raw);
      entries.push([name, Object.freeze({ type: typeof raw === "boolean" ? "boolean" : null, value: raw })]);
      continue;
    }
    const argument = members(wire[name], ["type", "value"]);
    if (argument === null || !Object.hasOwn(argument, "type") || !Object.hasOwn(argument, "value")) return failure(`invalid-argument:${name}`);
    const { type, value } = argument;
    if (type === "message") {
      const nested = members(value, ["key", "arguments", "fallbackText"]);
      if (nested === null) return failure(`invalid-argument:${name}`);
      const child = parseBody(value, nested, 2, depth + 1, state);
      if (!child.ok) return child;
      entries.push([name, Object.freeze({ type, value: child.value })]);
    } else if (type === "list") {
      if (!Array.isArray(value) || value.length > limits.listItems) return failure(`invalid-argument:${name}`);
      const items = Array.from(value); let length = 0;
      for (const item of items) { if (!text(item)) return failure(`invalid-argument:${name}`); length += item.length; }
      if (length > limits.argumentLength) return failure(`invalid-argument:${name}`);
      state.text += length;
      entries.push([name, Object.freeze({ type, value: Object.freeze(items) })]);
    } else if (scalarTypes.has(type)) {
      if ((type !== "boolean" && !text(value)) || !decodeCanonical(value, type).ok) return failure(`invalid-argument:${name}`);
      state.text += textLength(value);
      entries.push([name, Object.freeze({ type, value })]);
    } else return failure(`invalid-argument:${name}`);
  }
  return Object.freeze({ ok: true, value: Object.freeze({ key, arguments: freezeOwnRecord(entries), fallbackText }) });
}

// Copies the own enumerable members of a plain object once, so getters run at most once and later changes cannot affect the result.
function members(value, allowed) {
  if (!isRecord(value)) return null;
  const copy = Object.create(null);
  for (const name of Object.keys(value)) { if (allowed !== null && !allowed.includes(name)) return null; copy[name] = value[name]; }
  return copy;
}

// The canonical spellings of transport version 2, identical to the .NET TranslationReferenceArgument checks.
function decodeCanonical(value, type) {
  if (type === "boolean") return typeof value === "boolean" ? success(value) : failure();
  if (typeof value !== "string" || value.length > limits.argumentLength) return failure();
  if (type === "int64" && (value === "-0" || !/^-?(?:0|[1-9][0-9]*)$/.test(value))) return failure();
  if (type === "decimal") {
    const match = /^(-?)(0|[1-9][0-9]*)(?:\.([0-9]{1,28}))?$/.exec(value);
    if (!match) return failure();
    const coefficient = BigInt(match[2] + (match[3] ?? ""));
    if (coefficient > maximumCoefficient || (match[1] === "-" && coefficient === 0n)) return failure();
  }
  if (type === "guid" && value !== value.toLowerCase()) return failure();
  return decodeWireValue(value, type);
}

// Version 1 values are read as decodeTextReference has always read them.
function decodeArgument(version, argument, type) {
  if (version !== 1) return decodeCanonical(argument.value, type);
  if (type === "boolean") return argument.type === "boolean" ? success(argument.value) : failure();
  if (type === "list" || argument.type !== null) return failure();
  return decodeWireValue(argument.value, type);
}

function bindStrict(tree) {
  if (!Object.hasOwn(contracts, tree.key)) return failure("unknown-key");
  const inputs = contracts[tree.key].inputs, names = Object.keys(tree.arguments);
  if (names.length !== inputs.length || inputs.some(input => !Object.hasOwn(tree.arguments, input.name))) return failure("invalid-arguments");
  const entries = [];
  for (const input of inputs) {
    const argument = tree.arguments[input.name];
    if (!compatible(tree.version, argument, input.type)) return failure(`invalid-argument:${input.name}`);
    if (argument.type === "message") {
      const child = bindStrict({ ...argument.value, version: 2 });
      if (!child.ok) return child;
      const message = Object.freeze({ key: child.value.key, arguments: child.value.arguments, fallbackText: argument.value.fallbackText });
      decodedMessages.add(message);
      entries.push([input.name, message]);
    } else entries.push([input.name, carrier(tree.version, argument, input.type)]);
  }
  return Object.freeze({ ok: true, value: Object.freeze({ key: tree.key, arguments: freezeOwnRecord(entries) }) });
}

function formatStrict(reference, handlers, options) {
  if (!handlers || typeof handlers !== "object" || !Object.hasOwn(handlers, reference.key) || typeof handlers[reference.key] !== "function") return reference.fallbackText;
  const values = [];
  for (const name of Object.keys(reference.arguments)) {
    const value = reference.arguments[name];
    if (!decodedMessages.has(value)) { values.push([name, value]); continue; }
    let output;
    try { output = plainText(formatStrict(value, handlers, options)); } catch { output = undefined; }
    output ??= value.fallbackText;
    if (output === undefined) throw new RangeError(`Message argument '${name}' of '${reference.key}' cannot be formatted.`);
    values.push([name, output]);
  }
  return handlers[reference.key](freezeOwnRecord(values), options);
}

function translate(node, path, handlers, options, diagnostics) {
  if (!Object.hasOwn(contracts, node.key)) { diagnostics.push({ path, reason: "unknown-key" }); return undefined; }
  const inputs = contracts[node.key].inputs, names = Object.keys(node.arguments);
  if (names.length !== inputs.length || inputs.some(input => !Object.hasOwn(node.arguments, input.name) || !compatible(node.version, node.arguments[input.name], input.type))) {
    diagnostics.push({ path, reason: "incompatible-arguments" }); return undefined;
  }
  const values = [];
  for (const input of [...inputs].sort((left, right) => left.name < right.name ? -1 : left.name > right.name ? 1 : 0)) {
    const argument = node.arguments[input.name];
    if (argument.type !== "message") { values.push([input.name, carrier(node.version, argument, input.type)]); continue; }
    const nested = translate({ ...argument.value, version: 2 }, `${path}.${input.name}`, handlers, options, diagnostics) ?? argument.value.fallbackText;
    if (nested === undefined) return undefined;
    values.push([input.name, nested]);
  }
  const handler = handlers && typeof handlers === "object" && Object.hasOwn(handlers, node.key) ? handlers[node.key] : undefined;
  if (typeof handler !== "function") { diagnostics.push({ path, reason: "no-handler" }); return undefined; }
  let output;
  try { output = handler(freezeOwnRecord(values), options); } catch { diagnostics.push({ path, reason: "format-failed" }); return undefined; }
  const result = plainText(output);
  if (result === undefined) diagnostics.push({ path, reason: "structured-content" });
  return result;
}

function compatible(version, argument, type) {
  if (version !== 1) return argument.type === type || (argument.type === "message" && type === "string");
  return decodeArgument(1, argument, type).ok;
}

function carrier(version, argument, type) {
  if (type === "list") return argument.value;
  const decoded = decodeArgument(version, argument, type);
  if (!decoded.ok) throw new TypeError("A checked transport argument did not decode.");
  return decoded.value;
}

// Plain text, or the text of content whose selected variant has no markup; undefined for markup and documents.
function plainText(output) {
  if (typeof output === "string") return output;
  if (isRecord(output) && output.kind === "localized-content" && Array.isArray(output.nodes) && output.nodes.every(node => node?.kind === "text"))
    return output.nodes.map(node => node.value).join("");
  return undefined;
}

function resolution(outcome, value, diagnostics) {
  return Object.freeze({ outcome, text: value, diagnostics: Object.freeze(diagnostics.map(item => Object.freeze({ path: item.path, reason: item.reason }))) });
}

// A strict JSON reader: null-prototype objects, duplicate members rejected, integer numbers only, bounded nesting.
function parseJson(parser, depth) {
  if (depth > limits.jsonDepth) throw new LimitError("limit-exceeded");
  whitespace(parser);
  const source = parser.text, character = source[parser.index];
  if (character === "{") {
    parser.index++; const object = Object.create(null); whitespace(parser);
    if (source[parser.index] === "}") { parser.index++; return object; }
    for (;;) {
      whitespace(parser); if (source[parser.index] !== "\"") throw new SyntaxError();
      const name = parseString(parser); whitespace(parser);
      if (source[parser.index++] !== ":" || Object.hasOwn(object, name)) throw new SyntaxError();
      object[name] = parseJson(parser, depth + 1); whitespace(parser);
      const next = source[parser.index++];
      if (next === "}") return object;
      if (next !== ",") throw new SyntaxError();
    }
  }
  if (character === "[") {
    parser.index++; const array = []; whitespace(parser);
    if (source[parser.index] === "]") { parser.index++; return array; }
    for (;;) {
      array.push(parseJson(parser, depth + 1)); whitespace(parser);
      const next = source[parser.index++];
      if (next === "]") return array;
      if (next !== ",") throw new SyntaxError();
    }
  }
  if (character === "\"") return parseString(parser);
  for (const [literal, value] of [["true", true], ["false", false], ["null", null]])
    if (source.startsWith(literal, parser.index)) { parser.index += literal.length; return value; }
  // Every number in a reference is an integer version; a fraction or exponent is rejected, as .NET rejects it.
  const number = /-?(?:0|[1-9][0-9]*)/y; number.lastIndex = parser.index;
  const match = number.exec(source);
  if (!match || /[.eE0-9]/.test(source[parser.index + match[0].length] ?? "")) throw new SyntaxError();
  parser.index += match[0].length;
  return Number(match[0]);
}

function parseString(parser) {
  const source = parser.text; let result = ""; parser.index++;
  for (;;) {
    const character = source[parser.index++];
    if (character === undefined || character < " ") throw new SyntaxError();
    if (character === "\"") break;
    if (character !== "\\") { result += character; continue; }
    const escape = source[parser.index++];
    const simple = { "\"": "\"", "\\": "\\", "/": "/", b: "\b", f: "\f", n: "\n", r: "\r", t: "\t" }[escape];
    if (simple !== undefined) { result += simple; continue; }
    if (escape !== "u" || !/^[0-9a-fA-F]{4}$/.test(source.slice(parser.index, parser.index + 4))) throw new SyntaxError();
    result += String.fromCharCode(Number.parseInt(source.slice(parser.index, parser.index + 4), 16)); parser.index += 4;
  }
  // System.Text.Json cannot read a lone surrogate, so neither reader accepts one.
  if (!result.isWellFormed()) throw new SyntaxError();
  return result;
}

function whitespace(parser) { while (" \t\n\r".includes(parser.text[parser.index] ?? "x")) parser.index++; }

// UTF-8 bytes, counting a lone surrogate as the three bytes of its replacement character, as .NET counts it.
function utf8Length(value) {
  let length = 0;
  for (let index = 0; index < value.length; index++) {
    const code = value.charCodeAt(index);
    if (code < 0x80) length += 1;
    else if (code < 0x800) length += 2;
    else if (code >= 0xd800 && code <= 0xdbff && index + 1 < value.length && (value.charCodeAt(index + 1) & 0xfc00) === 0xdc00) { length += 4; index++; }
    else length += 3;
  }
  return length;
}

// Blank means empty or only Unicode White_Space characters, which is what .NET string.IsNullOrWhiteSpace tests.
function blank(value) { return /^[\t-\r \u0085\u00a0\u1680\u2000-\u200a\u2028\u2029\u202f\u205f\u3000]*$/.test(value); }
function text(value) { return typeof value === "string" && value.isWellFormed(); }
// Scalar values count their canonical text (booleans as true or false), lists the total length of their items.
function textLength(value) { return Array.isArray(value) ? value.reduce((total, item) => total + item.length, 0) : typeof value === "boolean" ? String(value).length : value.length; }
function isRecord(value) { return value !== null && typeof value === "object" && !Array.isArray(value); }
function success(value) { return Object.freeze({ ok: true, value }); }
function failure(reason) { return Object.freeze({ ok: false, reason }); }
class LimitError extends Error {}
