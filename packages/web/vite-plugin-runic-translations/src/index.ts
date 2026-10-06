import { execFile } from "node:child_process";
import { createHash, randomUUID } from "node:crypto";
import { lstatSync, readFileSync, readdirSync } from "node:fs";
import { mkdir, readFile, realpath, rename, rm, writeFile } from "node:fs/promises";
import { basename, dirname, isAbsolute, join, normalize, relative, resolve, sep } from "node:path";
import { promisify } from "node:util";
import type { HotUpdateOptions, Plugin, ViteDevServer } from "vite";
import { CompilerUnavailable, PersistentCompiler } from "./compiler-service.js";


export interface RunicTranslationsOptions {
  /** Runic translation directory or runic.json path. Defaults to ./translations. Watches recursive .mf2 and .rmf2 inputs. */
  readonly project?: string;
  /** Generated artifact directory for project mode. Defaults to ./.runic/translations. */
  readonly output?: string;
  /** Pre-generated RMF2 web-module-manifest-v3.json path when generation is owned by another build. */
  readonly manifest?: string;
  /** Authoring inputs to watch in manifest mode. The host must regenerate before refresh; the plugin cannot recompute sourceHash. */
  readonly sourceFiles?: readonly string[];
  /** Project-mode invocation working directory. Defaults to the Vite process working directory. */
  readonly cwd?: string;
  /** Project-mode compiler executable. Defaults to `dotnet`. */
  readonly command?: string;
  /** Project-mode arguments before `generate` or `serve`. Defaults to the local-tool invocation. */
  readonly commandArguments?: readonly string[];
  /** Generated ambient declarations for the manifest's virtual modules. Defaults to `<output>/virtual.d.ts` in project mode or beside an explicit manifest. Set false to disable. */
  readonly typeDeclarations?: string | false;
  /**
   * Keep one `runic-translations serve` compiler running for the dev server instead of starting the
   * tool for every save. Defaults to true. The plugin falls back to one-shot generation when the
   * tool does not support serve mode or keeps crashing. Production builds always run one-shot.
   */
  readonly persistentCompiler?: boolean;
  /**
   * Milliseconds the persistent compiler may take for one regeneration. Defaults to 120000. When it
   * does not answer in time, the plugin stops it, generates that change one-shot, and restarts it on
   * the next change.
   */
  readonly persistentCompilerTimeout?: number;
}

type EntryKind = "messages" | "runtime" | "server" | "transport" | "dynamic";
type Entries = Readonly<Record<EntryKind | "types", string>>;

interface ProjectSources {
  readonly manifest: string;
  readonly sourceFiles: readonly string[];
  readonly sourceRoots: readonly string[];
  readonly watchRoots: readonly string[];
}

interface CompilerProject extends ProjectSources {
  readonly project: string;
  readonly config: string;
  readonly output: string;
  readonly cwd: string;
  readonly command: string;
  readonly commandArguments: readonly string[];
}

interface ManifestAsset {
  readonly path: string;
  readonly sha256: string;
  readonly byteLength: number;
  readonly mediaType: string;
}

interface WebModuleManifest {
  readonly webModuleManifestVersion: unknown;
  readonly esmAbiVersion: unknown;
  readonly rmf2RuntimeAbiVersion: unknown;
  readonly messageGrammarVersion: unknown;
  readonly profile: unknown;
  readonly generatedNameVersion: unknown;
  readonly catalog: string;
  readonly contractFingerprint: string;
  readonly sourceHash: string;
  readonly entrypoints: Readonly<Record<string, unknown>>;
  readonly assets: readonly ManifestAsset[];
}

/** The module graph operations the plugin needs; satisfied by Vite's environment and mixed graphs. */
interface ModuleGraph<Module> {
  getModuleById(id: string): Module | undefined;
  getModulesByFile(file: string): Set<Module> | undefined;
  invalidateModule(module: Module): void;
}

