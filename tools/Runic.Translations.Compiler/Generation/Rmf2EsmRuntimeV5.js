// RMF2 execution-v2 ESM runtime. Configuration constants are emitted above.
const maximumCoefficient = 79228162514264337593543950335n;
const minimumInt64 = -9223372036854775808n;
const maximumInt64 = 9223372036854775807n;
const localeSet = new Set(locales);
const limits = Object.freeze({ maximumDocumentBytes: 8388608, maximumDepth: 64, maximumMessages: 50000, maximumPatternBytes: 65536, maximumArgumentsPerMessage: 32 });
// A validated message wrapper maps to its message contract (see contractOf).
const validatedMessages = new WeakMap();
const contractCopies = new WeakMap();
let structuredMessages;
const dynamicContracts = new WeakMap();
const decodedArtifacts = new WeakSet();
// The overlays that dynamic.js installs (installOverlay, installLocalePack), or null before the first one. Message functions,
// resolveLocale and getMessageLocale only test it, so a page that never installs an overlay bundles none of the overlay code.
let overlays = null;
// The shared locale channel: one locale for every generated catalog on a page or in a server process, and for every copy of
// this runtime, so an application and its libraries switch together. Its shape is a protocol between runtimes of different
// compiler releases and the Svelte adapter, so members are only ever added: { version: 1, resolver, storage, resolvers }.
// resolver is the page's locale resolver (configureSharedLocaleResolver, useMessageLocale(locale)): the last of resolvers,
// the installed ones in installation order, each removed by identity, so removals in any order leave the latest remaining
// one. storage is the AsyncLocalStorage that the first server.js creates, whose store { requested, timeZone } is the
// locale of the request runWithLocale runs.
const localeChannelKey = Symbol.for("runic.translations.locale");
function localeChannel() {
  let channel = globalThis[localeChannelKey];
  if (channel === undefined || channel === null || typeof channel !== "object") globalThis[localeChannelKey] = channel = { version: 1, resolver: null, storage: null, resolvers: [] };
  if (!Array.isArray(channel.resolvers)) channel.resolvers = typeof channel.resolver === "function" ? [channel.resolver] : [];
  return channel;
}
// Installs a resolver on top of an ordered list and returns its removal, which takes out exactly this installation
// (a resolver installed twice is two entries) and makes the latest remaining one current.
function installResolver(list, resolver, apply) {
  const entry = () => resolver(); list.push(entry); apply(entry); let active = true;
  return () => {
    if (!active) return; active = false;
    const index = list.indexOf(entry); if (index >= 0) list.splice(index, 1);
    apply(list.length === 0 ? null : list[list.length - 1]);
  };
}
function requestStore() {
  const store = globalThis[localeChannelKey]?.storage?.getStore?.();
  return store !== null && typeof store === "object" && typeof store.requested === "string" ? store : undefined;
}
// The resolver configureLocaleResolver installs for this catalog only (the latest of localeResolvers); null when it has
// none and the channel applies.
let localeResolver = null;
const localeResolvers = [];
// The tag the catalog formats in when a call names none: its own resolver, else the request's locale (runWithLocale of any
// catalog), else the shared resolver, else the document's lang, else the base locale.
function currentLocaleTag() {
  if (localeResolver !== null) return localeResolver();
  const store = requestStore(); if (store !== undefined) return store.requested;
  const shared = globalThis[localeChannelKey]?.resolver;
  if (typeof shared === "function") return shared();
  return typeof document === "undefined" ? baseLocale : (document.documentElement.lang || baseLocale);
}

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
  return installResolver(localeResolvers, resolver, current => { localeResolver = current; });
}

// Sets the locale of every generated catalog on the page (or in the process) that has no resolver of its own: the shared
// channel above. The resolver returns a tag that each catalog resolves under its own locales and policy, normally the
// requested tag of the application's locale source (de-CH), so a library that declares de-CH shows it. The resolver is
// process-wide: on a server, set the request's locale with runWithLocale instead, which every catalog reads first.
export function configureSharedLocaleResolver(resolver) {
  if (typeof resolver !== "function") throw new TypeError("The locale resolver must be a function.");
  const channel = localeChannel();
  return installResolver(channel.resolvers, resolver, current => { channel.resolver = current; });
}

// server.js: the process's one request-locale storage, created by the first catalog that needs it.
export function requestLocaleStorage(create) { const channel = localeChannel(); return channel.storage ??= create(); }

export function getLocale() { return resolveLocale(currentLocaleTag()); }

// The canonical tag the locale resolver asks for, before it resolves to a catalog locale: "de-CH" where getLocale()
// gives "de". Numbers, percentages, dates and times format in it when its language matches a message's content locale.
export function getRequestedLocale() { return requestedTag(currentLocaleTag()); }
function requestedTag(requested) {
  if (typeof requested !== "string") throw new RangeError("A locale string is required.");
  if (requested === "") return resolveLocale(requested);
  const canonical = canonicalLocale(requested); if (canonical === null) throw new RangeError(`Invalid locale '${requested}'.`);
  return canonical;
}

// The locale numbers, percentages, dates and times format in: the requested tag, without its extensions and private-use
// subtags, when it has the content locale's language and the same implied script, else the content locale. So de-CH
// over de text formats Swiss numbers, while sr-Latn over Cyrillic sr text, or zh-TW (Traditional) over zh (Simplified)
// text, keeps the content locale's. The script of a language CLDR writes in one script is dropped when it is the likely
// one (de-Latn-CH formats as de-CH), and -u-nu-latn is kept where the locale's CLDR default numbering system is not
// Latin (ar-EG-u-nu-latn). Plural, ordinal, list and relative-time data stay on the content locale. The .NET runtime
// applies the same rule (Rmf2LocalizedFormats.FormattingLocale). The content locale is a catalog locale, so its
// language's CLDR tables are embedded.
function formattingLocale(requested, content) {
  if (typeof requested !== "string" || requested === "" || requested.toLowerCase() === content.toLowerCase()) return content;
  const canonical = canonicalLocale(requested); if (canonical === null) return content;
  const latin = latinDigits(canonical), tag = withoutExtensions(canonical), left = cldrParse(tag), right = cldrParse(content);
  if (left.language.toLowerCase() !== right.language.toLowerCase() || (impliedScript(left) ?? "").toLowerCase() !== (impliedScript(right) ?? "").toLowerCase()) return content;
  const result = tag.toLowerCase() === content.toLowerCase() ? content : withoutDefaultScript(tag, left);
  return latin && numberFormat(result).system !== "latn" ? `${result}-u-nu-latn` : result;
}
// A canonical tag without its extensions and private-use subtags: en-US-u-ca-buddhist formats as en-US.
function withoutExtensions(canonical) { const match = /-[0-9a-z]-/i.exec(canonical); return match === null ? canonical : canonical.slice(0, match.index); }
// Whether a canonical tag's Unicode extension asks for Latin digits (-u-nu-latn).
function latinDigits(canonical) {
  const parts = canonical.toLowerCase().split("-"); let index = 1;
  while (index < parts.length && !(parts[index] === "u" || parts[index] === "x")) index++;
  if (index >= parts.length || parts[index] === "x") return false;
  for (index++; index + 1 < parts.length && parts[index].length > 1; index++) if (parts[index] === "nu") return parts[index + 1] === "latn";
  return false;
}
// The explicit script, else the CLDR likely script of the language and region (sr → Cyrl, sr-ME → Latn, zh-TW → Hant),
// else undefined for a language without CLDR likely subtags.
function impliedScript(tag) { return tag.script ?? cldrMaximize(tag).script; }
// The tag without its script when the script adds nothing: it is the likely script of the language and region, and CLDR
// writes the language in one script only (de-Latn-CH → de-CH; sr-Cyrl-BA and zh-Hans-HK stay).
function withoutDefaultScript(tag, parts) {
  if (parts.script === undefined || multiScript(parts.language)) return tag;
  return parts.script.toLowerCase() === (cldrMaximize({ ...parts, script: undefined }).script ?? "").toLowerCase() ? cldrFormat({ ...parts, script: undefined }) : tag;
}
// Whether the CLDR likely subtags for a language and its regions name more than one script (sr, zh, pa, uz, …): the
// values of the language-only and language-region keys. A language-script key names its own script (en-Shaw), so en is
// written in one script and en-Latn-US formats as en-US.
let multiScriptLanguages;
function multiScript(language) {
  if (multiScriptLanguages === undefined) {
    const scripts = new Map(); multiScriptLanguages = new Set();
    for (const [key, value] of Object.entries(generatedLocaleData.likely)) {
      const keyParts = cldrParse(key), parts = cldrParse(value);
      if (keyParts.script !== undefined || keyParts.language !== parts.language) continue;
      if (!scripts.has(parts.language)) scripts.set(parts.language, parts.script); else if (scripts.get(parts.language) !== parts.script) multiScriptLanguages.add(parts.language);
    }
  }
  return multiScriptLanguages.has(language.toLowerCase());
}

// Listeners for messages that show the text of a fallback locale, for development overlays and telemetry. A listener
// receives the message key, the catalog locale it was requested in and the locale whose text it shows; a listener
// that throws never affects formatting.
const fallbackListeners = new Set();
export function onFallback(listener) {
  if (typeof listener !== "function") throw new TypeError("A fallback listener must be a function.");
  const entry = { listener }; fallbackListeners.add(entry);
  return () => { fallbackListeners.delete(entry); };
}
function reportFallback(key, locale, content) {
  if (fallbackListeners.size === 0 || content === locale) return;
  for (const { listener } of [...fallbackListeners]) { try { listener(key, locale, content); } catch { /* Formatting is complete. */ } }
}

// The locales of a message for a request: the locale whose text it shows (for the lang attribute of a string message)
// and the locale its numbers and dates format in.
// The catalog's table (messageLocaleTable above) is parsed on first use, so a bundle that never calls this drops it.
let messageLocales;
export function getMessageLocale(key, options) {
  messageLocales ??= freezeTrusted(messageLocaleTable());
  const entry = typeof key === "string" && Object.hasOwn(messageLocales, key) ? messageLocales[key] : undefined; if (!entry) throw new RangeError(`Unknown message '${key}'.`);
  const requested = options?.locale === undefined ? getRequestedLocale() : requestedTag(options.locale), locale = resolveLocale(requested);
  let contentLocale = Object.hasOwn(entry, locale) ? entry[locale] : locale;
  if (overlays !== null) {
    // The catalog has its own text at a locale whose content the table does not redirect.
    const found = overlays.find(key, locale, step => !Object.hasOwn(entry, step));
    contentLocale = typeof found === "string" ? (Object.hasOwn(entry, found) ? entry[found] : found) : found.contentLocale;
  }
  return Object.freeze({ key, locale: contentLocale, formatLocale: formattingLocale(requested, contentLocale) });
}

// A mutable locale for an application: getLocale() is the catalog locale, getRequestedLocale() the canonical tag that
// was set (de-CH where getLocale() gives de). Setting the current catalog locale itself keeps a regional requested tag,
// so a navigation to a /de route keeps de-CH formatting. Listeners run when either changes and receive the catalog locale.
export function createLocaleSource(options = {}) {
  if (!isRecord(options)) throw new TypeError("Locale source options must be an object.");
  let requested = requestedTag(options.initialLocale ?? baseLocale), current = resolveLocale(requested); const listeners = new Set();
  return Object.freeze({
    getLocale: () => current,
    getRequestedLocale: () => requested,
    localeDirection,
    subscribe(listener) { if (typeof listener !== "function") throw new TypeError("A locale listener must be a function."); listeners.add(listener); let active = true; return () => { if (active) { active = false; listeners.delete(listener); } }; },
    setLocale(locale) {
      const nextRequested = requestedTag(locale), next = resolveLocale(nextRequested);
      if (next === current && (nextRequested === requested || nextRequested === next)) return;
      current = next; requested = nextRequested; for (const listener of [...listeners]) listener(next);
    },
  });
}

// A locale the catalog declares or an installed overlay adds.
function supportsLocale(tag) { return localeSet.has(tag) || overlays !== null && overlays.added.has(tag); }

export function resolveLocale(requested) {
  if (typeof requested !== "string") throw new RangeError("A locale string is required.");
  // An empty tag (the invariant culture, an unset lang) names no locale, so the unsupported-locale policy applies to it.
  const canonical = requested === "" ? "" : canonicalLocale(requested); if (canonical === null) throw new RangeError(`Invalid locale '${requested}'.`);
  if (supportsLocale(canonical)) return canonical;
  if (unsupportedLocalePolicy === "ParentsThenDefault") return (canonical === "" ? undefined : matchLocale(canonical)) ?? baseLocale;
  if (unsupportedLocalePolicy === "Default") return baseLocale;
  throw new RangeError(`Unsupported locale '${requested}'.`);
}

// The best declared locale for a list of preferences, such as navigator.languages, Accept-Language or LANGUAGE:
// the first preference that matches a declared locale wins, else the base locale. Invalid and empty entries are
// skipped, and so are und tags that are not declared; POSIX forms (de_DE.UTF-8) are accepted. The
// unsupported-locale policy does not apply.
export function negotiateLocale(preferences) {
  if (typeof preferences === "string") preferences = [preferences];
  if (preferences === null || typeof preferences !== "object" || typeof preferences[Symbol.iterator] !== "function") throw new TypeError("Locale preferences must be an iterable of strings.");
  for (const preference of preferences) {
    if (typeof preference !== "string") continue;
    const canonical = canonicalLocale(preference.replace(/[.@].*$/s, ""));
    if (canonical === null) continue;
    const match = supportsLocale(canonical) ? canonical : matchLocale(canonical);
    if (match !== undefined) return match;
  }
  return baseLocale;
}

// A declared (or overlay-added) locale for a canonical tag that is not one: CLDR likely-subtag matching, else the
// nearest truncation with the same likely script (tlh-x-a → tlh, but never sr-Latn-RS → sr or zh-Hant → zh), else
// undefined. The undetermined language (und, und-DE) matches nothing. LocaleNegotiation.Match in .NET.
function matchLocale(canonical, available = overlays === null ? locales : overlays.supported) {
  return cldrMatch(canonical, available) ?? truncateLocale(canonical, available);
}
// The nearest locale of available (canonical tags in ordinal order) left by removing subtags, with the same likely script.
function truncateLocale(canonical, available) {
  const wanted = cldrMaximize(cldrParse(canonical));
  if (wanted.language === "und") return undefined;
  let parent = canonical;
  while (parent.includes("-")) {
    parent = parent.slice(0, parent.lastIndexOf("-"));
    if (available.includes(parent) && cldrMaximize(cldrParse(parent)).script === wanted.script) return parent;
  }
  return undefined;
}

// The direction of a locale's script: the explicit script subtag, else its likely script from the pinned CLDR
// likelySubtags (as .NET's LocaleDirection), so "ar", "pa-PK" and "ku-Arab" are right-to-left and "ku", "he-Latn" and
// "sd-Deva" left-to-right. The catalog carries likely subtags only for its own languages; for another language the likely
// script comes from Intl.Locale's maximize(), and without Intl.Locale only an explicit script counts.
const rightToLeftScripts = new Set(generatedLocaleData.rightToLeftScripts);
export function localeDirection(locale) {
  if (typeof locale !== "string") throw new RangeError("A locale string is required.");
  const canonical = canonicalLocale(locale); if (canonical === null) throw new RangeError(`Invalid locale '${locale}'.`);
  return rightToLeft(canonical) ? "rtl" : "ltr";
}
const directionCache = new Map();
function rightToLeft(canonical) {
  let cached = directionCache.get(canonical); if (cached !== undefined) return cached;
  const tag = cldrCanonicalize(cldrParse(canonical));
  let script = tag.script ?? cldrMaximize(tag).script;
  if (script === undefined && typeof Intl === "object" && typeof Intl.Locale === "function") {
    try { script = new Intl.Locale(canonical).maximize().script; } catch { script = undefined; }
  }
  cached = script !== undefined && rightToLeftScripts.has(script);
  if (directionCache.size < 64) directionCache.set(canonical, cached);
  return cached;
}

// Whether any locale tag (requested, catalog or content) is right to left; an empty or invalid tag is not.
function rightToLeftTag(tag) {
  if (typeof tag !== "string" || tag === "") return false;
  const canonical = canonicalLocale(tag); return canonical !== null && rightToLeft(canonical);
}

// Whether a format call isolates its placeholders: the catalog's runtime.bidiIsolation "on" always, "off" never, and
// "default" when the requested locale, the catalog locale it resolved to or the message's content locale is right to left,
// decided per call, as .NET's Rmf2RuntimeEvaluator does. A right-to-left overlay of a left-to-right catalog is isolated too.
function bidiIsolationFor(requestedLocale, resolvedLocale, contentLocale) {
  const mode = generatedLocaleData.bidiIsolation;
  if (mode === "on") return true;
  if (mode !== "default") return false;
  return rightToLeftTag(requestedLocale) || rightToLeftTag(resolvedLocale) || rightToLeftTag(contentLocale);
}

/** Removes the bidi isolation controls U+2066–U+2069 that formatting puts around placeholders, for plain-text sinks such as terminals, logs and tests. */
export function removeBidiIsolates(text) {
  if (typeof text !== "string") throw new TypeError("Text must be a string.");
  return text.replace(/[\u2066-\u2069]/g, "");
}

