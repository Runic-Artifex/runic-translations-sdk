#!/usr/bin/env bun
// The conformance adapter of the generated ESM runtime (runic-sdk's eng/conformance/PROTOCOL.md,
// rax.conformance-adapter/1): `bun eng/conformance/cli.ts esm-translations` starts it after its build step has written
// the catalogs. One JSON request per line on stdin, one response per line on stdout; everything else goes to stderr.
//
// The ESM runtime is generated per catalog (generate --emit-esm), which is how this package's Vite plugin delivers it to
// an application. A pack is decoded by its own catalog's dynamic.js. The build step (ConformanceEsmCatalogs in the
// Compiler tests) writes, for each compiled group of the corpus, the ESM modules of the project that produced the group's
// committed artifact, and a catalog per locale-negotiation group, with index.json mapping each group to its module
// directory. This adapter uses only those generated modules' exports: decodeLocalePack and formatDynamicMessage,
// resolveTextReference and createTextReference, and negotiateLocale. It is a test file, never published.
import { readFileSync } from "node:fs";
import { join, resolve } from "node:path";
import { pathToFileURL } from "node:url";

const protocol = "rax.conformance-adapter/1";
const root = resolve(import.meta.dir, "../../../..");
const catalogs = process.env.RAX_TRANSLATIONS_ESM_CATALOGS ?? join(root, "artifacts/conformance/esm-catalogs");
const index: Record<string, string> = JSON.parse(readFileSync(join(catalogs, "index.json"), "utf8"));

// Nothing but responses may reach stdout.
console.log = console.error;

class Unsupported extends Error {}

type Catalog = { dynamic: any; transport: any; runtime: any; m: any };
const modules = new Map<string, Promise<Catalog>>();

function catalog(group: string): Promise<Catalog> {
  const directory = index[group];
  if (!directory) throw new Unsupported(`no generated catalog for ${group}`);
  let found = modules.get(directory);
  if (!found) {
    const load = (name: string) => import(pathToFileURL(join(catalogs, directory, name)).href);
    found = Promise.all([load("dynamic.js"), load("transport.js"), load("runtime.js"), load("messages.js")]).then(([dynamic, transport, runtime, messages]) => ({
      dynamic,
      transport,
      runtime,
      m: messages.m,
    }));
    modules.set(directory, found);
  }
  return found;
}

type Pack = { artifact: any; catalog: Catalog };
const packs = new Map<string, Pack>();

function pack(args: any): Pack {
  const found = packs.get(args.pack);
  if (!found) throw new Error(`unknown pack handle ${args.pack}`);
  return found;
}

// A corpus argument ({type, value}) as the value a generated message function takes for an input of that type.
const input = ({ type, value }: { type: string; value: any }) => (type === "int64" ? BigInt(value) : type === "boolean" ? value === true || value === "true" : value);
const inputs = (args: Record<string, any>) => Object.fromEntries(Object.entries(args ?? {}).map(([name, value]) => [name, input(value)]));

async function handle(op: string, args: any): Promise<unknown> {
  switch (op) {
    case "describe":
      return {
        protocol,
        implementation: { id: "esm-translations", name: "generated ESM runtime", version: (await catalog(Object.keys(index)[0])).runtime.esmAbiVersion?.toString() },
        corpora: ["translations"],
        roles: ["runtime"],
        features: [],
        // createTextReference writes version 2 references only.
        capabilities: [],
      };
    case "shutdown":
      return {};
    case "tr.loadPack": {
      const { dynamic } = await catalog(args.group);
      const decoded = await dynamic.decodeLocalePack(new Uint8Array(Buffer.from(args.artifact, "base64")), args.contract.locale);
      if (!decoded.ok) return { rejected: decoded.reason };
      const id = `p${packs.size + 1}`;
      packs.set(id, { artifact: decoded.value, catalog: await catalog(args.group) });
      return { pack: id };
    }
    case "tr.format": {
      const { artifact, catalog: c } = pack(args);
      try {
        return { text: c.dynamic.formatDynamicMessage(artifact, args.key, inputs(args.arguments), { locale: args.locale }) };
      } catch (error) {
        return { error: error instanceof Error ? `${error.name}: ${error.message}` : String(error) };
      }
    }
    case "tr.resolveReference": {
      const { catalog: c } = pack(args);
      const result = c.transport.resolveTextReference(args.json, c.m, { locale: args.locale });
      return { outcome: result.outcome, text: result.text ?? null, diagnostics: result.diagnostics };
    }
    case "tr.readReference": {
      const { catalog: c } = pack(args);
      const result = c.transport.resolveTextReference(args.json, c.m, { locale: "en" });
      const rejection = result.outcome === "failed" ? result.diagnostics.find((d: any) => d.path === "$") : undefined;
      return rejection ? { ok: false, reason: rejection.reason } : { ok: true };
    }
    case "tr.writeReference": {
      const { catalog: c } = pack(args);
      if (args.reference.version === 1) throw new Unsupported("createTextReference writes version 2 references only");
      const create = (description: any): any =>
        c.transport.createTextReference(
          description.key,
          Object.fromEntries(Object.entries<any>(description.arguments ?? {}).map(([name, { type, value }]) => [name, type === "message" ? create(value) : type === "int64" ? BigInt(value) : value])),
          description.fallbackText === undefined ? {} : { fallbackText: description.fallbackText },
        );
      return { text: JSON.stringify(create(args.reference)) };
    }
    case "tr.negotiate": {
      const { runtime } = await catalog(args.group);
      return { locale: runtime.negotiateLocale(args.preferences) };
    }
    default:
      throw new Unsupported(`unknown operation ${op}`);
  }
}

for await (const line of console) {
  if (!line.trim()) continue;
  let request: any;
  try {
    request = JSON.parse(line);
  } catch (error) {
    console.error(`unreadable request: ${line}`);
    continue;
  }
  let response: object;
  try {
    response = { seq: request.seq, ok: await handle(request.op, request.args ?? {}) };
  } catch (error) {
    const code = error instanceof Unsupported ? "unsupported" : "failed";
    response = { seq: request.seq, error: { code, message: error instanceof Error ? `${error.name}: ${error.message}` : String(error) } };
  }
  process.stdout.write(JSON.stringify(response) + "\n");
  if (request.op === "shutdown") process.exit(0);
}