/** Catalog state before a validated refresh, used to invalidate both old and new virtual modules. */
interface Change {
  readonly catalogs: readonly string[];
  readonly generatedPaths: ReadonlySet<string>;
}

const prefix = "\0virtual:runic-translations/";
const entryKinds: readonly EntryKind[] = ["messages", "runtime", "server", "transport", "dynamic"];
const supportedEsmAbiVersion = 4;
const supportedRmf2RuntimeAbiVersion = 2;
const execFileAsync = promisify(execFile);

/** Exposes compiler-generated ESM without coupling messages to Vite or a UI framework. */
export function runicTranslations(options: RunicTranslationsOptions = {}): Plugin {
  if (!options || typeof options !== "object" || Array.isArray(options))
    throw new TypeError("runicTranslations options must be an object.");
  if (options.persistentCompiler !== undefined && typeof options.persistentCompiler !== "boolean")
    throw new TypeError("persistentCompiler must be a boolean.");
  if (options.persistentCompilerTimeout !== undefined &&
      (typeof options.persistentCompilerTimeout !== "number" || !Number.isFinite(options.persistentCompilerTimeout) || options.persistentCompilerTimeout <= 0))
    throw new TypeError("persistentCompilerTimeout must be a positive number of milliseconds.");
  const compiler = options.manifest === undefined ? projectOptions(options) : undefined;
  let manifestPath = compiler?.manifest ?? resolve(options.manifest!);
  if (options.typeDeclarations !== undefined && options.typeDeclarations !== false &&
      (typeof options.typeDeclarations !== "string" || options.typeDeclarations.length === 0))
    throw new TypeError("typeDeclarations must be a non-empty path or false.");
  const typeDeclarationsBase = compiler?.cwd ?? dirname(manifestPath);
  const typeDeclarationsPath = options.typeDeclarations === false ? undefined : resolve(
    compiler?.cwd ?? process.cwd(),
    options.typeDeclarations ?? (compiler ? join(compiler.output, "virtual.d.ts") : join(dirname(manifestPath), "virtual.d.ts")),
  );
  const explicitSources = new Set((options.sourceFiles ?? []).map(path => resolve(path)));
  let sourceRoots: readonly string[] = compiler?.sourceRoots ?? [];
  let watchRoots: readonly string[] = compiler?.watchRoots ?? sourceRoots;
  let sourceFiles = new Set([...explicitSources, ...(compiler?.sourceFiles ?? [])]);
  let server: ViteDevServer | undefined;
  let service: PersistentCompiler | undefined;
  let cleanupServer: (() => void) | undefined;
  let updates: Promise<unknown> = Promise.resolve();
  let catalog: string | undefined;
  let entries: Entries | undefined;
  let generatedPaths: ReadonlySet<string> = new Set();
  let compilation: Promise<unknown> = Promise.resolve();
  let lastChange: { readonly key: string; readonly change: Promise<Change> } | undefined;

  async function generate(project: CompilerProject): Promise<void> {
    if (service && !service.disabledReason) {
      try {
        await service.generate({ project: project.project, output: project.output, emit: ["esm"] });
        return;
      } catch (error) {
        if (!(error instanceof CompilerUnavailable)) throw error;
      }
    }
    await execFileAsync(project.command,
      [...project.commandArguments, "generate", "--project", project.project, "--output", project.output, "--emit-esm"],
      { cwd: project.cwd, maxBuffer: 16 * 1024 * 1024 });
  }

  function compile(): Promise<ProjectSources | undefined> {
    if (!compiler) return Promise.resolve(undefined);
    const next = compilation.catch(() => undefined).then(async () => {
      const current = readProject(compiler.config, compiler.output);
      await generate(compiler);
      return current;
    });
    compilation = next;
    return next;
  }

  async function refresh(nextProject?: ProjectSources): Promise<void> {
    const nextManifestPath = nextProject?.manifest ?? manifestPath;
    const document = JSON.parse(await readFile(nextManifestPath, "utf8")) as WebModuleManifest;
    if (!document || typeof document !== "object" || Array.isArray(document))
      throw new Error("The Runic Translations web module manifest must be an object.");
    if (document.webModuleManifestVersion !== 3)
      throw new Error(`Unsupported Runic Translations web module manifest version '${String(document.webModuleManifestVersion)}'.`);
    const rootMembers = ["webModuleManifestVersion", "esmAbiVersion", "rmf2RuntimeAbiVersion", "messageGrammarVersion", "profile", "generatedNameVersion", "catalog", "contractFingerprint", "sourceHash", "entrypoints", "assets"];
    if (Object.keys(document).some(key => !rootMembers.includes(key)))
      throw new Error(`The Runic Translations v${document.webModuleManifestVersion} web module manifest contains an unknown member.`);
    if (document.esmAbiVersion !== supportedEsmAbiVersion)
      throw new Error(`Unsupported Runic ESM ABI version '${String(document.esmAbiVersion)}'. Expected '${supportedEsmAbiVersion}'.`);
    if (document.rmf2RuntimeAbiVersion !== supportedRmf2RuntimeAbiVersion || document.messageGrammarVersion !== 5 ||
        document.profile !== "rmf2-execution-v2" || document.generatedNameVersion !== 1 ||
        typeof document.sourceHash !== "string" || !/^sha256:[a-f0-9]{64}$/.test(document.sourceHash))
      throw new Error("The Runic Translations v3 web module manifest has an incompatible RMF2 execution contract.");
    if (typeof document.catalog !== "string" || !document.entrypoints ||
        typeof document.contractFingerprint !== "string" || !/^sha256:[a-f0-9]{64}$/.test(document.contractFingerprint))
      throw new Error("The Runic Translations web module manifest is malformed.");
    if (!/^[a-z][a-z0-9.-]*$/.test(document.catalog))
      throw new Error(`The Runic Translations v${document.webModuleManifestVersion} web module manifest has an invalid catalog ID.`);
    if (typeof document.entrypoints !== "object" || Array.isArray(document.entrypoints))
      throw new Error(`The Runic Translations v${document.webModuleManifestVersion} web module manifest has malformed entrypoints.`);
    const root = dirname(nextManifestPath);
    const realRoot = await realpath(root);
    const expectedEntrypoints: Readonly<Record<EntryKind | "types", string>> = {
      messages: "messages.js",
      types: "messages.d.ts",
      runtime: "runtime.js",
      server: "server.js",
      transport: "transport.js",
      dynamic: "dynamic.js",
    };
    if (Object.keys(document.entrypoints).some(kind => !Object.hasOwn(expectedEntrypoints, kind)) ||
        (Object.keys(expectedEntrypoints) as (keyof typeof expectedEntrypoints)[]).some(kind => document.entrypoints[kind] !== expectedEntrypoints[kind]))
      throw new Error(`The Runic Translations v${document.webModuleManifestVersion} web module manifest has invalid entrypoints.`);
    const requiredEntrypoints = expectedEntrypoints;
    if (!Array.isArray(document.assets))
      throw new Error("The Runic Translations web module manifest does not declare its generated assets.");
    const assets = new Map<string, string>();
    for (const asset of document.assets as unknown[]) {
      if (!asset || typeof asset !== "object" || Array.isArray(asset) ||
          Object.keys(asset).some(key => !["path", "sha256", "byteLength", "mediaType"].includes(key)))
        throw new Error(`The Runic Translations v${document.webModuleManifestVersion} web module manifest contains an unknown asset member.`);
      const { path: assetPath, sha256, byteLength, mediaType } = asset as Partial<Record<keyof ManifestAsset, unknown>>;
      if (typeof assetPath !== "string" || !/^(?!\/)(?!.*(?:^|\/)\.\.(?:\/|$))[A-Za-z0-9_$.-]+(?:\/[A-Za-z0-9_$.-]+)*$/.test(assetPath) || typeof sha256 !== "string" ||
          !/^[a-f0-9]{64}$/.test(sha256) || !Number.isSafeInteger(byteLength) || (byteLength as number) < 0 ||
          typeof mediaType !== "string" || !["text/javascript", "text/typescript"].includes(mediaType) || assets.has(assetPath))
        throw new Error("The Runic Translations web module manifest contains an invalid generated asset entry.");
      const path = await containedReal(root, realRoot, assetPath);
      const content = await readFile(path);
      if (content.byteLength !== byteLength || createHash("sha256").update(content).digest("hex") !== sha256)
        throw new Error(`Generated Runic asset integrity check failed: '${assetPath}'.`);
      assets.set(assetPath, path);
    }
    for (const path of Object.values(requiredEntrypoints)) {
      contained(root, path);
      if (!assets.has(path))
        throw new Error("The Runic Translations web module manifest omits a required generated entrypoint.");
    }
    const runtime = await readFile(assets.get(requiredEntrypoints.runtime)!, "utf8");
    const runtimeFingerprint = /^export const contractFingerprint = ("sha256:[a-f0-9]{64}");$/m.exec(runtime)?.[1];
    if (runtimeFingerprint !== JSON.stringify(document.contractFingerprint))
      throw new Error("The Runic Translations web module manifest fingerprint does not match its generated runtime.");
    const markers = new Map([...runtime.matchAll(/^export const (esmAbiVersion|rmf2RuntimeAbiVersion|messageGrammarVersion|profile|generatedNameVersion|sourceHash) = (.+);$/gm)]
      .map(match => [match[1], match[2]]));
    const expected = {
      esmAbiVersion: String(document.esmAbiVersion),
      rmf2RuntimeAbiVersion: String(document.rmf2RuntimeAbiVersion),
      messageGrammarVersion: String(document.messageGrammarVersion),
      profile: JSON.stringify(document.profile),
      generatedNameVersion: String(document.generatedNameVersion),
      sourceHash: JSON.stringify(document.sourceHash),
    };
    if (Object.entries(expected).some(([name, value]) => markers.get(name) !== value))
      throw new Error("The Runic Translations v3 web module manifest does not match its generated RMF2 runtime contract.");
    const nextGeneratedPaths = new Set(assets.values());
    const nextEntries: Entries = Object.freeze({
      messages: assets.get(requiredEntrypoints.messages)!,
      types: assets.get(requiredEntrypoints.types)!,
      runtime: assets.get(requiredEntrypoints.runtime)!,
      server: assets.get(requiredEntrypoints.server)!,
      transport: assets.get(requiredEntrypoints.transport)!,
      dynamic: assets.get(requiredEntrypoints.dynamic)!,
    });
    if (typeDeclarationsPath) {
      const safeDeclarationsPath = await safeTypeDeclarationsPath(typeDeclarationsPath, nextManifestPath, assets, typeDeclarationsBase);
      await writeTypeDeclarations(safeDeclarationsPath, document.catalog, root, assets, requiredEntrypoints.types);
    }
    manifestPath = nextManifestPath;
    if (nextProject) {
      sourceFiles = new Set([...explicitSources, ...nextProject.sourceFiles]);
      sourceRoots = nextProject.sourceRoots;
      watchRoots = nextProject.watchRoots;
      server?.watcher.add([...watchRoots]);
      server?.watcher.add([...sourceFiles]);
    }
    catalog = document.catalog;
    generatedPaths = nextGeneratedPaths;
    entries = nextEntries;
  }

  function virtualId(kind: EntryKind): string {
    return `${prefix}${catalog}/${kind}`;
  }

  function isSource(path: string): boolean {
    if (!compiler) return sourceFiles.has(path);
    if (path === compiler.config) return true;
    if (isWithin(compiler.output, path)) return false;
    return explicitSources.has(path) || (sourceRoots.some(root => isWithin(root, path)) && /\.(?:mf2|rmf2)$/i.test(path));
  }

  function isRelevant(path: string): boolean {
    return isSource(path) || (!compiler && isGenerated(path, manifestPath));
  }

  // Regenerates (project mode) and revalidates the manifest once per file change. Changes are
  // serialized; a failed change keeps the last validated catalog.
  function prepare(path: string): Promise<Change> | undefined {
    if (!isRelevant(path)) return undefined;
    const source = isSource(path);
    const operation = updates.catch(() => undefined).then(async () => {
      const previous: Change = { catalogs: catalog ? [catalog] : [], generatedPaths };
      const nextProject = compiler && source ? await compile() : undefined;
      await refresh(nextProject);
      return { catalogs: [...new Set([...previous.catalogs, catalog!])], generatedPaths: new Set([...previous.generatedPaths, ...generatedPaths]) };
    });
    updates = operation;
    return operation;
  }

  function invalidate<Module>(graph: ModuleGraph<Module>, change: Change): Module[] {
    const modules = change.catalogs.flatMap(id => entryKinds.map(kind => graph.getModuleById(`${prefix}${id}/${kind}`)))
      .filter((module): module is Module => module !== undefined);
    // Generated dependencies can keep transformed code after a virtual re-export is invalidated.
    for (const generated of change.generatedPaths)
      for (const module of graph.getModulesByFile(generated) ?? []) graph.invalidateModule(module);
    for (const module of modules) graph.invalidateModule(module);
    return modules;
  }

  function graphsOf(target: ViteDevServer): ModuleGraph<unknown>[] {
    const environments = (target as Partial<ViteDevServer>).environments;
    return environments ? Object.values(environments).map(environment => environment.moduleGraph as ModuleGraph<unknown>) : [target.moduleGraph as ModuleGraph<unknown>];
  }

  return {
    name: "runic-translations",
    enforce: "pre",

    configureServer(value) {
      server = value;
      if (compiler) server.watcher.add([...watchRoots]);
      if (compiler && options.persistentCompiler !== false) {
        const logger = (value as Partial<ViteDevServer>).config?.logger;
        service = new PersistentCompiler({
          command: compiler.command,
          commandArguments: compiler.commandArguments,
          cwd: compiler.cwd,
          requestTimeout: options.persistentCompilerTimeout,
          onDisabled: reason => logger?.warn(`[runic-translations] Using one-shot generation: ${reason}`, { timestamp: true }),
        });
      }
      // Added and removed sources change the catalog's membership; regenerate and reload.
      const membershipChanged = (path: string) => {
        const pending = prepare(resolve(path));
        if (!pending) return;
        void pending.then(change => {
          for (const graph of graphsOf(value)) invalidate(graph, change);
          value.ws.send({ type: "full-reload" });
        }).catch((error: Error) => {
          value.ws.send({ type: "error", err: { message: error.message, stack: error.stack ?? "", plugin: "runic-translations" } });
        });
      };
      server.watcher.on("add", membershipChanged);
      server.watcher.on("unlink", membershipChanged);
      const currentService = service;
      const cleanup = () => {
        value.watcher.off("add", membershipChanged);
        value.watcher.off("unlink", membershipChanged);
        void currentService?.close();
      };
      server.httpServer?.once("close", cleanup);
      cleanupServer = cleanup;
    },

    closeBundle() {
      cleanupServer?.();
    },

    async buildStart() {
      const nextProject = await compile();
      await refresh(nextProject);
      if (compiler) this.addWatchFile(compiler.project);
      if (!compiler) this.addWatchFile(manifestPath);
      for (const path of sourceFiles) this.addWatchFile(path);
      if (!compiler) for (const path of generatedPaths) this.addWatchFile(path);
    },

    async resolveId(id) {
      if (!entries) await refresh();
      if (id === `virtual:runic-translations/${catalog}` || id === `virtual:runic-translations/${catalog}/messages`)
        return virtualId("messages");
      for (const kind of entryKinds)
        if (kind !== "messages" && id === `virtual:runic-translations/${catalog}/${kind}`) return virtualId(kind);
      return null;
    },

    async load(id) {
      if (!entries) await refresh();
      for (const kind of entryKinds)
        if (id === virtualId(kind)) return `export * from ${JSON.stringify(toVitePath(entries![kind]))};\n`;
      return null;
    },

    // Vite calls this once per environment (client, ssr, ...) for the same change; the change is
    // compiled once and each environment invalidates its own module graph.
    async hotUpdate(update: HotUpdateOptions) {
      const path = resolve(update.file);
      if (compiler && isWithin(compiler.output, path)) return [];
      // The watcher listeners above own additions and removals.
      if (update.type !== "update") return isRelevant(path) ? [] : undefined;
      const key = `${path}\0${update.timestamp}`;
      let first = false;
      if (lastChange?.key !== key) {
        const change = prepare(path);
        if (!change) return undefined;
        lastChange = { key, change };
        first = true;
      }
      let change: Change;
      try {
        change = await lastChange.change;
      } catch (error) {
        // Report a failed change once, from the environment that started it.
        if (first) throw error;
        return [];
      }
      return invalidate(this.environment.moduleGraph, change);
    },
  };
}