// Unicode MessageFormat 2's default bidi strategy (LDML 48 "Handling Bidirectional Text"), as .NET's Rmf2Bidi: a
// placeholder of left-to-right direction stays bare in a left-to-right message unless its u:dir asks for isolation, and
// is otherwise wrapped in LRI…PDI; a right-to-left one in RLI…PDI; one of unknown direction in FSI…PDI. Without isolation
// for the call only an explicit u:dir of ltr, rtl or auto isolates. Numbers, dates and times have the direction of their
// formatting locale; relative times and lists (CLDR separators, each item isolated) of the content locale; UUIDs and
// booleans are left to right, and text from the caller has none.
function isolate(text, value, messageRtl, formatLocale, isolation) {
  const option = value.format.options["u:dir"];
  if (!isolation && (option === undefined || option === "inherit")) return text;
  const fn = value.format.function;
  const direction = option === "ltr" ? "ltr" : option === "rtl" ? "rtl" : option === "auto" ? "unknown"
    : option === "inherit" ? (messageRtl ? "rtl" : "ltr")
    : ["integer", "number", /*@formats*/"currency", "unit", /*@end-formats*/"date", "time", "datetime"].includes(fn) ? (rightToLeftTag(formatLocale) ? "rtl" : "ltr")
    : fn === "runic:relative-time" || fn === "runic:list" ? (messageRtl ? "rtl" : "ltr")
    : ["runic:uuid", "runic:boolean"].includes(fn) ? "ltr" : "unknown";
  const explicit = option === "ltr" || option === "rtl" || option === "auto";
  if (direction === "ltr") return !messageRtl && !explicit ? text : `\u2066${text}\u2069`;
  if (direction === "rtl") return `\u2067${text}\u2069`;
  return `\u2068${text}\u2069`;
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
  // A finite number is its shortest round-trip decimal, as String() writes it: 0.1 is 0.1, 1e-7 is 0.0000001.
  if (typeof value === "number") { if (!Number.isFinite(value)) throw new TypeError("A decimal input must be a finite number."); return parseDecimal(String(value)); }
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
    case "datetime": { const instant = coerceInstant(value); if (instant !== null) return instant; break; }
    case "guid": if (typeof value === "string" && /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(value)) return value.toLowerCase(); break;
    // A frozen snapshot, so later changes to the caller's array cannot affect formatting.
    case "list": if (Array.isArray(value)) { const items = Array.from(value); if (items.every(item => typeof item === "string")) return Object.freeze(items); } break;
  }
  throw new TypeError(`Input '${name}' does not contain its declared ${type} carrier.`);
}

// A date-time input: a valid Date, or an ISO 8601 string with seconds, an optional fraction and Z or an offset of at
// most 14 hours. The carrier is the canonical UTC yyyy-MM-ddTHH:mm:ssZ of the transport form, so sub-second digits
// are dropped.
function coerceInstant(value) {
  let milliseconds;
  if (value instanceof Date) milliseconds = value.getTime();
  else if (typeof value !== "string") return null;
  else if (validDateTime(value)) return value;
  else {
    const match = /^(\d{4}-\d{2}-\d{2})T(\d{2}:\d{2}:\d{2})(?:\.\d{1,9})?(?:Z|([+-])(\d{2}):(\d{2}))$/.exec(value);
    if (!match || !validDate(match[1]) || !validTime(match[2])) return null;
    const hours = Number(match[4] ?? 0), minutes = Number(match[5] ?? 0);
    if (minutes > 59 || hours * 60 + minutes > 14 * 60) return null;
    milliseconds = Date.parse(`${match[1]}T${match[2]}Z`) - (match[3] === "-" ? -1 : 1) * (hours * 60 + minutes) * 60000;
  }
  if (!Number.isFinite(milliseconds)) return null;
  const text = new Date(Math.floor(milliseconds / 1000) * 1000).toISOString().replace(/\.000Z$/, "Z");
  return validDateTime(text) ? text : null;
}

function validateInputs(inputs, contract) {
  if (!isRecord(inputs)) throw new TypeError("Message inputs must be an object.");
  const expected = contract.inputs.map(item => item.name).sort(); const actual = Object.keys(inputs).sort();
  if (actual.length !== expected.length || actual.some((name, index) => name !== expected[index])) throw new TypeError(`Message inputs must contain exactly: ${expected.join(", ")}.`);
  return freezeOwnRecord(contract.inputs.map(item => [item.name, Object.freeze({ carrier: coerceInput(inputs[item.name], item.type, item.name), type: item.type, format: Object.freeze({ function: defaultFunction(item.type), options: freezeOwnRecord([]) }) })]));
}

function defaultFunction(type) { return ({ string: "string", int64: "integer", decimal: "number", boolean: "runic:boolean", date: "date", time: "time", datetime: "datetime", guid: "runic:uuid", list: "runic:list" })[type]; }
function functionType(name) { return ({ string: "string", integer: "int64", number: "decimal", "runic:boolean": "boolean", date: "date", time: "time", datetime: "datetime", "runic:uuid": "guid", "runic:relative-time": "decimal", "runic:list": "list"/*@formats*/, currency: "decimal", unit: "decimal"/*@end-formats*/ })[name]; }
function optionType(fn, name) {
  if (name === "u:dir" && functionType(fn) !== undefined) return "string";
  if (["string", "runic:boolean", "integer", "number"].includes(fn) && name === "select") return "string";
  if ((fn === "integer" || fn === "number") && name === "useGrouping") return "string";
  /*@formats*/// :currency, :unit and compact notation; a catalog that uses none of them is built without these lines.
  if ((fn === "currency" || fn === "unit") && name === "useGrouping") return "string";
  if (fn === "number" && ["runic:notation", "runic:compactDisplay"].includes(name)) return "string";
  if (fn === "currency" && ["currency", "currencyDisplay", "currencySign"].includes(name)) return "string";
  if (fn === "currency" && name === "fractionDigits") return "int64";
  if (fn === "unit" && ["unit", "unitDisplay"].includes(name)) return "string";
  if (fn === "unit" && ["minimumFractionDigits", "maximumFractionDigits"].includes(name)) return "int64";
  /*@end-formats*/
  if (["number", "date", "time", "datetime", "runic:uuid"].includes(fn) && name === "style") return "string";
  if (fn === "datetime" && name === "timeZone") return "string";
  if (fn === "number" && ["minimumFractionDigits", "maximumFractionDigits"].includes(name)) return "int64";
  if (fn === "runic:relative-time" && ["unit", "numeric"].includes(name)) return "string";
  if (fn === "runic:list" && ["type", "style"].includes(name)) return "string";
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
    /*@formats*/// A currency or unit from a variable must be in the catalog's declared set (runic.json formats).
    const dynamicCode = expressionValue.options.some(option => option.name === expressionValue.function && ["input", "local"].includes(option.value.kind));
    /*@end-formats*/return Object.freeze({ carrier: inherited.carrier, type: inherited.type, format: Object.freeze({ function: expressionValue.function, options/*@formats*/, dynamicCode/*@end-formats*/ }) });
  };
  for (const declaration of wrapper.ast.declarations) {
    const resolved = expression(declaration.expression);
    if (declaration.kind === "input") inputValues[declaration.name] = resolved; else locals[declaration.name] = resolved;
  }
  return Object.freeze({ resolve, expression });
}

function validateResolvedOptions(fn, options) {
  const allowed = {
    string: { select: ["exact"] }, "runic:boolean": { select: ["exact"] }, integer: { select: ["plural", "ordinal", "exact"], useGrouping: ["auto", "always", "never", "min2"] },
    number: { select: ["plural", "ordinal", "exact"], style: ["decimal", "percent"], useGrouping: ["auto", "always", "never", "min2"]/*@formats*/, "runic:notation": ["standard", "compact"], "runic:compactDisplay": ["short", "long"]/*@end-formats*/ },
    /*@formats*/currency: { currency: /^[A-Z]{3}$/, currencyDisplay: ["symbol", "narrowSymbol", "code", "name"], currencySign: ["standard", "accounting"], useGrouping: ["auto", "always", "never", "min2"] },
    unit: { unit: /^[a-z0-9-]{1,64}$/, unitDisplay: ["short", "long", "narrow"], useGrouping: ["auto", "always", "never", "min2"] },
    /*@end-formats*/date: { style: ["iso", "short", "medium", "long", "full"] },
    time: { style: ["iso", "short", "medium", "long", "full"] }, datetime: { style: ["iso", "short", "medium", "long", "full"], timeZone: null }, "runic:uuid": { style: ["d", "n", "b", "p"] },
    "runic:relative-time": { unit: ["second", "minute", "hour", "day", "week", "month", "year"], numeric: ["always", "auto"] },
    "runic:list": { type: ["conjunction", "disjunction", "unit"], style: ["long", "short", "narrow"] },
  }[fn];
  if (!allowed) throw new TypeError("Unknown formatter.");
  const digitOption = name => ((fn === "number"/*@formats*/ || fn === "unit"/*@end-formats*/) && ["minimumFractionDigits", "maximumFractionDigits"].includes(name))/*@formats*/ || (fn === "currency" && name === "fractionDigits")/*@end-formats*/;
  for (const name of Object.keys(options)) {
    if (name === "u:dir") { if (!["ltr", "rtl", "auto", "inherit"].includes(options[name])) throw new RangeError("Invalid formatter option value."); continue; }
    if (!Object.hasOwn(allowed, name) && !digitOption(name)) throw new TypeError("Unknown formatter option.");
    if (digitOption(name)) { if (typeof options[name] !== "bigint" || options[name] < 0n || options[name] > 6n) throw new RangeError("Fraction digits are outside 0..6."); }
    else if (allowed[name] === null) { if (!validTimeZone(options[name])) throw new RangeError("Invalid time zone."); }
    else if (allowed[name] instanceof RegExp) { if (typeof options[name] !== "string" || !allowed[name].test(options[name])) throw new RangeError("Invalid formatter option value."); }
    else if (!allowed[name].includes(options[name])) throw new RangeError("Invalid formatter option value.");
  }
  /*@formats*/if (fn === "number" && options["runic:notation"] === "compact" && options.style === "percent") throw new RangeError("Compact notation does not apply to percentages.");
  /*@end-formats*/if (fn === "number"/*@formats*/ || fn === "unit"/*@end-formats*/) {
    const style = options.style ?? "decimal", minimum = options.minimumFractionDigits ?? 0n, maximum = options.maximumFractionDigits ?? (style === "percent" ? 4n : 6n);
    if (minimum > maximum || maximum > (style === "percent" ? 4n : 6n)) throw new RangeError("Invalid number precision.");
  }
}

// locale is the formatting locale of numbers (the number in relative time included), percentages, dates and times;
// contentLocale gives lists and the relative-time patterns.
function formatResolved(value, locale, contentLocale = locale, isolateItems = false) {
  const fn = value.format.function, options = value.format.options;
  switch (fn) {
    case "string": return value.carrier;
    case "runic:boolean": return value.carrier ? "true" : "false";
    case "integer": return formatInteger(value.carrier, locale, options.useGrouping ?? "auto");
    case "number": {
      const number = value.type === "int64" ? decimalFromInteger(value.carrier) : value.carrier;
      return /*@formats*/options["runic:notation"] === "compact" ? formatCompact(number, options["runic:compactDisplay"] ?? "short", options, locale) : /*@end-formats*/formatDecimal(number, locale, options);
    }
    /*@formats*/case "currency": return formatCurrency(value.type === "int64" ? decimalFromInteger(value.carrier) : value.carrier, options, locale, value.format.dynamicCode);
    case "unit": return formatUnit(value.type === "int64" ? decimalFromInteger(value.carrier) : value.carrier, options, locale, value.format.dynamicCode);
    /*@end-formats*/    case "date": return formatTemporal(value.carrier, fn, options.style ?? "iso", locale, "local");
    case "time": return formatTemporal(value.carrier, fn, options.style ?? "iso", locale, "local");
    case "datetime": return formatTemporal(value.carrier, fn, options.style ?? "iso", locale, options.timeZone ?? "local");
    case "runic:uuid": { const canonical = value.carrier; const style = options.style ?? "d"; if (style === "d") return canonical; if (style === "n") return canonical.replaceAll("-", ""); if (style === "b") return `{${canonical}}`; return `(${canonical})`; }
    case "runic:relative-time": return formatRelative(value.type === "int64" ? decimalFromInteger(value.carrier) : value.carrier, options.unit ?? "day", options.numeric ?? "always", contentLocale, locale);
    case "runic:list": return formatList(value.carrier, options.type ?? "conjunction", options.style ?? "long", contentLocale, isolateItems);
    default: throw new TypeError("Unknown formatter.");
  }
}

function decimalFromInteger(value) { return Object.freeze({ coefficient: value < 0n ? -value : value, scale: 0, negative: value < 0n }); }

// The digits :number displays before localization: percent multiplies by 100,
// halfExpand rounds to the maximum and trailing zeros stay down to the minimum.
// Plural, ordinal and exact selection use this same visible decimal.
function visibleNumber(value, options) {
  const percent = (options.style ?? "decimal") === "percent";
  const minimum = Number(options.minimumFractionDigits ?? 0n), maximum = Number(options.maximumFractionDigits ?? (percent ? 4n : 6n));
  let coefficient = value.coefficient, scale = value.scale - (percent ? 2 : 0);
  if (scale < 0) { coefficient *= 10n ** BigInt(-scale); scale = 0; }
  if (scale > maximum) { const divisor = 10n ** BigInt(scale - maximum); const quotient = coefficient / divisor, remainder = coefficient % divisor; coefficient = quotient + (remainder * 2n >= divisor ? 1n : 0n); scale = maximum; }
  while (scale > minimum && coefficient % 10n === 0n) { coefficient /= 10n; scale--; }
  while (scale < minimum) { coefficient *= 10n; scale++; }
  return Object.freeze({ coefficient, scale, negative: value.negative && coefficient !== 0n });
}

// :number, :integer and percent output from the pinned CLDR number formats, as .NET's Rmf2NumberFormats: the visible
// decimal's digits, grouped by the pattern's group sizes and the pinned minimumGroupingDigits, with the locale's
// decimal, group and minus symbols and its digits.
function formatDecimal(value, locale, options) {
  const visible = visibleNumber(value, options), percent = (options.style ?? "decimal") === "percent", format = numberFormat(locale);
  const digitText = numberDigits(visible, options.useGrouping ?? "auto", locale, format, percent);
  if (!percent) return `${visible.negative ? format.minus : ""}${digitText}`;
  return visible.negative ? format.negativePercentPrefix + digitText + format.negativePercentSuffix : format.percentPrefix + digitText + format.percentSuffix;
}

// An integer as :number writes it without fraction digits, for every useGrouping value (useGrouping=never included):
// the locale's minus sign, symbols, group sizes and digits.
function formatInteger(value, locale, grouping) {
  const format = numberFormat(locale), visible = decimalFromInteger(value);
  return `${visible.negative ? format.minus : ""}${numberDigits(visible, grouping, locale, format, false)}`;
}

// The unsigned digits with the decimal separator and, where useGrouping groups, the group separator.
function numberDigits(visible, grouping, locale, format, percent) {
  const digits = visible.coefficient.toString().padStart(visible.scale + 1, "0");
  const whole = digits.slice(0, digits.length - visible.scale), fraction = digits.slice(digits.length - visible.scale);
  const primary = percent ? format.percentPrimary : format.primary, secondary = percent ? format.percentSecondary : format.secondary;
  return localizeDigits(`${groupDigits(whole, grouping, format.group, primary, secondary, locale, format)}${fraction ? format.decimal + fraction : ""}`, format);
}

// The pinned CLDR number formats of a formatting locale: its default numbering system, or Latin digits for
// -u-nu-latn; CLDR root formats without data. Each record holds the default system, its 12 fields and, when it is not
// latn, latn's 12 (see eng/generate-cldr.mjs numberFields).
const rootNumberFormat = Object.freeze({ system: "latn", digits: null, decimal: ".", group: ",", minus: "-", primary: 3, secondary: 3, percentPrimary: 3, percentSecondary: 3, percentPrefix: "", percentSuffix: "%", negativePercentPrefix: "-", negativePercentSuffix: "%" });
// Cached by CLDR data ID, in the default system and in latn, so the caches are bounded by the embedded data whatever
// tags callers request; the requested tags seen last map to them, emptied when full. The number records of the message's
// own data (a locale artifact's formatData.numbers, which the artifact decoders install) come first.
const numberFormatCache = new Map(), latinNumberFormatCache = new Map(), localeNumberFormatCache = new Map();
let ownNumbers;
function numberFormat(locale) {
  const own = activeFormatData?.numbers === undefined ? undefined : ownNumbers?.(locale, activeFormatData.numbers);
  if (own !== undefined) return own;
  let format = localeNumberFormatCache.get(locale);
  if (format) return format;
  if (localeNumberFormatCache.size >= 256) localeNumberFormatCache.clear();
  format = dataNumberFormat(locale);
  localeNumberFormatCache.set(locale, format);
  return format;
}
function dataNumberFormat(locale) {
  const id = cldrLocale(locale).numbers;
  if (id === undefined) return rootNumberFormat;
  const latin = latinDigits(locale), cache = latin ? latinNumberFormatCache : numberFormatCache;
  let format = cache.get(id);
  if (format) return format;
  format = numberRecordFormat(generatedLocaleData.numbers[id], latin);
  cache.set(id, format);
  return format;
}
// A number record: the default system and its 12 fields, then latn's 12 when it is not latn. minimumGrouping is set for
// a message's own records (Rmf2NumberFormat.FromRecord); the catalog's comes from the locale's grouping entry.
function numberRecordFormat(record, latin, minimumGrouping) {
  const offset = record.length > 13 && latin ? 13 : 1, system = offset === 1 ? record[0] : "latn", field = index => record[offset + index];
  return Object.freeze({
    system, digits: system === "latn" ? null : Array.from(field(0)), decimal: field(1), group: field(2), minus: field(3),
    primary: Number(field(4)), secondary: Number(field(5)), percentPrimary: Number(field(6)), percentSecondary: Number(field(7)),
    percentPrefix: field(8), percentSuffix: field(9), negativePercentPrefix: field(10), negativePercentSuffix: field(11), minimumGrouping,
  });
}
// A locale artifact's number records: an overlay that adds a language formats with that language's symbols, digits and
// grouping on a catalog without them. Its CLDR likely subtags, parents and aliases resolve the tag. Cached per records
// object, for the requested tags seen last and by record, so the caches go away with the artifact.
const ownNumberCaches = new WeakMap();
function artifactNumberFormat(locale, numbers) {
  let cache = ownNumberCaches.get(numbers);
  if (!cache) ownNumberCaches.set(numbers, cache = { tables: formatDataTables(numbers), locales: new Map(), formats: new Map() });
  if (cache.locales.has(locale)) return cache.locales.get(locale);
  let format;
  const canonical = canonicalLocale(locale), records = numbers.locales ?? {}, grouping = numbers.grouping ?? {};
  if (canonical !== null) {
    const id = cldrDataId(withoutExtensions(canonical), cache.tables), recordId = cldrFindFrom(id, records, false, cache.tables);
    if (recordId !== undefined) {
      const groupingId = cldrFindFrom(id, grouping, false, cache.tables), latin = latinDigits(canonical), minimum = groupingId === undefined ? 1 : grouping[groupingId];
      const key = `${recordId}|${latin}|${minimum}`;
      format = cache.formats.get(key) ?? numberRecordFormat(records[recordId], latin, minimum);
      cache.formats.set(key, format);
    }
  }
  if (cache.locales.size >= 256) cache.locales.clear();
  cache.locales.set(locale, format);
  return format;
}
// The catalog's CLDR resolution tables with a locale artifact's for its languages.
function formatDataTables(numbers) {
  const merge = name => Object.assign(Object.create(null), generatedLocaleData[name], numbers[name] ?? {});
  return { likely: merge("likely"), parents: merge("parents"), aliases: merge("aliases") };
}
// Writes the ASCII digits of a text in the numbering system's digits.
function localizeDigits(text, format) { return format.digits === null ? text : text.replace(/[0-9]/g, digit => format.digits[digit.charCodeAt(0) - 48]); }

