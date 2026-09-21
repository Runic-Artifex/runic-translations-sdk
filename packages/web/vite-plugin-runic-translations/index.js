import { execFile } from "node:child_process";
import { createHash } from "node:crypto";
import { readFileSync, readdirSync, lstatSync } from "node:fs";
import { readFile } from "node:fs/promises";
import { dirname, isAbsolute, join, normalize, relative, resolve, sep } from "node:path";
import { promisify } from "node:util";

const prefix = "\0virtual:runic-translations/";
const supportedEsmAbiVersion = 3;
const supportedRmf2EsmAbiVersion = 4;
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
  let sourceRoots = project?.sourceRoots ?? (compiler ? [compiler.project] : []);
  let watchRoots = project?.watchRoots ?? sourceRoots;
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
      sourceRoots = current.sourceRoots;
      watchRoots = current.watchRoots;
      server?.watcher.add(watchRoots);
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
    if (!document || typeof document !== "object" || Array.isArray(document))
      throw new Error("The Runic ../web/vite-plugin-runic-translations module manifest must be an object.");
    if (![1, 2, 3].includes(document.webModuleManifestVersion))
      throw new Error(`Unsupported Runic ../web/vite-plugin-runic-translations module manifest version '${document.webModuleManifestVersion}'.`);
    const strictV2 = document.webModuleManifestVersion >= 2;
    const rmf2V5 = document.webModuleManifestVersion === 3;
    const rootMembers = rmf2V5
      ? ["webModuleManifestVersion", "esmAbiVersion", "rmf2RuntimeAbiVersion", "messageGrammarVersion", "profile", "generatedNameVersion", "catalog", "contractFingerprint", "sourceHash", "entrypoints", "assets"]
      : ["webModuleManifestVersion", "esmAbiVersion", "catalog", "contractFingerprint", "entrypoints", "assets"];
    if (strictV2 && Object.keys(document).some(key => !rootMembers.includes(key)))
      throw new Error(`The Runic ../web/vite-plugin-runic-translations v${document.webModuleManifestVersion} module manifest contains an unknown member.`);
    const expectedEsmAbi = rmf2V5 ? supportedRmf2EsmAbiVersion : supportedEsmAbiVersion;
    if (document.esmAbiVersion !== expectedEsmAbi)
      throw new Error(`Unsupported Runic ESM ABI version '${document.esmAbiVersion}'. Expected '${expectedEsmAbi}'.`);
    if (rmf2V5 && (document.rmf2RuntimeAbiVersion !== 2 || document.messageGrammarVersion !== 5 ||
        document.profile !== "rmf2-execution-v2" || document.generatedNameVersion !== 1 ||
        typeof document.sourceHash !== "string" || !/^sha256:[a-f0-9]{64}$/.test(document.sourceHash)))
      throw new Error("The Runic ../web/vite-plugin-runic-translations v3 module manifest has an incompatible RMF2 execution contract.");
    if (typeof document.catalog !== "string" || !document.entrypoints ||
        typeof document.contractFingerprint !== "string" || !/^sha256:[a-f0-9]{64}$/.test(document.contractFingerprint))
      throw new Error("The Runic ../web/vite-plugin-runic-translations module manifest is malformed.");
    if (strictV2 && !/^[a-z][a-z0-9.-]*$/.test(document.catalog))
      throw new Error(`The Runic ../web/vite-plugin-runic-translations v${document.webModuleManifestVersion} module manifest has an invalid catalog ID.`);
    if (strictV2 && (typeof document.entrypoints !== "object" || Array.isArray(document.entrypoints)))
      throw new Error(`The Runic ../web/vite-plugin-runic-translations v${document.webModuleManifestVersion} module manifest has malformed entrypoints.`);
    catalog = document.catalog;
    const root = dirname(manifestPath);
    const requiredEntrypoints = {
      messages: document.entrypoints.messages,
      runtime: document.entrypoints.runtime,
    };
    if (strictV2) {
      const expectedEntrypoints = {
        messages: "messages.js",
        types: "messages.d.ts",
        runtime: "runtime.js",
        server: "server.js",
        transport: "transport.js",
        dynamic: "dynamic.js",
      };
      if (Object.keys(document.entrypoints).some(kind => !Object.hasOwn(expectedEntrypoints, kind)) ||
          Object.keys(expectedEntrypoints).some(kind => document.entrypoints[kind] !== expectedEntrypoints[kind]))
        throw new Error(`The Runic ../web/vite-plugin-runic-translations v${document.webModuleManifestVersion} module manifest has invalid entrypoints.`);
      for (const kind of ["types", "server", "transport", "dynamic"])
        requiredEntrypoints[kind] = document.entrypoints[kind];
    }
    if (!Array.isArray(document.assets))
      throw new Error("The Runic ../web/vite-plugin-runic-translations module manifest does not declare its generated assets.");
    const assets = new Map();
    for (const asset of document.assets) {
      if (strictV2 && (!asset || typeof asset !== "object" || Array.isArray(asset) ||
          Object.keys(asset).some(key => !["path", "sha256", "byteLength", "mediaType"].includes(key))))
        throw new Error(`The Runic ../web/vite-plugin-runic-translations v${document.webModuleManifestVersion} module manifest contains an unknown asset member.`);
      if (!asset || typeof asset.path !== "string" || (strictV2 && !/^(?!\/)(?!.*(?:^|\/)\.\.(?:\/|$))[A-Za-z0-9_$.-]+(?:\/[A-Za-z0-9_$.-]+)*$/.test(asset.path)) || typeof asset.sha256 !== "string" ||
          !/^[a-f0-9]{64}$/.test(asset.sha256) || !Number.isSafeInteger(asset.byteLength) || asset.byteLength < 0 ||
          typeof asset.mediaType !== "string" || (strictV2 && !["text/javascript", "text/typescript"].includes(asset.mediaType)) || assets.has(asset.path))
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
    if (rmf2V5) {
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
        throw new Error("The Runic ../web/vite-plugin-runic-translations v3 module manifest does not match its generated RMF2 runtime contract.");
    }
    generatedPaths = new Set(assets.values());
    entries = Object.freeze({
      messages: assets.get(requiredEntrypoints.messages),
      runtime: assets.get(requiredEntrypoints.runtime),
      server: assets.get(document.entrypoints.server ?? "server.js") ?? contained(root, document.entrypoints.server ?? "server.js"),
      transport: assets.get(document.entrypoints.transport ?? "transport.js") ?? contained(root, document.entrypoints.transport ?? "transport.js"),
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
    return explicitSources.has(path) || (sourceRoots.some(root => isWithin(root, path)) && /\.(mf2|rmf2|toml)$/i.test(path));
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
      if (compiler) server.watcher.add(watchRoots);
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
  if (settings.sourceLayout !== undefined && settings.sourceLayout !== "locale-toml" && settings.sourceLayout !== "rmf2-v1")
    throw new Error(`Unsupported Runic translation sourceLayout '${settings.sourceLayout}'.`);
  if (settings.executionProfile !== undefined && settings.executionProfile !== "rmf2-execution-v2")
    throw new Error(`Unsupported Runic translation executionProfile '${settings.executionProfile}'.`);
  if (settings.executionProfile === "rmf2-execution-v2" && settings.sourceLayout !== "rmf2-v1")
    throw new Error("Runic translation executionProfile 'rmf2-execution-v2' requires sourceLayout 'rmf2-v1'.");
  const project = dirname(config);
  const sourceFiles = [config];
  function discover(directory) {
    ensureNoSymlinkAncestors(directory);
    if (lstatSync(directory).isSymbolicLink()) throw new Error("Translation source roots must not be symbolic links.");
    for (const entry of readdirSync(directory, { withFileTypes: true })) {
      const path = join(directory, entry.name);
      if (isWithin(output, path)) continue;
      if (entry.isSymbolicLink()) throw new Error("Translation sources must not traverse symbolic links.");
      if (entry.isDirectory()) discover(path);
      else if (entry.isFile() && (settings.sourceLayout === "locale-toml"
        ? directory === project && /\.toml$/i.test(path) : settings.sourceLayout === "rmf2-v1" ? /\.rmf2$/i.test(path) : /\.mf2$/i.test(path))) sourceFiles.push(path);
    }
  }
  const roots = settings.sourceLayout === "rmf2-v1" && settings.sourceRoots
    ? settings.sourceRoots.map(mount => resolve(project, mount.path)) : [project];
  for (const root of roots) discover(root);
  return {
    manifest: contained(output, settings.executionProfile === "rmf2-execution-v2"
      ? `${settings.catalog}.esm-v5/web-module-manifest-v3.json`
      : `${settings.catalog}.esm/web-module-manifest-v2.json`),
    sourceFiles: Object.freeze(sourceFiles.sort()),
    sourceRoots: Object.freeze(roots),
    watchRoots: Object.freeze([...new Set([project, ...roots])]),
  };
}

function ensureNoSymlinkAncestors(path) {
  let current = resolve(path);
  while (true) {
    if (lstatSync(current).isSymbolicLink()) throw new Error("Translation source roots must not traverse symbolic links.");
    const parent = dirname(current);
    if (parent === current) return;
    current = parent;
  }
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
