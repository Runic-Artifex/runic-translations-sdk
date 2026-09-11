import { existsSync, readFileSync } from "node:fs";
import { dirname, join, resolve } from "node:path";

export interface ServerLaunch { command: string; args: string[]; cwd: string }
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
