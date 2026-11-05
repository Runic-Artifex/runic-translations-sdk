import { existsSync, readFileSync, realpathSync, statSync } from "node:fs";
import { dirname, isAbsolute, join, normalize, relative, resolve, sep } from "node:path";

/** One installed copy of a library package: its real directory and the directory of its prebuilt catalog. */
export interface DependencyCopy {
  readonly packageRoot: string;
  /** The directory of the copy's prebuilt catalog (`<catalog>.esm-v5`), whose modules are redirected to the regenerated ones. */
  readonly prebuiltRoot: string;
  /** The contract fingerprint of the copy's prebuilt catalog, which the regenerated catalog must keep. */
  readonly contractFingerprint: string;
}

/**
 * A library catalog that a dependency ships: `package.json#runicTranslations` names the catalog and its packed `runic.json`
 * (the library's sources), and `exports["./runic-translations"]` its prebuilt web module manifest, whose directory the
 * library's own code imports from.
 */
export interface DependencyCatalog {
  readonly packageName: string;
  readonly version: string;
  readonly catalog: string;
  /** The library's packed runic.json, which the plugin regenerates the catalog from (that of the copy nearest the application). */
  readonly project: string;
  /** The copy nearest the application. */
  readonly packageRoot: string;
  readonly prebuiltRoot: string;
  /** Every installed copy of this package and version that discovery reached, the nearest first; all are redirected. */
  readonly copies: readonly DependencyCopy[];
}

export interface DependencyDiscovery {
  /** The directory of the application's package.json. */
  readonly root: string;
  readonly catalogs: readonly DependencyCatalog[];
  /** Declared dependencies that are not installed. */
  readonly warnings: readonly string[];
}

const catalogPattern = /^[a-z][a-z0-9.-]*$/;
const fingerprintPattern = /^sha256:[a-f0-9]{64}$/;
// The application's own development dependencies are bundled too; a library's are not installed for its consumers.
const applicationFields = ["dependencies", "devDependencies", "peerDependencies", "optionalDependencies"] as const;
const libraryFields = ["dependencies", "peerDependencies", "optionalDependencies"] as const;
// Missing optional and peer dependencies are normal.
const requiredFields: ReadonlySet<string> = new Set(["dependencies", "devDependencies"]);

/** The directory of the nearest package.json at or above `start` (Vite's root, which may be a subdirectory such as `src`). */
export function findApplicationRoot(start: string): string {
  for (let directory = resolve(start); ; directory = dirname(directory)) {
    if (existsSync(join(directory, "package.json"))) return directory;
    if (dirname(directory) === directory) throw new Error(`dependencies: "auto" needs the application's package.json in '${resolve(start)}' or above it.`);
  }
}

/**
 * Finds the library catalogs the application depends on: every dependency of the application's `package.json` (the nearest
 * at or above `start`) that declares `runicTranslations`, and, transitively, the dependencies those libraries declare
 * (`dependencies`, `peerDependencies`, `optionalDependencies`) that declare it too. A package without `runicTranslations` is
 * not a catalog and its dependencies are not followed. Members a later release adds to `runicTranslations` are ignored.
 *
 * Catalogs are deduplicated by catalog ID: copies of the same package and version (nested `node_modules`) share one
 * regenerated catalog; two versions, or two packages, that ship one catalog ID are an error.
 */
