// Runs the example under Bun, outside Vite: generates the catalogue with the repository CLI, starts the server, records
// an operation, reads the history from the server and from a separate tray process, and runs a bundled tray.
// Usage, from the repository root in the development shell: bun examples/bun/operation-history/verify.ts
import assert from "node:assert/strict";
import { mkdtempSync, rmSync } from "node:fs";
import { tmpdir } from "node:os";
import { join, resolve } from "node:path";

const example = import.meta.dir;
const repository = resolve(example, "../../..");
const generated = join(example, "generated");
const scratch = mkdtempSync(join(tmpdir(), "runic-bun-history-"));
const historyFile = join(scratch, "current.log");
const stored = join(example, "history/2026-04.log");
const environment = { ...process.env, HISTORY_FILE: historyFile, PORT: "0" };

function run(command: string[], cwd = example): string {
  const result = Bun.spawnSync(command, { cwd, env: environment, stdout: "pipe", stderr: "pipe" });
  const output = result.stdout.toString();
  assert.equal(result.exitCode, 0, `${command.join(" ")} failed:\n${output}${result.stderr.toString()}`);
  return output;
}

const english = [
  "Downloading firefox failed",
  "Switching to generation 41",
  "Removed 3 old generations",
  "Building firefox",
  "Changed firefox, gimp, and vlc",
  "Finished one step",
  "Building gimp failed",
];
const german = [
  "Downloading firefox ist fehlgeschlagen",
  "Wechsel zu Generation 41",
  "Removed 3 old generations",
  "Building firefox",
  "Geändert: firefox, gimp und vlc",
  "Ein Schritt abgeschlossen",
  "Bau von gimp ist fehlgeschlagen",
];

let server: ReturnType<typeof Bun.spawn> | undefined;
try {
  rmSync(generated, { recursive: true, force: true });
  run(["dotnet", "run", "--project", join(repository, "tools/dotnet-runic-translations"), "--configuration", "Release", "--",
    "generate", "--project", join(example, "translations"), "--output", generated, "--emit-esm"], repository);

  server = Bun.spawn([process.execPath, "server.ts"], { cwd: example, env: environment, stdout: "pipe", stderr: "inherit" });
  const reader = server.stdout.getReader();
  const first = new TextDecoder().decode((await reader.read()).value);
  const port = /listening (\d+)/.exec(first)?.[1];
  assert.ok(port, `The server did not start: ${first}`);
  const base = `http://127.0.0.1:${port}/operations`;

  const created = await fetch(base, { method: "POST", body: JSON.stringify({ package: "gimp" }) });
  assert.equal(created.status, 201);
  const reference = await created.json();
  assert.equal(reference.version, 2);
  assert.equal(reference.arguments.step.type, "message");

  const shown = (await (await fetch(`${base}?locale=de`)).json()) as { text: string; outcome: string; diagnostics: { path: string; reason: string }[] }[];
  assert.deepEqual(shown.map(entry => entry.text), german);
  assert.deepEqual(shown.map(entry => entry.outcome), ["translated", "translated", "fallback", "fallback", "translated", "translated", "translated"]);
  assert.deepEqual(shown[0].diagnostics, [{ path: "$", reason: "contract-changed" }, { path: "$.step", reason: "unknown-key" }]);
  assert.deepEqual(shown[3].diagnostics, [{ path: "$", reason: "fingerprint-mismatch" }]);
  assert.deepEqual(shown[6].diagnostics, []);

  // The tray reads the same files with the same catalogue, as a separate process and as a single bundled file.
  assert.deepEqual(run([process.execPath, "tray.ts", "de", stored, historyFile]).trimEnd().split("\n"), german);
  const bundle = join(scratch, "tray.js");
  run([process.execPath, "build", join(example, "tray.ts"), "--target", "bun", "--outfile", bundle]);
  assert.deepEqual(run([process.execPath, bundle, "en", stored, historyFile], scratch).trimEnd().split("\n"), english);
  console.log("BUN_OPERATION_HISTORY_OK");
} finally {
  server?.kill();
  await server?.exited;
  rmSync(scratch, { recursive: true, force: true });
  rmSync(generated, { recursive: true, force: true });
}
