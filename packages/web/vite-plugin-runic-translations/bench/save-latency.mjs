#!/usr/bin/env node
// Measures save-to-update latency of a real Vite dev server: the time from writing a translation
// source until the plugin has regenerated, validated and invalidated the virtual modules and Vite
// has sent the HMR update (or full reload) to clients.
//
//   node bench/save-latency.mjs --tool <dotnet-runic-translations.dll> [--plugin <module>]
//     [--project <translations directory to copy>] [--samples 10] [--persistent true|false]
//
// The default project is the editor's own catalog. The tool is invoked as `dotnet <dll>`; projects
// using `dotnet tool run` pay additional tool-resolution time on every cold invocation.
import { cp, mkdtemp, readFile, rm, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join, resolve } from "node:path";
import { parseArgs } from "node:util";
import { pathToFileURL } from "node:url";
import { createServer } from "vite";

const here = new URL(".", import.meta.url);
const { values } = parseArgs({
  options: {
    tool: { type: "string" },
    plugin: { type: "string", default: new URL("../dist/index.js", here).pathname },
    project: { type: "string", default: new URL("../../../../apps/translations-editor/EditorResources", here).pathname },
    samples: { type: "string", default: "10" },
    persistent: { type: "string", default: "true" },
  },
});
if (!values.tool) throw new Error("--tool <path to dotnet-runic-translations.dll> is required.");
const { runicTranslations } = await import(pathToFileURL(resolve(values.plugin)).href);

const root = await mkdtemp(join(tmpdir(), "runic-vite-latency-"));
try {
  const project = join(root, "translations");
  await cp(resolve(values.project), project, { recursive: true });
  const config = JSON.parse(await readFile(join(project, "runic.json"), "utf8"));
  const source = join(project, `${config.baseLocale}.rmf2`);
  const original = await readFile(source, "utf8");
  await writeFile(join(root, "main.js"), `import { m } from "virtual:runic-translations/${config.catalog}";\nexport default m;\n`);

  const options = { project, output: join(root, ".runic/translations"), command: "dotnet", commandArguments: [resolve(values.tool)] };
  if (values.persistent === "false") options.persistentCompiler = false;
  const started = performance.now();
  const server = await createServer({
    root, configFile: false, logLevel: "silent", clearScreen: false,
    plugins: [runicTranslations(options)],
    server: { port: 0, strictPort: false, watch: { usePolling: false } },
    optimizeDeps: { noDiscovery: true, include: [] },
  });
  await server.listen();
  await server.environments.client.transformRequest("/main.js");
  await server.environments.client.transformRequest(`virtual:runic-translations/${config.catalog}`);
  const ready = performance.now() - started;

  const waiters = [];
  const send = server.environments.client.hot.send.bind(server.environments.client.hot);
  server.environments.client.hot.send = (payload, ...rest) => {
    const message = typeof payload === "string" ? { type: payload } : payload;
    if (["update", "full-reload", "error"].includes(message.type)) waiters.shift()?.(message);
    return send(payload, ...rest);
  };
  const samples = [];
  for (let index = 0; index < Number(values.samples); index++) {
    const notified = new Promise((resolveUpdate, reject) => {
      const timer = setTimeout(() => reject(new Error("No HMR update within 60 s")), 60_000);
      waiters.push(message => { clearTimeout(timer); resolveUpdate(message); });
    });
    const before = performance.now();
    await writeFile(source, original.replace(/= Add message$/m, `= Add message ${index}`));
    const message = await notified;
    if (message.type === "error") throw new Error(`HMR error: ${message.err?.message}`);
    samples.push(performance.now() - before);
    await new Promise(resolveDelay => setTimeout(resolveDelay, 250));
  }
  await server.close();
  const sorted = [...samples].sort((left, right) => left - right);
  const median = sorted[Math.floor(sorted.length / 2)];
  const round = value => Math.round(value);
  console.log(JSON.stringify({
    plugin: values.plugin, persistent: values.persistent !== "false", project: values.project,
    serverReadyMs: round(ready), samplesMs: samples.map(round),
    minMs: round(sorted[0]), medianMs: round(median), maxMs: round(sorted.at(-1)),
  }));
} finally {
  await rm(root, { recursive: true, force: true });
}
