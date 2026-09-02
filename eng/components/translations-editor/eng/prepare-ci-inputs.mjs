#!/usr/bin/env node

import { cp, mkdir, readFile, readdir, writeFile } from "node:fs/promises";
import { dirname, join } from "node:path";
import { gunzipSync } from "node:zlib";

const root = process.cwd();
const feed = process.env.RUNIC_CANDIDATE_NPM_FEED;
const backup = process.env.RUNNER_TEMP
  ? join(process.env.RUNNER_TEMP, "runic-editor-candidate-inputs")
  : join(root, ".ci", "candidate-inputs");
if (!feed) throw new Error("RUNIC_CANDIDATE_NPM_FEED must name the local candidate archive directory.");

const inputs = ["Frontend/package.json", "Frontend/bun.lock", ".config/dotnet-tools.json"];
await mkdir(backup, { recursive: true });
for (const input of inputs) {
  await mkdir(join(backup, dirname(input)), { recursive: true });
  await cp(join(root, input), join(backup, input));
}
await writeFile(join(backup, "manifest.json"), `${JSON.stringify({ inputs }, null, 2)}\n`);

function readTarEntry(archive, entryName) {
  const tar = gunzipSync(archive);
  for (let offset = 0; offset + 512 <= tar.length;) {
    const header = tar.subarray(offset, offset + 512);
    if (header.every((byte) => byte === 0)) break;

    const readString = (start, length) => header.subarray(start, start + length).toString("utf8").replace(/\0.*$/s, "");
    const name = readString(0, 100);
    const prefix = readString(345, 155);
    const path = prefix ? `${prefix}/${name}` : name;
    const size = Number.parseInt(readString(124, 12).trim() || "0", 8);
    const dataOffset = offset + 512;
    if (path === entryName) return tar.subarray(dataOffset, dataOffset + size);
    offset = dataOffset + Math.ceil(size / 512) * 512;
  }
  throw new Error(`Archive does not contain '${entryName}'.`);
}

const archives = Object.fromEntries(
  await Promise.all(
    (await readdir(feed)).filter((name) => name.endsWith(".tgz")).map(async (name) => {
      const archive = join(feed, name);
      const manifest = JSON.parse(readTarEntry(await readFile(archive), "package/package.json").toString("utf8"));
      return [manifest.name, archive];
    }),
  ),
);
const frontendPath = join(root, "Frontend/package.json");
const frontend = JSON.parse(await readFile(frontendPath, "utf8"));
for (const section of ["dependencies", "devDependencies"]) {
  for (const name of Object.keys(frontend[section] ?? {})) {
    if (!name.startsWith("@runic-artifex/")) continue;
    if (!archives[name]) throw new Error(`Missing exact npm candidate '${name}'.`);
    frontend[section][name] = `file:${archives[name]}`;
  }
}
await writeFile(frontendPath, `${JSON.stringify(frontend, null, 2)}\n`);

const toolPath = join(root, ".config/dotnet-tools.json");
const toolManifest = JSON.parse(await readFile(toolPath, "utf8"));
const toolVersion = process.env.RUNIC_TRANSLATIONS_TOOL_VERSION;
if (!toolVersion) throw new Error("RUNIC_TRANSLATIONS_TOOL_VERSION must select the exact tool candidate.");
toolManifest.tools["dotnet-runic-translations"].version = toolVersion;
await writeFile(toolPath, `${JSON.stringify(toolManifest, null, 2)}\n`);
