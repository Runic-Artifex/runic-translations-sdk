import { readFile } from "node:fs/promises";
import { dirname, isAbsolute, join, normalize, resolve, sep } from "node:path";

const prefix = "\0virtual:runic-text-resources/";

/**
 * Exposes compiler-generated ESM without coupling messages to Vite or a UI framework.
 * @param {{ manifest: string, sourceFiles?: readonly string[] }} options
 */
export function runicTextResources(options) {
  if (!options || typeof options.manifest !== "string" || options.manifest.length === 0)
    throw new TypeError("runicTextResources requires a manifest path.");

  const manifestPath = resolve(options.manifest);
  const sourceFiles = Object.freeze((options.sourceFiles ?? []).map(path => resolve(path)));
  let catalog;
  let entries;

  async function refresh() {
    const document = JSON.parse(await readFile(manifestPath, "utf8"));
    if (document.webModuleManifestVersion !== 1)
      throw new Error(`Unsupported Runic web module manifest version '${document.webModuleManifestVersion}'.`);
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
    name: "runic-text-resources",
    enforce: "pre",

    async buildStart() {
      const document = await refresh();
      this.addWatchFile(manifestPath);
      for (const path of sourceFiles) this.addWatchFile(path);
      for (const asset of document.assets) this.addWatchFile(contained(dirname(manifestPath), asset.path));
    },

    async resolveId(id) {
      if (!entries) await refresh();
      if (id === `virtual:runic-text-resources/${catalog}` || id === `virtual:runic-text-resources/${catalog}/messages`)
        return virtualId("messages");
      if (id === `virtual:runic-text-resources/${catalog}/runtime`)
        return virtualId("runtime");
      if (id === `virtual:runic-text-resources/${catalog}/transport`)
        return virtualId("transport");
      if (id === `virtual:runic-text-resources/${catalog}/dynamic`)
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
      await refresh();
      const modules = [virtualId("messages"), virtualId("runtime"), virtualId("transport"), virtualId("dynamic")]
        .map(id => context.server.moduleGraph.getModuleById(id))
        .filter(Boolean);
      for (const module of modules) context.server.moduleGraph.invalidateModule(module);
      return modules;
    },
  };
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
