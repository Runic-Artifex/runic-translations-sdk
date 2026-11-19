#!/usr/bin/env bun
// A minimal stdio LSP client, as a generic editor (Neovim, Helix, LSP4IJ) is one: it starts the server with the command
// the language server guide gives those editors, sends no initializationOptions, and uses only standard LSP. It checks
// initialize, diagnostics, hover, completion, prepareRename and rename, a quick fix, and a clean shutdown and exit.
//
//   bun tests/lsp-generic-client/client.mjs <workspace> -- <server command...>
//
// <workspace> holds runic.json, en.rmf2 and de.rmf2 as eng/lsp-smoke.mjs writes them. No dependencies: Bun or Node.
import assert from "node:assert/strict";
import { spawn } from "node:child_process";
import { readFileSync } from "node:fs";
import { resolve } from "node:path";
import { pathToFileURL } from "node:url";

const separator = process.argv.indexOf("--");
assert.ok(separator === 3 && process.argv.length > 4, "Usage: client.mjs <workspace> -- <server command...>");
const workspace = resolve(process.argv[2]);
const [command, ...args] = process.argv.slice(4);
const timeoutMs = Number(process.env.RUNIC_LSP_SMOKE_TIMEOUT_MS ?? 60_000);

const uriOf = name => pathToFileURL(resolve(workspace, name)).href;
const englishUri = uriOf("en.rmf2"), germanUri = uriOf("de.rmf2");
const english = readFileSync(resolve(workspace, "en.rmf2"), "utf8");

// The UTF-16 position of the first occurrence of a needle, moved by delta characters.
function at(text, needle, delta = 0) {
  const offset = text.indexOf(needle);
  assert.ok(offset >= 0, `Missing ${needle}`);
  const before = text.slice(0, offset + delta);
  const line = before.split("\n").length - 1;
  return { line, character: before.length - (before.lastIndexOf("\n") + 1) };
}

const server = spawn(command, args, { cwd: workspace, stdio: ["pipe", "pipe", "pipe"] });
let stderr = "";
server.stderr.setEncoding("utf8");
server.stderr.on("data", chunk => { stderr += chunk; });
const exited = new Promise(resolveExit => server.on("exit", (code, signal) => resolveExit({ code, signal })));

// Content-Length framing over stdout. Responses complete their request; server requests get MethodNotFound, as a
// client without those features answers; notifications are kept.
let buffer = Buffer.alloc(0);
let nextId = 0;
const pending = new Map();
const notifications = [];
const serverRequests = [];
const waiters = [];
server.stdout.on("data", chunk => {
  buffer = Buffer.concat([buffer, chunk]);
  for (;;) {
    const headerEnd = buffer.indexOf("\r\n\r\n");
    if (headerEnd < 0) return;
    const match = /Content-Length: *(\d+)/i.exec(buffer.subarray(0, headerEnd).toString("ascii"));
    assert.ok(match, "Frame without Content-Length");
    const length = Number(match[1]);
    if (buffer.length < headerEnd + 4 + length) return;
    const message = JSON.parse(buffer.subarray(headerEnd + 4, headerEnd + 4 + length).toString("utf8"));
    buffer = buffer.subarray(headerEnd + 4 + length);
    if (message.method === undefined) {
      pending.get(message.id)?.(message);
      pending.delete(message.id);
    } else if (message.id !== undefined) {
      serverRequests.push(message);
      send({ jsonrpc: "2.0", id: message.id, error: { code: -32601, message: `Unhandled method ${message.method}` } });
    } else {
      notifications.push(message);
      for (const waiter of [...waiters]) waiter();
    }
  }
});

function send(message) {
  const body = Buffer.from(JSON.stringify(message), "utf8");
  server.stdin.write(`Content-Length: ${body.length}\r\n\r\n`);
  server.stdin.write(body);
}

function request(method, params) {
  const id = ++nextId;
  return new Promise((resolveResponse, reject) => {
    const timer = setTimeout(() => reject(new Error(`No response to ${method} within ${timeoutMs} ms`)), timeoutMs);
    pending.set(id, response => { clearTimeout(timer); resolveResponse(response); });
    send({ jsonrpc: "2.0", id, method, params });
  });
}

async function result(method, params) {
  const response = await request(method, params);
  assert.equal(response.error, undefined, `${method} failed: ${JSON.stringify(response.error)}`);
  return response.result;
}

const notify = (method, params) => send({ jsonrpc: "2.0", method, params });

function waitFor(predicate, description) {
  return new Promise((resolveWait, reject) => {
    const check = () => {
      const found = notifications.find(predicate);
      if (!found) return false;
      waiters.splice(waiters.indexOf(check), 1);
      clearTimeout(timer);
      resolveWait(found);
      return true;
    };
    const timer = setTimeout(() => { waiters.splice(waiters.indexOf(check), 1); reject(new Error(`Timed out waiting for ${description}`)); }, timeoutMs);
    waiters.push(check);
    check();
  });
}

const steps = [];
const step = (name, detail = "") => { steps.push(name); console.log(`ok ${steps.length} - ${name}${detail ? `: ${detail}` : ""}`); };

