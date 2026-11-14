// In-place updates of edited messages during development (translations#176). Everything here runs only with a dev
// server: the classifier compares the generated files of two regenerations, the transform makes generated message
// modules self-accepting, and the dev client tells the page that text changed. A production build never reaches it.

/** The generated files of one catalog after a validated regeneration. */
export interface CatalogSnapshot {
  readonly catalog: string;
  /** Path relative to the catalog directory (the shared runtime is `../runtime-core.js`) → its sha256. */
  readonly files: ReadonlyMap<string, string>;
  /** The same relative paths → absolute paths. */
  readonly paths: ReadonlyMap<string, string>;
  /** sha256 of `runtime.js` without its `sourceHash` line, the one line a text edit changes. */
  readonly runtime: string;
}

/** What a regeneration means for a page that is open. */
export type Classification =
  | { readonly kind: "none" }
  | { readonly kind: "in-place"; readonly modules: readonly string[] }
  | { readonly kind: "reload"; readonly reason: string };

/** The generated message modules the transform makes self-accepting. */
export const messageModulePattern = /^messages\/r_[0-9a-f]+\.js$/;

/** The line of a generated message module that holds its per-locale table (Rmf2EsmOutputRendererV5.Message). */
export const messagesAnchor = "const messages = /* @__PURE__ */ (() => {";

/** The dev client's import specifier and resolved ID. No catalog can take it: catalog IDs follow a slash. */
export const devClientSpecifier = "virtual:runic-translations:hmr";
export const devClientId = "\0runic-translations:hmr";

/** Removes the `sourceHash` export, which changes with every edit and which nothing reads. */
export function withoutSourceHash(runtime: string): string {
  return runtime.replace(/^export const sourceHash = "sha256:[a-f0-9]{64}";\r?\n/m, "");
}

function occurrences(code: string, text: string): number {
  let count = 0;
  for (let at = code.indexOf(text); at >= 0; at = code.indexOf(text, at + text.length)) count++;
  return count;
}

/** Whether a generated message module has the shape the transform rewrites: its anchor exactly once. */
export function hasMessagesAnchor(code: string): boolean {
  return occurrences(code, messagesAnchor) === 1;
}

/**
 * Makes a generated message module self-accepting, or returns undefined when its anchor is missing or not unique (the
 * module then keeps Vite's default propagation, and the classifier reloads for it).
 *
 * The message function closes over the module's `messages` table, and every importer holds the instance it imported:
 * ESM bindings cannot be replaced. So each new instance hands its table to the earlier instances through
 * `import.meta.hot.data`, which Vite keeps for the module's URL across updates, then reports the change and registers
 * its own setter. Every instance is kept current, not only the first: Vite gives the importers of an updated module a
 * new timestamp, so a chunk loaded later imports the instance of that update. A plain accept callback would not do:
 * Vite replaces the callbacks with each new instance's. `const` becomes `let` padded to the same length, so the columns
 * of that line stay.
 */
export function selfAccepting(code: string): string | undefined {
  if (!hasMessagesAnchor(code)) return undefined;
  return code.replace(messagesAnchor, `let   ${messagesAnchor.slice("const ".length)}`) + `
import { textChanged as __runicTextChanged } from ${JSON.stringify(devClientSpecifier)};
if (import.meta.hot) {
  const __runicSetters = import.meta.hot.data.runicMessageSetters ??= [];
  if (__runicSetters.length > 0) {
    for (const set of __runicSetters) set(messages);
    __runicTextChanged();
  }
  __runicSetters.push(next => { messages = next; });
  import.meta.hot.accept();
}
`;
}

/** The call of `formatMessage` in a generated message module's function, which marker mode wraps. */
export const formatCall = "return formatMessage(";

/**
 * Lets a generated message module mark its text while marker mode is on (runic-sdk#956: Runic Inspect's select mode), or
 * returns undefined when its `formatMessage` call is missing or not unique. The call goes through `__runicFormat`, a name
 * of the same length, so every column stays; `__runicFormat` formats as before and passes the result to the dev client's
 * `marked`, which returns it unchanged unless marker mode is on. The runtime is imported as a namespace, so a runtime
 * without `getMessageLocale` still links.
 */
