import { existsSync, lstatSync, readFileSync } from "node:fs";
import { dirname, join, resolve } from "node:path";

export interface ServerLaunch { command: string; args: string[]; cwd: string }
/**
 * Return the explicitly configured RMF2 roots for host-side file watching.
 * The language server remains authoritative for discovery and reparse-point
 * rejection; this helper only adds declared roots so mounted files outside the
 * workspace folder receive the same create/change/delete events.
 */
export function sourceWatchRoots(root: string): string[] {
  const config = [join(root, "runic.json"), join(root, "translations", "runic.json")]
    .find(path => existsSync(path));
  if (!config) return [root];
  let settings: unknown;
  try { settings = JSON.parse(readFileSync(config, "utf8")); } catch { return [root]; }
  if (!settings || typeof settings !== "object" ||
      (settings as { sourceLayout?: unknown }).sourceLayout !== "rmf2-v1") return [root];
  const mounts = (settings as { sourceRoots?: unknown }).sourceRoots;
  if (!Array.isArray(mounts)) return [root];
  const roots = [root];
  for (const mount of mounts) {
    if (!mount || typeof mount !== "object" || typeof (mount as { path?: unknown }).path !== "string") continue;
    const path = resolve(dirname(config), (mount as { path: string }).path);
    // Watching a symlink would make the host recurse into an untrusted tree;
    // the server will issue the detailed diagnostic for malformed mounts.
    if (hasSymlinkAncestor(path)) continue;
    if (!roots.includes(path)) roots.push(path);
  }
  return roots;
}

function hasSymlinkAncestor(path: string): boolean {
  let current = resolve(path);
  while (true) {
    try {
      if (lstatSync(current).isSymbolicLink()) return true;
    } catch (error) {
      if ((error as NodeJS.ErrnoException).code !== "ENOENT") throw error;
    }
    const parent = dirname(current);
    if (parent === current) return false;
    current = parent;
  }
}

/** Resolve only an explicit development assembly or the workspace's restored local tool. */
export function serverLaunch(root: string, dotnet: string, assembly: string): ServerLaunch {
  if (assembly) {
    const path = resolve(root, assembly);
    if (!existsSync(path)) throw new Error(`Runic server assembly does not exist: ${path}`);
    return { command: dotnet, args: [path, "lsp"], cwd: root };
  }
  let directory = root;
  while (true) {
    const path = join(directory, ".config", "dotnet-tools.json");
    if (existsSync(path)) {
      const manifest = JSON.parse(readFileSync(path, "utf8"));
      if (Object.values(manifest.tools ?? {}).some((tool: unknown) => Array.isArray((tool as { commands?: unknown }).commands) && (tool as { commands: unknown[] }).commands.includes("runic-translations")))
        return { command: dotnet, args: ["tool", "run", "runic-translations", "--", "lsp"], cwd: directory };
      if (manifest.isRoot) break;
    }
    const parent = dirname(directory);
    if (parent === directory) break;
    directory = parent;
  }
  throw new Error("No project-local runic-translations tool was found. Add it to .config/dotnet-tools.json and run dotnet tool restore, or configure runicTranslations.serverAssembly for SDK development.");
}