// useGrouping as ICU and Intl.NumberFormat apply it: auto groups once the digits above the primary group
// reach the pinned CLDR minimumGroupingDigits of the locale, min2 requires at least two, always one.
function groupDigits(digits, grouping, separator, primary, secondary, locale, format) {
  if (grouping === "never" || primary <= 0 || !separator) return digits;
  const pinned = format.minimumGrouping ?? cldrLocale(locale).grouping;
  const minimum = grouping === "always" ? 1 : grouping === "min2" ? Math.max(2, pinned) : pinned;
  if (digits.length - primary < minimum) return digits;
  let end = digits.length - primary; const groups = [digits.slice(end)];
  while (end > 0) { const start = Math.max(0, end - secondary); groups.unshift(digits.slice(start, end)); end = start; }
  return groups.join(separator);
}

// The number selection compares: :number uses its displayed digits; :integer and
// relative time use the canonical typed value. The carrier itself never changes. A compact :number selects on its
// compact-rounded value at full scale (1.2K as 1200), rounded as it displays in formatLocale, with the compact exponent
// as CLDR's e operand.
function selectionOperand(resolved, formatLocale) {
  const value = resolved.type === "int64" ? decimalFromInteger(resolved.carrier) : resolved.carrier;
  /*@formats*/if (resolved.format.function === "number" && resolved.format.options["runic:notation"] === "compact") return compactOperand(value, resolved.format.options, formatLocale);
  /*@end-formats*/return { visible: resolved.format.function === "number" ? visibleNumber(value, resolved.format.options) : value, exponent: 0 };
}

function visibleCanonical(value) {
  let coefficient = value.coefficient, scale = value.scale;
  while (scale > 0 && coefficient % 10n === 0n) { coefficient /= 10n; scale--; }
  return decimalCanonical({ coefficient, scale, negative: value.negative && coefficient !== 0n });
}

// Applies the pinned CLDR rules of a locale to the operands of a visible decimal (sign ignored).
function selectPlural(value, locale, ordinal, exponent = 0) {
  const entry = cldrLocale(locale), rule = ordinal ? entry.ordinal : entry.cardinal;
  if (rule < 0) throw new RangeError(`Plural selection is not supported for locale '${locale}'.`);
  const divisor = 10n ** BigInt(value.scale), f = value.coefficient % divisor;
  let t = f, w = BigInt(value.scale);
  while (w > 0n && t % 10n === 0n) { t /= 10n; w--; }
  return generatedPluralRules[rule](Object.freeze({ i: value.coefficient / divisor, v: BigInt(value.scale), w, f, t, e: BigInt(exponent) }));
}

// CLDR relative time: the number is formatted as :number formats it by default, in numberLocale's digits and symbols
// (the formatting locale), and picks locale's past or future pattern of its plural category; numeric=auto uses the
// relative-type text for whole values from -2 to 2.
// Each unit has 17 slots: past patterns (zero … other), future patterns likewise, then relative-type -2 … 2.
const relativeUnits = ["second", "minute", "hour", "day", "week", "month", "year"];
const pluralCategoryOrder = ["zero", "one", "two", "few", "many", "other"];
function formatRelative(value, unit, numeric, locale, numberLocale = locale) {
  const entry = cldrLocale(locale), data = entry.cardinal < 0 || entry.relativeTime === undefined ? undefined : generatedLocaleData.relativeTime[entry.relativeTime];
  if (!data) throw new RangeError(`Relative-time formatting is not supported for locale '${locale}'.`);
  const offset = relativeUnits.indexOf(unit) * 17, divisor = 10n ** BigInt(value.scale);
  if (numeric === "auto" && value.coefficient % divisor === 0n && value.coefficient / divisor <= 2n) {
    const whole = Number(value.coefficient / divisor) * (value.negative ? -1 : 1), text = data[offset + 14 + whole];
    if (text !== null) return text;
  }
  const absolute = decimalAbsolute(value), visible = visibleNumber(absolute, {});
  const patterns = offset + (value.negative ? 0 : 6), pattern = data[patterns + pluralCategoryOrder.indexOf(selectPlural(visible, locale, false))] ?? data[patterns + 5];
  return pattern.replace("{0}", formatDecimal(absolute, numberLocale, {}));
}

// A locale artifact's formatData (a dynamic artifact's, an overlay's) applies while its messages format
// (formatCompiledMessage): its number records first, and its currency, unit and compact data, else the catalog's.
let activeFormatData;
const messageFormatData = new WeakMap();

// The rules of embedded format data, as .NET's Rmf2FormatData.Parse checks them (rmf2-execution-v2.md, "Embedded format
// data"), so a crafted pack fails as RTR0023/malformed in both runtimes and never while it formats: closed members,
// bounded tables, records whose first and last values are present, and patterns with exactly their placeholders.
const formatObject = (value, maximum) => isRecord(value) && Object.keys(value).length <= maximum;
const formatArray = (value, minimum, maximum) => Array.isArray(value) && value.length >= minimum && value.length <= maximum;
const formatText = value => typeof value === "string" && value.length > 0 && value.length <= 128;
const formatRecord = (value, minimum, maximum) => formatArray(value, minimum, maximum) && value.every(item => item === null || formatText(item)) && value[0] !== null && value[value.length - 1] !== null;
const formatCode = value => /^[A-Z]{3}$/.test(value);
const formatUnitId = value => value.length <= 64 && /^[a-z0-9]+(?:-[a-z0-9]+)*$/.test(value);
const formatSystem = value => typeof value === "string" && /^[a-z]{4,8}$/.test(value);
const formatDataId = value => value.length <= 64 && /^[a-z]{2,3}(?:-[A-Z][a-z]{3})?(?:-(?:[A-Z]{2}|[0-9]{3}))?(?:-[a-z0-9]{4,8})*$/.test(value);
const formatTag = value => value.length >= 2 && value.length <= 64 && /^[A-Za-z0-9]{1,8}(?:-[A-Za-z0-9]{1,8})*$/.test(value);
const formatPlaceholders = (pattern, zero, one) => pattern.split("{0}").length - 1 === zero && pattern.split("{1}").length - 1 === one;
// An artifact's formatData, checked and frozen; undefined without one, null when it breaks the rules. Its number records
// apply from now on (numberFormat).
function artifactFormatData(snapshot) {
  if (!Object.hasOwn(snapshot, "formatData")) return undefined;
  if (!validFormatData(snapshot.formatData)) return null;
  ownNumbers = artifactNumberFormat;
  return freezeTrusted(snapshot.formatData);
}
function validFormatData(value) {
  const formatters = ["currencies", "dynamicCurrencies", "dynamicUnits", "locales"];
  if (!formatObject(value, 5) || Object.keys(value).some(name => name !== "numbers" && !formatters.includes(name))) return false;
  if (Object.hasOwn(value, "numbers") && !validFormatNumbers(value.numbers)) return false;
  return !formatters.some(name => Object.hasOwn(value, name)) || validFormatters(value);
}
// numbers: the number records and minimumGroupingDigits of the artifact's languages, and the likely subtags, parents and
// aliases that resolve their tags.
function validFormatNumbers(numbers) {
  if (!formatObject(numbers, 5)) return false;
  for (const [name, table] of Object.entries(numbers)) {
    const entries = name === "locales" || name === "grouping" ? formatObject(table, 2048) && Object.entries(table) : ["likely", "parents", "aliases"].includes(name) ? formatObject(table, 4096) && Object.entries(table) : false;
    if (!entries) return false;
    for (const [key, item] of entries) {
      const valid = name === "locales" ? formatDataId(key) && validNumberRecord(item)
        : name === "grouping" ? formatDataId(key) && Number.isInteger(item) && item >= 1 && item <= 4
        : formatTag(key) && formatText(item) && formatTag(item);
      if (!valid) return false;
    }
  }
  return true;
}
// [system, then 12 fields for it and, when it is not latn, 12 for latn]: digits, decimal, group, minus, four group sizes
// and the four percent affixes.
function validNumberRecord(record) {
  if (!Array.isArray(record) || (record.length !== 13 && record.length !== 25) || !record.every(item => typeof item === "string")) return false;
  if (!formatSystem(record[0]) || (record[0] === "latn") !== (record.length === 13)) return false;
  for (let block = 1; block < record.length; block += 12) {
    const digits = record[block];
    // Ten digits of one width: all in the Basic Multilingual Plane, or all surrogate pairs (Adlam, Chakma).
    if (block === 13 || record[0] === "latn" ? digits !== "0123456789" : !(digits.length === 10 && !/[\uD800-\uDFFF]/.test(digits) || /^(?:[\uD800-\uDBFF][\uDC00-\uDFFF]){10}$/.test(digits))) return false;
    for (let field = 1; field <= 3; field++) if (record[block + field].length < 1 || record[block + field].length > 8) return false;
    for (let field = 4; field <= 7; field++) if (!/^[1-9]$/.test(record[block + field])) return false;
    for (let field = 8; field <= 11; field++) if (record[block + field].length > 16) return false;
  }
  return true;
}
// @runic-formats-begin: the renderer replaces this block with stubs when the catalog uses none of these functions.
// :currency, :unit and compact notation from the CLDR data the compiler embedded for the catalog (D055.4): only the
// currencies, units and compact styles its messages use, for the CLDR locales of its languages. The .NET runtime's
// Rmf2CldrFormats applies the same steps, which follow ICU. Records: currency formats [standard, accounting, unit
// patterns other … many, currency decimal, currency group]; currencies [symbol, narrow, names other … many, pattern,
// decimal, group]; units [long, short, narrow] of [other … many, per]; compact by magnitude from 3 of [other … many, "1"].
const formatSlots = ["other", "zero", "one", "two", "few", "many"];
// The message's currency, unit and compact data: an artifact's own, else the catalog's (a stripped or hand-made artifact
// borrows them, as .NET binds a pack or overlay to its catalog).
function formatData() {
  const data = activeFormatData !== undefined && Object.hasOwn(activeFormatData, "locales") ? activeFormatData : generatedLocaleData.formats;
  if (data === undefined) throw new RangeError("The catalog embeds no CLDR currency, unit or compact data.");
  return data;
}
// The first entry along CLDR's parent chain of the formatting locale's data ID, resolved with the artifact's CLDR tables
// for its languages when it brings them.
const formatTableCache = new WeakMap();
function formatEntry(locale, section, key, select = value => value) {
  const locales = formatData().locales, numbers = activeFormatData?.numbers;
  let tables = generatedLocaleData;
  if (numbers !== undefined) { tables = formatTableCache.get(numbers); if (!tables) formatTableCache.set(numbers, tables = formatDataTables(numbers)); }
  for (let id = cldrDataId(withoutExtensions(locale), tables), depth = 0; id !== undefined && depth < 16; id = cldrParent(id, false, tables), depth++) {
    const entry = Object.hasOwn(locales, id) ? locales[id] : undefined, members = entry !== undefined && Object.hasOwn(entry, section) ? entry[section] : undefined;
    const value = members !== undefined && Object.hasOwn(members, key) ? select(members[key]) : undefined;
    if (value !== undefined) return value;
  }
  return undefined;
}
function formatPlural(record, offset, category) { return record[offset + formatSlots.indexOf(category)] ?? record[offset]; }
function formatCategory(visible, locale) { return cldrLocale(locale).cardinal < 0 ? "other" : selectPlural(visible, locale, false); }
function formatSubstitute(pattern, zero, one) { return pattern.replace(/\{([01])\}/g, (_, index) => index === "0" ? zero : one); }
// A number pattern's subpatterns as [prefix, suffix, primary, secondary]; an implicit negative puts "-" first. Cached per
// data object (validFormatters has checked the pattern), so the cache goes away with an artifact.
const formatPatternCaches = new WeakMap();
function formatPattern(pattern) {
  const data = formatData();
  let cache = formatPatternCaches.get(data);
  if (!cache) formatPatternCaches.set(data, cache = new Map());
  let parsed = cache.get(pattern);
  if (parsed) return parsed;
  const split = formatUnquoted(pattern, ";");
  const sub = text => {
    let first = -1, last = -1, quoted = false;
    for (let index = 0; index < text.length; index++) { if (text[index] === "'") { quoted = !quoted; continue; } if (!quoted && "#0,.".includes(text[index])) { if (first < 0) first = index; last = index; } }
    const groups = text.slice(first, last + 1).split(".")[0].split(",");
    const primary = groups.length > 1 ? groups[groups.length - 1].length : 0, secondary = groups.length > 2 ? groups[groups.length - 2].length : primary;
    return [text.slice(0, first), text.slice(last + 1), primary, secondary];
  };
  const positive = sub(split < 0 ? pattern : pattern.slice(0, split));
  parsed = [positive, split < 0 ? ["-" + positive[0], positive[1], positive[2], positive[3]] : sub(pattern.slice(split + 1))];
  cache.set(pattern, parsed);
  return parsed;
}
function formatUnquoted(text, value) { let quoted = false; for (let index = 0; index < text.length; index++) { if (text[index] === "'") quoted = !quoted; else if (!quoted && text[index] === value) return index; } return -1; }
// An affix without its quotes ('' is a quote) and with "-" as the locale's minus sign; "¤" stays.
function formatAffix(affix, minus) { return affix.replace(/'([^']*)'|-/g, (match, quoted) => quoted === undefined ? minus : quoted === "" ? "'" : quoted); }
function formatDigitText(visible, grouping, locale, format, primary, secondary, decimal, group) {
  const digits = visible.coefficient.toString().padStart(visible.scale + 1, "0");
  const whole = digits.slice(0, digits.length - visible.scale), fraction = digits.slice(digits.length - visible.scale);
  return localizeDigits(`${groupDigits(whole, grouping, group, primary, secondary, locale, format)}${fraction ? decimal + fraction : ""}`, format);
}
function formatRounded(value, minimum, maximum) { return visibleNumber(value, { minimumFractionDigits: BigInt(minimum), maximumFractionDigits: BigInt(maximum) }); }

function formatCurrency(value, options, locale, dynamic) {
  const data = formatData(), code = options.currency, display = options.currencyDisplay ?? "symbol", grouping = options.useGrouping ?? "auto";
  if (dynamic && !(data.dynamicCurrencies ?? []).includes(code)) throw new RangeError(`Currency '${code}' is not in the catalog's runic.json formats.currencies.`);
  const cldrDigits = Object.hasOwn(data.currencies, code) ? data.currencies[code] : undefined;
  if (cldrDigits === undefined) throw new RangeError(`The catalog does not embed currency '${code}'.`);
  const digits = options.fractionDigits === undefined ? cldrDigits : Number(options.fractionDigits);
  const visible = formatRounded(value, digits, digits), format = numberFormat(locale);
  const formats = formatEntry(locale, "currencyFormats", format.system), currency = formatEntry(locale, "currencies", code);
  if (!formats || !currency) throw new RangeError(`No data for currency '${code}' in locale '${locale}'.`);
  const decimal = currency[9] ?? formats[8] ?? format.decimal, group = currency[10] ?? formats[9] ?? format.group;
  if (display === "name") {
    const number = (visible.negative ? format.minus : "") + formatDigitText(visible, grouping, locale, format, format.primary, format.secondary, decimal, group);
    const category = formatCategory(visible, locale);
    return formatSubstitute(formatPlural(formats, 2, category), number, formatPlural(currency, 2, category));
  }
  const symbol = display === "code" ? code : display === "narrowSymbol" ? currency[1] ?? currency[0] : currency[0];
  // A currency's own pattern replaces the standard pattern for that currency.
  const [positive, negative] = formatPattern(options.currencySign === "accounting" ? formats[1] : currency[8] ?? formats[0]);
  const [prefixPattern, suffixPattern, primary, secondary] = visible.negative ? negative : positive;
  const text = formatDigitText(visible, grouping, locale, format, primary, secondary, decimal, group);
  let prefix = formatAffix(prefixPattern, format.minus), suffix = formatAffix(suffixPattern, format.minus);
  // CLDR currency spacing (root's in every locale): a no-break space between a digit and a symbol whose adjacent
  // character is neither a symbol (S) nor a separator (Z).
  const spaced = character => character !== undefined && !/[\p{S}\p{Z}]/u.test(character), digit = character => character !== undefined && /\p{Nd}/u.test(character);
  const characters = Array.from(symbol), textCharacters = Array.from(text);
  if (prefix.endsWith("¤") && spaced(characters[characters.length - 1]) && digit(textCharacters[0])) prefix += " ";
  if (suffix.startsWith("¤") && spaced(characters[0]) && digit(textCharacters[textCharacters.length - 1])) suffix = " " + suffix;
  return prefix.replaceAll("¤", symbol) + text + suffix.replaceAll("¤", symbol);
}

