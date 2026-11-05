import { existsSync, readFileSync, realpathSync, statSync } from "node:fs";
import { dirname, isAbsolute, join, normalize, relative, resolve, sep } from "node:path";

/**
 * A library catalog that a dependency ships: `package.json#runicTranslations` names the catalog and its packed `runic.json`
 * (the library's sources), and `exports["./runic-translations"]` its prebuilt web module manifest, whose directory the
 * library's own code imports from.
 */
export interface DependencyCatalog {
  readonly packageName: string;
  readonly packageRoot: string;
  readonly catalog: string;
  /** The library's packed runic.json, which the plugin regenerates the catalog from. */
  readonly project: string;
  /** The directory of the library's prebuilt catalog (`<catalog>.esm-v5`), whose modules are redirected to the regenerated ones. */
  readonly prebuiltRoot: string;
}

const catalogPattern = /^[a-z][a-z0-9.-]*$/;
const dependencyFields = ["dependencies", "devDependencies", "peerDependencies", "optionalDependencies"] as const;

/**
 * Finds the library catalogs of the application's direct dependencies (`package.json` in `root`). A dependency without
 * `runicTranslations` is not a catalog. Members a later release adds to `runicTranslations` are ignored.
 */
export function discoverDependencyCatalogs(root: string): DependencyCatalog[] {
  const manifest = join(root, "package.json");
  if (!existsSync(manifest)) throw new Error(`dependencies: "auto" needs the application's package.json in '${root}'.`);
  const application = JSON.parse(readFileSync(manifest, "utf8")) as Record<string, unknown>;
  const names = new Set<string>();
  for (const field of dependencyFields) {
    const value = application[field];
    if (value && typeof value === "object" && !Array.isArray(value)) for (const name of Object.keys(value)) names.add(name);
  }
  const catalogs: DependencyCatalog[] = [];
  const byCatalog = new Map<string, string>();
  for (const name of [...names].sort()) {
    const packageJson = findPackage(root, name);
    if (!packageJson) continue;
    const packageRoot = realpathSync(dirname(packageJson));
    const description = JSON.parse(readFileSync(packageJson, "utf8")) as Record<string, unknown>;
    const declared = description.runicTranslations;
    if (declared === undefined) continue;
    const fail = (reason: string): never => { throw new Error(`Dependency '${name}' declares an invalid runicTranslations catalog: ${reason}`); };
    if (!declared || typeof declared !== "object" || Array.isArray(declared)) fail("package.json#runicTranslations must be an object.");
    const { catalog, project } = declared as Record<string, unknown>;
    if (typeof catalog !== "string" || !catalogPattern.test(catalog)) fail("runicTranslations.catalog must be a catalog ID such as 'acme.grid'.");
    if (typeof project !== "string" || project.length === 0) fail("runicTranslations.project must name the packed runic.json.");
    // runic-* catalog IDs belong to the first-party Runic packages (D055.3).
    if ((catalog as string).startsWith("runic-") && !name.startsWith("@runic-artifex/"))
      fail(`catalog ID '${catalog as string}' is reserved for the Runic packages.`);
    const projectPath = inside(packageRoot, project as string, fail);
    if (!existsSync(projectPath) || !statSync(projectPath).isFile()) fail(`the packed runic.json '${project as string}' is missing.`);
    const exported = exportTarget((description as { exports?: unknown }).exports);
    if (exported === undefined) fail("package.json must export './runic-translations' as its web module manifest.");
    const manifestPath = inside(packageRoot, exported as string, fail);
    if (!existsSync(manifestPath)) fail(`the exported web module manifest '${exported as string}' is missing.`);
    const prebuilt = JSON.parse(readFileSync(manifestPath, "utf8")) as { catalog?: unknown };
    if (prebuilt?.catalog !== catalog) fail(`the exported web module manifest is of catalog '${String(prebuilt?.catalog)}', not '${catalog as string}'.`);
    const other = byCatalog.get(catalog as string);
    if (other) throw new Error(`Dependencies '${other}' and '${name}' both ship catalog '${catalog as string}'; catalog IDs must be unique.`);
    byCatalog.set(catalog as string, name);
    catalogs.push(Object.freeze({ packageName: name, packageRoot, catalog: catalog as string, project: projectPath, prebuiltRoot: realpathSync(dirname(manifestPath)) }));
  }
  return catalogs;
}

// Node's lookup of a package directory: node_modules/<name> from root upward.
function findPackage(root: string, name: string): string | undefined {
  for (let directory = resolve(root); ; directory = dirname(directory)) {
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
