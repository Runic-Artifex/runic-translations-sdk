import { execFile } from "node:child_process";
import { createHash, randomUUID } from "node:crypto";
import { existsSync, lstatSync, readFileSync, readdirSync, statSync } from "node:fs";
import { mkdir, readFile, realpath, rename, rm, writeFile } from "node:fs/promises";
import { basename, dirname, isAbsolute, join, normalize, relative, resolve, sep } from "node:path";
import { promisify } from "node:util";
import type { HotUpdateOptions, Logger, Plugin, ViteDevServer } from "vite";
import { CompilerUnavailable, PersistentCompiler } from "./compiler-service.js";
import { composeDeclarationMap, readMessagesDeclarationMap, writeDeclarationMap, type DeclarationCopy } from "./declaration-map.js";
import { discoverDependencyCatalogs, type DependencyCatalog } from "./dependencies.js";
import {
  classify, devClientCode, devClientId, devClientSpecifier, hasMessagesAnchor, messageModulePattern, selfAccepting, withoutSourceHash,
  type CatalogSnapshot, type Classification,
} from "./hmr.js";

interface ApplicationSettings {
  readonly locales?: readonly string[];
  readonly overrides: ReadonlyMap<string, { readonly from: readonly string[]; readonly directory: string }>;
}


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
  /**
   * Report, after a production build, the catalog's messages that no bundle of the build contains: `m` is a module
   * namespace, so a message the application never reads is tree-shaken away. Logged as information, once per build
   * environment that includes the catalog. Messages read only dynamically (the `dynamic` entry, keys computed at run
   * time) or by another build are reported too. Defaults to false.
   */
  readonly reportUnused?: boolean;
  /**
   * Generate the development pseudo-locales that runic.json declares in `pseudoLocales` (`en-XA`, `ar-XB`) in project
   * mode. Defaults to true when Vite's mode is `development` (the dev server's default) and false otherwise, so
   * production builds leave them out.
   */
  readonly pseudoLocales?: boolean;
  /**
   * `"auto"` regenerates the library catalogs of the application's dependencies: every direct dependency whose
   * `package.json` has `runicTranslations` (its catalog ID and packed `runic.json`) and exports `./runic-translations` (its
   * prebuilt web module manifest). Each is compiled with the application's overrides (runic.json `overrides`,
   * `translations/overrides/<catalog>/`) and filtered to `locales`, into `<output>/dependencies/` with one runtime that all
   * of them share. The library's imports of its own catalog are redirected there, its catalog is available as
   * `virtual:runic-translations/<catalog>`, and its types are written to `<output>/dependencies/<catalog>.d.ts`. Uses the
   * same compiler command as project mode. Defaults to false.
   */
  readonly dependencies?: "auto" | false;
  /**
   * The application's locales: dependency catalogs keep only the library locales these resolve to (with their fallbacks
   * and the library's base locale). Defaults to the locales of the project's runic.json in project mode, else all.
   */
  readonly locales?: readonly string[];
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
  invalidateModule(module: Module, seen?: Set<Module>, timestamp?: number, isHmr?: boolean): void;
}

/** Catalog state before a validated refresh, used to invalidate both old and new virtual modules. */
interface Change {
  readonly catalogs: readonly string[];
  readonly generatedPaths: ReadonlySet<string>;
  /** What the change means for an open page; a reload unless a dev server compared the regenerated files. */
  readonly update: Classification;
}

/** The hot channel and module graph of one Vite environment, as hotUpdate sees them. */
interface HotEnvironment {
  readonly moduleGraph: ModuleGraph<unknown>;
  readonly hot?: { send(payload: { readonly type: "full-reload"; readonly path: string }): void };
}

const prefix = "\0virtual:runic-translations/";
const entryKinds: readonly EntryKind[] = ["messages", "runtime", "server", "transport", "dynamic"];
const supportedEsmAbiVersion = 4;
const supportedRmf2RuntimeAbiVersion = 3;
const catalogPattern = /^[a-z][a-z0-9.-]*$/;
const execFileAsync = promisify(execFile);