function projectOptions(options: RunicTranslationsOptions): CompilerProject {
  const cwd = resolve(options.cwd ?? process.cwd());
  if (options.project !== undefined && (typeof options.project !== "string" || options.project.length === 0))
    throw new TypeError("project must be a non-empty path.");
  if (options.output !== undefined && (typeof options.output !== "string" || options.output.length === 0))
    throw new TypeError("output must be a non-empty path.");
  if (options.command !== undefined && (typeof options.command !== "string" || options.command.length === 0))
    throw new TypeError("command must be a non-empty executable name.");
  if (options.commandArguments !== undefined && (!Array.isArray(options.commandArguments) || options.commandArguments.some(argument => typeof argument !== "string")))
    throw new TypeError("commandArguments must contain only strings.");
  const supplied = resolve(cwd, options.project ?? "translations");
  const config = supplied.endsWith(`${sep}runic.json`) ? supplied : join(supplied, "runic.json");
  const project = dirname(config);
  const output = resolve(cwd, options.output ?? ".runic/translations");
  return Object.freeze({
    project,
    config,
    output,
    ...readProject(config, output),
    cwd,
    command: options.command ?? "dotnet",
    commandArguments: Object.freeze([...(options.commandArguments ?? ["tool", "run", "runic-translations", "--"])]),
  });
}

