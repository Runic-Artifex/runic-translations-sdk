// The language server's large-catalog budgets (tests/lsp-budget/README.md): writes the catalog that budgets.json
// describes, starts the built tool's `lsp` over stdio as an editor does, measures what an editor user waits for, and
// checks the budgets.
//   bun tests/lsp-budget/measure.mjs [--update] [--samples <n>] [--repeats <n>] [--features <n>]
// --update rewrites each budget's measured value and limit from this run (the limit is the measured value times the
// entry's `headroom`). --features changes the catalog's size (features × 1,000 messages) for a local comparison; the
// budgets apply to budgets.json's size only. The report goes to standard output and, in GitHub Actions, to the job
// summary; the measurements to artifacts/lsp-budget/results.json.
import assert from "node:assert/strict";
import { spawn, spawnSync } from "node:child_process";
import { appendFileSync, mkdirSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import os from "node:os";
import { resolve } from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";
import { featureName, featureSource, writeCatalog } from "./fixture.mjs";

const root = resolve(fileURLToPath(import.meta.url), "../../..");
const budgetsFile = resolve(root, "tests/lsp-budget/budgets.json");
const toolProject = resolve(root, "tools/dotnet-runic-translations/dotnet-runic-translations.csproj");
// RUNIC_LSP_BUDGET_TOOL measures another build of the tool (a copy of its bin directory), for before/after comparisons.
const tool = process.env.RUNIC_LSP_BUDGET_TOOL ?? resolve(root, "tools/dotnet-runic-translations/bin/Release/net10.0/dotnet-runic-translations.dll");
// RUNIC_LSP_BUDGET_OUTPUT puts the catalog and results elsewhere, for a run beside another one.
const output = process.env.RUNIC_LSP_BUDGET_OUTPUT ?? resolve(root, "artifacts/lsp-budget");
const timeoutMs = 300_000;

// A stdio LSP connection to one server process.
export class Connection {
  constructor(directory) {
    this.process = spawn("dotnet", [tool, "lsp"], { cwd: directory, stdio: ["pipe", "pipe", "pipe"] });
    this.stderr = "";
    this.process.stderr.setEncoding("utf8");
    this.process.stderr.on("data", chunk => { this.stderr += chunk; });
    this.exited = new Promise(resolveExit => this.process.on("exit", code => resolveExit(code)));
    this.buffer = Buffer.alloc(0);
    this.nextId = 0;
    this.pending = new Map();
    this.listeners = new Set();
    this.process.stdout.on("data", chunk => this.read(chunk));
  }

  read(chunk) {
    this.buffer = Buffer.concat([this.buffer, chunk]);
    for (;;) {
      const headerEnd = this.buffer.indexOf("\r\n\r\n");
      if (headerEnd < 0) return;
      const length = Number(/Content-Length: *(\d+)/i.exec(this.buffer.subarray(0, headerEnd).toString("ascii"))[1]);
      if (this.buffer.length < headerEnd + 4 + length) return;
      const message = JSON.parse(this.buffer.subarray(headerEnd + 4, headerEnd + 4 + length).toString("utf8"));
      this.buffer = this.buffer.subarray(headerEnd + 4 + length);
      if (message.method === undefined) { this.pending.get(message.id)?.(message); this.pending.delete(message.id); }
      else if (message.id !== undefined) this.send({ jsonrpc: "2.0", id: message.id, error: { code: -32601, message: "Unhandled" } });
      else for (const listener of [...this.listeners]) listener(message);
    }
  }

  send(message) {
    const body = Buffer.from(JSON.stringify(message), "utf8");
    this.process.stdin.write(`Content-Length: ${body.length}\r\n\r\n`);
    this.process.stdin.write(body);
  }

  notify(method, params) { this.send({ jsonrpc: "2.0", method, params }); }

  async request(method, params) {
    const id = ++this.nextId;
    const response = await new Promise((resolveResponse, reject) => {
      const timer = setTimeout(() => reject(new Error(`No response to ${method} within ${timeoutMs} ms.\n${this.stderr}`)), timeoutMs);
      this.pending.set(id, message => { clearTimeout(timer); resolveResponse(message); });
      this.send({ jsonrpc: "2.0", id, method, params });
    });
    assert.equal(response.error, undefined, `${method} failed: ${JSON.stringify(response.error)}`);
    return response.result;
  }

  // Resolves with the next notification that matches, from now on.
  next(predicate, description) {
    return new Promise((resolveNext, reject) => {
      const timer = setTimeout(() => { this.listeners.delete(listener); reject(new Error(`Timed out waiting for ${description}.\n${this.stderr}`)); }, timeoutMs);
      const listener = message => {
        if (!predicate(message)) return;
        clearTimeout(timer);
        this.listeners.delete(listener);
        resolveNext(message);
      };
      this.listeners.add(listener);
    });
  }

  // Resident and peak resident memory of the server process (Linux), in bytes.
  memory() {
    const status = readFileSync(`/proc/${this.process.pid}/status`, "utf8");
    const value = name => Number(new RegExp(`^${name}:\\s+(\\d+) kB`, "m").exec(status)[1]) * 1024;
    return { residentBytes: value("VmRSS"), peakResidentBytes: value("VmHWM") };
  }

  async close() {
    await this.request("shutdown", null);
    this.notify("exit", null);
    this.process.stdin.end();
    assert.equal(await this.exited, 0, "the server did not exit cleanly");
  }
}

const elapsed = start => Number(process.hrtime.bigint() - start) / 1e6;
const median = values => [...values].sort((a, b) => a - b)[Math.floor(values.length / 2)];

// The UTF-16 position of the first occurrence of a needle, moved by delta characters.
function at(text, needle, delta = 0) {
  const offset = text.indexOf(needle);
  assert.ok(offset >= 0, `Missing ${needle}`);
  const before = text.slice(0, offset + delta);
  return { line: before.split("\n").length - 1, character: before.length - (before.lastIndexOf("\n") + 1) };
}

// One server session over the catalog: the measurements of one sample.
async function sample(directory, catalog, repeats) {
  const connection = new Connection(directory);
  try {
    await connection.request("initialize", { processId: process.pid, rootUri: pathToFileURL(directory).href,
      capabilities: { general: { positionEncodings: ["utf-16"] }, textDocument: { rename: { prepareSupport: true } },
        workspace: { workspaceEdit: { documentChanges: true } } } });
    connection.notify("initialized", {});
    const uri = pathToFileURL(resolve(directory, featureName(0), "en.rmf2")).href;
    const text = featureSource("en", catalog.messagesPerFeature);
    const publication = expectEmpty => connection.next(message => message.method === "textDocument/publishDiagnostics" &&
      message.params.uri === uri && (message.params.diagnostics.length === 0) === expectEmpty, `diagnostics of ${uri}`);
    const result = {};

    // Open: the whole catalog is validated with the open buffer before the first publication for it.
    let start = process.hrtime.bigint();
    const opened = publication(true);
    connection.notify("textDocument/didOpen", { textDocument: { uri, languageId: "rmf2", version: 1, text } });
    await opened;
    result.openMs = elapsed(start);
    Object.assign(result, connection.memory());
    result.peaks = { open: result.peakResidentBytes };

    // An edit that breaks a message, and the one that repairs it: each until its diagnostics arrive.
    const input = text.indexOf("{$name}");
    const broken = `${text.slice(0, input)}{$name${text.slice(input + "{$name}".length)}`;
    result.editMs = [];
    // A hover sent as the diagnostics arrive: it meets whatever the server does after applying a validation.
    result.hoverAfterDiagnosticsMs = [];
    for (let version = 2; version < 2 + 2 * repeats; version++) {
      start = process.hrtime.bigint();
      const published = publication(version % 2 === 1);
      connection.notify("textDocument/didChange", { textDocument: { uri, version }, contentChanges: [{ text: version % 2 === 0 ? broken : text }] });
      await published;
      result.editMs.push(elapsed(start));
      start = process.hrtime.bigint();
      await connection.request("textDocument/hover", { textDocument: { uri }, position: at(text, "  m1 =", 2) });
      result.hoverAfterDiagnosticsMs.push(elapsed(start));
    }

    result.peaks.edits = connection.memory().peakResidentBytes;
    // Hover on a message name, and completion inside an input: their latency in the valid catalog.
    result.hoverMs = [];
    result.completionMs = [];
    for (let repeat = 0; repeat < repeats; repeat++) {
      start = process.hrtime.bigint();
      const hover = await connection.request("textDocument/hover", { textDocument: { uri }, position: at(text, `  m${15 + 20 * repeat} =`, 3) });
      result.hoverMs.push(elapsed(start));
      assert.ok(hover?.contents?.value, "hover returned no contents");
      start = process.hrtime.bigint();
      const completion = await connection.request("textDocument/completion", { textDocument: { uri }, position: at(text, `{$name} ${14 + 20 * repeat}}}`, 2) });
      result.completionMs.push(elapsed(start));
      assert.ok((Array.isArray(completion) ? completion : completion.items).some(item => item.label === "$name"), "completion lacks $name");
    }

    // While typing: a burst of keystrokes, then a hover or a completion, as an editor sends them. The request's latency,
    // and the time from the last keystroke to the diagnostics of its version.
    result.hoverTypingMs = [];
    result.completionTypingMs = [];
    result.typingDiagnosticsMs = [];
    let version = 100;
    for (let repeat = 0; repeat < repeats; repeat++)
      for (const kind of ["hover", "completion"]) {
        let last = 0;
        for (let key = 0; key < catalog.keystrokes; key++) {
          last = ++version;
          connection.notify("textDocument/didChange", { textDocument: { uri, version: last },
            contentChanges: [{ text: text.replace("Open the selected", `Open the ${"x".repeat(key + 1)} selected`) }] });
        }
        const typed = process.hrtime.bigint();
        const diagnostics = connection.next(message => message.method === "textDocument/publishDiagnostics" && message.params.uri === uri &&
          message.params.version === last, `diagnostics of version ${last}`);
        if (kind === "hover") {
          const hover = await connection.request("textDocument/hover", { textDocument: { uri }, position: at(text, `  m${15 + 20 * repeat} =`, 3) });
          result.hoverTypingMs.push(elapsed(typed));
          assert.ok(hover?.contents?.value, "hover while typing returned no contents");
        } else {
          const completion = await connection.request("textDocument/completion", { textDocument: { uri }, position: at(text, `{$name} ${14 + 20 * repeat}}}`, 2) });
          result.completionTypingMs.push(elapsed(typed));
          assert.ok((Array.isArray(completion) ? completion : completion.items).some(item => item.label === "$name"), "completion while typing lacks $name");
        }
        const published = await diagnostics;
        result.typingDiagnosticsMs.push(elapsed(typed));
        assert.deepEqual(published.params.diagnostics, [], "the typed text has diagnostics");
      }
    // Back to the text on disk, so the rename sees the saved catalog.
    const restoredVersion = ++version;
    const restored = connection.next(message => message.method === "textDocument/publishDiagnostics" && message.params.uri === uri &&
      message.params.version === restoredVersion, "diagnostics of the restored text");
    connection.notify("textDocument/didChange", { textDocument: { uri, version: restoredVersion }, contentChanges: [{ text }] });
    await restored;

    result.peaks.typing = connection.memory().peakResidentBytes;
    // F2 on a resource: prepareRename, then the rename across every locale's file of the feature.
    const position = at(text, "  m0 =", 2);
    start = process.hrtime.bigint();
    const prepared = await connection.request("textDocument/prepareRename", { textDocument: { uri }, position });
    const rename = await connection.request("textDocument/rename", { textDocument: { uri }, position, newName: "first" });
    result.renameMs = elapsed(start);
    assert.equal(prepared?.placeholder, "m0", "prepareRename");
    assert.equal(rename.documentChanges.length, catalog.locales, "the rename did not edit every locale");

    result.peakResidentBytes = result.peaks.rename = connection.memory().peakResidentBytes;
    await connection.close();
    return result;
  } catch (error) {
    connection.process.kill();
    throw error;
  }
}

// The value of each metric from the samples: a latency is the median over every sample's measurements, memory the
// largest sample.
export const metrics = {
  "lsp/open-to-diagnostics-ms": samples => median(samples.map(item => item.openMs)),
  "lsp/edit-to-diagnostics-ms": samples => median(samples.flatMap(item => item.editMs)),
  "lsp/hover-ms": samples => median(samples.flatMap(item => item.hoverMs)),
  "lsp/completion-ms": samples => median(samples.flatMap(item => item.completionMs)),
  "lsp/hover-after-diagnostics-ms": samples => median(samples.flatMap(item => item.hoverAfterDiagnosticsMs)),
  "lsp/hover-while-typing-ms": samples => median(samples.flatMap(item => item.hoverTypingMs)),
  "lsp/completion-while-typing-ms": samples => median(samples.flatMap(item => item.completionTypingMs)),
  "lsp/typing-to-diagnostics-ms": samples => median(samples.flatMap(item => item.typingDiagnosticsMs)),
  "lsp/rename-ms": samples => median(samples.map(item => item.renameMs)),
  "lsp/resident-bytes-after-open": samples => Math.max(...samples.map(item => item.residentBytes)),
  "lsp/peak-resident-bytes": samples => Math.max(...samples.map(item => item.peakResidentBytes)),
};

const format = (name, value) => name.endsWith("-bytes") || name.includes("-bytes-")
  ? `${(value / (1 << 20)).toFixed(0)} MiB` : `${Math.round(value).toLocaleString("en-US")} ms`;

function table(catalog, results) {
  const lines = [`## Language server budgets (${(catalog.features * catalog.messagesPerFeature).toLocaleString("en-US")} messages × ${catalog.locales} locales, ${os.availableParallelism()} processors)`, "",
    "| Budget | Value | Limit | Target (#271) | Status |", "| --- | ---: | ---: | ---: | --- |"];
  for (const [name, result] of Object.entries(results))
    lines.push(`| \`${name}\` | ${format(name, result.value)} | ${result.limit === null ? "–" : format(name, result.limit)} | ${result.target === null ? "–" : format(name, result.target)} | ${result.status} |`);
  return `${lines.join("\n")}\n`;
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  const args = process.argv.slice(2);
  const updating = args.includes("--update");
  const budgets = JSON.parse(readFileSync(budgetsFile, "utf8"));
  const option = name => args.includes(name) ? Number(args[args.indexOf(name) + 1]) : undefined;
  const samples = option("--samples") || budgets.samples;
  if (option("--features")) budgets.catalog.features = option("--features");
  if (option("--repeats")) budgets.repeats = option("--repeats");
  for (const name of Object.keys(budgets.cases)) if (!metrics[name]) throw new Error(`budgets.json has ${name}, which measure.mjs does not measure.`);
  for (const name of Object.keys(metrics)) if (!budgets.cases[name]) throw new Error(`${name} has no entry in budgets.json.`);
  assert.equal(process.platform, "linux", "The language server budgets measure memory through /proc: run them on Linux.");

  if (!process.env.RUNIC_LSP_BUDGET_TOOL) {
    const build = spawnSync("dotnet", ["build", toolProject, "-c", "Release", "--nologo", "-v:q"], { cwd: root, stdio: "inherit" });
    if (build.status !== 0) throw new Error("Building the tool failed.");
  }
  mkdirSync(output, { recursive: true });
  const directory = resolve(output, "catalog");
  const files = writeCatalog(directory, budgets.catalog);
  console.log(`Wrote ${files} resource files: ${budgets.catalog.features} features × ${budgets.catalog.messagesPerFeature} messages × ${budgets.catalog.locales} locales.`);

  const measured = [];
  for (let index = 0; index < samples; index++) {
    const result = await sample(directory, budgets.catalog, budgets.repeats);
    console.log(`Sample ${index + 1}: open ${Math.round(result.openMs)} ms, edit ${result.editMs.map(Math.round).join("/")} ms, hover ${result.hoverMs.map(Math.round).join("/")} ms, ` +
      `completion ${result.completionMs.map(Math.round).join("/")} ms, while typing: hover ${result.hoverTypingMs.map(Math.round).join("/")} ms, ` +
      `completion ${result.completionTypingMs.map(Math.round).join("/")} ms, diagnostics ${result.typingDiagnosticsMs.map(Math.round).join("/")} ms, rename ${Math.round(result.renameMs)} ms, ` +
      `resident ${(result.residentBytes / (1 << 20)).toFixed(0)} MiB, peak ${(result.peakResidentBytes / (1 << 20)).toFixed(0)} MiB`);
    measured.push(result);
  }
  rmSync(directory, { recursive: true, force: true });

  const results = {};
  const failures = [];
  for (const [name, entry] of Object.entries(budgets.cases)) {
    const value = metrics[name](measured);
    let failure = null;
    if (!updating && entry.limit === undefined) failure = `${name} has no limit in budgets.json; run bun tests/lsp-budget/measure.mjs --update.`;
    else if (!updating && value > entry.limit) failure = `${name} is ${format(name, value)}, over its limit ${format(name, entry.limit)}.`;
    // Well under its limit: not a failure, but the budget can be lowered (a run on the CI runner, then --update).
    const improved = !failure && !updating && entry.measured !== undefined && value < 0.5 * entry.measured;
    results[name] = { value, limit: updating ? null : entry.limit ?? null, target: entry.target ?? null,
      status: failure ? "failed" : updating ? "recorded" : improved ? "ok, improved: lower the budget" : "ok", failure };
    if (failure) failures.push(failure);
    if (updating) {
      entry.measured = Math.round(value);
      entry.limit = Math.ceil(value * entry.headroom);
    }
  }
  writeFileSync(resolve(output, "results.json"), `${JSON.stringify({ schema: 1, catalog: budgets.catalog, processors: os.availableParallelism(),
    cases: results, samples: measured }, null, 2)}\n`);
  if (updating) {
    writeFileSync(budgetsFile, `${JSON.stringify(budgets, null, 2)}\n`);
    console.log(`Updated ${budgetsFile}.`);
  }
  const summary = table(budgets.catalog, results);
  console.log(summary);
  if (process.env.GITHUB_STEP_SUMMARY) appendFileSync(process.env.GITHUB_STEP_SUMMARY, summary);
  for (const failure of failures) console.error(failure);
  process.exit(failures.length ? 1 : 0);
}