function formatUnit(value, options, locale, dynamic) {
  const data = formatData(), unit = options.unit, display = options.unitDisplay ?? "short", grouping = options.useGrouping ?? "auto";
  if (dynamic && !(data.dynamicUnits ?? []).includes(unit)) throw new RangeError(`Unit '${unit}' is not in the catalog's runic.json formats.units.`);
  const visible = visibleNumber(value, options);
  if (unit === "percent" && display !== "long") {
    const format = numberFormat(locale), digits = numberDigits(visible, grouping, locale, format, true);
    return visible.negative ? format.negativePercentPrefix + digits + format.negativePercentSuffix : format.percentPrefix + digits + format.percentSuffix;
  }
  const format = numberFormat(locale), number = `${visible.negative ? format.minus : ""}${numberDigits(visible, grouping, locale, format, false)}`;
  const category = formatCategory(visible, locale), width = ["long", "short", "narrow"].indexOf(display);
  // A width CLDR does not repeat is the nearest wider one's.
  const widthOf = record => { for (let index = Math.min(width, record.length - 1); index >= 0; index--) if (record[index] !== null) return record[index]; throw new RangeError("A unit record has no long width."); };
  const own = formatEntry(locale, "units", unit);
  if (own !== undefined) return formatSubstitute(formatPlural(widthOf(own), 0, category), number, "");
  const split = unit.indexOf("-per-"), numeratorRecord = formatEntry(locale, "units", unit.slice(0, split)), denominatorRecord = formatEntry(locale, "units", unit.slice(split + 5));
  if (split < 0 || !numeratorRecord || !denominatorRecord) throw new RangeError(`The catalog does not embed unit '${unit}' for locale '${locale}'.`);
  const numerator = widthOf(numeratorRecord), denominator = widthOf(denominatorRecord), text = formatSubstitute(formatPlural(numerator, 0, category), number, "");
  if (denominator[6] != null) return formatSubstitute(denominator[6], text, "");
  const per = formatEntry(locale, "per", display);
  if (per === undefined) throw new RangeError(`The catalog does not embed unit '${unit}' for locale '${locale}'.`);
  return formatSubstitute(per, text, formatSubstitute(formatPlural(denominator, 0, "one"), "", "").trim());
}

// Compact notation: the number scaled by the CLDR pattern of its magnitude and rounded, by default to whole numbers with
// at least two significant digits, else to the fraction digit options (only a minimum: at most max(3, minimum) digits,
// as Intl.NumberFormat rounds); a value that rounds up to the next magnitude takes that magnitude's pattern (999999 →
// 1M). The pattern follows the plural category of the scaled number, and useGrouping=auto groups as min2, as ICU does.
function compactRound(value, display, options, locale) {
  const format = numberFormat(locale), table = formatEntry(locale, "compact", format.system, systems => Object.hasOwn(systems, display) ? systems[display] : undefined);
  if (!table) throw new RangeError(`No compact patterns for locale '${locale}'.`);
  let coefficient = value.coefficient, scale = value.scale;
  while (scale > 0 && coefficient % 10n === 0n) { coefficient /= 10n; scale--; }
  const magnitudeOf = (c, s) => c === 0n ? 0 : c.toString().length - s - 1, magnitude = magnitudeOf(coefficient, scale);
  const largest = table.length + 2;
  const multiplier = m => {
    if (m < 3) return 0;
    const capped = Math.min(m, largest), pattern = table[capped - 3][0];
    if (pattern === "0") return 0;
    const start = formatUnquoted(pattern, "0");
    if (start < 0) return capped;
    let zeros = 0; while (pattern[start + zeros] === "0") zeros++;
    return capped - (zeros - 1);
  };
  const round = shift => {
    let c = coefficient, s = scale + shift;
    if (s < 0) { c *= 10n ** BigInt(-s); s = 0; }
    const whole = c === 0n ? 1 : c.toString().length - s;
    const minimum = options.minimumFractionDigits === undefined ? 0 : Number(options.minimumFractionDigits);
    const maximum = options.maximumFractionDigits !== undefined ? Number(options.maximumFractionDigits) : options.minimumFractionDigits !== undefined ? Math.max(3, minimum) : Math.max(0, whole >= 2 ? 0 : 2 - whole);
    if (s > maximum) { const divisor = 10n ** BigInt(s - maximum), remainder = c % divisor; c = c / divisor + (remainder * 2n >= divisor ? 1n : 0n); s = maximum; }
    while (s > minimum && c % 10n === 0n) { c /= 10n; s--; }
    while (s < minimum) { c *= 10n; s++; }
    return Object.freeze({ coefficient: c, scale: s, negative: value.negative && c !== 0n });
  };
  let exponent = multiplier(magnitude), rounded = round(exponent);
  if (rounded.coefficient !== 0n && magnitudeOf(rounded.coefficient, rounded.scale) + exponent !== magnitude) {
    const next = multiplier(magnitude + 1);
    if (next !== exponent) { exponent = next; rounded = round(exponent); }
  }
  return { rounded, exponent, shown: rounded.coefficient === 0n ? 0 : magnitudeOf(rounded.coefficient, rounded.scale) + exponent, table, format };
}
function formatCompact(value, display, options, locale) {
  const { rounded, exponent, shown, table, format } = compactRound(value, display, options, locale);
  const grouping = options.useGrouping ?? "auto";
  const number = formatDigitText(rounded, grouping === "auto" ? "min2" : grouping, locale, format, format.primary, format.secondary, format.decimal, format.group);
  const sign = rounded.negative ? format.minus : "";
  if (exponent === 0 && shown < 3) return sign + number;
  const record = table[Math.min(shown, table.length + 2) - 3];
  let pattern = formatPlural(record, 0, formatCategory(rounded, locale));
  if (rounded.coefficient === 1n && rounded.scale === 0 && record[6] != null) pattern = record[6];
  if (pattern === "0") return sign + number;
  const zeros = text => { const start = formatUnquoted(text, "0"); if (start < 0) return formatAffix(text, format.minus); let end = start; while (text[end] === "0") end++; return formatAffix(text.slice(0, start), format.minus) + number + formatAffix(text.slice(end), format.minus); };
  const split = formatUnquoted(pattern, ";");
  if (rounded.negative && split >= 0) return zeros(pattern.slice(split + 1));
  return sign + zeros(split < 0 ? pattern : pattern.slice(0, split));
}
// The number a compact :number selects on: the compact-rounded value at full scale (1.2K as 1200), with the compact
// exponent as CLDR's e (c) operand, as TR35 and ICU select a compact number (Rmf2CldrFormats.CompactOperand).
function compactOperand(value, options, locale) {
  const { rounded, exponent } = compactRound(value, options["runic:compactDisplay"] ?? "short", options, locale);
  const visible = exponent >= rounded.scale
    ? Object.freeze({ coefficient: rounded.coefficient * 10n ** BigInt(exponent - rounded.scale), scale: 0, negative: rounded.negative })
    : Object.freeze({ coefficient: rounded.coefficient, scale: rounded.scale - exponent, negative: rounded.negative });
  return { visible, exponent };
}