/** Exposes compiler-generated ESM without coupling messages to Vite or a UI framework. */
export function runicTranslations(options: RunicTranslationsOptions = {}): Plugin {
  if (!options || typeof options !== "object" || Array.isArray(options))
    throw new TypeError("runicTranslations options must be an object.");
  if (options.persistentCompiler !== undefined && typeof options.persistentCompiler !== "boolean")
    throw new TypeError("persistentCompiler must be a boolean.");
  if (options.pseudoLocales !== undefined && typeof options.pseudoLocales !== "boolean")
    throw new TypeError("pseudoLocales must be a boolean.");
  if (options.persistentCompilerTimeout !== undefined &&
      (typeof options.persistentCompilerTimeout !== "number" || !Number.isFinite(options.persistentCompilerTimeout) || options.persistentCompilerTimeout <= 0))
    throw new TypeError("persistentCompilerTimeout must be a positive number of milliseconds.");
  if (options.reportUnused !== undefined && typeof options.reportUnused !== "boolean")
    throw new TypeError("reportUnused must be a boolean.");
  if (options.dependencies !== undefined && options.dependencies !== false && options.dependencies !== "auto")
    throw new TypeError('dependencies must be "auto" or false.');
  if (options.locales !== undefined && (!Array.isArray(options.locales) || options.locales.some(locale => typeof locale !== "string" || locale.length === 0)))
    throw new TypeError("locales must be an array of locale tags.");
  const compiler = options.manifest === undefined ? projectOptions(options) : undefined;
  let manifestPath = compiler?.manifest ?? resolve(options.manifest!);
  if (options.typeDeclarations !== undefined && options.typeDeclarations !== false &&
      (typeof options.typeDeclarations !== "string" || options.typeDeclarations.length === 0))
    throw new TypeError("typeDeclarations must be a non-empty path or false.");
  const typeDeclarationsBase = compiler?.cwd ?? dirname(manifestPath);
  // The application's output: a manifest inside node_modules (a library's) never gets declarations beside it.
  const applicationOutput = compiler?.output ?? resolve(options.cwd ?? process.cwd(), ".runic/translations");
  const typeDeclarationsPath = options.typeDeclarations === false ? undefined : resolve(
    compiler?.cwd ?? process.cwd(),
    options.typeDeclarations ?? (compiler ? join(compiler.output, "virtual.d.ts")
      : manifestPath.split(sep).includes("node_modules") ? join(applicationOutput, "virtual.d.ts") : join(dirname(manifestPath), "virtual.d.ts")),
  );
  // dependencies: "auto": the regenerated library catalogs, discovered in the config hook.
  const tool = compiler ?? toolOptions(options);
  const dependencyOutput = join(applicationOutput, "dependencies");
  let dependencyCatalogs: readonly DependencyCatalog[] = [];
  let dependencyEntries = new Map<string, Entries>();
  let dependencyPaths: ReadonlySet<string> = new Set();
  let dependencyRegeneration: Promise<unknown> = Promise.resolve();
  // The settings the dependency catalogs were last generated with, to regenerate when runic.json changes them.
  let dependencySettingsKey: string | undefined;
  let dependencyWarnings: readonly string[] = [];
  let settingsCache: { readonly stamp: string; readonly value: ApplicationSettings } | undefined;
  const reportedWarnings = new Set<string>();
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
  // Development only: the generated files of the last validated regeneration of the catalog and of the dependency
  // catalogs, which the next regeneration is compared with, and the message modules the transform could not make
  // self-accepting.
  let catalogSnapshot: CatalogSnapshot | undefined;
  let dependencySnapshots: readonly CatalogSnapshot[] | undefined;
  const notSelfAccepting = new Set<string>();
  let compilation: Promise<unknown> = Promise.resolve();
  // The compiled change of each file event, by path and timestamp, which Vite's environments (client, ssr, ...) share.
  // Vite handles the events of different files concurrently, so several are in flight at once.
  const changes = new Map<string, Promise<Change>>();
  // The latest event of each path, so a settle check of an earlier event leaves the check to the later one.
  const latestEvents = new Map<string, object>();
  let logger: Logger | undefined;
  // reportUnused logs once per build environment, not once per output: `build.rollupOptions.output` may list several,
  // and library mode bundles each of `build.lib.formats` separately. A watch rebuild reports again.
  const reportedEnvironments = new Set<string>();
  // Resolved from Vite's mode in configResolved unless the option sets it.
  let pseudoLocales = options.pseudoLocales === true;

  async function generate(project: CompilerProject): Promise<void> {
    if (service && !service.disabledReason) {
      try {
        await service.generate({ project: project.project, output: project.output, emit: ["esm"], ...(pseudoLocales ? { pseudoLocales: true } : {}) });
        return;
      } catch (error) {
        if (!(error instanceof CompilerUnavailable)) throw error;
      }
    }
    await execFileAsync(project.command,
      [...project.commandArguments, "generate", "--project", project.project, "--output", project.output, "--emit-esm", ...(pseudoLocales ? ["--pseudo-locales"] : [])],
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
    const loaded = await loadManifest(nextManifestPath);
    const { document, root, assets, entries: nextEntries, generatedPaths: nextGeneratedPaths, typesEntry } = loaded;
    const nextSnapshot = server ? snapshotOf(loaded) : undefined;
    if (typeDeclarationsPath) {
      const safeDeclarationsPath = await safeTypeDeclarationsPath(typeDeclarationsPath, nextManifestPath, assets, typeDeclarationsBase);
      await writeTypeDeclarations(safeDeclarationsPath, document.catalog, root, assets, typesEntry, dependencyDeclarations(safeDeclarationsPath));
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
    catalogSnapshot = nextSnapshot;
  }

  function virtualId(kind: EntryKind, id = catalog): string {
    return `${prefix}${id}/${kind}`;
  }

  function warn(message: string): void {
    if (reportedWarnings.has(message)) return;
    reportedWarnings.add(message);
    if (logger) logger.warn(`[runic-translations] ${message}`, { timestamp: true });
    else console.warn(`[runic-translations] ${message}`);
  }

  // The application's locales (the option, else its runic.json locales) and overrides per catalog (runic.json overrides).
  // Read once per change of runic.json; while it is invalid (mid-edit in the development server), the last valid settings
  // stay in effect and the problem is reported once.
  function applicationSettings(): ApplicationSettings {
    if (!compiler) return { locales: options.locales, overrides: new Map() };
    let stamp: string;
    try {
      const status = statSync(compiler.config);
      stamp = `${status.mtimeMs}:${status.size}`;
    } catch (error) {
      if (settingsCache) return settingsCache.value;
      throw error;
    }
    if (settingsCache?.stamp === stamp) return settingsCache.value;
    let value: ApplicationSettings;
    try {
      value = readApplicationSettings(compiler);
    } catch (error) {
      if (!settingsCache) throw error;
      warn(`${compiler.config} cannot be read (${(error as Error).message}); its last valid overrides and locales stay in effect.`);
      settingsCache = { stamp, value: settingsCache.value };
      return settingsCache.value;
    }
    settingsCache = { stamp, value };
    return value;
  }

  function readApplicationSettings(project: CompilerProject): ApplicationSettings {
    const overrides = new Map<string, { from: string[]; directory: string }>();
    const settings = JSON.parse(readFileSync(project.config, "utf8")) as { baseLocale?: unknown; locales?: unknown; overrides?: unknown };
    if (!settings || typeof settings !== "object" || Array.isArray(settings)) throw new Error("runic.json must contain a JSON object.");
    let locales = options.locales;
    if (locales === undefined && Array.isArray(settings.locales))
      locales = settings.locales.map(item => typeof item === "string" ? item : (item as { tag?: unknown })?.tag).filter((tag): tag is string => typeof tag === "string");
    if (locales && typeof settings.baseLocale === "string" && !locales.includes(settings.baseLocale)) locales = [settings.baseLocale, ...locales];
    if (Array.isArray(settings.overrides))
      for (const entry of settings.overrides as { catalog?: unknown; from?: unknown }[])
        if (typeof entry?.catalog === "string") {
          // "from" is a package name or a list of them, such as the NuGet and npm packages of a library shipped both ways.
          const from = typeof entry.from === "string" ? [entry.from] : Array.isArray(entry.from) && entry.from.length > 0 &&
            entry.from.every(item => typeof item === "string") ? entry.from as string[] : undefined;
          if (from) overrides.set(entry.catalog, { from, directory: join(project.project, "overrides", entry.catalog) });
        }
    return { locales, overrides };
  }

  function settingsKey(settings: ApplicationSettings): string {
    return JSON.stringify([settings.locales ?? null, [...settings.overrides].map(([catalog, { from }]) => [catalog, from])]);
  }

  // The directories whose files make up the dependency catalogs: the libraries' packed sources and the application's
  // overrides (the whole overrides directory, so a new catalog directory is seen).
  function dependencyWatchPaths(settings: ApplicationSettings): string[] {
    return [...dependencyCatalogs.map(dependency => dirname(dependency.project)), ...(compiler ? [join(compiler.project, "overrides")] : []),
      ...[...settings.overrides.values()].map(item => item.directory)];
  }

  // The declarations of the dependency catalogs, referenced from the application's declarations.
  function dependencyDeclarations(from: string): string[] {
    return dependencyCatalogs.map(dependency => toVitePath(relative(dirname(from), join(dependencyOutput, `${dependency.catalog}.d.ts`))));
  }

  // Compiles every dependency catalog with the application's overrides and locales into one directory with one shared
  // runtime, then checks each regenerated manifest. A failure keeps the last validated catalogs.
  // Resolves with the generated paths and snapshots before this regeneration, taken in turn, so concurrent changes compare
  // each regeneration with the one before it.
  function regenerateDependencies(): Promise<DependencyGeneration> {
    const next = dependencyRegeneration.catch(() => undefined).then(async (): Promise<DependencyGeneration> => {
      const before: DependencyGeneration = { paths: dependencyPaths, snapshots: dependencySnapshots };
      if (options.dependencies !== "auto") return before;
      const settings = applicationSettings();
      const { locales, overrides } = settings;
      // As RTR0104 in a .NET build: every override must name a catalog that a dependency ships, from one of the packages it
      // names. "from" may list the library's NuGet package too, which never matches an npm package name.
      for (const [overridden, { from }] of overrides) {
        const dependency = dependencyCatalogs.find(item => item.catalog === overridden);
        const npm = from.filter(name => name.startsWith("@") || !/[A-Z]/.test(name));
        const named = (npm.length > 0 ? npm : from).map(name => `'${name}'`).join(" or ");
        if (!dependency)
          throw new Error(`RTR0104: Catalog '${overridden}' to override is not shipped by any dependency; add ${named} to the application's ` +
            `package.json dependencies (its package.json must declare runicTranslations), or correct "catalog" in runic.json.`);
        if (!from.includes(dependency.packageName))
          throw new Error(`RTR0104: Catalog '${overridden}' comes from package '${dependency.packageName}', not ${named}; correct "from" in runic.json.`);
      }
      // Override sources of a catalog that runic.json does not list are ignored, as in a .NET build.
      if (compiler) {
        const root = join(compiler.project, "overrides");
        if (existsSync(root))
          for (const entry of readdirSync(root, { withFileTypes: true }))
            if (entry.isDirectory() && !overrides.has(entry.name))
              warn(`RTR0104: translations/overrides/${entry.name} is not listed in runic.json "overrides"; its sources are ignored.`);
      }
      if (dependencyCatalogs.length === 0) {
        dependencySettingsKey = settingsKey(settings);
        return before;
      }
      const entriesByCatalog = new Map<string, Entries>();
      const paths = new Set<string>();
      const snapshots: CatalogSnapshot[] = [];
      for (const dependency of dependencyCatalogs) {
        const directory = overrides.get(dependency.catalog)?.directory;
        await execFileAsync(tool.command, [...tool.commandArguments, "generate", "--project", dependency.project, "--output", dependencyOutput, "--emit-esm", "--shared-runtime",
          ...(directory && existsSync(directory) ? ["--overrides", directory] : []), ...(locales ? ["--locales", locales.join(",")] : [])],
        { cwd: tool.cwd, maxBuffer: 16 * 1024 * 1024 });
        const manifest = join(dependencyOutput, `${dependency.catalog}.esm-v5`, "web-module-manifest-v3.json");
        const loaded = await loadManifest(manifest);
        if (loaded.document.catalog !== dependency.catalog)
          throw new Error(`The regenerated catalog of '${dependency.packageName}' is '${loaded.document.catalog}', not '${dependency.catalog}'.`);
        // Overrides and locale filtering keep the library's contract, so a different fingerprint means the package's packed
        // sources are not those its prebuilt catalog was built from: its code could import modules the regeneration lacks.
        for (const copy of dependency.copies)
          if (copy.contractFingerprint !== loaded.document.contractFingerprint)
            throw new Error(`The catalog '${dependency.catalog}' regenerated from the packed runic.json of '${dependency.packageName}@${dependency.version}' ` +
              `has contract fingerprint ${loaded.document.contractFingerprint}, but the prebuilt catalog its code imports (${copy.prebuiltRoot}) has ` +
              `${copy.contractFingerprint}: the package's runic/ sources do not match its prebuilt catalog. Ask its author to republish it ` +
              `with both generated from the same sources, or leave dependencies off.`);
        if (typeDeclarationsPath) {
          const declarations = await safeTypeDeclarationsPath(join(dependencyOutput, `${dependency.catalog}.d.ts`), manifest, loaded.assets, applicationOutput);
          await writeTypeDeclarations(declarations, dependency.catalog, loaded.root, loaded.assets, loaded.typesEntry);
        }
        entriesByCatalog.set(dependency.catalog, loaded.entries);
        for (const path of loaded.generatedPaths) paths.add(path);
        if (server) snapshots.push(snapshotOf(loaded));
      }
      dependencyEntries = entriesByCatalog;
      dependencyPaths = paths;
      dependencySnapshots = server ? snapshots : undefined;
      dependencySettingsKey = settingsKey(settings);
      server?.watcher.add(dependencyWatchPaths(settings));
      return before;
    });
    dependencyRegeneration = next;
    return next;
  }

  // The regenerated module for an import of a module of a library's prebuilt catalog (any installed copy of it), undefined
  // for a path outside every prebuilt catalog, and an error for a module the regenerated catalog lacks: falling through to
  // the prebuilt module would load a second runtime for the same catalog.
  async function redirect(path: string): Promise<{ readonly target?: string; readonly error?: string } | undefined> {
    for (const dependency of dependencyCatalogs) {
      for (const copy of dependency.copies) {
        if (!isWithin(copy.prebuiltRoot, path)) continue;
        const relativePath = relative(copy.prebuiltRoot, path);
        const directory = join(dependencyOutput, `${dependency.catalog}.esm-v5`);
        const target = join(existsSync(directory) ? await realpath(directory) : directory, relativePath);
        if (dependencyPaths.has(target)) return { target };
        return { error: `'${dependency.packageName}' imports '${toVitePath(relativePath)}' of its prebuilt catalog '${dependency.catalog}', which the ` +
          `regenerated catalog does not have. The package's packed runic/ sources do not match its prebuilt catalog.` };
      }
    }
    return undefined;
  }

  function isDependencySource(path: string): boolean {
    if (dependencyCatalogs.length === 0) return false;
    const { overrides } = applicationSettings();
    return dependencyCatalogs.some(dependency => isWithin(dirname(dependency.project), path) ||
      (overrides.get(dependency.catalog) !== undefined && isWithin(overrides.get(dependency.catalog)!.directory, path)));
  }

  // An importer inside another installed copy of a discovered package (node_modules/<name>/ outside every discovered copy)
  // keeps its prebuilt catalog and runtime, next to the regenerated one.
  function warnUndiscoveredCopy(importer: string): void {
    for (const dependency of dependencyCatalogs) {
      const marker = `${sep}node_modules${sep}${dependency.packageName.split("/").join(sep)}${sep}`;
      const at = importer.lastIndexOf(marker);
      if (at < 0 || dependency.copies.some(copy => isWithin(copy.packageRoot, importer))) continue;
      warn(`Another copy of '${dependency.packageName}' (${importer.slice(0, at + marker.length - 1)}) is imported, which dependency discovery did not reach: ` +
        `it keeps its prebuilt catalog '${dependency.catalog}' and runtime, without the application's overrides. Install one copy (npm dedupe), ` +
        `or add the package that depends on it to the application's dependencies.`);
    }
  }

  // runic.json changed: regenerate the dependency catalogs when their overrides or locales did, and reload.
  function settingsChanged(target: ViteDevServer): void {
    if (dependencySettingsKey === undefined || settingsKey(applicationSettings()) === dependencySettingsKey) return;
    const previous = new Set(dependencyPaths);
    const previousCatalogs = [...dependencyEntries.keys()];
    void regenerateDependencies().then(() => {
      const change = { catalogs: previousCatalogs, generatedPaths: new Set([...previous, ...dependencyPaths]), update: reload("runic.json changed the overrides or locales of the dependency catalogs") };
      reloadServer(target, change);
    }).catch((error: Error) => {
      target.ws.send({ type: "error", err: { message: error.message, stack: error.stack ?? "", plugin: "runic-translations" } });
    });
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
  // serialized; a failed change keeps the last validated catalog, and its snapshot, which the next change is compared with.
  function prepare(path: string): Promise<Change> | undefined {
    if (!isRelevant(path)) return undefined;
    const source = isSource(path);
    const operation = updates.catch(() => undefined).then(async () => {
      const previous = { catalogs: catalog ? [catalog] : [], generatedPaths, snapshot: catalogSnapshot };
      const nextProject = compiler && source ? await compile() : undefined;
      await refresh(nextProject);
      const update = compiler && path === compiler.config ? reload("runic.json changed")
        : await classified(previous.snapshot && catalogSnapshot ? classify([previous.snapshot], [catalogSnapshot]) : undefined);
      return { catalogs: [...new Set([...previous.catalogs, catalog!])], generatedPaths: new Set([...previous.generatedPaths, ...generatedPaths]), update };
    });
    updates = operation;
    return operation;
  }

  function reload(reason: string): Classification {
    return { kind: "reload", reason };
  }

  // An in-place change needs every changed message module to update itself: the transform made the loaded one
  // self-accepting, and the regenerated one still has the shape it rewrites.
  async function classified(update: Classification | undefined): Promise<Classification> {
    if (!update) return reload("the catalog was not compared with its previous generation");
    if (update.kind !== "in-place") return update;
    for (const module of update.modules)
      if (notSelfAccepting.has(module) || !hasMessagesAnchor(await readFile(module, "utf8")))
        return reload(`${basename(module)} cannot update itself: it does not have the shape of a generated message module`);
    return update;
  }

  function logReload(reason: string): void {
    const message = `[runic-translations] page reload: ${reason}`;
    if (logger) logger.info(message, { timestamp: true });
    else console.info(message);
  }

  // Applies a compiled change to one environment: in place, the changed message modules get a new HMR timestamp and are
  // returned (they accept themselves, so Vite sends one js-update each); a reload invalidates everything and sends
  // full-reload explicitly, so a module that happens to accept the virtual modules cannot keep a half-updated page.
  function apply(environment: HotEnvironment, change: Change, timestamp: number, first: boolean): unknown[] {
    const graph = environment.moduleGraph;
    const { update } = change;
    if (update.kind === "none") return [];
    if (update.kind === "in-place") {
      const seen = new Set<unknown>(), modules: unknown[] = [];
      for (const file of update.modules)
        for (const module of graph.getModulesByFile(file) ?? []) {
          graph.invalidateModule(module, seen, timestamp, true);
          modules.push(module);
        }
      return modules;
    }
    invalidate(graph, change, timestamp);
    if (first) logReload(update.reason);
    if (environment.hot) environment.hot.send({ type: "full-reload", path: "*" });
    else if (first) server?.ws.send({ type: "full-reload", path: "*" });
    return [];
  }

  // A change the watcher reported outside hotUpdate (an added or removed file, a runic.json setting): reload every
  // environment.
  function reloadServer(target: ViteDevServer, change: Change): void {
    const environments = (target as Partial<ViteDevServer>).environments;
    if (change.update.kind === "reload") logReload(change.update.reason);
    if (!environments) {
      invalidate(target.moduleGraph as ModuleGraph<unknown>, change);
      target.ws.send({ type: "full-reload", path: "*" });
      return;
    }
    for (const environment of Object.values(environments)) {
      invalidate(environment.moduleGraph as ModuleGraph<unknown>, change);
      environment.hot.send({ type: "full-reload", path: "*" });
    }
  }

  function invalidate<Module>(graph: ModuleGraph<Module>, change: Pick<Change, "catalogs" | "generatedPaths">, timestamp?: number): Module[] {
    const modules = change.catalogs.flatMap(id => entryKinds.map(kind => graph.getModuleById(`${prefix}${id}/${kind}`)))
      .filter((module): module is Module => module !== undefined);
    // Generated dependencies can keep transformed code after a virtual re-export is invalidated.
    // During HMR they also need a new timestamp: the browser caches ES modules by URL, so a
    // re-fetched virtual module would otherwise import the previous generated messages again.
    const seen = new Set<Module>();
    const invalidateModule = (module: Module) => timestamp === undefined
      ? graph.invalidateModule(module)
      : graph.invalidateModule(module, seen, timestamp, true);
    for (const generated of change.generatedPaths)
      for (const module of graph.getModulesByFile(generated) ?? []) invalidateModule(module);
    for (const module of modules) invalidateModule(module);
    return modules;
  }

  // The watcher reports only the first change of a file within 50 ms (chokidar's throttle) and never the later ones, so a
  // save that follows another within that time, after the compiler read the file, would be lost. Once the change is
  // handled and that time has passed, a file that differs from what it was before the change is reported again, so the
  // last save always wins. A later event of the same file does its own check.
  function settle(path: string, before: string, change: Promise<unknown>): void {
    const arrived = Date.now(), event = {};
    latestEvents.set(path, event);
    void change.catch(() => undefined).then(() => {
      const timer = setTimeout(() => {
        if (latestEvents.get(path) !== event || !server || fingerprint(path) === before) return;
        latestEvents.delete(path);
        server.watcher.emit("change", path);
      }, Math.max(0, arrived + watcherThrottle - Date.now()));
      timer.unref?.();
    });
  }

  function developmentTransform(code: string, id: string): { code: string; map: null } | null {
    const path = resolve(id.replace(/[?#].*$/, ""));
    let result = code;
    if (!compiler && !pseudoLocales && generatedPaths.has(path)) result = withoutPseudoLocales(result);
    if (isMessageModule(path)) {
      const accepting = selfAccepting(result);
      if (accepting === undefined) notSelfAccepting.add(path);
      else {
        notSelfAccepting.delete(path);
        result = accepting;
      }
    }
    return result === code ? null : { code: result, map: null };
  }

  // A generated declaration file, or its declaration map (`.d.ts.map`, written next to it for editor navigation).
  function isGeneratedDeclaration(path: string): boolean {
    const declaration = path.endsWith(".map") ? path.slice(0, -".map".length) : path;
    return /\.d\.[cm]?ts$/.test(declaration) &&
      (declaration === typeDeclarationsPath || isWithin(applicationOutput, declaration) || isWithin(dirname(manifestPath), declaration));
  }

  // A generated message module of the catalog or of a dependency catalog, which the dev server makes self-accepting.
  function isMessageModule(path: string): boolean {
    return (generatedPaths.has(path) || dependencyPaths.has(path)) && messageModulePattern.test(`${basename(dirname(path))}/${basename(path)}`);
  }

  return {
    name: "runic-translations",
    enforce: "pre",

    // dependencies: "auto": Vite must load the libraries through this plugin, so it does not pre-bundle them in development
    // or leave them to Node during server rendering.
    config(config, environment) {
      // The dev server does not watch the generated declarations: the plugin writes them and needs no event for them,
      // and a framework plugin that reloads on any .d.ts change (Analog) would turn every edit into a page reload.
      const watch = environment?.command === "serve" && config.server?.watch !== null
        ? { server: { watch: { ignored: [(path: string) => isGeneratedDeclaration(resolve(path))] } } } : undefined;
      if (options.dependencies !== "auto") return watch;
      // The nearest package.json at or above the Vite root (which may be a subdirectory such as src).
      const discovery = discoverDependencyCatalogs(resolve(options.cwd ?? process.cwd(), config.root ?? "."));
      dependencyCatalogs = discovery.catalogs;
      dependencyWarnings = discovery.warnings;
      const names = [...new Set(dependencyCatalogs.map(dependency => dependency.packageName))];
      return names.length === 0 ? watch : { ...watch, optimizeDeps: { exclude: names }, ssr: { noExternal: names } };
    },

    configResolved(config) {
      logger = config.logger;
      for (const message of dependencyWarnings) warn(message);
      if (options.pseudoLocales === undefined) pseudoLocales = config.mode === "development";
    },

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
        // An added or removed override of a dependency catalog regenerates the dependencies.
        if (isDependencySource(resolve(path))) {
          const previous = new Set(dependencyPaths);
          void regenerateDependencies().then(() => {
            reloadServer(value, { catalogs: [], generatedPaths: new Set([...previous, ...dependencyPaths]),
              update: reload(`a source of a dependency catalog was added or removed (${basename(path)})`) });
          }).catch((error: Error) => {
            value.ws.send({ type: "error", err: { message: error.message, stack: error.stack ?? "", plugin: "runic-translations" } });
          });
          return;
        }
        const pending = prepare(resolve(path));
        if (!pending) return;
        void pending.then(change => {
          reloadServer(value, { ...change, update: reload(`a translation source was added or removed (${basename(path)})`) });
        }).catch((error: Error) => {
          value.ws.send({ type: "error", err: { message: error.message, stack: error.stack ?? "", plugin: "runic-translations" } });
        });
      };
      // runic.json "overrides" and "locales" decide what the dependency catalogs contain.
      const configChanged = (path: string) => {
        if (compiler && options.dependencies === "auto" && resolve(path) === resolve(compiler.config)) settingsChanged(value);
      };
      server.watcher.on("add", membershipChanged);
      server.watcher.on("unlink", membershipChanged);
      server.watcher.on("change", configChanged);
      if (options.dependencies === "auto") server.watcher.add(dependencyWatchPaths(applicationSettings()));
      const currentService = service;
      const cleanup = () => {
        value.watcher.off("add", membershipChanged);
        value.watcher.off("unlink", membershipChanged);
        value.watcher.off("change", configChanged);
        void currentService?.close();
      };
      server.httpServer?.once("close", cleanup);
      cleanupServer = cleanup;
    },

    watchChange() {
      reportedEnvironments.clear();
    },

    closeBundle() {
      cleanupServer?.();
    },

    // Message modules that tree-shaking left out of every chunk are messages this build never reads.
    generateBundle(_, bundle) {
      if (!options.reportUnused || !entries || !catalog) return;
      const environment = environmentName(this);
      if (reportedEnvironments.has(environment)) return;
      const report = unusedMessages(entries.messages, bundle);
      if (!report) return;
      reportedEnvironments.add(environment);
      if (report.unused.length === 0) return;
      const shown = report.unused.slice(0, 20).join(", ") + (report.unused.length > 20 ? `, and ${report.unused.length - 20} more` : "");
      const message = `[runic-translations] ${report.unused.length} of ${report.total} messages of catalog "${catalog}" are not used by this build: ${shown}.`;
      if (logger) logger.info(message, { timestamp: true });
      else this.info(message);
    },

    async buildStart() {
      const nextProject = await compile();
      await regenerateDependencies();
      await refresh(nextProject);
      for (const dependency of dependencyCatalogs) this.addWatchFile(dependency.project);
      if (compiler) this.addWatchFile(compiler.project);
      if (!compiler) this.addWatchFile(manifestPath);
      for (const path of sourceFiles) this.addWatchFile(path);
      if (!compiler) for (const path of generatedPaths) this.addWatchFile(path);
    },

    async resolveId(id, importer) {
      // The dev client, imported only by message modules the dev server made self-accepting.
      if (id === devClientSpecifier) return devClientId;
      if (!entries) await refresh();
      for (const name of [catalog, ...dependencyEntries.keys()]) {
        if (id === `virtual:runic-translations/${name}` || id === `virtual:runic-translations/${name}/messages`)
          return virtualId("messages", name);
        for (const kind of entryKinds)
          if (kind !== "messages" && id === `virtual:runic-translations/${name}/${kind}`) return virtualId(kind, name);
      }
      // A library's import of its own catalog goes to the regenerated catalog.
      if (dependencyCatalogs.length !== 0 && importer && !id.startsWith("\0")) {
        const from = importer.replace(/[?#].*$/, "");
        if (dependencyCatalogs.some(dependency => dependency.copies.some(copy => isWithin(copy.packageRoot, from)))) {
          const resolved = id.startsWith(".") ? resolve(dirname(from), id)
            : (await (this as unknown as { resolve(source: string, importer: string, options: { skipSelf: boolean }): Promise<{ id: string } | null> })
              .resolve(id, importer, { skipSelf: true }))?.id.replace(/[?#].*$/, "");
          const redirected = resolved ? await redirect(resolved) : undefined;
          if (redirected?.error) this.error(redirected.error);
          if (redirected?.target) return redirected.target;
        } else warnUndiscoveredCopy(from);
      }
      return null;
    },

    async load(id) {
      if (id === devClientId) return devClientCode;
      if (!entries) await refresh();
      for (const kind of entryKinds)
        if (id === virtualId(kind)) return `export * from ${JSON.stringify(toVitePath(entries![kind]))};\n`;
      for (const [name, dependency] of dependencyEntries)
        for (const kind of entryKinds)
          if (id === virtualId(kind, name)) return `export * from ${JSON.stringify(toVitePath(dependency[kind]))};\n`;
      return null;
    },

    // Manifest mode: the generated directory may come from a development build (a Debug dotnet build) that added the
    // pseudo-locales. Unless they are on (Vite's development mode, or the option), they are taken out of the bundle.
    //
    // With a dev server, generated message modules also accept themselves (see hmr.ts), so an edited message updates in
    // place. A build has no dev server and never reaches that branch.
    transform(code, id) {
      if (server) return developmentTransform(code, id);
      if (compiler || pseudoLocales) return null;
      const path = resolve(id.replace(/[?#].*$/, ""));
      if (!generatedPaths.has(path)) return null;
      const filtered = withoutPseudoLocales(code);
      return filtered === code ? null : { code: filtered, map: null };
    },

    // Vite calls this once per environment (client, ssr, ...) for the same change; the change is
    // compiled once and each environment invalidates its own module graph.
    async hotUpdate(update: HotUpdateOptions) {
      const path = resolve(update.file);
      if (compiler && isWithin(compiler.output, path)) return [];
      if (isWithin(dependencyOutput, path)) return [];
      const key = `${path}\0${update.timestamp}`;
      const dependency = isDependencySource(path);
      // The watcher listeners above own additions and removals.
      if (update.type !== "update") return dependency || isRelevant(path) ? [] : undefined;
      let pending = changes.get(key);
      const first = pending === undefined;
      if (pending === undefined) {
        // Read before the compiler can, for the settle check below.
        const before = fingerprint(path);
        // A library source or an application override of a dependency catalog: regenerate the dependency catalogs and
        // compare them with their previous generation, as for the application's catalog.
        pending = dependency
          ? regenerateDependencies().then(async previous => ({
            catalogs: [], generatedPaths: new Set([...previous.paths, ...dependencyPaths]),
            update: await classified(previous.snapshots && dependencySnapshots ? classify(previous.snapshots, dependencySnapshots) : undefined),
          }))
          : prepare(path);
        if (!pending) return undefined;
        changes.set(key, pending);
        for (const old of changes.keys()) {
          if (changes.size <= 64) break;
          changes.delete(old);
        }
        settle(path, before, pending);
      }
      let change: Change;
      try {
        change = await pending;
      } catch (error) {
        // Report a failed change once, from the environment that started it; the page keeps its last good text.
        if (first) throw error;
        return [];
      }
      return apply(this.environment as unknown as HotEnvironment, change, update.timestamp, first) as never[];
    },
  };
}

/** The dependency catalogs' generated paths and snapshots before a regeneration. */
interface DependencyGeneration {
  readonly paths: ReadonlySet<string>;
  readonly snapshots: readonly CatalogSnapshot[] | undefined;
}

// Longer than the time in which the watcher drops further changes of a file (chokidar: 50 ms).
const watcherThrottle = 120;

// A file's content, to see whether it changed; "" when it cannot be read.
function fingerprint(path: string): string {
  try {
    return createHash("sha256").update(readFileSync(path)).digest("hex");
  } catch {
    return "";
  }
}

/** A validated web module manifest and its generated files. */
interface LoadedManifest {
  readonly document: WebModuleManifest;
  readonly root: string;
  readonly assets: ReadonlyMap<string, string>;
  readonly entries: Entries;
  readonly generatedPaths: ReadonlySet<string>;
  readonly typesEntry: string;
  /** The validated runtime.js. */
  readonly runtimeSource: string;
}

/** The generated files of a validated catalog, which the next regeneration is compared with (development only). */
function snapshotOf(loaded: LoadedManifest): CatalogSnapshot {
  const files = new Map<string, string>(), paths = new Map<string, string>();
  for (const asset of loaded.document.assets) {
    files.set(asset.path, asset.sha256);
    paths.set(asset.path, loaded.assets.get(asset.path)!);
  }
  const shared = (loaded.document as { readonly sharedRuntime?: { readonly sha256: string } }).sharedRuntime;
  if (shared) {
    files.set("../runtime-core.js", shared.sha256);
    paths.set("../runtime-core.js", join(dirname(loaded.root), "runtime-core.js"));
  }
  const runtime = createHash("sha256").update(withoutSourceHash(loaded.runtimeSource)).digest("hex");
  return { catalog: loaded.document.catalog, files, paths, runtime };
}

/**
 * Reads and checks a web module manifest v3 and every asset it lists. Members a later compiler adds within ESM ABI 4 (on
 * the manifest, its entrypoints or its assets) are ignored, so a library built by a newer compiler patch still loads; the
 * members this plugin reads must be exactly as it expects. A shared runtime (sharedRuntime, beside the catalog directory)
 * is checked like an asset.
 */
async function loadManifest(manifestPath: string): Promise<LoadedManifest> {
  const document = JSON.parse(await readFile(manifestPath, "utf8")) as WebModuleManifest;
  if (!document || typeof document !== "object" || Array.isArray(document))
    throw new Error("The Runic Translations web module manifest must be an object.");
  if (document.webModuleManifestVersion !== 3)
    throw new Error(`Unsupported Runic Translations web module manifest version '${String(document.webModuleManifestVersion)}'.`);
  if (document.esmAbiVersion !== supportedEsmAbiVersion)
    throw new Error(`Unsupported Runic ESM ABI version '${String(document.esmAbiVersion)}'. Expected '${supportedEsmAbiVersion}'.`);
  if (document.rmf2RuntimeAbiVersion !== supportedRmf2RuntimeAbiVersion || document.messageGrammarVersion !== 5 ||
      document.profile !== "rmf2-execution-v2" || document.generatedNameVersion !== 1 ||
      typeof document.sourceHash !== "string" || !/^sha256:[a-f0-9]{64}$/.test(document.sourceHash))
    throw new Error("The Runic Translations v3 web module manifest has an incompatible RMF2 execution contract.");
  if (typeof document.catalog !== "string" || !document.entrypoints ||
      typeof document.contractFingerprint !== "string" || !/^sha256:[a-f0-9]{64}$/.test(document.contractFingerprint))
    throw new Error("The Runic Translations web module manifest is malformed.");
  if (!catalogPattern.test(document.catalog))
    throw new Error(`The Runic Translations v${document.webModuleManifestVersion} web module manifest has an invalid catalog ID.`);
  if (typeof document.entrypoints !== "object" || Array.isArray(document.entrypoints))
    throw new Error(`The Runic Translations v${document.webModuleManifestVersion} web module manifest has malformed entrypoints.`);
  const root = dirname(manifestPath);
  const realRoot = await realpath(root);
  const expectedEntrypoints: Readonly<Record<EntryKind | "types", string>> = {
    messages: "messages.js",
    types: "messages.d.ts",
    runtime: "runtime.js",
    server: "server.js",
    transport: "transport.js",
    dynamic: "dynamic.js",
  };
  if ((Object.keys(expectedEntrypoints) as (keyof typeof expectedEntrypoints)[]).some(kind => document.entrypoints[kind] !== expectedEntrypoints[kind]))
    throw new Error(`The Runic Translations v${document.webModuleManifestVersion} web module manifest has invalid entrypoints.`);
  if (!Array.isArray(document.assets))
    throw new Error("The Runic Translations web module manifest does not declare its generated assets.");
  const assets = new Map<string, string>();
  for (const asset of document.assets as unknown[]) {
    if (!asset || typeof asset !== "object" || Array.isArray(asset))
      throw new Error(`The Runic Translations v${document.webModuleManifestVersion} web module manifest contains a malformed asset.`);
    const { path: assetPath, sha256, byteLength, mediaType } = asset as Partial<Record<keyof ManifestAsset, unknown>>;
    if (typeof assetPath !== "string" || !/^(?!\/)(?!.*(?:^|\/)\.\.(?:\/|$))[A-Za-z0-9_$.-]+(?:\/[A-Za-z0-9_$.-]+)*$/.test(assetPath) || typeof sha256 !== "string" ||
        !/^[a-f0-9]{64}$/.test(sha256) || !Number.isSafeInteger(byteLength) || (byteLength as number) < 0 ||
        typeof mediaType !== "string" || !["text/javascript", "text/typescript"].includes(mediaType) || assets.has(assetPath))
      throw new Error("The Runic Translations web module manifest contains an invalid generated asset entry.");
    const path = await containedReal(root, realRoot, assetPath);
    await checkIntegrity(path, assetPath, sha256, byteLength as number);
    assets.set(assetPath, path);
  }
  for (const path of Object.values(expectedEntrypoints)) {
    contained(root, path);
    if (!assets.has(path))
      throw new Error("The Runic Translations web module manifest omits a required generated entrypoint.");
  }
  const runtime = await readFile(assets.get(expectedEntrypoints.runtime)!, "utf8");
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
  const generatedPaths = new Set(assets.values());
  // A catalog generated with a shared runtime imports ../runtime-core.js; it is the same file for every catalog of a directory.
  const shared = (document as { readonly sharedRuntime?: unknown }).sharedRuntime;
  if (shared !== undefined) {
    const { path: sharedPath, sha256, byteLength } = (shared ?? {}) as Partial<Record<"path" | "sha256" | "byteLength", unknown>>;
    if (sharedPath !== "../runtime-core.js" || typeof sha256 !== "string" || !/^[a-f0-9]{64}$/.test(sha256) || !Number.isSafeInteger(byteLength))
      throw new Error("The Runic Translations web module manifest has an invalid shared runtime.");
    const path = join(dirname(root), "runtime-core.js");
    if (lstatSync(path).isSymbolicLink()) throw new Error("A generated shared runtime must not be a symbolic link.");
    await checkIntegrity(path, sharedPath, sha256, byteLength as number);
    generatedPaths.add(await realpath(path));
  }
  const entries: Entries = Object.freeze({
    messages: assets.get(expectedEntrypoints.messages)!,
    types: assets.get(expectedEntrypoints.types)!,
    runtime: assets.get(expectedEntrypoints.runtime)!,
    server: assets.get(expectedEntrypoints.server)!,
    transport: assets.get(expectedEntrypoints.transport)!,
    dynamic: assets.get(expectedEntrypoints.dynamic)!,
  });
  return { document, root, assets, entries, generatedPaths, typesEntry: expectedEntrypoints.types, runtimeSource: runtime };
}

async function checkIntegrity(path: string, name: string, sha256: string, byteLength: number): Promise<void> {
  const content = await readFile(path);
  if (content.byteLength !== byteLength || createHash("sha256").update(content).digest("hex") !== sha256)
    throw new Error(`Generated Runic asset integrity check failed: '${name}'.`);
}

// The Vite environment a hook runs in ("client", "ssr" or a custom one).
function environmentName(context: unknown): string {
  return (context as { readonly environment?: { readonly name?: string } }).environment?.name ?? "client";
}

/**
 * The messages of a catalog whose modules no chunk renders: the generated `messages/_index.js` names each message's
 * module, and a bundle keeps only the modules the application reads. Undefined when the bundle has none of the
 * catalog's modules (another environment's build, such as a server bundle that does not use it).
 */
function unusedMessages(messagesEntry: string, bundle: Readonly<Record<string, unknown>>): { readonly unused: readonly string[]; readonly total: number } | undefined {
  const directory = join(dirname(messagesEntry), "messages");
  let index: string;
  try { index = readFileSync(join(directory, "_index.js"), "utf8"); } catch { return undefined; }
  const names = new Map<string, string>();
  for (const match of index.matchAll(/^export \{ [^ ]+ as ("(?:[^"\\]|\\.)*"|[^ ]+) \} from "\.\/([^"]+)";$/gm))
    names.set(match[2]!, match[1]!.startsWith("\"") ? JSON.parse(match[1]!) as string : match[1]!);
  const used = new Set<string>();
  let present = false;
  for (const output of Object.values(bundle)) {
    const chunk = output as { readonly type?: string; readonly modules?: Readonly<Record<string, { readonly renderedLength: number }>> };
    if (chunk.type !== "chunk" || !chunk.modules) continue;
    for (const [id, module] of Object.entries(chunk.modules)) {
      const path = normalize(id.split("?")[0]!);
      if (!isWithin(dirname(messagesEntry), path) && path !== normalize(messagesEntry)) continue;
      present = true;
      if (dirname(path) === normalize(directory) && module.renderedLength > 0) used.add(basename(path));
    }
  }
  if (!present) return undefined;
  const unused = [...names].filter(([file]) => !used.has(file)).map(([, name]) => name).sort();
  return { unused, total: names.size };
}

// The compiler command for regenerating dependency catalogs in manifest mode: project mode's defaults.
function toolOptions(options: RunicTranslationsOptions): { readonly cwd: string; readonly command: string; readonly commandArguments: readonly string[] } {
  return Object.freeze({
    cwd: resolve(options.cwd ?? process.cwd()),
    command: options.command ?? "dotnet",
    commandArguments: Object.freeze([...(options.commandArguments ?? ["tool", "run", "runic-translations", "--"])]),
  });
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

async function writeTypeDeclarations(path: string, catalog: string, root: string, assets: ReadonlyMap<string, string>, messagesTypes: string,
  references: readonly string[] = []): Promise<void> {
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
      .replace(/^\/\/# sourceMappingURL=.*$/mu, "")
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
  // messages.d.ts.map: Go to Definition on m.key() opens the message's source through <path>.map.
  const messagesMap = await readMessagesDeclarationMap(assets.get(messagesTypes)!, messages);
  const parts = [
    "// <auto-generated />",
    "// Generated from the validated Runic web module manifest. Do not edit." +
      references.map(reference => `\n/// <reference path=${JSON.stringify(reference)} />`).join(""),
    moduleDeclaration("messages", virtual("messages"), messages),
    moduleDeclaration("messages", `${virtual("messages")}/messages`, messages),
    ...(["runtime", "server", "transport", "dynamic"] as const).map(kind =>
      moduleDeclaration(kind, virtual(kind), sources.get(kind)!)),
    messagesMap ? `//# sourceMappingURL=${basename(path)}.map\n` : "",
  ];
  const declarations = parts.join("\n\n");
  let map: string | undefined;
  if (messagesMap) {
    // The two copies of messages.d.ts: each body starts below its `declare module` line, without the header rewrite drops.
    const skippedLines = /^\s*\/\/ <auto-generated \/>\s*/u.exec(messages)?.[0].split("\n").length ?? 1;
    const copies: DeclarationCopy[] = [2, 3].map(index => ({
      startLine: parts.slice(0, index).join("\n\n").split("\n").length + 2,
      lines: parts[index].split("\n").slice(1, -1),
      skippedLines: skippedLines - 1,
      indent: "  ",
    }));
    map = composeDeclarationMap(path, declarations.split("\n").length - 1, messagesMap, copies);
  }
  // Before the declarations: a moved source line changes only the map.
  await mkdir(dirname(path), { recursive: true });
  await writeDeclarationMap(`${path}.map`, map);

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

// The development pseudo-locales of runic.json pseudoLocales (Rmf2PseudoLocalesV5 in the compiler).
const pseudoLocaleTags: readonly string[] = ["en-XA", "ar-XB"];

// Takes the pseudo-locales out of generated ESM: the runtime's declared locales, so they no longer resolve, and their
// messages, so their text is not bundled. A generated module that still names a pseudo-locale message is an error rather
// than a bundle that ships it.
function withoutPseudoLocales(code: string): string {
  const pseudo = pseudoLocaleTags.map(tag => JSON.stringify(tag));
  let result = code.replace(/^export const locales = Object\.freeze\((\[[^\]\n]*\])\);$/m, (line, list: string) => {
    const tags = JSON.parse(list) as string[];
    const kept = tags.filter(tag => !pseudoLocaleTags.includes(tag));
    return kept.length === tags.length ? line : `export const locales = Object.freeze(${JSON.stringify(kept)});`;
  });
  result = result.replace(/^[ \t]*messages\[("[A-Za-z-]+")\] = freezeGenerated[A-Za-z]*\(.*\);\r?\n/gm,
    (line, tag: string) => pseudo.includes(tag) ? "" : line);
  if (pseudo.some(tag => result.includes(`messages[${tag}]`)))
    throw new Error("A generated Runic message module contains pseudo-locale text that the plugin cannot remove; regenerate the catalog without pseudo-locales.");
  return result;
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