async function writeTypeDeclarations(path: string, catalog: string, root: string, assets: ReadonlyMap<string, string>, messagesTypes: string): Promise<void> {
  const typeEntrypoints: Readonly<Record<EntryKind, string>> = {
    messages: messagesTypes,
    runtime: "runtime.d.ts",
    server: "server.d.ts",
    transport: "transport.d.ts",
    dynamic: "dynamic.d.ts",
  };
  const sources = new Map<EntryKind, string>();
  for (const [kind, relativePath] of Object.entries(typeEntrypoints) as [EntryKind, string][]) {
    contained(root, relativePath);
    const asset = assets.get(relativePath);
    if (!asset)
      throw new Error(`The Runic Translations web module manifest omits the '${kind}' generated type declarations.`);
    sources.set(kind, await readFile(asset, "utf8"));
  }
  const virtual = (kind: EntryKind) => `virtual:runic-translations/${catalog}${kind === "messages" ? "" : `/${kind}`}`;
  const rewrite = (kind: EntryKind, source: string) => {
    let rewritten = source
      .replace(/^\s*\/\/ <auto-generated \/>\s*/u, "")
      .replaceAll("export declare ", "export ")
      .replaceAll('from "./runtime.js"', `from ${JSON.stringify(virtual("runtime"))}`);
    if (kind === "runtime") rewritten = [
      "type RunicDomDocument = typeof globalThis extends { document: infer Value } ? Value : never;",
      "type RunicDomNode = typeof globalThis extends { Node: { prototype: infer Value } } ? Value : never;",
      rewritten.replace(/\bDocument\b/g, "RunicDomDocument").replace(/\bNode\b/g, "RunicDomNode"),
    ].join("\n");
    return rewritten;
  };
  const moduleDeclaration = (kind: EntryKind, specifier: string, source: string) => {
    const body = rewrite(kind, source).trimEnd().split("\n").map(line => `  ${line}`).join("\n");
    return `declare module ${JSON.stringify(specifier)} {\n${body}\n}`;
  };
  const messages = sources.get("messages")!;
  const declarations = [
    "// <auto-generated />",
    "// Generated from the validated Runic web module manifest. Do not edit.",
    moduleDeclaration("messages", virtual("messages"), messages),
    moduleDeclaration("messages", `${virtual("messages")}/messages`, messages),
    ...(["runtime", "server", "transport", "dynamic"] as const).map(kind =>
      moduleDeclaration(kind, virtual(kind), sources.get(kind)!)),
    "",
  ].join("\n\n");

  let previous: string | undefined;
  try {
    previous = await readFile(path, "utf8");
  } catch (error) {
    if (errorCode(error) !== "ENOENT") throw error;
  }
  if (previous === declarations) return;
  await mkdir(dirname(path), { recursive: true });
  const temporary = `${path}.${process.pid}.${randomUUID()}.tmp`;
  try {
    await writeFile(temporary, declarations, { flag: "wx" });
    await rename(temporary, path);
  } catch (error) {
    await rm(temporary, { force: true });
    throw error;
  }
}