// The currency, unit and compact members of formatData. A catalog built without these formatters does not read them
// and rejects the messages that use them as unsupported-function (formatterFeature).
function validFormatters(value) {
  const { currencies, locales } = value;
  if (!formatObject(currencies, 256) || !formatObject(locales, 2048)) return false;
  if (Object.entries(currencies).some(([code, digits]) => !formatCode(code) || !Number.isInteger(digits) || digits < 0 || digits > 6)) return false;
  for (const name of ["dynamicCurrencies", "dynamicUnits"]) if (Object.hasOwn(value, name)) {
    const list = value[name];
    if (!formatArray(list, 0, 256) || new Set(list).size !== list.length || !list.every(item => formatText(item) && (name === "dynamicUnits" ? formatUnitId(item) : formatCode(item) && Object.hasOwn(currencies, item)))) return false;
  }
  const sections = ["currencyFormats", "currencies", "units", "per", "compact"];
  return Object.entries(locales).every(([id, entry]) => formatDataId(id) && formatObject(entry, 5) && Object.entries(entry).every(([section, members]) => sections.includes(section) &&
    formatObject(members, section === "per" ? 3 : section === "currencyFormats" || section === "compact" ? 16 : 256) && Object.entries(members).every(([key, item]) => validFormatMember(section, key, item))));
}
function validFormatMember(section, key, item) {
  const optional = (record, from, to, test) => record.slice(from, to).every(pattern => pattern === null || test(pattern));
  switch (section) {
    // [standard, accounting, unit pattern other … many, currency decimal, currency group]
    case "currencyFormats": return formatSystem(key) && formatRecord(item, 3, 10) && validCurrencyPattern(item[0]) && validCurrencyPattern(item[1]) && item[2] !== null &&
      optional(item, 2, 8, pattern => formatPlaceholders(pattern, 1, 1)) && optional(item, 8, 10, pattern => pattern.length <= 8);
    // [symbol, narrow, names other … many, pattern, decimal, group]
    case "currencies": return formatCode(key) && formatRecord(item, 3, 11) && item[2] !== null && optional(item, 8, 9, validCurrencyPattern) && optional(item, 9, 11, pattern => pattern.length <= 8);
    // [long, short, narrow] of [other … many, per]: a unit pattern has at most one {0} (CLDR names some units without the
    // number), a per-unit pattern exactly one.
    case "units": return formatUnitId(key) && formatArray(item, 1, 3) && item.every((width, index) => (width === null && index > 0) ||
      (formatRecord(width, 1, 7) && optional(width, 0, 6, pattern => pattern.split("{0}").length <= 2 && !pattern.includes("{1}")) && optional(width, 6, 7, pattern => formatPlaceholders(pattern, 1, 0))));
    case "per": return ["long", "short", "narrow"].includes(key) && formatText(item) && formatPlaceholders(item, 1, 1);
    case "compact": return formatSystem(key) && formatObject(item, 2) && Object.entries(item).every(([style, table]) => (style === "short" || style === "long") &&
      formatArray(table, 1, 32) && table.every((record, index) => formatRecord(record, 1, 7) && optional(record, 0, 7, pattern => validCompactPattern(pattern, index + 3))));
  }
  return false;
}
// The subpatterns at an unquoted ";" (one or two), each with balanced quotes; null otherwise.
function formatSubpatterns(pattern) {
  let quotes = 0, split = -1;
  for (let index = 0; index < pattern.length; index++) { if (pattern[index] === "'") quotes++; else if (pattern[index] === ";" && quotes % 2 === 0) { if (split >= 0) return null; split = index; } }
  const result = split < 0 ? [pattern] : [pattern.slice(0, split), pattern.slice(split + 1)];
  return quotes % 2 === 0 && result.every(item => (item.split("'").length - 1) % 2 === 0) ? result : null;
}
// One unquoted ¤ and one digit run per subpattern (#,##0.00: groups of # and 0, the last ending in 0, one decimal point).
function validCurrencyPattern(pattern) {
  const subpatterns = typeof pattern === "string" ? formatSubpatterns(pattern) : null;
  return subpatterns !== null && subpatterns.every(subpattern => {
    let currency = 0, first = -1, last = -1, quoted = false;
    for (let index = 0; index < subpattern.length; index++) {
      const item = subpattern[index];
      if (item === "'") { quoted = !quoted; continue; }
      if (quoted) continue;
      if (item === "¤") currency++;
      else if ("#0,.".includes(item)) { if (first >= 0 && last !== index - 1) return false; if (first < 0) first = index; last = index; }
    }
    const parts = first < 0 ? [] : subpattern.slice(first, last + 1).split(".");
    return currency === 1 && first >= 0 && parts.length <= 2 && parts[0].split(",").every(group => /^[#0]+$/.test(group)) && parts[0].endsWith("0") && (parts.length === 1 || /^[#0]*$/.test(parts[1]));
  });
}
// "0", or at most one unquoted run of zeros per subpattern, of at most magnitude − 2 zeros (a divisor of at least 10³).
function validCompactPattern(pattern, magnitude) {
  if (pattern === "0") return true;
  const subpatterns = formatSubpatterns(pattern);
  return subpatterns !== null && subpatterns.every(subpattern => {
    let runs = 0, zeros = 0, quoted = false;
    for (let index = 0; index < subpattern.length; index++) { const item = subpattern[index]; if (item === "'") { quoted = !quoted; continue; } if (quoted || item !== "0") continue; if (index === 0 || subpattern[index - 1] !== "0") runs++; zeros++; }
    return runs <= 1 && zeros <= magnitude - 2;
  });
}
// Every function and option this runtime has: none is a stripped formatter feature.
function formatterFeature() { return false; }
// @runic-formats-end

// Joins the items with the pinned CLDR list patterns, as Intl.ListFormat does: end(n-2, n-1), then
// middle(i, rest) and start(0, rest). The .NET runtime applies the same generated patterns and Spanish rule.
// isolateItems wraps each item in FSI…PDI (bidi isolation); the Spanish and Hebrew rules still look at the raw item.
function formatList(items, type, style, locale, isolateItems = false) {
  const entry = cldrLocale(locale), data = entry.list === undefined ? undefined : generatedLocaleData.list[entry.list];
  if (!data) throw new RangeError(`List formatting is not supported for locale '${locale}'.`);
  const offset = (["conjunction", "disjunction", "unit"].indexOf(type) * 3 + ["long", "short", "narrow"].indexOf(style)) * 4;
  const [start, middle, end, two] = data.slice(offset, offset + 4), language = entry.language;
  const item = index => isolateItems ? `\u2068${items[index]}\u2069` : items[index];
  if (items.length === 0) return "";
  if (items.length === 1) return item(0);
  const last = items[items.length - 1];
  if (items.length === 2) return applyListPattern(contextualListPattern(language, two, end, two, last), item(0), item(1));
  let text = applyListPattern(contextualListPattern(language, two, end, end, last), item(items.length - 2), item(items.length - 1));
  for (let index = items.length - 3; index >= 1; index--) text = applyListPattern(middle, item(index), text);
  return applyListPattern(start, item(0), text);
}

// Every pinned pattern has {0} before {1}.
function applyListPattern(pattern, first, second) {
  const zero = pattern.indexOf("{0}"), one = pattern.indexOf("{1}");
  return pattern.slice(0, zero) + first + pattern.slice(zero + 3, one) + second + pattern.slice(one + 3);
}

// ICU, and therefore Intl.ListFormat, changes Spanish "y" to "e" before an /i/ sound
// and "o" to "u" before an /o/ sound. The rule applies to the two-item and end
// patterns of a pattern set that uses "{0} y {1}" (or else "{0} o {1}"), and tests the last item.
// Hebrew "ו" takes a hyphen ("ו-") before an item that does not start with a Hebrew character.
function contextualListPattern(language, two, end, pattern, last) {
  if (language === "he" || language === "iw") return pattern === "{0} \u05d5{1}" && last.length > 0 && !/^[\u0591-\u05f4\ufb1d-\ufb4f]/.test(last) ? "{0} \u05d5-{1}" : pattern;
  if (language !== "es") return pattern;
  if (two === "{0} y {1}" || end === "{0} y {1}") return pattern === "{0} y {1}" && /^(?:[iI]|[hH][iI](?![aAeE]))/.test(last) ? "{0} e {1}" : pattern;
  if (two === "{0} o {1}" || end === "{0} o {1}") return pattern === "{0} o {1}" && /^(?:[oO8]|[hH][oO]|11(?: |$))/.test(last) ? "{0} u {1}" : pattern;
  return pattern;
}

// Locale identity over the pinned CLDR tables, exactly as CldrLocales.cs does in .NET: likely subtags, the
// CLDR data ID of a tag, its parent chains and likely-subtag matching. Only the tables of the catalog's
// languages are embedded; matching and lookup never need others.
// The requested tags seen last, emptied when full so that any tags callers send leave room for the next ones; the data
// entries are cached by their CLDR anchors (cldrAnchors), which the embedded data bounds, so a tag outside the first
// cache costs its canonicalization and anchors, never the full lookups.
const cldrLocaleCache = new Map(), cldrEntryCache = new Map();
const noLocaleData = Object.freeze({ language: "", cardinal: -1, ordinal: -1, grouping: 1, numbers: undefined, list: undefined, relativeTime: undefined });
function cldrLocale(locale) {
  let entry = cldrLocaleCache.get(locale);
  if (entry) return entry;
  const canonical = canonicalLocale(locale);
  if (canonical === null) entry = noLocaleData;
  else {
    const [data, truncated] = cldrAnchors(canonical), key = `${data ?? ""}|${truncated ?? ""}`;
    let found = cldrEntryCache.get(key);
    if (found === undefined) {
      const cardinalId = cldrFindFrom(truncated, generatedLocaleData.cardinal, true), ordinalId = cldrFindFrom(truncated, generatedLocaleData.ordinal, true), groupingId = cldrFindFrom(data, generatedLocaleData.grouping, false);
      const list = cldrFindFrom(data, generatedLocaleData.list, false), relativeTime = cldrFindFrom(data, generatedLocaleData.relativeTime, false);
      // A locale with CLDR data but no plural rules of its own (agq, rw) uses CLDR's root rule (0, other only).
      const rootRule = list !== undefined || relativeTime !== undefined ? 0 : -1;
      found = Object.freeze({
        cardinal: cardinalId === undefined ? rootRule : generatedLocaleData.cardinal[cardinalId],
        ordinal: cardinalId === undefined ? rootRule : ordinalId === undefined ? 0 : generatedLocaleData.ordinal[ordinalId],
        grouping: groupingId === undefined ? 1 : generatedLocaleData.grouping[groupingId],
        numbers: cldrFindFrom(data, generatedLocaleData.numbers, false),
        list,
        relativeTime,
      });
      cldrEntryCache.set(key, found);
    }
    entry = Object.freeze({ language: cldrParse(canonical).language, ...found });
  }
  if (cldrLocaleCache.size >= 256) cldrLocaleCache.clear();
  cldrLocaleCache.set(locale, entry);
  return entry;
}
function cldrParse(canonical) {
  const parts = canonical.split("-"); let index = 1, script, region, variants = "";
  if (parts[0].length <= 3) for (let count = 0; count < 3 && index < parts.length && parts[index].length === 3 && /^[a-z]/i.test(parts[index]); count++) index++;
  if (index < parts.length && parts[index].length === 4 && /^[a-z]/i.test(parts[index])) script = parts[index++];
  if (index < parts.length && (parts[index].length === 2 || (parts[index].length === 3 && /^\d/.test(parts[index])))) region = parts[index++];
  while (index < parts.length && (parts[index].length >= 5 || (parts[index].length === 4 && /^\d/.test(parts[index])))) variants += "-" + parts[index++];
  return { language: parts[0], script, region, variants };
}
function cldrFormat(tag) { return tag.language + (tag.script === undefined ? "" : "-" + tag.script) + (tag.region === undefined ? "" : "-" + tag.region) + tag.variants; }
function cldrTable(table, key) { return Object.hasOwn(table, key) ? table[key] : undefined; }
// A deprecated or legacy language code becomes its replacement, whose script and region fill those the tag lacks:
// iw → he, tl → fil, sh → sr-Latn, cnr → sr-ME.
// tables: the catalog's CLDR resolution tables, or those with a locale artifact's (formatDataTables).
function cldrCanonicalize(tag, tables = generatedLocaleData) {
  const replacement = cldrTable(tables.aliases, tag.language);
  if (replacement === undefined) return tag;
  const found = cldrParse(replacement);
  return { ...tag, language: found.language, script: tag.script ?? found.script, region: tag.region ?? found.region };
}
function cldrMaximize(tag, tables = generatedLocaleData) {
  tag = cldrCanonicalize(tag, tables);
  const likely = tables.likely;
  let value;
  if (tag.script !== undefined && tag.region !== undefined) value = cldrTable(likely, `${tag.language}-${tag.script}-${tag.region}`);
  if (value === undefined && tag.region !== undefined) value = cldrTable(likely, `${tag.language}-${tag.region}`);
  if (value === undefined && tag.script !== undefined) value = cldrTable(likely, `${tag.language}-${tag.script}`);
  value ??= cldrTable(likely, tag.language);
  if (value === undefined) return tag;
  const found = cldrParse(value);
  return { ...tag, script: tag.script ?? found.script, region: tag.region ?? found.region };
}
function cldrDataId(canonical, tables = generatedLocaleData) {
  const tag = cldrCanonicalize(cldrParse(canonical), tables);
  const script = tag.script ?? cldrMaximize(tag, tables).script, likely = cldrTable(tables.likely, tag.language);
  const defaultScript = likely === undefined ? undefined : cldrParse(likely).script;
  return cldrFormat({ ...tag, script: script !== undefined && script === defaultScript ? undefined : script });
}
function cldrParent(id, truncate, tables = generatedLocaleData) {
  const explicit = truncate ? undefined : cldrTable(tables.parents, id);
  if (explicit !== undefined) return explicit === "root" ? undefined : explicit;
  const tag = cldrParse(id);
  if (tag.variants) return cldrFormat({ ...tag, variants: tag.variants.slice(0, tag.variants.lastIndexOf("-")) });
  if (tag.region !== undefined) return cldrFormat({ ...tag, region: undefined });
  if (tag.script !== undefined && truncate) return tag.language;
  return undefined;
}
function cldrFindFrom(id, table, truncate, tables = generatedLocaleData) {
  for (let depth = 0; id !== undefined && depth < 16; id = cldrParent(id, truncate, tables), depth++) if (Object.hasOwn(table, id)) return id;
  return undefined;
}
// Where the data lookups of a tag start, as CldrLocales.Anchors in .NET: the first ID of its data ID's parent chain, and
// of its truncation chain, that an embedded table or the parent table has. de-AA, de-AB, … and de share de's.
let cldrKnownIds;
function cldrAnchors(canonical) {
  if (cldrKnownIds === undefined) {
    cldrKnownIds = {};
    for (const table of [generatedLocaleData.parents, generatedLocaleData.cardinal, generatedLocaleData.ordinal, generatedLocaleData.grouping, generatedLocaleData.numbers, generatedLocaleData.list, generatedLocaleData.relativeTime])
      for (const id of Object.keys(table)) cldrKnownIds[id] = true;
  }
  const id = cldrDataId(canonical);
  return [cldrFindFrom(id, cldrKnownIds, false), cldrFindFrom(id, cldrKnownIds, true)];
}
function cldrChain(id) { const result = []; for (let current = id; current !== undefined && result.length < 16; current = cldrParent(current, false)) result.push(current); return result; }
// A language and its bare-language CLDR parent (nb, nn → no) match after every same-language candidate (1000); two
// languages with the same parent, nb and nn, after those (2000); -1: not related.
function cldrRelatedPenalty(first, second) {
  const firstParent = cldrParent(first, false), secondParent = cldrParent(second, false);
  if (firstParent === second || secondParent === first) return 1000;
  return firstParent !== undefined && firstParent === secondParent ? 2000 : -1;
}
// Both sides maximized must share language and script. Equal maximized tags match best (the one nearest in the
// desired tag's CLDR chain first), then the nearest CLDR
// ancestor of the desired tag that a locale shares (the ancestor itself first), then a sibling region (the likely
// region first), then a related language (cldrRelatedPenalty). The undetermined language only matches itself.
// Ties keep the ordinal order of the declared locales.
function cldrMatch(desired, available) {
  if (available.includes(desired)) return desired;
  const wanted = cldrMaximize(cldrParse(desired));
  if (wanted.language === "und") return undefined;
  const chain = cldrChain(cldrDataId(desired));
  let best, bestScore = Infinity;
  for (const locale of available) {
    const candidate = cldrMaximize(cldrParse(locale));
    const penalty = candidate.language === wanted.language ? 0 : cldrRelatedPenalty(wanted.language, candidate.language);
    if (penalty < 0) continue;
    if (candidate.script !== wanted.script) continue;
    let score;
    if (candidate.region === wanted.region) {
      // Equal maximized tags: the nearest in the desired tag's CLDR chain first (en-US-posix → en-US before en).
      const step = chain.indexOf(cldrDataId(locale));
      score = step < 0 ? 16 : step;
    } else {
      // The nearest ancestor of the desired tag that the candidate shares; the ancestor itself before its other descendants.
      score = -1; const candidateId = cldrDataId(locale), candidateChain = cldrChain(candidateId);
      for (let step = 1; step < chain.length && score < 0; step++) {
        if (!candidateChain.includes(chain[step])) continue;
        const ancestor = cldrMaximize(cldrParse(chain[step]));
        const itself = candidateId === chain[step] || (ancestor.language === candidate.language && ancestor.script === candidate.script && ancestor.region === candidate.region);
        score = 20 + 2 * step + (itself ? 0 : 1);
      }
      if (score < 0) score = candidate.region === cldrMaximize({ language: wanted.language, script: wanted.script, region: undefined, variants: "" }).region ? 100 : 101;
    }
    score += penalty;
    if (score < bestScore) { best = locale; bestScore = score; }
  }
  return best;
}

// Localized dates and times from Intl, by the rules the .NET runtime applies to platform data: a date in its CLDR
// length with a Gregorian year of at least four digits (so short dates show the full year); a wall-clock time in the
// short or else the medium CLDR length, which has no zone name; a date-time as its date and time in the time zone,
// joined by a space. Intl writes Latin digits, which are then written in the locale's pinned CLDR numbering system, as
// numbers are, and U+202F is written as a space, as V8 writes it.
function formatTemporal(value, type, style, locale, timeZone) {
  if (style === "iso") return value;
  if (type === "date") return formatWallDate(Number(value.slice(0, 4)), Number(value.slice(5, 7)), Number(value.slice(8, 10)), style, locale);
  if (type === "time") return formatWallTime(Number(value.slice(0, 2)), Number(value.slice(3, 5)), Number(value.slice(6, 8)), style, locale);
  const wall = wallClock(new Date(value), timeZone);
  return `${formatWallDate(wall.year, wall.month, wall.day, style, locale)} ${formatWallTime(wall.hour, wall.minute, wall.second, style, locale)}`;
}

const temporalFormatCache = new Map();
function temporalFormat(locale, kind, style, timeZone) {
  const cacheKey = `${locale}\u0000${kind}\u0000${style}\u0000${timeZone ?? ""}`;
  let format = temporalFormatCache.get(cacheKey);
  if (format) return format;
  const options = kind === "year" ? { year: "numeric", calendar: style, timeZone: "UTC", numberingSystem: "latn" }
    : kind === "date" ? { dateStyle: style, timeZone: "UTC", numberingSystem: "latn" }
    : kind === "time" ? { timeStyle: style, timeZone: "UTC", numberingSystem: "latn" }
    : { calendar: "gregory", numberingSystem: "latn", hourCycle: "h23", year: "numeric", month: "numeric", day: "numeric", hour: "numeric", minute: "numeric", second: "numeric", ...(timeZone === undefined ? {} : { timeZone }) };
  format = new Intl.DateTimeFormat(kind === "zone" ? "en-US" : intlLocales(locale), options);
  // Emptied when full, so that the tags callers send never leave the cache full of others.
  if (temporalFormatCache.size >= 256) temporalFormatCache.clear();
  temporalFormatCache.set(cacheKey, format);
  return format;
}

// The locale, then its CLDR parents, then en-US: an engine without data for the locale (Intl data covers fewer locales
// than CLDR) uses the nearest parent it has (ht → fr-HT) and else en-US, never the host's default locale, so server and
// browser agree.
function intlLocales(locale) {
  const canonical = canonicalLocale(locale); if (canonical === null) return [locale, "en-US"];
  return [canonical, ...cldrChain(cldrDataId(canonical)).slice(1), "en-US"];
}

function utcDate(year, month, day, hour = 0, minute = 0, second = 0) { const date = new Date(0); date.setUTCFullYear(year, month - 1, day); date.setUTCHours(hour, minute, second, 0); return date; }

// A shorter year field is replaced by the full year: a Gregorian year padded to four digits, as .NET's yyyy, or the
// full year of the locale's calendar (Thai Buddhist 2569 rather than 69).
function formatWallDate(year, month, day, style, locale) {
  const format = temporalFormat(locale, "date", style), calendar = format.resolvedOptions().calendar, date = utcDate(year, month, day);
  return localizeDigits(temporalText(format.formatToParts(date).map(part => part.type !== "year" || !/^\d{1,3}$/.test(part.value) ? part.value
    : calendar === "gregory" ? String(year).padStart(4, "0") : fullYear(locale, calendar, date, part.value)).join("")), numberFormat(locale));
}
function fullYear(locale, calendar, date, short) {
  const parts = temporalFormat(locale, "year", calendar).formatToParts(date), year = parts.find(part => part.type === "year")?.value;
  return year !== undefined && year.length > short.length && /^\d+$/.test(year) ? year : short;
}

function formatWallTime(hour, minute, second, style, locale) {
  return localizeDigits(temporalText(temporalFormat(locale, "time", style === "short" ? "short" : "medium").format(utcDate(1970, 1, 1, hour, minute, second))), numberFormat(locale));
}

// U+202F is written as a space, as V8 writes it, and a run of one bidirectional mark (U+200E, U+200F, U+061C) once.
function temporalText(value) { return value.replaceAll("\u202F", " ").replace(/([\u200E\u200F\u061C])\1+/g, "$1"); }

// The wall-clock fields of an instant in "local", "utc" or an IANA zone. "local" is the zone the time-zone resolver
// returns (configureTimeZoneResolver; the server entry's runWithLocale installs one for each request), else the host's
// zone, read on every call so a changed TZ or browser zone applies at once. A zone the resolver returns that Intl does
// not know is ignored, as resolvers read untrusted cookies and headers. An unknown timeZone option throws a RangeError
// naming the zone, as .NET throws a TranslationFormatException, and so does a wall-clock year outside 1..9999.
function wallClock(instant, timeZone) {
  const zone = timeZone === "local" ? localTimeZone() : timeZone === "utc" ? "UTC" : timeZone;
  let format;
  try { format = temporalFormat("en-US", "zone", "", zone); }
  catch (error) { if (error instanceof RangeError) throw new RangeError(`The time zone '${timeZone}' is not available. It is unknown to this JavaScript engine's time zone data.`); throw error; }
  const fields = Object.create(null);
  for (const part of format.formatToParts(instant)) if (part.type !== "literal") fields[part.type] = part.value;
  const year = fields.era === "BC" ? 1 - Number(fields.year) : Number(fields.year);
  if (!(year >= 1 && year <= 9999)) throw new RangeError("The date-time is outside the supported range in its time zone.");
  return { year, month: Number(fields.month), day: Number(fields.day), hour: Number(fields.hour) % 24, minute: Number(fields.minute), second: Number(fields.second) };
}

let timeZoneResolver = null;
export function configureTimeZoneResolver(resolver) {
  if (typeof resolver !== "function") throw new TypeError("The time zone resolver must be a function.");
  const previous = timeZoneResolver; timeZoneResolver = resolver;
  return () => { if (timeZoneResolver === resolver) timeZoneResolver = previous; };
}
// The zone "local" means now: the resolver's zone when it names one Intl knows, else the host's (undefined when the
// engine does not report it).
function localTimeZone() {
  // This catalog's resolver, else the zone of the request that runWithLocale (of any catalog) runs.
  const resolved = timeZoneResolver === null ? requestStore()?.timeZone : timeZoneResolver();
  if (resolved !== undefined && resolved !== null) {
    if (resolved === "utc" || resolved === "UTC") return "UTC";
    if (validTimeZone(resolved)) { try { temporalFormat("en-US", "zone", "", resolved); return resolved; } catch { /* Unknown: use the host's zone. */ } }
  }
  try { return new Intl.DateTimeFormat().resolvedOptions().timeZone ?? undefined; } catch { return undefined; }
}

function validTimeZone(value) { return typeof value === "string" && value.length > 0 && value.length <= 64 && /^[A-Za-z][A-Za-z0-9_+-]*(?:\/[A-Za-z0-9_+-]+)*$/.test(value); }

// A generated message function: messages holds the message's compiled text by catalog locale. The requested tag (de-CH)
// chooses the formatting locale of numbers and dates, the catalog locale (de) the text. With overlays installed, the text is
// the first of each chain locale's overlay text and the catalog's own text (locale-first).
export function formatMessage(key, messages, inputs, options) {
  const requested = options?.locale ?? getRequestedLocale(), locale = resolveLocale(requested);
  let wrapper = messages[locale];
  if (overlays !== null) { const found = overlays.find(key, locale, step => messages[step]?.contentLocale === step); wrapper = typeof found === "string" ? messages[found] : found; }
  return formatCompiledMessage(key, wrapper, inputs, requested, locale);
}

// requestedLocale is the tag the caller asked for (de-CH), resolvedLocale the catalog locale it resolved to (de),
// which defaults to the requested one; the wrapper's content locale differs from it after a fallback.
export function formatCompiledMessage(key, wrapper, inputs, requestedLocale, resolvedLocale = requestedLocale) {
  const data = wrapper ? messageFormatData.get(wrapper) : undefined;
  if (data === undefined) return formatWrapper(key, wrapper, inputs, requestedLocale, resolvedLocale);
  const previous = activeFormatData; activeFormatData = data;
  try { return formatWrapper(key, wrapper, inputs, requestedLocale, resolvedLocale); }
  finally { activeFormatData = previous; }
}

function formatWrapper(key, wrapper, inputs, requestedLocale, resolvedLocale) {
  if (!wrapper) throw new RangeError(`Unknown message '${key}'.`);
  const contract = validatedMessages.get(wrapper); if (!contract || contract.key !== key) throw new TypeError("A validated v5 message is required.");
  const context = contextFor(wrapper, inputs, contract), locale = wrapper.contentLocale, formatLocale = formattingLocale(requestedLocale, locale);
  const selections = wrapper.ast.selectors.map(selector => {
    const resolved = context.resolve(selector.value, selector.type);
    if (!["int64", "decimal"].includes(selector.type)) return { resolved, canonical: canonicalCarrier(resolved.carrier, selector.type), category: null };
    // Exact keys and CLDR categories compare the same visible decimal.
    const { visible, exponent } = selectionOperand(resolved, formatLocale);
    return { resolved, canonical: visibleCanonical(visible), category: selector.function === "exact" ? null : selectPlural(visible, locale, selector.function === "ordinal", exponent) };
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
  const built = buildNodes(selected.nodes, context, formatLocale, locale, `v${selectedIndex}.`, bidiIsolationFor(requestedLocale, resolvedLocale, locale));
  if (typeof resolvedLocale === "string") reportFallback(key, resolvedLocale, locale);
  if (!contract.structured) { if (built.some(node => node.kind !== "text")) throw new TypeError("Structured content cannot be formatted as text."); return built.map(node => node.value).join(""); }
  const kind = contract.content === "document" ? "localized-document" : "localized-content";
  return Object.freeze({ kind, key, locale, formatLocale, nodes: Object.freeze(built) });
}

function buildNodes(nodes, context, locale, contentLocale, path, isolation = false) {
  const messageRtl = rightToLeftTag(contentLocale);
  const roots = [], stack = [{ children: roots }];
  for (let index = 0; index < nodes.length; index++) {
    const node = nodes[index], parent = stack.at(-1).children;
    if (node.kind === "text") parent.push({ kind: "text", value: node.value, annotations: [] });
    else if (node.kind === "expression") { const value = context.expression(node.expression); parent.push({ kind: "text", value: isolate(formatResolved(value, locale, contentLocale, isolation), value, messageRtl, locale, isolation), annotations: annotations(node.expression.annotations) }); }
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

// A message's contract: its key, inputs and whether it is structured, plus the content profile, slots and document
// skeletons that validation needs. Each generated message module passes its own contract's data to the freeze functions,
// so a bundle holds only the contracts of the messages it calls; dynamic.js carries the catalog's whole table. The
// runtime keeps one frozen copy per data object and registers nothing by key: renderers check content against the
// catalog's own table (structuredMessage), so no contract made at run time changes how other content renders.
function contractOf(value) {
  let contract = isRecord(value) ? contractCopies.get(value) : undefined; if (contract) return contract;
  if (!isRecord(value) || typeof value.key !== "string" || value.inputs !== undefined && !Array.isArray(value.inputs)) throw new TypeError("A generated v5 message contract is required.");
  contract = Object.freeze({ key: value.key, structured: value.structured === true, inputs: freezeTrusted(value.inputs ?? []),
    content: value.content ?? "inline", slots: freezeTrusted(value.slots ?? {}), skeletons: freezeTrusted(value.skeletons ?? []),
    contentLocales: value.contentLocales === undefined ? undefined : freezeTrusted(value.contentLocales) });
  contractCopies.set(value, contract); return contract;
}
// The content kind and slots of each structured message, from the catalog's table above (structuredMessageTable), parsed
// on first use. Renderers read only this, so content formatted in another process (server load data, postMessage,
// stored content) renders without its message module, and a bundle that never renders content drops the table.
function structuredMessage(key) {
  structuredMessages ??= freezeTrusted(structuredMessageTable());
  return typeof key === "string" && Object.hasOwn(structuredMessages, key) ? structuredMessages[key] : undefined;
}

// Compact generated messages. A message of text and plain input placeholders is written as its parts, text strings and
// [inputName] for {$inputName}; these rebuild the same normalized AST, which freezeGeneratedMessage validates as before.
export function freezeGeneratedText(contract, contentLocale, text) { return freezeGeneratedPattern(contract, contentLocale, text === "" ? [] : [text]); }
export function freezeGeneratedPattern(value, contentLocale, parts) {
  const contract = contractOf(value), key = contract.key, inputs = contract.inputs;
  const nodes = parts.map(part => {
    if (typeof part === "string") return { kind: "text", value: part };
    const input = inputs.find(item => item.name === part[0]);
    if (!input) throw new TypeError(`Invalid generated RMF2 v5 message: ${key} has no input ${part[0]}.`);
    return { kind: "expression", expression: { operand: { kind: "input", value: input.name }, valueType: input.type, options: [], annotations: [] } };
  });
  return freezeGeneratedMessage(contract, { contentLocale, ast: { astVersion: 5, profile: "rmf2-execution-v2",
    inputs: inputs.map(item => ({ name: item.name, type: item.type })), declarations: [], selectors: [], variants: [{ keys: [], nodes }] } });
}

export function freezeGeneratedMessage(data, value) {
  const contract = contractOf(data), error = validateMessageWrapper(value, contract, null, contract.key);
  if (error) throw new TypeError(`Invalid generated RMF2 v5 message: ${error}.`);
  const result=cloneFreeze(value);validatedMessages.set(result,contract);return result;
}

// The catalog's whole contract tables come from dynamic.js, so only applications that decode artifacts carry them.
function dynamicContract(tables, key) {
  let contracts = dynamicContracts.get(tables); if (!contracts) dynamicContracts.set(tables, contracts = new Map());
  let contract = contracts.get(key);
  if (!contract) { const markup = tables.rmf2Contract.messages?.[key]; contract = contractOf({ ...markup, key, structured: tables.messageContracts[key].structured, inputs: tables.messageContracts[key].inputs, contentLocales: markup?.contentLocales ?? {} }); contracts.set(key, contract); }
  return contract;
}

export function decodeLocaleArtifactV5(value, tables) {
  if (!isRecord(tables?.rmf2Contract) || !isRecord(tables.messageContracts)) throw new TypeError("The catalog's contract tables are required; import decodeLocaleArtifact from dynamic.js.");
  const captured = snapshotJson(value, limits.maximumDepth, limits.maximumDocumentBytes);
  if (!captured.ok) return failure(`RTR0023/${captured.reason}`);
  const snapshot = captured.value;
  const envelope = validateEnvelope(snapshot, tables.rmf2Contract); if (envelope) return failure(envelope);
  const entries = [];
  for (const [key, message] of Object.entries(snapshot.messages)) {
    if (!Object.hasOwn(tables.messageContracts, key)) return failure("RTR0023/unknown-key");
    const contract = dynamicContract(tables, key);
    const error = validateMessageWrapper(message, contract, snapshot.locale, key); if (error) return failure(`RTR0023/${error}`);
    validatedMessages.set(message, contract); entries.push([key,message]);
  }
  // formatData: the CLDR data of the artifact's messages: number records when they format numbers, and currency, unit and
  // compact data when they use those functions.
  const formats = artifactFormatData(snapshot); if (formats === null) return failure("RTR0023/malformed");
  const artifact=Object.freeze({ artifactVersion: 5, messageGrammarVersion: 5, profile, catalog, locale: snapshot.locale, contractFingerprint, messages: freezeOwnRecord(entries), markupContract: tables.rmf2Contract,
    ...(formats === undefined ? {} : { formatData: formats }) });decodedArtifacts.add(artifact);
  if (formats !== undefined) for (const message of Object.values(artifact.messages)) messageFormatData.set(message, formats);
  return Object.freeze({ ok: true, value: artifact });
}

export async function decodeLocalePackV5(content, expectedLocale, integrityVerifier, tables) {
  if (!isRecord(tables?.rmf2Contract) || !isRecord(tables.messageContracts)) throw new TypeError("The catalog's contract tables are required; import decodeLocalePack from dynamic.js.");
  const read = await readLocalePack(content, integrityVerifier); if (!read.ok) return read;
  const result = decodeLocaleArtifactV5(read.value, tables); if (!result.ok) return result;
  return result.value.locale === expectedLocale ? result : failure("RTR0023/locale-mismatch");
}

// Untrusted pack bytes to a JSON value: size, the integrity callback on its own copy, depth, UTF-8 and duplicate members.
async function readLocalePack(content, integrityVerifier) {
  if (!(content instanceof Uint8Array) || content.byteLength === 0) return failure("RTR0023/malformed");
  if (content.byteLength > limits.maximumDocumentBytes) return failure("RTR0023/limit-exceeded");
  const snapshot = new Uint8Array(content);
  if (integrityVerifier) { try { if (!await integrityVerifier(new Uint8Array(snapshot))) return failure("RTR0023/integrity-rejected"); } catch { return failure("RTR0023/integrity-rejected"); } }
  if (!withinJsonDepth(snapshot, limits.maximumDepth)) return failure("RTR0023/limit-exceeded");
  try { const text = new TextDecoder("utf-8", { fatal: true }).decode(snapshot); if (hasDuplicateJsonProperties(text)) return failure("RTR0023/malformed"); return success(JSON.parse(text)); } catch { return failure("RTR0023/malformed"); }
}

export function formatDynamicMessageV5(artifact, key, inputs = Object.create(null), options) {
  if (!artifact || !decodedArtifacts.has(artifact)) throw new TypeError("A validated v5 locale artifact is required.");
  if (!Object.hasOwn(artifact.messages, key)) throw new RangeError(`Locale artifact has no message '${key}'.`);
  const requested = options?.locale === undefined ? artifact.locale : requestedTag(options.locale);
  if (resolveLocale(requested) !== artifact.locale) throw new RangeError("A dynamic locale artifact can only format its own locale.");
  return formatCompiledMessage(key, artifact.messages[key], inputs, requested, artifact.locale);
}

// Overlays: application-owned messages for this catalog, such as an application's text for a library's keys, a locale the
// library lacks, or a pack downloaded at run time (dynamic.js installOverlay and installLocalePack). As in .NET's
// TranslationOverlay, an artifact is checked key by key against the catalog's contract and not by its fingerprint, so a pack
// built for another release of the catalog keeps the keys both releases share. A key the catalog no longer has, or whose
// inputs, content kind or markup differ, is skipped with a diagnostic and the catalog's text applies. Precedence is .NET's
// OverlayTranslationProvider's, with each install as one overlay: for each locale of the requested locale's chain, the
// installs in installation order (the first that has the key wins), then the catalog, and only then the next locale. An
// install's fallback copies (messages whose content locale is not its locale) count at their content locale, only for a
// locale an overlay adds and only from an install whose locale is on the chain. A locale the catalog lacks is added; it falls
// back to the locale its install names, else to its nearest CLDR parent that the catalog declares or an overlay adds, else
// to the catalog locale CLDR likely subtags match, else to the base locale, and from the first catalog locale on the
// catalog's chain applies.
function overlayState(tables) {
  if (overlays !== null) return overlays;
  const entries = [], listeners = new Set(), cache = new Map(), added = new Map(), fallbacks = tables.localeFallbacks;
  // The requested locale, its added parents and the catalog locale they fall back to, then the catalog's chain.
  const chain = locale => {
    const steps = []; let current = locale;
    while (added.has(current)) { steps.push(current); current = added.get(current); if (steps.includes(current)) { current = baseLocale; break; } }
    for (; typeof current === "string" && !steps.includes(current); current = Object.hasOwn(fallbacks, current) ? fallbacks[current] : null) steps.push(current);
    return steps;
  };
  overlays = {
    entries, listeners, added,
    // The catalog's locales and the added ones, in ordinal order.
    supported: locales,
    // The overlay message for key at locale, or the catalog locale whose own text applies; owns(step) tells whether the
    // catalog has its own text for the key at a catalog locale.
    find(key, locale, owns) {
      const id = locale + "\u0000" + key; if (cache.has(id)) return cache.get(id);
      const steps = chain(locale); let found = baseLocale;
      search: for (const step of steps) {
        const declared = localeSet.has(step);
        for (const entry of entries) {
          const message = entry.locale === step ? entry.own.get(key) : declared || !steps.includes(entry.locale) ? undefined : entry.copies.get(step)?.get(key);
          if (message !== undefined) { found = message; break search; }
        }
        if (declared && owns(step)) { found = step; break; }
      }
      cache.set(id, found); return found;
    },
    changed() {
      added.clear(); cache.clear();
      // The first explicit fallback of an added locale applies; installs that name none accept it.
      const explicit = new Map();
      for (const entry of entries) if (!localeSet.has(entry.locale) && (explicit.get(entry.locale) ?? null) === null) explicit.set(entry.locale, entry.fallback);
      const supported = this.supported = Object.freeze([...locales, ...explicit.keys()].sort((left, right) => left < right ? -1 : left > right ? 1 : 0));
      for (const [locale, fallback] of explicit)
        added.set(locale, fallback ?? nearestParent(locale, supported) ?? matchLocale(locale, locales) ?? baseLocale);
      for (const listener of [...listeners]) { try { listener(); } catch { /* The overlays are installed. */ } }
    },
  };
  return overlays;
}

// The nearest parent of a canonical tag among candidates (canonical tags in ordinal order): the nearest of its CLDR parent
// locales (es-419 for es-MX, de for de-CH), else the nearest truncation with its likely script. Every result is a strict
// ancestor, so a chain of nearest parents ends. LocaleRequestResolver.NearestParent in .NET.
function nearestParent(canonical, candidates) {
  const steps = cldrChain(cldrDataId(canonical)); let best, bestStep = Infinity;
  for (const candidate of candidates) {
    if (candidate === canonical) continue;
    const step = steps.indexOf(cldrDataId(candidate), 1);
    if (step > 0 && step < bestStep) { best = candidate; bestStep = step; }
  }
  return best ?? truncateLocale(canonical, candidates);
}

export function installOverlayV5(value, tables, options) {
  if (!isRecord(tables?.rmf2Contract) || !isRecord(tables.messageContracts) || !isRecord(tables.localeFallbacks)) throw new TypeError("The catalog's contract tables are required; import installOverlay from dynamic.js.");
  if (options !== undefined && !isRecord(options)) throw new TypeError("Overlay options must be an object.");
  const fallback = options?.fallbackLocale ?? null;
  if (fallback !== null && (typeof fallback !== "string" || !localeSet.has(fallback))) throw new RangeError(`An added locale must fall back to a locale the catalog declares, not '${fallback}'.`);
  const captured = snapshotJson(value, limits.maximumDepth, limits.maximumDocumentBytes);
  if (!captured.ok) return failure(`RTR0023/${captured.reason}`);
  const snapshot = captured.value, envelope = validateOverlayEnvelope(snapshot); if (envelope) return failure(envelope);
  // formatData: the CLDR data of the overlay's messages (number records, currency, unit and compact data).
  const formats = artifactFormatData(snapshot); if (formats === null) return failure("RTR0023/malformed");
  const locale = snapshot.locale, declared = localeSet.has(locale);
  if (!declared && fallback === locale) throw new RangeError("A locale cannot fall back to itself.");
  const state = overlayState(tables);
  // A locale the catalog declares keeps the catalog's chain, so a library release that adds the locale keeps the overlay working.
  if (!declared && fallback !== null) for (const entry of state.entries)
    if (entry.locale === locale && entry.fallback !== null && entry.fallback !== fallback) throw new RangeError(`Locale '${locale}' is already added with fallback '${entry.fallback}'.`);
  const own = new Map(), copies = new Map(), diagnostics = [], markup = snapshot.markupContract.messages;
  for (const key of Object.keys(snapshot.messages)) {
    const message = snapshot.messages[key];
    if (!isRecord(message) || typeof message.contentLocale !== "string" || canonicalLocale(message.contentLocale) !== message.contentLocale || !isRecord(message.ast)) return failure("RTR0023/malformed-pattern");
    // The artifact's own contract states each message's content locale and kind.
    const linked = isRecord(markup) && Object.hasOwn(markup, key) && isRecord(markup[key]) ? markup[key] : undefined;
    if (!linked || !isRecord(linked.contentLocales) || linked.contentLocales[locale] !== message.contentLocale) return failure("RTR0023/argument-contract-mismatch");
    const copy = message.contentLocale !== locale;
    // A fallback copy (a build with relaxed completeness) is not application text for a locale the catalog declares.
    if (copy && localeSet.has(message.contentLocale)) continue;
    const report = reason => { diagnostics.push(Object.freeze({ catalog, locale: message.contentLocale, key, reason })); };
    if (!Object.hasOwn(tables.messageContracts, key)) { report("unknown-key"); continue; }
    const contract = dynamicContract(tables, key), inputs = orderedInputs(message.ast.inputs, contract.inputs);
    if (inputs === null) { report("incompatible-inputs"); continue; }
    if ((linked.content ?? "inline") !== contract.content) { report("content-kind-changed"); continue; }
    const wrapper = { contentLocale: message.contentLocale, ast: { ...message.ast, inputs } };
    const error = validateMessageWrapper(wrapper, contract, null, key, tag => tag === message.contentLocale);
    if (error === "argument-contract-mismatch" || error === "document-structure-mismatch") { report("incompatible-markup"); continue; }
    // A function or option this catalog was built without (:currency, :unit, compact notation).
    if (error === "unsupported-function") { report("unsupported-function"); continue; }
    if (error) return failure(`RTR0023/${error}`);
    // Generated functions format a message without markup as a string, so an overlay cannot add markup to it.
    if (!contract.structured && wrapper.ast.variants.some(variant => variant.nodes.some(node => node.kind === "markup"))) { report("incompatible-markup"); continue; }
    // This runtime embeds the plural, list, relative-time and number data of the catalog's languages only; the overlay's
    // formatData brings the number records of its own.
    if (!localeSet.has(message.contentLocale) && !hasLocaleData(message.contentLocale, wrapper.ast, formats)) { report("unsupported-locale-data"); continue; }
    const result = cloneFreeze(wrapper); validatedMessages.set(result, contract);
    if (formats !== undefined) messageFormatData.set(result, formats);
    if (!copy) own.set(key, result);
    else { let table = copies.get(message.contentLocale); if (!table) copies.set(message.contentLocale, table = new Map()); table.set(key, result); }
  }
  const entry = Object.freeze({ locale, fallback: declared ? null : fallback, own, copies });
  state.entries.push(entry); state.changed();
  let installed = true;
  const remove = () => { if (!installed) return; installed = false; state.entries.splice(state.entries.indexOf(entry), 1); state.changed(); };
  return success(Object.freeze({ catalog, locale, contractFingerprint: snapshot.contractFingerprint, messages: own.size, diagnostics: Object.freeze(diagnostics), remove }));
}

export async function installLocalePackV5(content, tables, options) {
  if (options !== undefined && !isRecord(options)) throw new TypeError("Pack options must be an object.");
  const { locale, integrityVerifier, fallbackLocale } = options ?? {};
  // The locale the pack was requested for, as decodeLocalePack's expected locale: a pack from a misrouted or replaced URL
  // can then neither replace another locale's text nor add a locale.
  if (typeof locale !== "string" || canonicalLocale(locale) !== locale) throw new TypeError("installLocalePack needs options.locale, the canonical locale the pack was requested for.");
  if (integrityVerifier !== undefined && typeof integrityVerifier !== "function") throw new TypeError("The integrity verifier must be a function.");
  const read = await readLocalePack(content, integrityVerifier); if (!read.ok) return read;
  if (!isRecord(read.value) || read.value.locale !== locale) return failure("RTR0023/locale-mismatch");
  return installOverlayV5(read.value, tables, fallbackLocale === undefined ? undefined : { fallbackLocale });
}

export function onOverlayChangeV5(listener, tables) {
  if (typeof listener !== "function") throw new TypeError("An overlay listener must be a function.");
  const state = overlayState(tables), entry = () => listener(); state.listeners.add(entry);
  return () => { state.listeners.delete(entry); };
}

// A locale artifact v5 of this catalog from any release: the fingerprint is read, not compared, and the locale may be one
// the catalog lacks. Its markup contract must be version 2; each message is checked against this catalog's contract.
function validateOverlayEnvelope(value) {
  const members = ["artifactVersion","messageGrammarVersion","profile","catalog","locale","contractFingerprint","messages","markupContract"];
  if (!isRecord(value)) return "RTR0023/malformed";
  if (Object.keys(value).some(name => !members.includes(name) && name !== "formatData")) return "RTR0023/unknown-member";
  if (!members.every(name => Object.hasOwn(value, name))) return "RTR0023/malformed";
  if (value.artifactVersion !== 5) return "RTR0023/artifact-version-mismatch";
  if (value.messageGrammarVersion !== 5 || value.profile !== profile) return "RTR0023/message-grammar-version-mismatch";
  if (!isRecord(value.markupContract) || value.markupContract.version !== 2) return "RTR0023/markup-contract-version-mismatch";
  if (value.catalog !== catalog) return "RTR0023/catalog-mismatch";
  if (typeof value.contractFingerprint !== "string" || !/^sha256:[0-9a-f]{64}$/.test(value.contractFingerprint)) return "RTR0023/malformed";
  if (typeof value.locale !== "string" || canonicalLocale(value.locale) !== value.locale) return "RTR0023/locale-mismatch";
  if (!isRecord(value.messages)) return "RTR0023/malformed"; if (Object.keys(value.messages).length > limits.maximumMessages) return "RTR0023/limit-exceeded";
  return null;
}

// The message's inputs in the contract's order, or null when their names or types differ from the contract's.
function orderedInputs(inputs, expected) {
  if (!Array.isArray(inputs) || inputs.length !== expected.length) return null;
  const ordered = [];
  for (const input of expected) { const match = inputs.find(item => isRecord(item) && item.name === input.name); if (!match || match.type !== input.type) return null; ordered.push(match); }
  return ordered;
}

// Whether this runtime has the CLDR data a message needs in a locale the catalog does not declare, found as formatting
// finds it (cldrLocale: the locale's CLDR data ID, its parents and likely subtags), so an added de-AT uses the embedded de
// rules and an added pt-PT its own rules when they differ from pt's.
// Numbers, dates and times need the language's number records (the catalog's or the overlay's formatData), and currency
// names, units and compact numbers its plural rules.
function hasLocaleData(locale, ast, formats) {
  const functions = new Set();
  let numbers = false, categories = false;
  const collect = expression => {
    if (!isRecord(expression)) return;
    const fn = typeof expression.function === "string" ? expression.function : defaultFunction(expression.valueType);
    const option = name => expression.options.find(item => item.name === name)?.value, literal = (name, value) => { const found = option(name); return found !== undefined && (found.kind !== "string-literal" || found.value === value); };
    functions.add(fn);
    numbers ||= ["integer", "number", "currency", "unit", "runic:relative-time", "date", "time", "datetime"].includes(fn);
    categories ||= fn === "unit" || (fn === "currency" && literal("currencyDisplay", "name")) || (fn === "number" && literal("runic:notation", "compact"));
  };
  for (const declaration of ast.declarations) collect(declaration.expression);
  for (const variant of ast.variants) for (const node of variant.nodes) if (node.kind === "expression") collect(node.expression);
  const data = cldrLocale(locale);
  if (numbers && data.numbers === undefined && (formats?.numbers === undefined || artifactNumberFormat(locale, formats.numbers) === undefined)) return false;
  const plural = ast.selectors.some(selector => selector.function !== "exact") || functions.has("runic:relative-time") || categories;
  if (plural && data.cardinal < 0) return false;
  if (functions.has("runic:relative-time") && data.relativeTime === undefined) return false;
  return !functions.has("runic:list") || data.list !== undefined;
}

export function decodeWireValue(value, type) {
  try {
    if (type === "decimal") return success(parseDecimal(value));
    if (type === "int64") { if (typeof value !== "string" || !/^-?(?:0|[1-9][0-9]*)$/.test(value)) return failure(); const parsed = BigInt(value); if (parsed < minimumInt64 || parsed > maximumInt64) return failure(); return success(parsed); }
    if (type === "boolean") return typeof value === "boolean" ? success(value) : failure();
    if (typeof value !== "string" || value.length > 16384) return failure();
    // The transport form of a date-time stays exactly UTC yyyy-MM-ddTHH:mm:ssZ; callers' wider inputs do not apply.
    if (type === "datetime") return validDateTime(value) ? success(value) : failure();
    return success(coerceInput(value, type, "wire"));
  } catch { return failure(); }
}

function validateEnvelope(value, rmf2Contract) {
  const members=["artifactVersion","messageGrammarVersion","profile","catalog","locale","contractFingerprint","messages","markupContract"];
  if (!isRecord(value)) return "RTR0023/malformed";
  if (Object.keys(value).some(name=>!members.includes(name)&&name!=="formatData")) return "RTR0023/unknown-member";
  if (!members.every(name=>Object.hasOwn(value,name))) return "RTR0023/malformed";
  if (value.artifactVersion !== 5) return "RTR0023/artifact-version-mismatch";
  if (value.messageGrammarVersion !== 5 || value.profile !== profile) return "RTR0023/message-grammar-version-mismatch";
  if (!isRecord(value.markupContract) || value.markupContract.version !== 2) return "RTR0023/markup-contract-version-mismatch";
  if (value.catalog !== catalog) return "RTR0023/catalog-mismatch"; if (value.contractFingerprint !== contractFingerprint) return "RTR0023/contract-fingerprint-mismatch";
  if (typeof value.locale !== "string" || !localeSet.has(value.locale) || canonicalLocale(value.locale) !== value.locale) return "RTR0023/locale-mismatch";
  if (!isRecord(value.messages)) return "RTR0023/malformed"; if (Object.keys(value.messages).length > limits.maximumMessages) return "RTR0023/limit-exceeded";
  if (JSON.stringify(value.markupContract) !== JSON.stringify(rmf2Contract)) return "RTR0023/argument-contract-mismatch";
  return null;
}

// accepts tests the content locale; by default it must be one of the catalog's locales.
function validateMessageWrapper(wrapper, contract, artifactLocale, key, accepts = null) {
  if (!contract || !isRecord(wrapper)) return "malformed-pattern";
  const wrapperMembers=["contentLocale","ast"];
  if (Object.keys(wrapper).some(name=>!wrapperMembers.includes(name))) return "unknown-member";
  if (!wrapperMembers.every(name=>Object.hasOwn(wrapper,name)) || typeof wrapper.contentLocale !== "string" || !(accepts === null ? localeSet.has(wrapper.contentLocale) : accepts(wrapper.contentLocale))) return "malformed-pattern";
  if (artifactLocale !== null && contract.contentLocales?.[artifactLocale] !== wrapper.contentLocale) return "argument-contract-mismatch";
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
    const stack = [], slotCounts = Object.create(null), slotRequirements = contract.slots; let totalNodes = 0, textBytes = 0;
    for (const node of variant.nodes) {
      if (++totalNodes > 4096) return "limit-exceeded";
      if (!isRecord(node) || typeof node.kind !== "string") return "malformed-pattern";
      if (node.kind === "text") { const memberError=closedMemberError(node,["kind","value"]);if(memberError)return memberError;if (typeof node.value !== "string") return "malformed-pattern"; textBytes += new TextEncoder().encode(node.value).length; if (textBytes > limits.maximumPatternBytes) return "limit-exceeded"; }
      else if (node.kind === "expression") { const memberError=closedMemberError(node,["kind","expression"]);if(memberError)return memberError;const error = validateExpression(node.expression,symbols); if (error) return error; }
      else if (node.kind === "markup") {
        const memberError=closedMemberError(node,["kind","name","markupKind","options","annotations"]);if(memberError)return memberError==="unknown-member"?memberError:"argument-contract-mismatch";
        const annotationError=validateAnnotations(node.annotations,"argument-contract-mismatch");if(annotationError)return annotationError;
        if (!validName(node.name,true) || !["open","close","standalone"].includes(node.markupKind) || !Object.hasOwn(markupContracts,node.name)) return "argument-contract-mismatch";
        const markupError = validateMarkupNode(node,symbols,slotRequirements,slotCounts,stack); if (markupError) return markupError;
        if (node.markupKind === "open") { stack.push(node.name); if (stack.length > 16) return "limit-exceeded"; } else if (node.markupKind === "close" && stack.pop() !== node.name) return "malformed-pattern";
      }
      else return "malformed-pattern";
    }
    if (stack.length) return "malformed-pattern";
    for (const [slot,bounds] of Object.entries(slotRequirements)) if ((slotCounts[slot] ?? 0) < bounds.min || (slotCounts[slot] ?? 0) > bounds.max) return "argument-contract-mismatch";
    // Document profile v1: block rules and leaf invariants are malformed patterns; a skeleton
    // outside the locked source skeletons is a structure mismatch. Inline messages stay inline.
    if (contract.content === "document") { const skeleton = documentSkeleton(variant.nodes); if (skeleton === null) return "malformed-pattern"; if (!contract.skeletons.includes(skeleton)) return "document-structure-mismatch"; }
    else if (variant.nodes.some(node => node.kind === "markup" && markupContracts[node.name].placement !== "inline")) return "malformed-pattern";
  }
  return fallback ? null : "malformed-pattern";
}

// Checks the block rules and leaf invariants of one balanced document variant and returns its
// encoded skeleton, or null when the variant breaks a rule (document profile v1, sections 3 to 6).
function documentSkeleton(nodes) {
  const frames = [{ model: "root", first: true }]; let encoded = "", leaf = [];
  for (const node of nodes) {
    const frame = frames.at(-1), contract = node.kind === "markup" ? markupContracts[node.name] : null;
    if (frame.model === "root" || frame.model === "list") {
      if (!contract) return null;
      if (node.markupKind === "close") { frames.pop(); if (frame.model === "list") encoded += ")"; continue; }
      if (contract.placement !== (frame.model === "root" ? "block" : "list-item")) return null;
      if (!frame.first) encoded += ","; frame.first = false;
      encoded += node.name.startsWith("runic:") ? node.name.slice(6) : node.name;
      const options = skeletonOptions(node.options); if (options) encoded += `[${options}]`;
      if (contract.children === "list-items") { encoded += "("; frames.push({ model: "list", first: true }); } else { frames.push({ model: "leaf" }); leaf = []; }
    } else {
      // Inside a leaf block or its inline markup: a close in the leaf frame closes the block.
      if (contract && node.markupKind === "close") { if (frame.model === "leaf" && !normalizedLeaf(leaf)) return null; frames.pop(); continue; }
      if (contract && contract.placement !== "inline") return null;
      leaf.push(node); if (contract && node.markupKind === "open") frames.push({ model: "inline" });
    }
  }
  return frames.length === 1 ? encoded : null;
}
// The compiler's option encoding: ordinal key order, canonical literals with the skeleton metacharacters escaped, variables as $name.
function skeletonOptions(options) { return [...options].sort((left, right) => left.name < right.name ? -1 : left.name > right.name ? 1 : 0).map(option => { if (["input","local"].includes(option.value.kind)) return `${option.name}=$${option.value.value}`; const value = option.value.canonical ?? option.value.value; let text = ""; for (let index = 0; index < value.length; index++) { if ("\\[](),;=".includes(value[index]) || index === 0 && value[index] === "$") text += "\\"; text += value[index]; } return `${option.name}=${text}`; }).join(";"); }
// No CR or LF in leaf text and no space or tab at either end; paired inline tags are transparent, placeholders and standalone markup are atoms.
function normalizedLeaf(leaf) { const characters = []; for (const node of leaf) { if (node.kind === "text") { for (const character of node.value) { if (character === "\r" || character === "\n") return false; characters.push(character); } } else if (node.kind === "expression" || node.markupKind === "standalone") characters.push(null); } return !characters.length || ![" ","\t"].includes(characters[0]) && ![" ","\t"].includes(characters.at(-1)); }

function validateExpression(expression, symbols, references) {
  const memberError=closedMemberError(expression,["operand","valueType","function","options","annotations"],["operand","valueType","options","annotations"]);if(memberError)return memberError;
  if(Object.hasOwn(expression,"function")&&expression.function===undefined)return "malformed-pattern";
  const valueError=validateValue(expression.operand);if(valueError)return valueError;
  const annotationError=validateAnnotations(expression.annotations,"malformed-pattern");if(annotationError)return annotationError;
  if (!["string","int64","decimal","boolean","date","time","datetime","guid","list"].includes(expression.valueType)) return "malformed-pattern";
  if (!Array.isArray(expression.options)) return "malformed-pattern";
  if (["input","local"].includes(expression.operand.kind)) { if (!Object.hasOwn(symbols,expression.operand.value) || symbols[expression.operand.value].type !== expression.valueType) return "argument-contract-mismatch"; }
  else { try { literal(expression.operand, expression.valueType); } catch { return "malformed-pattern"; } }
  if (expression.function === undefined) return expression.options.length === 0 ? null : "malformed-pattern";
  const accepted = functionType(expression.function); if (!accepted && formatterFeature(expression.function)) return "unsupported-function";
  if (!accepted || !(accepted === expression.valueType || (expression.valueType === "int64" && accepted === "decimal"))) return "argument-contract-mismatch";
  const optionError=validateOptions(expression.options,symbols,expression.function);if(optionError)return optionError;
  if (references) for (const option of expression.options) referenceValue(option.value,references);
  return null;
}

function validateOptions(options, symbols, fn) {
  if (!Array.isArray(options) || options.length > 256) return "argument-contract-mismatch"; const names = new Set(), staticEntries=[]; let dynamic=false;
  for (const option of options) { const memberError=closedMemberError(option,["name","value"]);if(memberError)return memberError==="unknown-member"?memberError:"argument-contract-mismatch";const valueError=validateValue(option.value);if(valueError)return ["unknown-member","malformed"].includes(valueError)?valueError:"argument-contract-mismatch";if(!validName(option.name,true)||names.has(option.name))return "argument-contract-mismatch";names.add(option.name); if (fn) { const type = optionType(fn,option.name); if (!type) return formatterFeature(fn,option.name) ? "unsupported-function" : "argument-contract-mismatch"; if (["input","local"].includes(option.value.kind)) { if (option.name === "select" || option.name === "u:dir" || !Object.hasOwn(symbols,option.value.value) || symbols[option.value.value].type !== type || !symbols[option.value.value].explicitDependencies) return "argument-contract-mismatch"; dynamic=true; } else { try { staticEntries.push([option.name,literal(option.value,type)]); } catch { return "argument-contract-mismatch"; } } } }
  /*@formats*/if((fn==="currency"||fn==="unit")&&!names.has(fn))return "argument-contract-mismatch";
  /*@end-formats*/if(fn){try{const resolved=freezeOwnRecord(staticEntries);validateResolvedOptions(fn,resolved);if(dynamic&&(fn==="number"/*@formats*/||fn==="unit"/*@end-formats*/)){const style=resolved.style,min=resolved.minimumFractionDigits??0n,max=resolved.maximumFractionDigits;if(style==="percent"&&min>4n||max!==undefined&&min>max)return "argument-contract-mismatch";}}catch{return "argument-contract-mismatch";}}
  return null;
}
function validateMarkupNode(node,symbols,requirements,counts,stack) {
  const contract=markupContracts[node.name],functional=["runic:link","runic:action","runic:icon"].includes(node.name);
  if(!Array.isArray(node.options))return "argument-contract-mismatch";
  if(node.markupKind==="close") return node.options.length===0?null:"argument-contract-mismatch";
  if((contract.kind==="standalone")!==(node.markupKind==="standalone"))return "argument-contract-mismatch";
  if(contract.interactive&&stack.some(name=>markupContracts[name]?.interactive))return "argument-contract-mismatch";
  if(node.options.length>256)return "limit-exceeded";
  const values=Object.create(null);
  for(const option of node.options){const memberError=closedMemberError(option,["name","value"]);if(memberError)return memberError==="unknown-member"?memberError:"argument-contract-mismatch";const valueError=validateValue(option.value);if(valueError)return ["unknown-member","malformed"].includes(valueError)?valueError:"argument-contract-mismatch";if(!validName(option.name)||Object.hasOwn(values,option.name))return "argument-contract-mismatch";values[option.name]=option.value;}
  if(functional){const reference=values.ref;if(!reference||reference.kind!=="string-literal"||!Object.hasOwn(requirements,reference.value)||requirements[reference.value].kind!==node.name)return "argument-contract-mismatch";counts[reference.value]=(counts[reference.value]??0)+1;}
  const expected=Object.keys(contract.options);
  if(expected.some(name=>!Object.hasOwn(values,name))||Object.keys(values).some(name=>name!=="ref"&&!Object.hasOwn(contract.options,name)))return "argument-contract-mismatch";
  for(const name of expected){const schema=contract.options[name],value=values[name];if(["input","local"].includes(value.kind)){if(schema.literalOnly||!Object.hasOwn(symbols,value.value)||!markupTypeAccepts(schema.type,symbols[value.value].type))return "argument-contract-mismatch";}else if(!markupLiteral(schema,value))return "argument-contract-mismatch";}
  return null;
}
function markupTypeAccepts(schema,type){return schema==="number"?["int64","decimal"].includes(type):schema==="integer"?type==="int64":schema==="boolean"?type==="boolean":type==="string";}
function markupLiteral(schema,value){try{if(schema.type==="number")return value.kind==="number-literal"&&decimalCanonical(parseDecimal(value.value))===value.canonical;if(schema.type==="integer")return (value.kind==="string-literal"||value.kind==="number-literal"&&value.canonical===value.value)&&integerOption(value.value,schema);if(value.kind!=="string-literal")return false;if(schema.type==="boolean")return ["true","false"].includes(value.value);if(schema.type==="enum")return schema.values.includes(value.value);return true;}catch{return false;}}
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
// POSIX-style tags such as de_DE name the same locale as de-DE.
function canonicalLocale(value) {
  if (typeof value!=="string") return null; value=value.replaceAll("_","-");
  if (!value||value.startsWith("-")||value.endsWith("-")) return null;
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
// The project's own tags (runic.json markup contracts) need no binding: without one they reach factory.element
// like the built-in tags, so a factory that dispatches by name (the Svelte LocalizedInline custom snippets) renders
// them. A binding still replaces factory.element for its tag.
export function createInlineRenderer(factory, bindings = []) {
  return createRendererCore(factory, bindings, true, false);
}
export function createDocumentRenderer(factory, bindings = []) {
  return createRendererCore(factory, bindings, true, true);
}
// projection: plain-text mode, where a supplied custom binding applies only to explicit and
// alternateText policies; children, omit and lineBreak keep their declared projection (as in .NET).
function createRendererCore(factory, bindings = [], allowUnboundCustom = false, documents = false, projection = false) {
  if (!factory || typeof factory.text !== "function" || typeof factory.element !== "function") throw new TypeError("An inline renderer requires text and element factories.");
  if (documents && typeof factory.block !== "function") throw new TypeError("A document renderer requires a block factory.");
  const custom = new Map();
  for (const binding of bindings) {
    if (!isRecord(binding) || !isRecord(binding.contract) || typeof binding.render !== "function") throw new TypeError("Invalid markup renderer binding.");
    const suppliedContract = binding.contract, declared = markupContracts[suppliedContract.name];
    if (!declared || suppliedContract.name.startsWith("runic:") || custom.has(suppliedContract.name) || declared.kind !== suppliedContract.kind || declared.placement !== (suppliedContract.placement ?? "inline") || declared.children !== suppliedContract.children || declared.plainText !== suppliedContract.plainText || declared.interactive !== suppliedContract.interactive) throw new TypeError("Incompatible or duplicate renderer contract.");
    const suppliedOptions = suppliedContract.options ?? Object.create(null);
    if (!isRecord(suppliedOptions) || Object.keys(declared.options).length !== Object.keys(suppliedOptions).length || Object.entries(declared.options).some(([name,schema]) => {
      const actual = suppliedOptions[name];
      return !isRecord(actual) || actual.type !== schema.type || JSON.stringify(actual.values ?? []) !== JSON.stringify(schema.values) || (actual.default ?? null) !== schema.default || !!actual.literalOnly !== schema.literalOnly || schema.type === "integer" && (actual.minimum ?? -2147483648) !== schema.minimum || schema.type === "integer" && (actual.maximum ?? 2147483647) !== schema.maximum;
    })) throw new TypeError("Renderer option contract mismatch.");
    custom.set(suppliedContract.name,binding.render);
  }
  function render(content,{slots=Object.create(null)}={}) {
    const messageContract=isRecord(content)?structuredMessage(content.key):undefined,requirements=messageContract?.slots;if(!isRecord(content)||!["localized-content","localized-document"].includes(content.kind)||!requirements||!isRecord(slots))throw new TypeError("Unknown RMF2 content contract.");
    const document=messageContract.content==="document";
    if(document!==(content.kind==="localized-document"))throw new TypeError("Unknown RMF2 content contract.");
    if(document!==documents)throw new TypeError(document?`'${content.key}' is a document message; render it with createDocumentRenderer.`:`'${content.key}' is an inline message; render it with createInlineRenderer.`);
    const validatedSlots=Object.create(null);for (const [reference,requirement] of Object.entries(requirements)) validatedSlots[reference]=validateSlotBinding(reference,requirement.kind,slots[reference]);
    const counts=Object.create(null);let count=0;
    function checkOptions(node,contract){let binding;for(const[name,value]of Object.entries(node.attributes)){if(name==="ref"&&["runic:link","runic:action","runic:icon"].includes(node.name)){binding=validatedSlots[value];if(requirements[value]?.kind!==node.name||!binding)throw new TypeError(`Invalid slot '${value}'.`);counts[value]=(counts[value]??0)+1;}else if(!contract.options[name]||!renderOption(contract.options[name],value))throw new TypeError(`Invalid option '${name}'.`);}if(["runic:link","runic:action","runic:icon"].includes(node.name)&&!binding)throw new TypeError("Missing functional slot ref.");for(const name of Object.keys(contract.options))if(!Object.hasOwn(node.attributes,name))throw new TypeError(`Missing option '${name}'.`);return binding;}
    // In documents an inline element's occurrence is <blockPath>/<index path>; inline messages keep <key>:<node occurrence>.
    function visit(nodes,interactive=false,depth=0,prefix=null){if(depth>16)throw new RangeError("Inline nesting exceeds 16 levels.");return nodes.map((node,index)=>{if(++count>4096)throw new RangeError("Inline node limit exceeded.");if(node.kind==="text")return node;const contract=markupContracts[node.name];if(node.kind!=="element"||!contract||contract.placement!=="inline"||(contract.kind==="standalone")!==node.standalone||interactive&&contract.interactive)throw new TypeError("Unknown or invalid inline markup.");const binding=checkOptions(node,contract);const occurrence=prefix===null?`${content.key}:${node.occurrence}`:prefix+index;const element={name:node.name,children:visit(node.children,interactive||contract.interactive,depth+1,prefix===null?null:occurrence+"."),options:node.attributes,binding,occurrence,locale:content.locale,standalone:node.standalone};if(!custom.has(node.name)&&!node.name.startsWith("runic:")&&!allowUnboundCustom)throw new TypeError(`No renderer linked for '${node.name}'.`);return element;});}
    function blocks(nodes,list,path,depth){const siblings=Object.create(null);return nodes.map(node=>{if(++count>4096)throw new RangeError("Document node limit exceeded.");const contract=node.kind==="element"?markupContracts[node.name]:undefined;if(!contract||node.standalone||contract.placement!==(list?"list-item":"block"))throw new TypeError("Invalid document structure.");if(depth+1>16)throw new RangeError("Document nesting exceeds 16 levels.");checkOptions(node,contract);const name=node.name.startsWith("runic:")?node.name.slice(6):node.name;siblings[name]=(siblings[name]??0)+1;const occurrence=`${path?path+"/":""}${name}[${siblings[name]}]`;return contract.children==="list-items"?{block:true,name:node.name,options:node.attributes,children:blocks(node.children,true,occurrence,depth+1),occurrence}:{block:true,name:node.name,options:node.attributes,children:visit(node.children,false,depth+1,occurrence+"/"),occurrence};});}
    const output=document?blocks(content.nodes,false,"",0):visit(content.nodes);for(const[slot,bounds]of Object.entries(requirements))if((counts[slot]??0)<bounds.min||(counts[slot]??0)>bounds.max)throw new TypeError(`Slot multiplicity mismatch for '${slot}'.`);
    function materialize(node){if(node.kind==="text")return factory.text(node.value);if(node.block)return factory.block(node.name,node.options,Object.freeze(node.children.map(materialize)),Object.freeze({occurrence:node.occurrence,locale:content.locale}));const contract=markupContracts[node.name];const bound=custom.has(node.name)&&(!projection||["explicit","alternateText"].includes(contract?.plainText));if(projection&&!bound&&!node.name.startsWith("runic:")&&["omit","lineBreak"].includes(contract?.plainText))return contract.plainText==="lineBreak"?"\n":"";const element={...node,children:node.children.map(materialize)};return bound?custom.get(node.name)(element):factory.element(element);}
    return Object.freeze(output.map(materialize));
  }
  return Object.freeze({ render, extend(extra) { return createRendererCore(factory,[...bindings,...extra],allowUnboundCustom,documents,projection); } });
}
function validateSlotBinding(reference,kind,binding){if(binding?.kind!==kind)throw new TypeError(`Missing or incompatible slot '${reference}'.`);return kind==="runic:link"?linkBinding(binding):kind==="runic:action"?actionBinding(binding):iconBinding(binding);}
// The project's own markup contracts from runic.json, by name, in the form defineMarkup returns: pass one to
// bindMarkup to replace a tag's default rendering without restating its contract.
export const projectMarkup = freezeTrusted(Object.fromEntries(Object.entries(markupContracts)
  .filter(([name]) => !name.startsWith("runic:")).map(([name, contract]) => [name, { name, ...contract }])));
export function defineMarkup(contract) { if (!isRecord(contract) || !validName(contract.name,true) || contract.name.startsWith("runic:") || !["paired","standalone"].includes(contract.kind) || contract.placement !== undefined && contract.placement !== "inline" || contract.children !== (contract.kind === "standalone" ? "none" : "inline") || typeof contract.interactive !== "boolean" || !["children","lineBreak","alternateText","explicit","omit"].includes(contract.plainText) || contract.options !== undefined && !isRecord(contract.options)) throw new TypeError("Invalid markup contract."); return cloneFreeze(contract); }
export function bindMarkup(contract,render) { if (typeof render !== "function") throw new TypeError("A markup renderer is required."); return Object.freeze({contract,render}); }
export function toPlainText(content,{slots=Object.create(null),allowActionLabels=false,annotateLinkDestinations=false,custom=[],listMarker="- "}={}) {
  if (typeof listMarker !== "string") throw new TypeError("The list marker must be a string.");
  const document = content?.kind === "localized-document";
  const renderer=createRendererCore({text:value=>value,element({name,children,binding,locale}) { if (name==="runic:br") return "\n"; if (name==="runic:action"&&!allowActionLabels) throw new TypeError("Action labels require explicit projection policy."); if (name==="runic:icon") { if (binding?.decorative) return ""; const label=binding?.accessibleName?.(locale); if (typeof label!=="string"||!label.trim()) throw new TypeError("Meaningful icon alternate text is empty."); return label; } const contract=markupContracts[name]; if(!name.startsWith("runic:")&&contract?.plainText==="lineBreak")return "\n";if(!name.startsWith("runic:")&&contract?.plainText==="omit")return "";if(!name.startsWith("runic:")&&["explicit","alternateText"].includes(contract?.plainText))throw new TypeError("Custom markup requires an explicit plain-text adapter.");const text=children.join(""); return name==="runic:link"&&annotateLinkDestinations?`${text} (${binding.href})`:text; },
    // Document projection (document profile v1): items are joined by one line break, further item lines are indented two spaces.
    block(name,options,children) { if (!["runic:ul","runic:ol"].includes(name)) return children.join(""); const start=name==="runic:ol"?Number(options.start):1; return children.map((item,index)=>(name==="runic:ol"?`${listNumber(start+index,options.marker)}. `:listMarker)+item.split("\n").map((line,lineIndex)=>lineIndex&&line?`  ${line}`:line).join("\n")).join("\n"); }},custom,true,document,true);
  // Empty blocks project to nothing and add no separator, so the text never starts or ends with a line break.
  const parts=renderer.render(content,{slots});
  return document?parts.filter(part=>part!=="").join("\n\n"):parts.join("");
}
// Fixed, locale-independent numbering: decimal, bijective base-26 letters, and roman numerals for 1 to 3999 with a decimal fallback.
function listNumber(value,marker) {
  if ((marker==="lower-alpha"||marker==="upper-alpha")&&value>0) { let letters=""; for (let rest=value; rest>0; rest=Math.floor((rest-1)/26)) letters=String.fromCharCode(97+(rest-1)%26)+letters; return marker==="upper-alpha"?letters.toUpperCase():letters; }
  if ((marker==="lower-roman"||marker==="upper-roman")&&value>=1&&value<=3999) { let roman="",rest=value; for (const [amount,symbol] of [[1000,"M"],[900,"CM"],[500,"D"],[400,"CD"],[100,"C"],[90,"XC"],[50,"L"],[40,"XL"],[10,"X"],[9,"IX"],[5,"V"],[4,"IV"],[1,"I"]]) for (; rest>=amount; rest-=amount) roman+=symbol; return marker==="lower-roman"?roman.toLowerCase():roman; }
  return String(value);
}
export function createDomInlineRenderer(document,custom=[]) { return createInlineRenderer({text:value=>document.createTextNode(value),element({name,children,binding,locale}) { if(name==="runic:icon"){const node=typeof binding.asset==="function"?binding.asset(document):binding.asset?.cloneNode?.(true);if(!node||typeof node.setAttribute!=="function")throw new TypeError("The application icon asset must create a DOM element.");if(binding.decorative){node.setAttribute("aria-hidden","true");node.removeAttribute("aria-label");}else{const label=binding.accessibleName(locale);if(typeof label!=="string"||!label.trim())throw new TypeError("Meaningful icon alternate text is empty.");node.setAttribute("role","img");node.setAttribute("aria-label",label);}return node;}const tags={"runic:strong":"strong","runic:em":"em","runic:bold":"span","runic:italic":"span","runic:code":"code","runic:br":"br","runic:link":"a","runic:action":"button"};if(!Object.hasOwn(tags,name))throw new TypeError(`No DOM renderer linked for '${name}'.`);const node=document.createElement(tags[name]);if(name==="runic:bold")node.style.fontWeight="bold";if(name==="runic:italic")node.style.fontStyle="italic";if(name==="runic:link")node.href=binding.href;if(name==="runic:action"){node.type="button";node.addEventListener("click",binding.onActivate);}node.append(...children);return node;}},custom); }
function renderOption(schema,value){if(typeof value!=="string")return false;if(schema.type==="integer")return integerOption(value,schema);if(schema.type==="enum")return schema.values.includes(value);if(schema.type==="boolean")return value==="true"||value==="false";if(schema.type==="number"){try{parseDecimal(value);return true;}catch{return false;}}return true;}
// Canonical decimal integer text within the declared bounds; identical to the compiler and .NET rule.
function integerOption(text,schema){return typeof text==="string"&&text.length<=11&&/^(?:0|-?[1-9][0-9]*)$/.test(text)&&Number(text)>=schema.minimum&&Number(text)<=schema.maximum;}
// Document profile v1 DOM adapter: semantic elements built with createElement and text nodes, never HTML strings,
// and no inline style attributes (strict CSP). Leaves carry the runic-leaf class (white-space: pre-wrap). setContent
// and clearContent retire the previous render: its actions stop calling back and its links lose their destination.
// Paragraphs and headings with no children or only empty text, and lists with no items, are skipped; occurrences keep counting them.
const domDocumentLifetimes=new WeakMap();
// pageDirection defaults, per render, to the document's <html dir> when it is ltr or rtl, else to the active locale's
// direction (getLocale(), the same source as SvelteKit's %runic.dir%), so English fallback blocks on an Arabic page get dir="ltr".
export function createDomDocumentRenderer(document,{headingBase=2,custom=[],pageDirection}={}) {
  if(!Number.isInteger(headingBase)||headingBase<1||headingBase>9)throw new RangeError("headingBase must be an integer from 1 to 9.");
  if(pageDirection!==undefined&&pageDirection!=="ltr"&&pageDirection!=="rtl")throw new RangeError("pageDirection must be \"ltr\" or \"rtl\".");
  const page=()=>{if(pageDirection!==undefined)return pageDirection;const dir=document.documentElement?.getAttribute?.("dir")?.toLowerCase();return dir==="ltr"||dir==="rtl"?dir:localeDirection(getLocale());};
  let lifetime=null;
  const empty=new WeakSet();
  const create=(tag,occurrence)=>{const node=document.createElement(tag);node.setAttribute("data-runic-occurrence",occurrence);return node;};
  const core=createDocumentRenderer({text(value){const node=document.createTextNode(value);if(value==="")empty.add(node);return node;},
    element({name,children,binding,locale,occurrence}) {
      if(name==="runic:icon"){const node=typeof binding.asset==="function"?binding.asset(document):binding.asset?.cloneNode?.(true);if(!node||typeof node.setAttribute!=="function")throw new TypeError("The application icon asset must create a DOM element.");if(binding.decorative){node.setAttribute("aria-hidden","true");node.removeAttribute("aria-label");}else{const label=binding.accessibleName(locale);if(typeof label!=="string"||!label.trim())throw new TypeError("Meaningful icon alternate text is empty.");node.setAttribute("role","img");node.setAttribute("aria-label",label);}node.setAttribute("data-runic-occurrence",occurrence);return node;}
      const tag={"runic:strong":"strong","runic:em":"em","runic:bold":"b","runic:italic":"i","runic:code":"code","runic:br":"br","runic:link":"a","runic:action":"button"}[name];
      if(!tag)throw new TypeError(`No DOM renderer linked for '${name}'.`);
      const node=create(tag,occurrence),own=lifetime;
      if(name==="runic:link"){node.setAttribute("href",binding.href);own.links.push(node);}
      if(name==="runic:action"){node.setAttribute("type","button");let activate=binding.onActivate;const listener=()=>{if(own.active)activate?.();};node.addEventListener("click",listener);own.actions.push({node,release(){activate=null;node.removeEventListener("click",listener);}});}
      node.append(...children);return node;
    },
    block(name,options,children,{occurrence,locale}) {
      const present=children.filter(child=>child!==null);
      if(name==="runic:ul"||name==="runic:ol"?present.length===0:name!=="runic:li"&&present.every(child=>empty.has(child)))return null;
      let node;
      if(name==="runic:h"){const level=headingBase+Number(options.level)-1;node=create(level<=6?"h"+level:"h6",occurrence);if(level>6){node.setAttribute("role","heading");node.setAttribute("aria-level",String(level));}}
      else if(name==="runic:ol"){node=create("ol",occurrence);if(options.start!=="1")node.setAttribute("start",options.start);const type={"lower-alpha":"a","upper-alpha":"A","lower-roman":"i","upper-roman":"I"}[options.marker];if(type)node.setAttribute("type",type);}
      else if(["runic:p","runic:ul","runic:li"].includes(name))node=create(name.slice(6),occurrence);
      else throw new TypeError(`No DOM renderer linked for block '${name}'.`);
      if(!["runic:ul","runic:ol"].includes(name))node.setAttribute("class","runic-leaf");
      // Top-level blocks carry the effective content locale, which differs from the requested one after a fallback, and its
      // direction when it differs from the page's (English fallback text in an Arabic page).
      if(!occurrence.includes("/")){node.setAttribute("lang",locale);const direction=localeDirection(locale);if(direction!==lifetime.page)node.setAttribute("dir",direction);}
      node.append(...present);return node;
    }},custom);
  // A nested render (from an icon's accessibleName or a custom binding) restores the outer render's lifetime.
  function build(content,options){const own={active:true,links:[],actions:[],page:page()},outer=lifetime;lifetime=own;try{return {nodes:core.render(content,options).filter(node=>node!==null),own};}finally{lifetime=outer;}}
  // Retiring also drops the references to application callbacks, so retained nodes do not keep them alive.
  function retire(target){const previous=domDocumentLifetimes.get(target);if(!previous)return;previous.active=false;for(const link of previous.links)link.removeAttribute("href");for(const action of previous.actions){action.node.setAttribute("disabled","");action.release();}previous.links.length=0;previous.actions.length=0;domDocumentLifetimes.delete(target);}
  return Object.freeze({
    render(content,options){return build(content,options).nodes;},
    // Builds first, so a binding failure leaves the displayed content and its callbacks untouched.
    setContent(target,content,options){const {nodes,own}=build(content,options);retire(target);domDocumentLifetimes.set(target,own);target.replaceChildren(...nodes);},
    clearContent(target){retire(target);target.replaceChildren();},
    extend(extra){return createDomDocumentRenderer(document,{headingBase,custom:[...custom,...extra],pageDirection});},
  });
}
