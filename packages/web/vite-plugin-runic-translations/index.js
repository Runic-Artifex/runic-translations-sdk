import { execFile } from "node:child_process";
import { createHash } from "node:crypto";
import { readFileSync, readdirSync, statSync } from "node:fs";
import { readFile } from "node:fs/promises";
import { dirname, isAbsolute, join, normalize, resolve, sep } from "node:path";
import { promisify } from "node:util";

const prefix = "\0virtual:runic-translations/";
const supportedEsmAbiVersion = 3;
const execFileAsync = promisify(execFile);

/**
 * Exposes compiler-generated ESM without coupling messages to Vite or a UI framework.
 * @param {{ project?: string, output?: string, manifest?: string, sourceFiles?: readonly string[],
 *   command?: string, commandArguments?: readonly string[], cwd?: string }} [options]
 */
export function runicTranslations(options = {}) {
  if (!options || typeof options !== "object" || Array.isArray(options))
    throw new TypeError("runicTranslations options must be an object.");
  const project = options.manifest === undefined ? projectOptions(options) : undefined;
  const manifestPath = project?.manifest ?? resolve(options.manifest);
  const compiler = project;
  const sourceFiles = Object.freeze(Array.from(new Set([
    ...(options.sourceFiles ?? []).map(path => resolve(path)),
    ...(project?.sourceFiles ?? []),
  ])));
  let catalog;
  let entries;
  let compilation = Promise.resolve();

  async function compile() {
    if (!compiler) return;
    const argumentsValue = [...compiler.commandArguments, "generate", "--project", compiler.project, "--output", compiler.output, "--emit-esm"];
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
    if (typeof document.catalog !== "string" || !document.entrypoints ||
        typeof document.contractFingerprint !== "string" || !/^sha256:[a-f0-9]{64}$/.test(document.contractFingerprint))
      throw new Error("The Runic web module manifest is malformed.");
    catalog = document.catalog;
    const root = dirname(manifestPath);
    const requiredEntrypoints = {
      messages: document.entrypoints.messages,
      runtime: document.entrypoints.runtime,
    };
    if (!Array.isArray(document.assets))
      throw new Error("The Runic web module manifest does not declare its generated assets.");
    const assets = new Map();
    for (const asset of document.assets) {
      if (!asset || typeof asset.path !== "string" || typeof asset.sha256 !== "string" ||
          !/^[a-f0-9]{64}$/.test(asset.sha256) || !Number.isSafeInteger(asset.byteLength) || asset.byteLength < 0 ||
          typeof asset.mediaType !== "string" || assets.has(asset.path))
        throw new Error("The Runic web module manifest contains an invalid generated asset entry.");
      const path = contained(root, asset.path);
      const content = await readFile(path);
      if (content.byteLength !== asset.byteLength || createHash("sha256").update(content).digest("hex") !== asset.sha256)
        throw new Error(`Generated Runic asset integrity check failed: '${asset.path}'.`);
      assets.set(asset.path, path);
    }
    for (const path of Object.values(requiredEntrypoints)) {
      if (typeof path !== "string")
        throw new Error("The Runic web module manifest omits a required generated entrypoint.");
      contained(root, path);
      if (!assets.has(path))
        throw new Error("The Runic web module manifest omits a required generated entrypoint.");
    }
    const runtime = await readFile(assets.get(requiredEntrypoints.runtime), "utf8");
    const runtimeFingerprint = /^export const contractFingerprint = ("sha256:[a-f0-9]{64}");$/m.exec(runtime)?.[1];
    if (runtimeFingerprint !== JSON.stringify(document.contractFingerprint))
      throw new Error("The Runic web module manifest fingerprint does not match its generated runtime.");
    entries = Object.freeze({
      messages: assets.get(requiredEntrypoints.messages),
      runtime: assets.get(requiredEntrypoints.runtime),
      server: assets.get(document.entrypoints.server ?? "server.js") ?? contained(root, document.entrypoints.server ?? "server.js"),
      transport: assets.get("transport.js") ?? contained(root, "transport.js"),
      dynamic: assets.get(document.entrypoints.dynamic ?? "dynamic.js") ?? contained(root, document.entrypoints.dynamic ?? "dynamic.js"),
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
      if (id === `virtual:runic-translations/${catalog}/server`)
        return virtualId("server");
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
      if (id === virtualId("server")) return `export * from ${JSON.stringify(toVitePath(entries.server))};\n`;
      if (id === virtualId("transport")) return `export * from ${JSON.stringify(toVitePath(entries.transport))};\n`;
      if (id === virtualId("dynamic")) return `export * from ${JSON.stringify(toVitePath(entries.dynamic))};\n`;
      return null;
    },

    async handleHotUpdate(context) {
      if (context.file !== manifestPath && !sourceFiles.includes(context.file) && !isGenerated(context.file, manifestPath)) return;
      if (compiler && sourceFiles.includes(context.file)) await compile();
      await refresh();
      const modules = [virtualId("messages"), virtualId("runtime"), virtualId("server"), virtualId("transport"), virtualId("dynamic")]
        .map(id => context.server.moduleGraph.getModuleById(id))
        .filter(Boolean);
      for (const module of modules) context.server.moduleGraph.invalidateModule(module);
      return modules;
    },
  };
}

function projectOptions(options) {
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
  let settings;
  try {
    settings = JSON.parse(readFileSync(config, "utf8"));
  } catch (error) {
    throw new Error(`Could not read Runic translation project '${config}': ${error.message}`);
  }
  if (!settings || settings.schemaVersion !== 1 || typeof settings.catalog !== "string" || settings.catalog.length === 0)
    throw new Error("The Runic translation project must declare schemaVersion 1 and a catalog ID.");
  const project = dirname(config);
  const output = resolve(cwd, options.output ?? ".runic/translations");
  const sourceFiles = [config];
  for (const entry of readdirSync(project, { recursive: true })) {
    const path = join(project, entry);
    if (statSync(path).isFile() && path.endsWith(".mf2")) sourceFiles.push(path);
  }
  return Object.freeze({
    project,
    output,
    manifest: join(output, `${settings.catalog}.esm`, "web-module-manifest-v1.json"),
    sourceFiles: Object.freeze(sourceFiles),
    cwd,
    command: options.command ?? "dotnet",
    commandArguments: Object.freeze(options.commandArguments ?? ["tool", "run", "runic-translations", "--"]),
  });
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