async function safeTypeDeclarationsPath(path: string, manifestPath: string, assets: ReadonlyMap<string, string>, base: string): Promise<string> {
  const ancestors = ancestorsBelowCommonRoot(path, base);
  for (const current of ancestors) {
    try {
      if (lstatSync(current).isSymbolicLink())
        throw new Error(`Generated Runic type declaration paths must not traverse symbolic links: '${path}'.`);
    } catch (error) {
      if (errorCode(error) !== "ENOENT") throw error;
    }
  }
  await mkdir(dirname(path), { recursive: true });
  for (const current of ancestors) {
    if (lstatSync(current).isSymbolicLink())
      throw new Error(`Generated Runic type declaration paths must not traverse symbolic links: '${path}'.`);
  }
  try {
    if (lstatSync(path).isSymbolicLink())
      throw new Error(`Generated Runic type declaration path must not be a symbolic link: '${path}'.`);
  } catch (error) {
    if (errorCode(error) !== "ENOENT") throw error;
  }
  let canonical: string;
  try {
    canonical = await realpath(path);
  } catch (error) {
    if (errorCode(error) !== "ENOENT") throw error;
    canonical = join(await realpath(dirname(path)), basename(path));
  }
  const manifest = await realpath(manifestPath);
  const folded = canonical.toLowerCase();
  if ([manifest, ...assets.values()].some(protectedPath =>
    protectedPath === canonical || protectedPath.toLowerCase() === folded))
    throw new Error("Generated Runic virtual type declarations must not overwrite the manifest or one of its assets.");
  return canonical;
}

