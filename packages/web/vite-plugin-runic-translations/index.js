import { execFile } from "node:child_process";
import { readFile } from "node:fs/promises";
import { dirname, isAbsolute, join, normalize, resolve, sep } from "node:path";
import { promisify } from "node:util";

const prefix = "\0virtual:runic-translations/";
const supportedEsmAbiVersion = 2;
const execFileAsync = promisify(execFile);

/**
 * Exposes compiler-generated ESM without coupling messages to Vite or a UI framework.
 * @param {{ manifest: string, sourceFiles?: readonly string[], compiler?: {
 *   catalog: string, documents: readonly string[], output: string, cwd?: string,
 *   command?: string, commandArguments?: readonly string[]
 * } }} options
 */
export function runicTranslations(options) {
  if (!options || typeof options.manifest !== "string" || options.manifest.length === 0)
    throw new TypeError("runicTranslations requires a manifest path.");

  const manifestPath = resolve(options.manifest);
  const compiler = compilerOptions(options.compiler);
  const sourceFiles = Object.freeze(Array.from(new Set([
    ...(options.sourceFiles ?? []).map(path => resolve(path)),
    ...(compiler ? [compiler.catalog, ...compiler.documents.filter(path => !hasGlob(path))] : []),
  ])));
  let catalog;
  let entries;
  let compilation = Promise.resolve();

  async function compile() {
    if (!compiler) return;
    const argumentsValue = [
      ...compiler.commandArguments,
      "generate",
      "--catalog", compiler.catalog,
      "--documents", ...compiler.documents,
      "--output", compiler.output,
      "--emit-esm",
    ];
    compilation = compilation.catch(() => undefined).then(() => execFileAsync(compiler.command, argumentsValue, {
      cwd: compiler.cwd,
      maxBuffer: 16 * 1024 * 1024,
    })).then(() => undefined);
    return compilation;
  }

  async function refresh() {
    const document = JSON.parse(await readFile(manifestPath, "utf8"));
    if (document.webModuleManifestVersion !== 1)
      throw new Error(`Unsupported Runic web module manifest version '${document.webModuleManifestVersion}'.`);
    if (document.esmAbiVersion !== supportedEsmAbiVersion)
      throw new Error(`Unsupported Runic ESM ABI version '${document.esmAbiVersion}'. Expected '${supportedEsmAbiVersion}'.`);
    if (typeof document.catalog !== "string" || !document.entrypoints)
      throw new Error("The Runic web module manifest is malformed.");
    catalog = document.catalog;
    const root = dirname(manifestPath);
    entries = Object.freeze({
      messages: contained(root, document.entrypoints.messages),
      runtime: contained(root, document.entrypoints.runtime),
      transport: contained(root, "transport.js"),
      dynamic: contained(root, document.entrypoints.dynamic ?? "dynamic.js"),
    });
    return document;
  }

  function virtualId(kind) {
    return `${prefix}${catalog}/${kind}`;
  }

  return {
    name: "runic-translations",
    enforce: "pre",

    async buildStart() {
      await compile();
      const document = await refresh();
      this.addWatchFile(manifestPath);
      for (const path of sourceFiles) this.addWatchFile(path);
      for (const asset of document.assets) this.addWatchFile(contained(dirname(manifestPath), asset.path));
    },

    async resolveId(id) {
      if (!entries) await refresh();
      if (id === `virtual:runic-translations/${catalog}` || id === `virtual:runic-translations/${catalog}/messages`)
        return virtualId("messages");
      if (id === `virtual:runic-translations/${catalog}/runtime`)
        return virtualId("runtime");
      if (id === `virtual:runic-translations/${catalog}/transport`)
        return virtualId("transport");
      if (id === `virtual:runic-translations/${catalog}/dynamic`)
        return virtualId("dynamic");
      return null;
    },

    async load(id) {
      if (!entries) await refresh();
      if (id === virtualId("messages")) return `export * from ${JSON.stringify(toVitePath(entries.messages))};\n`;
      if (id === virtualId("runtime")) return `export * from ${JSON.stringify(toVitePath(entries.runtime))};\n`;
      if (id === virtualId("transport")) return `export * from ${JSON.stringify(toVitePath(entries.transport))};\n`;
      if (id === virtualId("dynamic")) return `export * from ${JSON.stringify(toVitePath(entries.dynamic))};\n`;
      return null;
    },

    async handleHotUpdate(context) {
      if (context.file !== manifestPath && !sourceFiles.includes(context.file) && !isGenerated(context.file, manifestPath)) return;
      if (compiler && sourceFiles.includes(context.file)) await compile();
      await refresh();
      const modules = [virtualId("messages"), virtualId("runtime"), virtualId("transport"), virtualId("dynamic")]
        .map(id => context.server.moduleGraph.getModuleById(id))
        .filter(Boolean);
      for (const module of modules) context.server.moduleGraph.invalidateModule(module);
      return modules;
    },
  };
}

function compilerOptions(value) {
  if (value === undefined) return undefined;
  if (!value || typeof value !== "object" || Array.isArray(value))
    throw new TypeError("compiler must be an options object.");
  if (typeof value.catalog !== "string" || value.catalog.length === 0)
    throw new TypeError("compiler.catalog must be a non-empty path.");
  if (!Array.isArray(value.documents) || value.documents.length === 0 || value.documents.some(path => typeof path !== "string" || path.length === 0))
    throw new TypeError("compiler.documents must contain at least one non-empty path or glob.");
  if (typeof value.output !== "string" || value.output.length === 0)
    throw new TypeError("compiler.output must be a non-empty path.");
  if (value.command !== undefined && (typeof value.command !== "string" || value.command.length === 0))
    throw new TypeError("compiler.command must be a non-empty executable name.");
  if (value.commandArguments !== undefined && (!Array.isArray(value.commandArguments) || value.commandArguments.some(argument => typeof argument !== "string")))
    throw new TypeError("compiler.commandArguments must contain only strings.");
  const cwd = resolve(value.cwd ?? process.cwd());
  return Object.freeze({
    catalog: resolve(cwd, value.catalog),
    documents: Object.freeze(value.documents.map(path => hasGlob(path) ? path : resolve(cwd, path))),
    output: resolve(cwd, value.output),
    cwd,
    command: value.command ?? "dotnet",
    commandArguments: Object.freeze(value.commandArguments ?? ["tool", "run", "runic-translations", "--"]),
  });
}

function hasGlob(path) {
  return /[*?\[\]{}]/.test(path);
}

function contained(root, relativePath) {
  if (typeof relativePath !== "string" || isAbsolute(relativePath)) throw new Error("A generated module path must be relative.");
  const path = resolve(root, normalize(relativePath));
  const boundary = root.endsWith(sep) ? root : root + sep;
  if (!path.startsWith(boundary)) throw new Error(`Generated module path escapes its manifest root: '${relativePath}'.`);
  return path;
}

function isGenerated(path, manifestPath) {
  const root = dirname(manifestPath);
  return path === manifestPath || path.startsWith(root + sep);
}

function toVitePath(path) {
  return path.split(sep).join("/");
}
