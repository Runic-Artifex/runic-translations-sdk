import { execFile } from "node:child_process";
import { createHash, randomUUID } from "node:crypto";
import { readFileSync, readdirSync, lstatSync } from "node:fs";
import { mkdir, readFile, realpath, rename, rm, writeFile } from "node:fs/promises";
import { basename, dirname, isAbsolute, join, normalize, relative, resolve, sep } from "node:path";
import { promisify } from "node:util";

const prefix = "\0virtual:runic-translations/";
const supportedEsmAbiVersion = 4;
const supportedRmf2RuntimeAbiVersion = 2;
const execFileAsync = promisify(execFile);

/**
 * Exposes compiler-generated ESM without coupling messages to Vite or a UI framework.
 * @param {{ project?: string, output?: string, manifest?: string, sourceFiles?: readonly string[],
 *   command?: string, commandArguments?: readonly string[], cwd?: string,
 *   typeDeclarations?: string | false }} [options]
 */
export function runicTranslations(options = {}) {
  if (!options || typeof options !== "object" || Array.isArray(options))
    throw new TypeError("runicTranslations options must be an object.");
  const project = options.manifest === undefined ? projectOptions(options) : undefined;
  let manifestPath = project?.manifest ?? resolve(options.manifest);
  const compiler = project;
  if (options.typeDeclarations !== undefined && options.typeDeclarations !== false &&
      (typeof options.typeDeclarations !== "string" || options.typeDeclarations.length === 0))
    throw new TypeError("typeDeclarations must be a non-empty path or false.");
  const typeDeclarationsPath = options.typeDeclarations === false ? undefined : resolve(
    project?.cwd ?? process.cwd(),
    options.typeDeclarations ?? (compiler ? join(compiler.output, "virtual.d.ts") : join(dirname(manifestPath), "virtual.d.ts")),
  );
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
    compilation = compilation.catch(() => undefined).then(async () => {
      const current = readProject(compiler.config, compiler.output);
      await execFileAsync(compiler.command, argumentsValue, {
        cwd: compiler.cwd,
        maxBuffer: 16 * 1024 * 1024,
      });
      return current;
    });
    return compilation;
  }

  async function refresh(nextProject) {
    const nextManifestPath = nextProject?.manifest ?? manifestPath;
    const document = JSON.parse(await readFile(nextManifestPath, "utf8"));
    if (!document || typeof document !== "object" || Array.isArray(document))
      throw new Error("The Runic ../web/vite-plugin-runic-translations module manifest must be an object.");
    if (document.webModuleManifestVersion !== 3)
      throw new Error(`Unsupported Runic ../web/vite-plugin-runic-translations module manifest version '${document.webModuleManifestVersion}'.`);
    const rootMembers = ["webModuleManifestVersion", "esmAbiVersion", "rmf2RuntimeAbiVersion", "messageGrammarVersion", "profile", "generatedNameVersion", "catalog", "contractFingerprint", "sourceHash", "entrypoints", "assets"];
    if (Object.keys(document).some(key => !rootMembers.includes(key)))
      throw new Error(`The Runic ../web/vite-plugin-runic-translations v${document.webModuleManifestVersion} module manifest contains an unknown member.`);
    if (document.esmAbiVersion !== supportedEsmAbiVersion)
      throw new Error(`Unsupported Runic ESM ABI version '${document.esmAbiVersion}'. Expected '${supportedEsmAbiVersion}'.`);
    if (document.rmf2RuntimeAbiVersion !== supportedRmf2RuntimeAbiVersion || document.messageGrammarVersion !== 5 ||
        document.profile !== "rmf2-execution-v2" || document.generatedNameVersion !== 1 ||
        typeof document.sourceHash !== "string" || !/^sha256:[a-f0-9]{64}$/.test(document.sourceHash))
      throw new Error("The Runic ../web/vite-plugin-runic-translations v3 module manifest has an incompatible RMF2 execution contract.");
    if (typeof document.catalog !== "string" || !document.entrypoints ||
        typeof document.contractFingerprint !== "string" || !/^sha256:[a-f0-9]{64}$/.test(document.contractFingerprint))
      throw new Error("The Runic ../web/vite-plugin-runic-translations module manifest is malformed.");
    if (!/^[a-z][a-z0-9.-]*$/.test(document.catalog))
      throw new Error(`The Runic ../web/vite-plugin-runic-translations v${document.webModuleManifestVersion} module manifest has an invalid catalog ID.`);
    if (typeof document.entrypoints !== "object" || Array.isArray(document.entrypoints))
      throw new Error(`The Runic ../web/vite-plugin-runic-translations v${document.webModuleManifestVersion} module manifest has malformed entrypoints.`);
    const root = dirname(nextManifestPath);
    const realRoot = await realpath(root);
    const requiredEntrypoints = {
      messages: document.entrypoints.messages,
      runtime: document.entrypoints.runtime,
    };
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
    if (!Array.isArray(document.assets))
      throw new Error("The Runic ../web/vite-plugin-runic-translations module manifest does not declare its generated assets.");
    const assets = new Map();
    for (const asset of document.assets) {
      if (!asset || typeof asset !== "object" || Array.isArray(asset) ||
          Object.keys(asset).some(key => !["path", "sha256", "byteLength", "mediaType"].includes(key)))
        throw new Error(`The Runic ../web/vite-plugin-runic-translations v${document.webModuleManifestVersion} module manifest contains an unknown asset member.`);
      if (typeof asset.path !== "string" || !/^(?!\/)(?!.*(?:^|\/)\.\.(?:\/|$))[A-Za-z0-9_$.-]+(?:\/[A-Za-z0-9_$.-]+)*$/.test(asset.path) || typeof asset.sha256 !== "string" ||
          !/^[a-f0-9]{64}$/.test(asset.sha256) || !Number.isSafeInteger(asset.byteLength) || asset.byteLength < 0 ||
          typeof asset.mediaType !== "string" || !["text/javascript", "text/typescript"].includes(asset.mediaType) || assets.has(asset.path))
        throw new Error("The Runic ../web/vite-plugin-runic-translations module manifest contains an invalid generated asset entry.");
      const path = await containedReal(root, realRoot, asset.path);
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
    const nextGeneratedPaths = new Set(assets.values());
    const nextEntries = Object.freeze({
      messages: assets.get(requiredEntrypoints.messages),
      types: assets.get(requiredEntrypoints.types),
      runtime: assets.get(requiredEntrypoints.runtime),
      server: assets.get(document.entrypoints.server),
      transport: assets.get(document.entrypoints.transport),
      dynamic: assets.get(document.entrypoints.dynamic),
    });
    if (typeDeclarationsPath) {
      const safeDeclarationsPath = await safeTypeDeclarationsPath(typeDeclarationsPath, nextManifestPath, assets);
      await writeTypeDeclarations(safeDeclarationsPath, document.catalog, root, assets, requiredEntrypoints);
    }
    manifestPath = nextManifestPath;
    if (nextProject) {
      sourceFiles = new Set([...explicitSources, ...nextProject.sourceFiles]);
      sourceRoots = nextProject.sourceRoots;
      watchRoots = nextProject.watchRoots;
      server?.watcher.add(watchRoots);
      server?.watcher.add([...sourceFiles]);
    }
    catalog = document.catalog;
    generatedPaths = nextGeneratedPaths;
    entries = nextEntries;
    return document;
  }

  function virtualId(kind) {
    return `${prefix}${catalog}/${kind}`;
  }

  function isSource(path) {
    if (!compiler) return sourceFiles.has(path);
    if (path === compiler.config) return true;
    if (isWithin(compiler.output, path)) return false;
    return explicitSources.has(path) || (sourceRoots.some(root => isWithin(root, path)) && /\.(?:mf2|rmf2)$/i.test(path));
  }

  function update(path, targetServer) {
    const source = isSource(path);
    if (!source && (compiler || !isGenerated(path, manifestPath))) return;
    const operation = updates.catch(() => undefined).then(async () => {
      const previousCatalog = catalog;
      const previousPaths = generatedPaths;
      const nextProject = compiler && source ? await compile() : undefined;
      await refresh(nextProject);
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

async function writeTypeDeclarations(path, catalog, root, assets, entrypoints) {
  const typeEntrypoints = {
    messages: entrypoints.types,
    runtime: "runtime.d.ts",
    server: "server.d.ts",
    transport: "transport.d.ts",
    dynamic: "dynamic.d.ts",
  };
  const sources = new Map();
  for (const [kind, relativePath] of Object.entries(typeEntrypoints)) {
    contained(root, relativePath);
    const asset = assets.get(relativePath);
    if (!asset)
      throw new Error(`The Runic ../web/vite-plugin-runic-translations module manifest omits the '${kind}' generated type declarations.`);
    sources.set(kind, await readFile(asset, "utf8"));
  }
  const virtual = kind => `virtual:runic-translations/${catalog}${kind === "messages" ? "" : `/${kind}`}`;
  const rewrite = (kind, source) => {
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
  const moduleDeclaration = (kind, specifier, source) => {
    const body = rewrite(kind, source).trimEnd().split("\n").map(line => `  ${line}`).join("\n");
    return `declare module ${JSON.stringify(specifier)} {\n${body}\n}`;
  };
  const messages = sources.get("messages");
  const declarations = [
    "// <auto-generated />",
    "// Generated from the validated Runic web module manifest. Do not edit.",
    moduleDeclaration("messages", virtual("messages"), messages),
    moduleDeclaration("messages", `${virtual("messages")}/messages`, messages),
    ...["runtime", "server", "transport", "dynamic"].map(kind =>
      moduleDeclaration(kind, virtual(kind), sources.get(kind))),
    "",
  ].join("\n\n");

  let previous;
  try {
    previous = await readFile(path, "utf8");
  } catch (error) {
    if (error?.code !== "ENOENT") throw error;
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

async function safeTypeDeclarationsPath(path, manifestPath, assets) {
  let current = dirname(path);
  while (true) {
    try {
      if (lstatSync(current).isSymbolicLink())
        throw new Error(`Generated Runic type declaration paths must not traverse symbolic links: '${path}'.`);
    } catch (error) {
      if (error?.code !== "ENOENT") throw error;
    }
    const parent = dirname(current);
    if (parent === current) break;
    current = parent;
  }
  await mkdir(dirname(path), { recursive: true });
  current = dirname(path);
  while (true) {
    if (lstatSync(current).isSymbolicLink())
      throw new Error(`Generated Runic type declaration paths must not traverse symbolic links: '${path}'.`);
    const parent = dirname(current);
    if (parent === current) break;
    current = parent;
  }
  try {
    if (lstatSync(path).isSymbolicLink())
      throw new Error(`Generated Runic type declaration path must not be a symbolic link: '${path}'.`);
  } catch (error) {
    if (error?.code !== "ENOENT") throw error;
  }
  const canonical = join(await realpath(dirname(path)), basename(path));
  const manifest = await realpath(manifestPath);
  if (canonical === manifest || [...assets.values()].some(asset => asset === canonical))
    throw new Error("Generated Runic virtual type declarations must not overwrite the manifest or one of its assets.");
  return canonical;
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
      else if (entry.isFile() && /\.(?:mf2|rmf2)$/i.test(path)) sourceFiles.push(path);
    }
  }
  if (settings.sourceRoots !== undefined && (!Array.isArray(settings.sourceRoots) || settings.sourceRoots.some(mount => !mount || typeof mount.path !== "string" || mount.path.length === 0)))
    throw new Error("Runic translation sourceRoots must contain non-empty paths.");
  const roots = settings.sourceRoots
    ? settings.sourceRoots.map(mount => resolve(project, mount.path)) : [project];
  for (const root of roots) discover(root);
  return {
    manifest: contained(output, `${settings.catalog}.esm-v5/web-module-manifest-v3.json`),
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

async function containedReal(root, realRoot, relativePath) {
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

function isGenerated(path, manifestPath) {
  const root = dirname(manifestPath);
  return path === manifestPath || path.startsWith(root + sep);
}

function toVitePath(path) {
  return path.split(sep).join("/");
}