function readProject(config: string, output: string): ProjectSources {
  let settings: { schemaVersion?: unknown; catalog?: unknown; sourceRoots?: unknown } | null;
  try {
    settings = JSON.parse(readFileSync(config, "utf8")) as typeof settings;
  } catch (error) {
    throw new Error(`Could not read Runic translation project '${config}': ${(error as Error).message}`);
  }
  if (!settings || settings.schemaVersion !== 1 || typeof settings.catalog !== "string" || settings.catalog.length === 0)
    throw new Error("The Runic translation project must declare schemaVersion 1 and a catalog ID.");
  const project = dirname(config);
  const sourceFiles = [config];
  function discover(directory: string): void {
    ensureNoSymlinkAncestors(directory, project);
    if (lstatSync(directory).isSymbolicLink()) throw new Error("Translation source roots must not be symbolic links.");
    for (const entry of readdirSync(directory, { withFileTypes: true })) {
      const path = join(directory, entry.name);
      if (isWithin(output, path)) continue;
      if (entry.isSymbolicLink()) throw new Error("Translation sources must not traverse symbolic links.");
      if (entry.isDirectory()) discover(path);
      else if (entry.isFile() && /\.(?:mf2|rmf2)$/i.test(path)) sourceFiles.push(path);
    }
  }
  const mounts = settings.sourceRoots;
  if (mounts !== undefined && (!Array.isArray(mounts) || mounts.some(mount => !mount || typeof mount.path !== "string" || mount.path.length === 0)))
    throw new Error("Runic translation sourceRoots must contain non-empty paths.");
  const roots = mounts ? (mounts as { path: string }[]).map(mount => resolve(project, mount.path)) : [project];
  for (const root of roots) discover(root);
  return {
    manifest: contained(output, `${settings.catalog}.esm-v5/web-module-manifest-v3.json`),
    sourceFiles: Object.freeze(sourceFiles.sort()),
    sourceRoots: Object.freeze(roots),
    watchRoots: Object.freeze([...new Set([project, ...roots])]),
  };
}

