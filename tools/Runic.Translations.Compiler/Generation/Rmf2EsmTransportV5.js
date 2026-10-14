// Transport versions 1 and 2. The generator prepends the imports and the catalog, contractFingerprint and contracts constants.
const limits = Object.freeze({ arguments: 32, keyLength: 512, argumentLength: 16384, fallbackLength: 65536, listItems: 256, depth: 8, references: 32 });
const scalarTypes = new Set(["string", "int64", "decimal", "boolean", "date", "time", "datetime", "guid"]);
const decodedMessages = new WeakSet(), createdReferences = new WeakSet();

export function decodeTextReference(value) {
  if (!value || typeof value !== "object" || Array.isArray(value)) return failure("unsupported-version");
  let version;
  try { version = value.version; } catch { return failure("malformed"); }
  if (version !== 2) return decodeVersion1(value);
  const parsed = parseReference(value);
  if (!parsed.ok) return parsed;
  const tree = parsed.value;
  if (tree.version !== 2) return failure("malformed");
  if (tree.catalog !== catalog) return failure("catalog-mismatch");
  // A version 2 reference describes its own argument types, so the key's current contract decides, not the whole catalog's fingerprint.
  const bound = bindStrict(tree);
  return bound.ok ? Object.freeze({ ok: true, value: Object.freeze({ version: 2, catalog, contractFingerprint: tree.contractFingerprint, key: bound.value.key, arguments: bound.value.arguments, fallbackText: tree.fallbackText }) }) : bound;
}

export function formatTextReference(reference, handlers, options) {
  const text = formatStrict(reference, handlers, options);
  if (text === undefined) throw new RangeError(`No text-reference handler for '${reference.key}'.`);
  return text;
}

// Writes a version 2 reference for this catalog. A string input may receive another reference created here, which is stored as
// a message argument; integers accept bigint or safe numbers, decimals a decimal carrier or canonical string.
export function createTextReference(key, inputs = {}, options = {}) {
  const body = writeBody(key, inputs, options, 1, { references: 0 });
  const reference = Object.freeze({ version: 2, catalog, contractFingerprint, ...body });
  createdReferences.add(reference);
  return reference;
}

function writeBody(key, inputs, options, depth, state) {
  if (typeof key !== "string" || !Object.hasOwn(contracts, key)) throw new RangeError(`Unknown message '${key}'.`);
  if (depth > limits.depth || ++state.references > limits.references) throw new RangeError("The reference nests too many messages.");
  if (!isRecord(inputs)) throw new TypeError("Message inputs must be an object.");
  const contract = contracts[key].inputs, names = Object.keys(inputs);
  if (names.length !== contract.length || contract.some(input => !Object.hasOwn(inputs, input.name))) throw new TypeError(`Message inputs must contain exactly: ${contract.map(input => input.name).sort().join(", ")}.`);
  const entries = [];
  for (const input of [...contract].sort((left, right) => left.name < right.name ? -1 : left.name > right.name ? 1 : 0)) {
    const value = inputs[input.name];
    if (input.type === "string" && createdReferences.has(value)) {
      const { key: nestedKey, arguments: nestedArguments, fallbackText } = value;
      const nested = Object.freeze({ key: nestedKey, arguments: nestedArguments, ...(fallbackText === undefined ? {} : { fallbackText }) });
      checkNesting(nested, depth + 1, state);
      entries.push([input.name, Object.freeze({ type: "message", value: nested })]);
    } else entries.push([input.name, Object.freeze({ type: input.type, value: encode(value, input.type, input.name) })]);
  }
  const fallbackText = options?.fallbackText;
  if (fallbackText !== undefined && (typeof fallbackText !== "string" || fallbackText.length > limits.fallbackLength)) throw new TypeError("Invalid fallback text.");
  return { key, arguments: freezeOwnRecord(entries), ...(fallbackText === undefined ? {} : { fallbackText }) };
}

function checkNesting(body, depth, state) {
  if (depth > limits.depth || ++state.references > limits.references) throw new RangeError("The reference nests too many messages.");
  for (const name of Object.keys(body.arguments)) if (body.arguments[name].type === "message") checkNesting(body.arguments[name].value, depth + 1, state);
}