export function withMarkers(code: string): string | undefined {
  if (occurrences(code, formatCall) !== 1) return undefined;
  return code.replace(formatCall, "return __runicFormat(") + `
import { marked as __runicMarked } from ${JSON.stringify(devClientSpecifier)};
import * as __runicRuntime from "../runtime.js";
function __runicFormat(key, table, inputs, options) {
  return __runicMarked(formatMessage(key, table, inputs, options), __runicRuntime.catalog, key,
    () => __runicRuntime.getMessageLocale?.(key, options)?.locale);
}
`;
}

/**
 * The marker characters (docs/guides/translations/esm.md, "Message markers"): a marker is U+2063, the message's ID in
 * binary with U+2061 for 0 and U+2062 for 1 (most significant bit first), then U+2064. All four are invisible format
 * characters (default ignorable, line-break class AL), so a marked text keeps its width and its line breaks.
 */
export const markerCharacters = { start: "\u2063", zero: "\u2061", one: "\u2062", end: "\u2064" } as const;

/**
 * The dev client. It creates the text-change channel (docs/guides/translations/esm.md, "Text changes during
 * development") and, once per Vite update in which message tables changed, increments its revision and calls its
 * listeners. With no listener the page reloads, since nothing would show the new text. Without a DOM (a server-side
 * module runner) it flushes on a timer and never reloads.
 *
 * It also adds the channel's `markers` member: while an inspector turns marker mode on, every generated message function
 * prefixes its text with an invisible marker that names the message ("Message markers" in the same guide).
 */
export const devClientCode = `const key = Symbol.for("runic.translations.text");
const start = "\\u2063", zero = "\\u2061", one = "\\u2062", end = "\\u2064";
const markerPattern = /\\u2063([\\u2061\\u2062]{1,24})\\u2064/g;
function channel() {
  let text = globalThis[key];
  if (text === undefined || text === null || typeof text !== "object")
    globalThis[key] = text = { version: 1, revision: 0, listeners: new Set() };
  if (typeof text.revision !== "number") text.revision = 0;
  if (!(text.listeners instanceof Set)) text.listeners = new Set();
  if (text.markers === undefined || text.markers === null || typeof text.markers !== "object") text.markers = createMarkers();
  return text;
}
function notify(text) {
  text.revision++;
  const listeners = [...text.listeners];
  for (const listener of listeners) {
    try { listener(); } catch (error) { console.error(error); }
  }
  return listeners.length;
}
// Marker mode: off until an inspector turns it on. A message's ID is its index in messages, one per catalog, key and locale.
function createMarkers() {
  let enabled = false;
  const messages = [], ids = new Map();
  return {
    version: 1,
    get enabled() { return enabled; },
    set(on) {
      if (typeof on !== "boolean") throw new TypeError("markers.set needs a boolean.");
      if (on === enabled) return 0;
      enabled = on;
      return notify(channel());
    },
    marker(catalog, messageKey, locale) {
      const name = catalog + "\\u0000" + messageKey + "\\u0000" + locale;
      let id = ids.get(name);
      if (id === undefined) {
        id = messages.length;
        messages.push(Object.freeze({ catalog, key: messageKey, locale }));
        ids.set(name, id);
      }
      return start + id.toString(2).replaceAll("0", zero).replaceAll("1", one) + end;
    },
    decode(value) {
      const found = [];
      if (typeof value !== "string") return found;
      for (const match of value.matchAll(markerPattern)) {
        const message = messages[parseInt(match[1].replaceAll(zero, "0").replaceAll(one, "1"), 2)];
        if (message !== undefined) found.push(message);
      }
      return found;
    },
    strip(value) { return typeof value === "string" ? value.replace(markerPattern, "") : value; },
  };
}
channel();
// A formatted message with its marker in front while marker mode is on, else the value itself. Structured content gets
// the marker in its first text; an inline message without text gets a text node with only the marker.
function markContent(content, marker) {
  let done = false;
  const visit = nodes => {
    let changed = false;
    const result = nodes.map(node => {
      if (done) return node;
      if (node.kind === "text") { done = changed = true; return Object.freeze({ ...node, value: marker + node.value }); }
      if (node.kind !== "element" || !Array.isArray(node.children)) return node;
      const children = visit(node.children);
      if (children === node.children) return node;
      changed = true;
      return Object.freeze({ ...node, children });
    });
    return changed ? Object.freeze(result) : nodes;
  };
  let nodes = visit(content.nodes);
  if (!done) {
    if (content.kind !== "localized-content") return content;
    nodes = Object.freeze([Object.freeze({ kind: "text", value: marker, annotations: Object.freeze([]) }), ...content.nodes]);
  }
  return Object.freeze({ ...content, nodes });
}
export function marked(value, catalog, messageKey, localeOf) {
  const markers = globalThis[key]?.markers;
  if (markers?.enabled !== true || typeof markers.marker !== "function") return value;
  try {
    if (typeof value === "string") return markers.marker(catalog, messageKey, localeOf() ?? "") + value;
    if (value !== null && typeof value === "object" && Array.isArray(value.nodes) && (value.kind === "localized-content" || value.kind === "localized-document"))
      return markContent(value, markers.marker(catalog, messageKey, value.locale));
  } catch (error) {
    console.error(error);
  }
  return value;
}
const browser = typeof window !== "undefined" && typeof document !== "undefined";
let pending = false;
function flush() {
  if (!pending) return;
  pending = false;
  if (notify(channel()) === 0 && browser) {
    console.info("[runic-translations] page reload: nothing on this page follows text changes (the text-change channel has no listener).");
    location.reload();
  }
}
if (import.meta.hot && browser) import.meta.hot.on("vite:afterUpdate", flush);
export function textChanged() {
  if (pending) return;
  pending = true;
  if (!browser) setTimeout(flush, 0);
}
`;

