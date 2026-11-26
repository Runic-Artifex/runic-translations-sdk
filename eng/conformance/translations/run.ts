// Runs one Translations case through an adapter (eng/conformance/PROTOCOL.md, "Translations"). The driver reads the
// committed artifacts and contracts, applies the pack recipes and macros, and matches every result; the adapter only
// loads packs, formats, negotiates and reads or writes text references with its implementation.
import { readFileSync } from "node:fs";
import { join } from "node:path";
import { AdapterError, type Adapter, type Case, type Outcome, type RunContext } from "../core/index";
import { applyRecipe } from "./recipes";

type Group = { artifact: Uint8Array; contract: any };

const groups = new Map<string, Group>();
let compiledRoot = "";

export function setCorpusRoot(root: string) {
  compiledRoot = join(root, "../corpus/compiled");
  groups.clear();
}

function group(id: string): Group {
  let found = groups.get(id);
  if (!found) {
    const index = JSON.parse(readFileSync(join(compiledRoot, "index.json"), "utf8"));
    const entry = index.groups.find((g: any) => g.id === id);
    if (!entry) throw new Error(`compiled/index.json has no group ${id}`);
    found = { artifact: new Uint8Array(readFileSync(join(compiledRoot, entry.artifact))), contract: JSON.parse(readFileSync(join(compiledRoot, entry.contract), "utf8")) };
    groups.set(id, found);
  }
  return found;
}

const base64 = (bytes: Uint8Array) => Buffer.from(bytes).toString("base64");

// The packs an adapter process has loaded, by group: a committed artifact is loaded once per process.
const loaded = new WeakMap<Adapter, Map<string, string>>();

async function load(adapter: Adapter, id: string, bytes?: Uint8Array): Promise<{ pack?: string; rejected?: string }> {
  const { artifact, contract } = group(id);
  return adapter.call("tr.loadPack", { group: id, artifact: base64(bytes ?? artifact), contract });
}

async function pack(adapter: Adapter, id: string): Promise<string> {
  let packs = loaded.get(adapter);
  if (!packs) loaded.set(adapter, (packs = new Map()));
  let handle = packs.get(id);
  if (!handle) {
    const result = await load(adapter, id);
    if (!result.pack) throw new Error(`the committed artifact of ${id} was rejected: ${result.rejected}`);
    handle = result.pack;
    packs.set(id, handle);
  }
  return handle;
}

// The date and percent corpus's tolerance: U+0020, U+00A0 and U+202F are the same space, U+0027 and U+2019 the same
// apostrophe (specs/translations/corpus/README.md).
const tolerant = (text: string) => text.replace(/[  ]/g, " ").replace(/’/g, "'");

const fail = (detail: string): Outcome => ({ outcome: "fail", detail });
const pass: Outcome = { outcome: "pass" };

export async function runTranslationsCase(adapter: Adapter, c: Case, context: RunContext): Promise<Outcome> {
  if (!c.roles.some((role) => context.roles.has(role))) return { outcome: "excluded", detail: `role:${c.roles.join(",")}` };
  const missing = c.requires.filter((name) => !context.supports.has(name));
  if (missing.length) return { outcome: "unsupported", requires: missing, detail: `the adapter does not list ${missing.join(", ")}` };
  try {
    return await run(adapter, c);
  } catch (e) {
    if (e instanceof AdapterError && e.code === "unsupported") return { outcome: "unsupported", detail: e.message };
    throw e;
  }
}

async function run(adapter: Adapter, c: Case): Promise<Outcome> {
  const b = c.body;
  switch (c.kind) {
    case "format": {
      const result = await adapter.call("tr.format", { pack: await pack(adapter, b.group), key: b.key, arguments: b.arguments, locale: b.locale });
      if (result.error !== undefined) return fail(`expected ${JSON.stringify(b.expected)}, the format failed: ${result.error}`);
      const same = (expected: string) => result.text === expected || (b.tolerant && tolerant(result.text) === tolerant(expected));
      if (same(b.expected)) return pass;
      // A per-platform alternate is not accepted here: the status file records it as a deviation (TR-1, the D011 row
      // specs/translations/conformance/differences.md#platform-date-data).
      if (b.alternates.some(same)) return fail(`platform date data: wrote the alternate ${JSON.stringify(result.text)}, not ${JSON.stringify(b.expected)} (${b.note})`);
      return fail(`expected ${JSON.stringify(b.expected)}, got ${JSON.stringify(result.text)}`);
    }
    case "pack": {
      const bytes = applyRecipe(group(b.group).artifact, b.recipe);
      const result = await load(adapter, b.group, bytes);
      if (b.rejection === null) return result.pack ? pass : fail(`expected the pack to load, it was rejected: ${result.rejected}`);
      if (result.pack) return fail(`expected ${b.rejection}, the pack loaded`);
      return result.rejected === b.rejection ? pass : fail(`expected ${b.rejection}, got ${result.rejected}`);
    }
    case "reference": {
      const result = await adapter.call("tr.resolveReference", { pack: await pack(adapter, b.group), locale: b.locale, json: b.json });
      const describe = (r: any) =>
        `${r.outcome} ${r.text === null || r.text === undefined ? "null" : `«${r.text}»`} [${r.diagnostics.map((d: any) => `${d.path}:${d.reason}`).join(", ")}]`;
      const expected = describe(b.expected), actual = describe(result);
      return expected === actual ? pass : fail(`expected ${expected}, got ${actual}`);
    }
    case "reference-rejected": {
      const result = await adapter.call("tr.readReference", { pack: await pack(adapter, "transport-v2/reader.en"), json: b.json });
      if (result.ok) return fail(`expected the reference to be rejected (${b.reason}), it was read`);
      // An implementation that names the reason must name the corpus's; one that rejects without a reason passes.
      return result.reason === undefined || result.reason === b.reason ? pass : fail(`expected the reason ${b.reason}, got ${result.reason}`);
    }
    case "reference-written": {
      const result = await adapter.call("tr.writeReference", { pack: await pack(adapter, b.group), reference: b.reference });
      return result.text === b.expected ? pass : fail(`expected ${b.expected}\n    wrote    ${result.text}`);
    }
    case "negotiate": {
      const failures: string[] = [];
      for (const item of b.cases) {
        const result = await adapter.call("tr.negotiate", { group: b.group, locales: b.locales, base: b.base, preferences: item.preferences });
        if (result.locale !== item.expected) failures.push(`${JSON.stringify(item.preferences)}: expected ${item.expected}, got ${result.locale}`);
      }
      return failures.length ? fail(failures.join("\n")) : pass;
    }
    default:
      throw new Error(`unknown case kind ${c.kind}`);
  }
}