function encode(value, type, name) {
  let wire = value;
  if (type === "int64" && (typeof value === "bigint" || Number.isSafeInteger(value))) wire = String(value);
  else if (type === "decimal" && isRecord(value) && typeof value.coefficient === "bigint" && Number.isInteger(value.scale) && value.scale >= 0) {
    const digits = value.coefficient.toString().padStart(value.scale + 1, "0");
    const unsigned = value.scale === 0 ? digits : `${digits.slice(0, digits.length - value.scale)}.${digits.slice(digits.length - value.scale)}`;
    wire = value.negative === true && /[1-9]/.test(unsigned) ? `-${unsigned}` : unsigned;
  } else if (type === "list" && Array.isArray(value)) {
    const items = Array.from(value); let length = 0;
    if (items.length <= limits.listItems && items.every(item => typeof item === "string" && (length += item.length) <= limits.argumentLength)) return Object.freeze(items);
    throw new TypeError(`Input '${name}' is not a list of at most ${limits.listItems} strings.`);
  }
  if (type === "list" || !decodeCanonical(wire, type).ok) throw new TypeError(`Input '${name}' is not a canonical ${type} value.`);
  return wire;
}

// The persisted-reference policy, shared with the .NET TranslationReferenceResolver. It never throws for a catalog change.
export function resolveTextReference(value, handlers, options) {
  const diagnostics = [];
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

function decodeVersion1(value) {
  try {
    const names = Object.keys(value).sort(); const fallbackPresent = names.includes("fallbackText"); const expectedMembers = ["arguments","catalog","contractFingerprint",...(fallbackPresent ? ["fallbackText"] : []),"key","version"].sort(); if (names.length !== expectedMembers.length || names.some((name,index) => name !== expectedMembers[index])) return failure("malformed");
    const version = value.version, wireCatalog = value.catalog, wireFingerprint = value.contractFingerprint, key = value.key, argumentsValue = value.arguments, fallbackText = fallbackPresent ? value.fallbackText : undefined;
    if (version !== 1) return failure("unsupported-version"); if (wireCatalog !== catalog) return failure("catalog-mismatch"); if (wireFingerprint !== contractFingerprint) return failure("fingerprint-mismatch");
    if (typeof key !== "string" || !Object.hasOwn(contracts, key)) return failure("unknown-key");
    const contract = contracts[key].inputs; if (!argumentsValue || typeof argumentsValue !== "object" || Array.isArray(argumentsValue)) return failure("invalid-arguments");
    const argumentNames = Object.keys(argumentsValue).sort(); const expectedArguments = contract.map(item => item.name).sort(); if (argumentNames.length !== expectedArguments.length || argumentNames.some((name,index) => name !== expectedArguments[index])) return failure("invalid-arguments");
    const entries = []; for (const item of contract) { const wireValue = argumentsValue[item.name]; const decoded = decodeWireValue(wireValue, item.type); if (!decoded.ok) return failure(`invalid-argument:${item.name}`); entries.push([item.name, decoded.value]); }
    if (fallbackPresent && (typeof fallbackText !== "string" || fallbackText.length > 65536)) return failure("invalid-fallback");
    return Object.freeze({ ok: true, value: Object.freeze({ version: 1, catalog, contractFingerprint, key, arguments: freezeOwnRecord(entries), fallbackText }) });
  } catch { return failure("malformed"); }
}

// Reads a version 1 or 2 wire value into a detached tree without consulting the receiver's contracts. Structural errors reject
// the whole reference: they come from a faulty writer, never from a catalog change.
function parseReference(value) {
  if (!isRecord(value)) return failure("unsupported-version");
  const state = { references: 0, ancestors: new Set() };
  try {
    const root = members(value, ["version", "catalog", "contractFingerprint", "key", "arguments", "fallbackText"]);
    if (root === null) return failure("malformed");
    if (root.version !== 1 && root.version !== 2) return failure("unsupported-version");
    if (typeof root.catalog !== "string" || !root.catalog.trim() || typeof root.contractFingerprint !== "string" ||
      !/^sha256:[0-9a-f]{64}$/.test(root.contractFingerprint)) return failure("malformed");
    const tree = parseBody(value, root, root.version, 1, state);
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
  if (typeof key !== "string" || !key.trim() || key.length > limits.keyLength) return failure("malformed");
  let fallbackText;
  if (Object.hasOwn(body, "fallbackText")) {
    fallbackText = body.fallbackText;
    if (typeof fallbackText !== "string" || fallbackText.length > limits.fallbackLength) return failure("invalid-fallback");
  }
  const wire = members(body.arguments, null);
  if (wire === null) return failure("invalid-arguments");
  const names = Object.keys(wire).sort();
  if (names.length > limits.arguments) throw new LimitError("limit-exceeded");
  const entries = [];
  for (const name of names) {
    if (!name.trim()) return failure("invalid-arguments");
    if (version === 1) {
      const raw = wire[name];
      if (typeof raw !== "boolean" && (typeof raw !== "string" || raw.length > limits.argumentLength)) return failure(`invalid-argument:${name}`);
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
      for (const item of items) { if (typeof item !== "string") return failure(`invalid-argument:${name}`); length += item.length; }
      if (length > limits.argumentLength) return failure(`invalid-argument:${name}`);
      entries.push([name, Object.freeze({ type, value: Object.freeze(items) })]);
    } else if (scalarTypes.has(type)) {
      if (!decodeCanonical(value, type).ok) return failure(`invalid-argument:${name}`);
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
  if (type === "decimal" && (!/^-?(?:0|[1-9][0-9]*)(?:\.[0-9]+)?$/.test(value) || /^-0(?:\.0+)?$/.test(value))) return failure();
  if (type === "guid" && value !== value.toLowerCase()) return failure();
  return decodeWireValue(value, type);
}

function bindStrict(tree) {
  if (!Object.hasOwn(contracts, tree.key)) return failure("unknown-key");
  const inputs = contracts[tree.key].inputs, names = Object.keys(tree.arguments);
  if (names.length !== inputs.length) return failure("invalid-arguments");
  const entries = [];
  for (const input of inputs) {
    if (!Object.hasOwn(tree.arguments, input.name)) return failure("invalid-arguments");
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
    let text;
    try { text = plainText(formatStrict(value, handlers, options)); } catch { text = undefined; }
    text ??= value.fallbackText;
    if (text === undefined) throw new RangeError(`Message argument '${name}' of '${reference.key}' cannot be formatted.`);
    values.push([name, text]);
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
    const text = translate({ ...argument.value, version: 2 }, `${path}.${input.name}`, handlers, options, diagnostics) ?? argument.value.fallbackText;
    if (text === undefined) return undefined;
    values.push([input.name, text]);
  }
  const handler = handlers && typeof handlers === "object" && Object.hasOwn(handlers, node.key) ? handlers[node.key] : undefined;
  if (typeof handler !== "function") { diagnostics.push({ path, reason: "no-handler" }); return undefined; }
  let output;
  try { output = handler(freezeOwnRecord(values), options); } catch { diagnostics.push({ path, reason: "format-failed" }); return undefined; }
  const text = plainText(output);
  if (text === undefined) diagnostics.push({ path, reason: "structured-content" });
  return text;
}

function compatible(version, argument, type) {
  if (version !== 1) return argument.type === type || (argument.type === "message" && type === "string");
  // Version 1 carries strings and booleans; the receiver's type decides how a string is read.
  if (type === "boolean" || type === "list") return argument.type === type;
  return argument.type === null && decodeCanonical(argument.value, type).ok;
}

function carrier(version, argument, type) {
  if (type === "list") return argument.value;
  const decoded = decodeCanonical(argument.value, type);
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

function resolution(outcome, text, diagnostics) {
  return Object.freeze({ outcome, text, diagnostics: Object.freeze(diagnostics.map(item => Object.freeze({ path: item.path, reason: item.reason }))) });
}

function isRecord(value) { return value !== null && typeof value === "object" && !Array.isArray(value); }
function success(value) { return Object.freeze({ ok: true, value }); }
function failure(reason) { return Object.freeze({ ok: false, reason }); }
class LimitError extends Error {}