/**
 * Compares the catalogs of two regenerations. In place when the same files exist, `runtime.js` changed at most in its
 * `sourceHash`, and every other changed file is a message module or a declaration; nothing when only declarations
 * changed; a reload with a reason otherwise. The rule is structural: whatever changes the runtime's tables, the
 * contract or the set of modules reloads.
 */
export function classify(previous: readonly CatalogSnapshot[], next: readonly CatalogSnapshot[]): Classification {
  const before = new Map(previous.map(snapshot => [snapshot.catalog, snapshot]));
  const after = new Map(next.map(snapshot => [snapshot.catalog, snapshot]));
  if (before.size !== after.size || [...after.keys()].some(catalog => !before.has(catalog)))
    return { kind: "reload", reason: "the catalogs changed" };
  const modules: string[] = [];
  for (const [catalog, current] of after) {
    const earlier = before.get(catalog)!;
    const paths = new Set([...earlier.files.keys(), ...current.files.keys()]);
    // Membership first: an added message also rewrites _index.js, and the reason should name the cause.
    const membership = [...paths].filter(path => !earlier.files.has(path) || !current.files.has(path));
    if (membership.some(path => messageModulePattern.test(path)))
      return { kind: "reload", reason: `messages were added, removed or renamed in catalog '${catalog}'` };
    if (membership.length > 0)
      return { kind: "reload", reason: `${membership[0]} was ${earlier.files.has(membership[0]!) ? "removed from" : "added to"} catalog '${catalog}'` };
    for (const path of paths) {
      if (earlier.files.get(path) === current.files.get(path) || path.endsWith(".d.ts")) continue;
      if (messageModulePattern.test(path)) { modules.push(current.paths.get(path)!); continue; }
      if (path === "runtime.js" && earlier.runtime === current.runtime) continue;
      return { kind: "reload", reason: `${path} of catalog '${catalog}' changed (the catalog's contract changed)` };
    }
  }
  return modules.length === 0 ? { kind: "none" } : { kind: "in-place", modules };
}