function ensureNoSymlinkAncestors(path: string, project: string): void {
  for (const ancestor of ancestorsBelowCommonRoot(path, project))
    if (lstatSync(ancestor).isSymbolicLink()) throw new Error("Translation source roots must not traverse symbolic links.");
}

// Returns the ancestors of path below the deepest directory it shares with base. Components above
// that belong to the environment: macOS /tmp and /var, or a symlinked home or checkout, are links a
// project cannot avoid. Callers check the path itself.
function ancestorsBelowCommonRoot(path: string, base: string): string[] {
  const target = resolve(path);
  let boundary = resolve(base);
  while (!isWithin(boundary, target)) {
    const parent = dirname(boundary);
    if (parent === boundary) return [];
    boundary = parent;
  }
  const ancestors: string[] = [];
  for (let current = dirname(target); current !== boundary && isWithin(boundary, current); current = dirname(current))
    ancestors.push(current);
  return ancestors;
}

function isWithin(root: string, path: string): boolean {
  const candidate = relative(root, path);
  return candidate === "" || (!isAbsolute(candidate) && candidate !== ".." && !candidate.startsWith(`..${sep}`));
}

function contained(root: string, relativePath: unknown): string {
  if (typeof relativePath !== "string" || isAbsolute(relativePath)) throw new Error("A generated module path must be relative.");
  const path = resolve(root, normalize(relativePath));
  const boundary = root.endsWith(sep) ? root : root + sep;
  if (!path.startsWith(boundary)) throw new Error(`Generated module path escapes its manifest root: '${relativePath}'.`);
  return path;
}

async function containedReal(root: string, realRoot: string, relativePath: string): Promise<string> {
  const path = contained(root, relativePath);
  let current = root;
  for (const component of normalize(relativePath).split(sep)) {
    current = join(current, component);
    if (lstatSync(current).isSymbolicLink())
      throw new Error(`Generated module paths must not traverse symbolic links: '${relativePath}'.`);
  }
  const realPath = await realpath(path);
  if (!isWithin(realRoot, realPath))
    throw new Error(`Generated module path escapes its manifest root through a symbolic link: '${relativePath}'.`);
  return realPath;
}

function isGenerated(path: string, manifestPath: string): boolean {
  const root = dirname(manifestPath);
  return path === manifestPath || path.startsWith(root + sep);
}

function toVitePath(path: string): string {
  return path.split(sep).join("/");
}

function errorCode(error: unknown): unknown {
  return (error as { code?: unknown } | null)?.code;
}