try {
  // The capabilities a generic client typically advertises; deliberately no initializationOptions.
  const initialize = await result("initialize", {
    processId: process.pid,
    clientInfo: { name: "runic-lsp-generic-client-smoke" },
    rootUri: pathToFileURL(workspace).href,
    workspaceFolders: [{ uri: pathToFileURL(workspace).href, name: "smoke" }],
    capabilities: {
      workspace: { applyEdit: true, workspaceEdit: { documentChanges: true, resourceOperations: ["create", "rename", "delete"] } },
      textDocument: {
        synchronization: { didSave: true },
        publishDiagnostics: { relatedInformation: true },
        hover: { contentFormat: ["markdown", "plaintext"] },
        completion: { completionItem: { snippetSupport: false } },
        rename: { prepareSupport: true },
        codeAction: { codeActionLiteralSupport: { codeActionKind: { valueSet: ["quickfix", "refactor", "refactor.extract", "refactor.inline"] } } },
      },
    },
  });
  const capabilities = initialize.capabilities;
  assert.equal(capabilities.experimental?.runic?.protocol, 2, "experimental.runic.protocol");
  assert.equal(capabilities.hoverProvider, true, "hoverProvider");
  assert.ok(capabilities.completionProvider, "completionProvider");
  assert.equal(capabilities.renameProvider?.prepareProvider, true, "renameProvider.prepareProvider");
  assert.ok(capabilities.codeActionProvider?.codeActionKinds?.includes("quickfix"), "codeActionProvider quickfix");
  step("initialize", `${initialize.serverInfo.name} ${initialize.serverInfo.version}, protocol ${capabilities.experimental.runic.protocol}`);
  notify("initialized", {});

  notify("textDocument/didOpen", { textDocument: { uri: englishUri, languageId: "rmf2", version: 1, text: english } });
  const clean = await waitFor(message => message.method === "textDocument/publishDiagnostics" && message.params.uri === englishUri,
    "diagnostics for en.rmf2");
  assert.deepEqual(clean.params.diagnostics, [], "the valid catalog has diagnostics");
  step("didOpen and diagnostics", "none for the valid catalog");

  const hover = await result("textDocument/hover", { textDocument: { uri: englishUri }, position: at(english, "title") });
  assert.ok(hover?.contents?.value?.includes("title"), `hover: ${JSON.stringify(hover)}`);
  step("hover");

  const completion = await result("textDocument/completion", { textDocument: { uri: englishUri }, position: at(english, "{$name}", 2) });
  const labels = (Array.isArray(completion) ? completion : completion?.items ?? []).map(item => item.label);
  assert.ok(labels.includes("$name"), `completion labels: ${labels.join(", ")}`);
  step("completion", `${labels.length} items`);

  const inputPosition = at(english, "{$name}", 2);
  const prepare = await result("textDocument/prepareRename", { textDocument: { uri: englishUri }, position: inputPosition });
  assert.equal(prepare?.placeholder, "name", `prepareRename: ${JSON.stringify(prepare)}`);
  step("prepareRename");
  const rename = await result("textDocument/rename", { textDocument: { uri: englishUri }, position: inputPosition, newName: "person" });
  const edited = (rename?.documentChanges ?? []).map(change => change.textDocument?.uri).sort();
  assert.deepEqual(edited, [germanUri, englishUri].sort(), `rename: ${JSON.stringify(rename)}`);
  assert.ok(rename.documentChanges.every(change => change.edits[0].newText.includes("{$person}")), "rename did not rename the input");
  step("rename", "en.rmf2 and de.rmf2");

  // An unsaved edit that adds an assignment without a body: RTR0050, whose quick fix makes the message empty.
  const broken = `${english}empty =\n`;
  const published = notifications.length;
  notify("textDocument/didChange", { textDocument: { uri: englishUri, version: 2 }, contentChanges: [{ text: broken }] });
  const update = await waitFor(message => notifications.indexOf(message) >= published && message.method === "textDocument/publishDiagnostics" &&
    message.params.uri === englishUri && message.params.diagnostics.length > 0, "diagnostics after the edit");
  const diagnostic = update.params.diagnostics.find(item => item.code === "RTR0050");
  assert.ok(diagnostic, `diagnostics: ${JSON.stringify(update.params.diagnostics)}`);
  step("didChange and diagnostics", `${diagnostic.code ?? ""} ${diagnostic.message}`.trim());

  const actions = await result("textDocument/codeAction", {
    textDocument: { uri: englishUri }, range: diagnostic.range, context: { diagnostics: [diagnostic], only: ["quickfix"] } });
  const fix = (actions ?? []).find(action => action.kind === "quickfix" && action.edit);
  assert.ok(fix, `code actions: ${JSON.stringify(actions)}`);
  assert.equal(fix.edit.documentChanges?.[0]?.edits?.[0]?.newText, " {{}}", `quick fix: ${JSON.stringify(fix)}`);
  step("codeAction", fix.title);

  assert.equal(await result("shutdown", null), null, "shutdown result");
  notify("exit", null);
  server.stdin.end();
  const exit = await Promise.race([exited, new Promise(resolveTimeout => setTimeout(() => resolveTimeout({ code: "timeout" }), timeoutMs))]);
  assert.deepEqual(exit, { code: 0, signal: null }, "the server did not exit cleanly");
  step("shutdown and exit");
  assert.deepEqual(serverRequests.map(message => message.method).filter(method => method !== "client/registerCapability"), [],
    "unexpected server-to-client requests");
  console.log(`LSP_GENERIC_CLIENT_SMOKE_OK (${steps.length} steps)`);
} catch (error) {
  console.error(`not ok - ${error.stack ?? error.message}`);
  if (stderr) console.error(`--- server stderr ---\n${stderr}`);
  server.kill();
  process.exitCode = 1;
}