export function discoverDependencyCatalogs(start: string): DependencyDiscovery {
  const root = findApplicationRoot(start);
  rejectPlugAndPlay(root);
  const application = readJson(join(root, "package.json"));
  const warnings: string[] = [];
  const byCatalog = new Map<string, { packageName: string; version: string; project: string; copies: DependencyCopy[] }>();
  const visited = new Set<string>();
  // Breadth first, so the copy nearest the application comes first.
  const queue: { name: string; from: string; declaredBy: string; required: boolean }[] = [];
  const enqueue = (manifest: Record<string, unknown>, from: string, declaredBy: string, fields: readonly string[]) => {
    const names = new Map<string, boolean>();
    for (const field of fields) {
      const value = manifest[field];
      if (value && typeof value === "object" && !Array.isArray(value))
        for (const name of Object.keys(value)) names.set(name, (names.get(name) ?? false) || requiredFields.has(field));
    }
    for (const [name, required] of [...names].sort(([left], [right]) => left < right ? -1 : left > right ? 1 : 0))
      queue.push({ name, from, declaredBy, required });
  };
  enqueue(application, root, "the application", applicationFields);
  for (let index = 0; index < queue.length; index++) {
    const { name, from, declaredBy, required } = queue[index];
    const packageJson = findPackage(from, name);
    if (!packageJson) {
      if (required) warnings.push(`Dependency '${name}' of ${declaredBy} is not installed in node_modules, so its catalog (if it has one) is not regenerated.`);
      continue;
    }
    const packageRoot = realpathSync(dirname(packageJson));
    if (visited.has(packageRoot)) continue;
    visited.add(packageRoot);
    const description = readJson(packageJson);
    const declared = description.runicTranslations;
    if (declared === undefined) continue;
    const fail = (reason: string): never => { throw new Error(`Dependency '${name}' (${packageRoot}) declares an invalid runicTranslations catalog: ${reason}`); };
    if (!declared || typeof declared !== "object" || Array.isArray(declared)) fail("package.json#runicTranslations must be an object.");
    const { catalog, project } = declared as Record<string, unknown>;
    if (typeof catalog !== "string" || !catalogPattern.test(catalog)) fail("runicTranslations.catalog must be a catalog ID such as 'acme.grid'.");
    if (typeof project !== "string" || project.length === 0) fail("runicTranslations.project must name the packed runic.json.");
    // runic-* catalog IDs belong to the first-party Runic packages (D055.3); npm scopes are owned, so the scope is a real check.
    if ((catalog as string).startsWith("runic-") && !name.startsWith("@runic-artifex/"))
      fail(`catalog ID '${catalog as string}' is reserved for the Runic packages (RTR0106).`);
    const projectPath = inside(packageRoot, project as string, fail);
    if (!existsSync(projectPath) || !statSync(projectPath).isFile()) fail(`the packed runic.json '${project as string}' is missing.`);
    const exported = exportTarget((description as { exports?: unknown }).exports);
    if (exported === undefined) fail("package.json must export './runic-translations' as its web module manifest.");
    const manifestPath = inside(packageRoot, exported as string, fail);
    if (!existsSync(manifestPath)) fail(`the exported web module manifest '${exported as string}' is missing.`);
    const prebuilt = readJson(manifestPath) as { catalog?: unknown; contractFingerprint?: unknown };
    if (prebuilt?.catalog !== catalog) fail(`the exported web module manifest is of catalog '${String(prebuilt?.catalog)}', not '${catalog as string}'.`);
    if (typeof prebuilt.contractFingerprint !== "string" || !fingerprintPattern.test(prebuilt.contractFingerprint))
      fail("the exported web module manifest has no valid contract fingerprint.");
    const version = typeof description.version === "string" ? description.version : "";
    const copy: DependencyCopy = Object.freeze({ packageRoot, prebuiltRoot: realpathSync(dirname(manifestPath)), contractFingerprint: prebuilt.contractFingerprint as string });
    const existing = byCatalog.get(catalog as string);
    if (!existing) byCatalog.set(catalog as string, { packageName: name, version, project: projectPath, copies: [copy] });
    else if (existing.packageName !== name)
      throw new Error(`Dependencies '${existing.packageName}' and '${name}' both ship catalog '${catalog as string}'; catalog IDs must be unique.`);
    else if (existing.version !== version)
      throw new Error(`Catalog '${catalog as string}' is installed in two versions: '${name}@${existing.version}' (${existing.copies[0].packageRoot}) and ` +
        `'${name}@${version}' (${packageRoot}). A catalog is regenerated once for the whole application, so install one version ` +
        `(for example with npm dedupe, or "overrides" or "resolutions" in the application's package.json).`);
    else existing.copies.push(copy);
    enqueue(description, packageRoot, `'${name}'`, libraryFields);
  }
  const catalogs = [...byCatalog].map(([catalog, found]) => Object.freeze({
    packageName: found.packageName, version: found.version, catalog, project: found.project,
    packageRoot: found.copies[0].packageRoot, prebuiltRoot: found.copies[0].prebuiltRoot, copies: Object.freeze(found.copies),
  }));
  return Object.freeze({ root, catalogs: Object.freeze(catalogs), warnings: Object.freeze(warnings) });
}

// Yarn Plug'n'Play installs no node_modules, so discovery would silently find nothing: under its runtime, or with a
// .pnp.cjs and no node_modules between the application and the directory that holds it.
function rejectPlugAndPlay(root: string): void {
  let marker = (process.versions as Record<string, string | undefined>).pnp !== undefined ? "the running Yarn Plug'n'Play runtime" : undefined;
  for (let directory = root; marker === undefined; directory = dirname(directory)) {
    if (existsSync(join(directory, "node_modules"))) break;
    for (const file of [".pnp.cjs", ".pnp.js"]) if (marker === undefined && existsSync(join(directory, file))) marker = join(directory, file);
    if (dirname(directory) === directory) break;
  }
  if (marker === undefined) return;
  throw new Error(`dependencies: "auto" finds library catalogs in node_modules, which Yarn Plug'n'Play does not create (${marker}). ` +
    `Set "nodeLinker: node-modules" in .yarnrc.yml, or leave dependencies off: libraries then keep their prebuilt catalogs, ` +
    `without the application's overrides and locale filtering.`);
}

function readJson(path: string): Record<string, unknown> {
  const value = JSON.parse(readFileSync(path, "utf8")) as unknown;
  if (!value || typeof value !== "object" || Array.isArray(value)) throw new Error(`'${path}' must contain a JSON object.`);
  return value as Record<string, unknown>;
}

// Node's lookup of a package directory: node_modules/<name> from the importing directory upward.
function findPackage(from: string, name: string): string | undefined {
  for (let directory = resolve(from); ; directory = dirname(directory)) {
    const candidate = join(directory, "node_modules", ...name.split("/"), "package.json");
    if (existsSync(candidate)) return candidate;
    if (dirname(directory) === directory) return undefined;
  }
}

// exports["./runic-translations"]: a path, or a conditions object whose "default" or "import" is one.
function exportTarget(exports: unknown): string | undefined {
  if (!exports || typeof exports !== "object" || Array.isArray(exports)) return undefined;
  const entry = (exports as Record<string, unknown>)["./runic-translations"];
  if (typeof entry === "string") return entry;
  if (entry && typeof entry === "object" && !Array.isArray(entry)) {
    const conditions = entry as Record<string, unknown>;
    for (const condition of ["import", "default"]) if (typeof conditions[condition] === "string") return conditions[condition] as string;
  }
  return undefined;
}

function inside(root: string, path: string, fail: (reason: string) => never): string {
  if (isAbsolute(path)) fail(`'${path}' must be relative to the package.`);
  const target = resolve(root, normalize(path));
  const candidate = relative(root, target);
  if (candidate === "" || candidate === ".." || candidate.startsWith(`..${sep}`) || isAbsolute(candidate)) fail(`'${path}' is outside the package.`);
  return target;
}
