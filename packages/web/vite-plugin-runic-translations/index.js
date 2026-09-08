import { execFile } from "node:child_process";
import { createHash } from "node:crypto";
import { readFileSync, readdirSync } from "node:fs";
import { readFile } from "node:fs/promises";
import { dirname, isAbsolute, join, normalize, relative, resolve, sep } from "node:path";
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
  let manifestPath = project?.manifest ?? resolve(options.manifest);
  const compiler = project;
  const explicitSources = new Set((options.sourceFiles ?? []).map(path => resolve(path)));
  let sourceFiles = new Set([...explicitSources, ...(project?.sourceFiles ?? [])]);
  let server;
  let cleanupWatcher;
  let updates = Promise.resolve();
  let catalog;
  let entries;
  let generatedPaths = new Set();
  let compilation = Promise.resolve();

  async function compile() {
    if (!compiler) return;
    const argumentsValue = [...compiler.commandArguments, "generate", "--project", compiler.project, "--output", compiler.output, "--emit-esm"];
    compilation = compilation.catch(() => undefined).then(() => {
      const current = readProject(compiler.config, compiler.output);
      manifestPath = current.manifest;
      sourceFiles = new Set([...explicitSources, ...current.sourceFiles]);
      server?.watcher.add([...sourceFiles]);
      return execFileAsync(compiler.command, argumentsValue, {
        cwd: compiler.cwd,
        maxBuffer: 16 * 1024 * 1024,
      });
    }).then(() => undefined);
    return compilation;
  }

  async function refresh() {
    const document = JSON.parse(await readFile(manifestPath, "utf8"));
    if (document.webModuleManifestVersion !== 1)
      throw new Error(`Unsupported Runic ../web/vite-plugin-runic-translations module manifest version '${document.webModuleManifestVersion}'.`);
    if (document.esmAbiVersion !== supportedEsmAbiVersion)
      throw new Error(`Unsupported Runic ESM ABI version '${document.esmAbiVersion}'. Expected '${supportedEsmAbiVersion}'.`);
    if (typeof document.catalog !== "string" || !document.entrypoints ||
        typeof document.contractFingerprint !== "string" || !/^sha256:[a-f0-9]{64}$/.test(document.contractFingerprint))
      throw new Error("The Runic ../web/vite-plugin-runic-translations module manifest is malformed.");
    catalog = document.catalog;
    const root = dirname(manifestPath);
    const requiredEntrypoints = {
      messages: document.entrypoints.messages,
      runtime: document.entrypoints.runtime,
    };
    if (!Array.isArray(document.assets))
      throw new Error("The Runic ../web/vite-plugin-runic-translations module manifest does not declare its generated assets.");
    const assets = new Map();
    for (const asset of document.assets) {
      if (!asset || typeof asset.path !== "string" || typeof asset.sha256 !== "string" ||
          !/^[a-f0-9]{64}$/.test(asset.sha256) || !Number.isSafeInteger(asset.byteLength) || asset.byteLength < 0 ||
          typeof asset.mediaType !== "string" || assets.has(asset.path))
        throw new Error("The Runic ../web/vite-plugin-runic-translations module manifest contains an invalid generated asset entry.");
      const path = contained(root, asset.path);
      const content = await readFile(path);
      if (content.byteLength !== asset.byteLength || createHash("sha256").update(content).digest("hex") !== asset.sha256)
        throw new Error(`Generated Runic asset integrity check failed: '${asset.path}'.`);
      assets.set(asset.path, path);
    }
    for (const path of Object.values(requiredEntrypoints)) {
      if (typeof path !== "string")
        throw new Error("The Runic ../web/vite-plugin-runic-translations module manifest omits a required generated entrypoint.");
      contained(root, path);
      if (!assets.has(path))
        throw new Error("The Runic ../web/vite-plugin-runic-translations module manifest omits a required generated entrypoint.");
    }
    const runtime = await readFile(assets.get(requiredEntrypoints.runtime), "utf8");
    const runtimeFingerprint = /^export const contractFingerprint = ("sha256:[a-f0-9]{64}");$/m.exec(runtime)?.[1];
    if (runtimeFingerprint !== JSON.stringify(document.contractFingerprint))
      throw new Error("The Runic ../web/vite-plugin-runic-translations module manifest fingerprint does not match its generated runtime.");
    generatedPaths = new Set(assets.values());
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

  function isSource(path) {
    if (!compiler) return sourceFiles.has(path);
    if (path === compiler.config) return true;
    if (isWithin(compiler.output, path)) return false;
    // Include either authoring extension so mixed-layout inputs reach the compiler's diagnostics.
    return explicitSources.has(path) || (isWithin(compiler.project, path) && /\.(mf2|toml)$/i.test(path));
  }

  function update(path, targetServer) {
    const source = isSource(path);
    if (!source && (compiler || !isGenerated(path, manifestPath))) return;
    const operation = updates.catch(() => undefined).then(async () => {
      const previousCatalog = catalog;
      const previousPaths = generatedPaths;
      if (compiler && source) await compile();
      await refresh();
      const ids = new Set([previousCatalog, catalog].filter(Boolean));
      const modules = [...ids].flatMap(id => ["messages", "runtime", "server", "transport", "dynamic"]
        .map(kind => targetServer.moduleGraph.getModuleById(`${prefix}${id}/${kind}`))).filter(Boolean);
      // Generated dependencies can keep transformed code after a virtual re-export is invalidated.
      for (const generated of new Set([...previousPaths, ...generatedPaths]))
        for (const module of targetServer.moduleGraph.getModulesByFile?.(generated) ?? [])
          targetServer.moduleGraph.invalidateModule(module);
      for (const module of modules) targetServer.moduleGraph.invalidateModule(module);
      return modules;
    });
    updates = operation;
    return operation;
  }

  return {
    name: "runic-translations",
    enforce: "pre",

    configureServer(value) {
      server = value;
      if (compiler) server.watcher.add(compiler.project);
      const membershipChanged = path => {
        const pending = update(resolve(path), server);
        if (!pending) return;
        void pending.then(() => server.ws.send({ type: "full-reload" })).catch(error => {
          server.ws.send({ type: "error", err: { message: error.message, stack: error.stack, plugin: "runic-translations" } });
        });
      };
      server.watcher.on("add", membershipChanged);
      server.watcher.on("unlink", membershipChanged);
      const cleanup = () => {
        server.watcher.off("add", membershipChanged);
        server.watcher.off("unlink", membershipChanged);
      };
      server.httpServer?.once("close", cleanup);
      cleanupWatcher = cleanup;
    },

    closeBundle() {
      cleanupWatcher?.();
    },

    async buildStart() {
      await compile();
      const document = await refresh();
      if (compiler) this.addWatchFile(compiler.project);
      if (!compiler) this.addWatchFile(manifestPath);
      for (const path of sourceFiles) this.addWatchFile(path);
      if (!compiler) for (const asset of document.assets) this.addWatchFile(contained(dirname(manifestPath), asset.path));
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
      const path = resolve(context.file);
      if (compiler && isWithin(compiler.output, path)) return [];
      return update(path, context.server);
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
  const project = dirname(config);
  const output = resolve(cwd, options.output ?? ".runic/translations");
  return Object.freeze({
    project,
    config,
    output,
    ...readProject(config, output),
    cwd,
    command: options.command ?? "dotnet",
    commandArguments: Object.freeze(options.commandArguments ?? ["tool", "run", "runic-translations", "--"]),
  });
}

function readProject(config, output) {
  let settings;
  try {
    settings = JSON.parse(readFileSync(config, "utf8"));
  } catch (error) {
    throw new Error(`Could not read Runic translation project '${config}': ${error.message}`);
  }
  if (!settings || settings.schemaVersion !== 1 || typeof settings.catalog !== "string" || settings.catalog.length === 0)
    throw new Error("The Runic translation project must declare schemaVersion 1 and a catalog ID.");
  if (settings.sourceLayout !== undefined && settings.sourceLayout !== "locale-toml")
    throw new Error(`Unsupported Runic translation sourceLayout '${settings.sourceLayout}'.`);
  const project = dirname(config);
  const sourceFiles = [config];
  function discover(directory) {
    for (const entry of readdirSync(directory, { withFileTypes: true })) {
      const path = join(directory, entry.name);
      if (isWithin(output, path)) continue;
      if (entry.isDirectory()) discover(path);
      else if (entry.isFile() && (settings.sourceLayout === "locale-toml"
        ? directory === project && /\.toml$/i.test(path) : /\.mf2$/i.test(path))) sourceFiles.push(path);
    }
  }
  discover(project);
  return {
    manifest: contained(output, `${settings.catalog}.esm/web-module-manifest-v1.json`),
    sourceFiles: Object.freeze(sourceFiles.sort()),
  };
}

function isWithin(root, path) {
  const candidate = relative(root, path);
  return candidate === "" || (!isAbsolute(candidate) && candidate !== ".." && !candidate.startsWith(`..${sep}`));
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
