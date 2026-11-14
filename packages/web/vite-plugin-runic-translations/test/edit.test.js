// The in-page editing API of the dev server (#178): who may call it, how requests are checked, and how the compiler's
// replies map to HTTP. The compiler side (reading and writing sources) is tested in the Build suite's
// ServeMessageEditingTests, and end to end in translations-svelte's verify-hmr.mjs.
import assert from "node:assert/strict";
import { mkdtemp, mkdir, rm, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { Readable } from "node:stream";
import test from "node:test";
import { runicTranslations } from "../dist/index.js";
import { AccessList, allowListFromEnvironment, createEditMiddleware, isLoopback, normalizeAddress, parseAllowList } from "../dist/edit.js";

const message = { catalog: "app", key: "title", locales: [{ locale: "en", text: "Hello", file: { path: "/p/en.rmf2", hash: "none" } }] };
const hash = `sha256:${"a".repeat(64)}`;

test("the allow list accepts addresses, CIDR networks and host names, and rejects anything else", () => {
  const rules = parseAllowList(["100.64.0.0/10", "192.168.1.20", "fd7a:115c:a1e0::/48", "[fe80::1]", " laptop.tailnet.ts.net ", "agent-server"], "test");
  assert.deepEqual(rules.map(rule => rule.kind), ["network", "address", "network", "address", "host", "host"]);
  assert.deepEqual(rules[0], { kind: "network", text: "100.64.0.0/10", address: "100.64.0.0", prefix: 10, family: "ipv4" });
  assert.equal(rules[2].family, "ipv6");
  assert.equal(rules[4].name, "laptop.tailnet.ts.net");
  for (const bad of ["100.64.0.0/33", "fd00::/129", "100.64.0.0/", "100.64.0.0/x", "300.1.1.1/8", "1.2.3", "*", "not a host", "", "-bad.example", "a/b/c"])
    assert.throws(() => parseAllowList([bad], "inPageEditing.allow"), error => error instanceof TypeError && error.message.startsWith("inPageEditing.allow"), bad);
  assert.throws(() => parseAllowList([42], "inPageEditing.allow"), TypeError);
  assert.deepEqual(allowListFromEnvironment(" 100.64.0.0/10, 10.0.0.5\nlaptop "), ["100.64.0.0/10", "10.0.0.5", "laptop"]);
  assert.deepEqual(allowListFromEnvironment(undefined), []);
});

test("loopback is always allowed; other clients only inside an allowed network, address or host", async () => {
  for (const address of ["127.0.0.1", "127.8.9.10", "::1", "::ffff:127.0.0.1"]) assert.ok(isLoopback(address), address);
  for (const address of ["10.0.0.1", "::2", "fe80::1", "::ffff:10.0.0.1", "garbage"]) assert.ok(!isLoopback(address), address);
  assert.equal(normalizeAddress("::ffff:100.100.1.2"), "100.100.1.2");
  assert.equal(normalizeAddress("fe80::1%eth0"), "fe80::1");

  const none = new AccessList([]);
  assert.ok(await none.allows("127.0.0.1"));
  assert.ok(await none.allows("::1"));
  assert.ok(!(await none.allows("100.100.1.2")));
  assert.ok(!(await none.allows(undefined)));

  const lookups = [];
  const access = new AccessList(parseAllowList(["100.64.0.0/10", "192.168.1.20", "fd7a:115c:a1e0::/48", "laptop.example"], "test"),
    async name => { lookups.push(name); return ["192.168.7.7", "fd00::7"]; });
  for (const [address, allowed] of [
    ["100.64.0.1", true], ["100.127.255.254", true], ["100.128.0.1", false], ["100.63.255.255", false], ["::ffff:100.100.1.2", true],
    ["192.168.1.20", true], ["192.168.1.21", false],
    ["fd7a:115c:a1e0::1", true], ["fd7a:115c:a1e0:ffff::1", true], ["fd7a:115c:a1e1::1", false],
    ["192.168.7.7", true], ["fd00::7", true], ["fd00::8", false],
  ]) assert.equal(await access.allows(address), allowed, address);
  assert.deepEqual(lookups, ["laptop.example"], "a host name is resolved once and cached");
});

// Calls the middleware as connect would, with a client address and headers.
function invoke(middleware, { method = "GET", url, headers = {}, address = "127.0.0.1", body } = {}) {
  return new Promise((resolve, reject) => {
    const request = Readable.from(body === undefined ? [] : [Buffer.from(typeof body === "string" ? body : JSON.stringify(body))]);
    Object.assign(request, { method, url, headers: { host: "localhost:5173", ...headers }, socket: { remoteAddress: address } });
    const response = {
      statusCode: 200, headers: {}, headersSent: false,
      setHeader(name, value) { this.headers[name.toLowerCase()] = value; },
      end(text) { resolve({ status: this.statusCode, headers: this.headers, body: text ? JSON.parse(text) : undefined }); },
    };
    middleware(request, response, error => error ? reject(error) : resolve("next"));
  });
}

function backend(reply = { ok: true, exitCode: 0, result: { message } }, overrides = {}) {
  const calls = [];
  return {
    calls,
    unavailable: () => undefined,
    catalogs: () => ({ application: "app", libraries: ["acme.grid"] }),
    request: async (method, body) => { calls.push([method, body]); if (reply instanceof Error) throw reply; return typeof reply === "function" ? reply(method, body) : reply; },
    ...overrides,
  };
}

const sameOrigin = { origin: "http://localhost:5173", "sec-fetch-site": "same-origin" };
const post = (body, headers = {}) => ({ method: "POST", url: "/__runic-translations/message", headers: { ...sameOrigin, "content-type": "application/json", ...headers }, body });

test("GET reads a message of the application's or a library catalog", async () => {
  const api = backend();
  const middleware = createEditMiddleware(api, new AccessList([]), "/");
  const read = await invoke(middleware, { url: "/__runic-translations/message?key=title", headers: { "sec-fetch-site": "same-origin" } });
  assert.equal(read.status, 200);
  assert.deepEqual(read.body, { message });
  assert.equal(read.headers["cache-control"], "no-store");
  assert.deepEqual(api.calls.at(-1), ["message", { key: "title" }]);

  await invoke(middleware, { url: "/__runic-translations/message?key=empty&catalog=acme.grid" });
  assert.deepEqual(api.calls.at(-1), ["message", { catalog: "acme.grid", key: "empty" }]);
  await invoke(middleware, { url: "/__runic-translations/message?key=title&catalog=app" });
  assert.deepEqual(api.calls.at(-1), ["message", { key: "title" }], "the application's catalog is the compiler's default");

  const unknown = await invoke(middleware, { url: "/__runic-translations/message?key=title&catalog=other" });
  assert.equal(unknown.status, 404);
  assert.match(unknown.body.detail, /app, acme\.grid/);
  for (const key of ["", "a b", "../x", "x".repeat(513)])
    assert.equal((await invoke(middleware, { url: `/__runic-translations/message?key=${encodeURIComponent(key)}` })).status, 400, key);
  assert.equal(api.calls.length, 3, "invalid requests never reach the compiler");

  assert.equal(await invoke(middleware, { url: "/src/main.ts" }), "next", "other paths pass through");
  assert.equal((await invoke(middleware, { url: "/__runic-translations/other" })).status, 404);
  const based = createEditMiddleware(api, new AccessList([]), "/app/");
  assert.equal((await invoke(based, { url: "/app/__runic-translations/message?key=title" })).status, 200, "below Vite's base");
});

test("POST writes one locale's text; compiler outcomes map to HTTP statuses", async () => {
  const api = backend({ ok: true, exitCode: 0, result: { written: ["/p/de.rmf2"], message } });
  const middleware = createEditMiddleware(api, new AccessList([]), "/");
  const edit = { key: "title", locale: "de", text: "Hallo", expectedHash: hash };
  const written = await invoke(middleware, post(edit));
  assert.equal(written.status, 200);
  assert.deepEqual(written.body, { written: ["/p/de.rmf2"], message });
  assert.deepEqual(api.calls.at(-1), ["set-message", { key: "title", locale: "de", text: "Hallo", expectedHash: hash }]);
  await invoke(middleware, post({ ...edit, catalog: "acme.grid", expectedHash: null }));
  assert.deepEqual(api.calls.at(-1), ["set-message", { catalog: "acme.grid", key: "title", locale: "de", text: "Hallo", expectedHash: null }]);

  const diagnostics = [{ code: "RCLI8204", severity: "error", message: "does not compile" }, { code: "RTR0066", severity: "error", message: "Unterminated", path: "translations/de.rmf2", line: 1, column: 9 }];
  const cases = [
    [{ ok: false, exitCode: 1, diagnostics: [{ code: "RCLI8204", severity: "error", message: "changed since" }], result: { error: "conflict", message } }, 409, "conflict"],
    [{ ok: false, exitCode: 1, diagnostics, result: { error: "invalid" } }, 422, "invalid"],
    [{ ok: false, exitCode: 2, diagnostics: [{ code: "RCLI8203", severity: "error", message: "no locale" }], result: { error: "not-found" } }, 404, "not-found"],
    [{ ok: false, exitCode: 1, diagnostics: [{ code: "RCLI8204", severity: "error", message: "no file" }], result: { error: "unsupported", message } }, 422, "unsupported"],
    [{ ok: false, exitCode: 2, diagnostics: [{ code: "RCLI8203", severity: "error", message: "set-message requires text" }], result: undefined }, 400, "bad-request"],
  ];
  for (const [reply, status, error] of cases) {
    const result = await invoke(createEditMiddleware(backend(reply), new AccessList([]), "/"), post(edit));
    assert.equal(result.status, status, error);
    assert.equal(result.body.error, error);
    assert.equal(result.body.detail, reply.diagnostics[0].message);
    if (reply.result?.message) assert.deepEqual(result.body.message, message, `${error}: the current message`);
    if (error === "invalid") assert.deepEqual(result.body.diagnostics, [diagnostics[1]], "only the translation diagnostics");
  }
  const down = await invoke(createEditMiddleware(backend(new Error("exited")), new AccessList([]), "/"), post(edit));
  assert.equal(down.status, 503);
  const manifest = await invoke(createEditMiddleware(backend(undefined, { unavailable: () => "needs project mode" }), new AccessList([]), "/"), post(edit));
  assert.equal(manifest.status, 501);

  for (const [body, description] of [[{ ...edit, key: 1 }, "key"], [{ ...edit, locale: "" }, "locale"], [{ ...edit, text: 5 }, "text"],
    [{ ...edit, expectedHash: "md5:x" }, "hash"], [{ ...edit, catalog: 3 }, "catalog"], ["[1]", "array"], ["{", "malformed"]])
    assert.equal((await invoke(middleware, post(body))).status, 400, description);
  assert.equal((await invoke(middleware, post(edit, { "content-type": "text/plain" }))).status, 415);
  assert.equal((await invoke(middleware, post("x".repeat(600 * 1024)))).status, 413);
  const put = await invoke(middleware, { ...post(edit), method: "PUT" });
  assert.equal(put.status, 405);
  assert.equal(put.headers.allow, "GET, HEAD, POST");
});

test("requests from beyond loopback, or from another origin, are refused unless allowed", async () => {
  const api = backend();
  const warnings = [];
  const middleware = createEditMiddleware(api, new AccessList([]), "/", { warn: line => warnings.push(line) });
  const read = { url: "/__runic-translations/message?key=title" };
  for (const address of ["100.100.1.2", "192.168.1.20", "fd7a:115c:a1e0::1", "::ffff:10.0.0.1", null]) {
    const refused = await invoke(middleware, { ...read, address });
    assert.equal(refused.status, 403, String(address));
    assert.match(refused.body.detail, /RUNIC_TRANSLATIONS_EDIT_ALLOW/);
  }
  assert.match(warnings[0], /refused a request from 100\.100\.1\.2/);
  // X-Forwarded-For is never trusted.
  assert.equal((await invoke(middleware, { ...read, address: "100.100.1.2", headers: { "x-forwarded-for": "127.0.0.1" } })).status, 403);

  const allowed = createEditMiddleware(api, new AccessList(parseAllowList(["100.64.0.0/10", "fd7a:115c:a1e0::/48"], "test")), "/");
  assert.equal((await invoke(allowed, { ...read, address: "100.100.1.2", headers: { host: "100.100.1.1:5173" } })).status, 200);
  assert.equal((await invoke(allowed, { ...read, address: "fd7a:115c:a1e0::9" })).status, 200);
  assert.equal((await invoke(allowed, { ...read, address: "100.128.0.1" })).status, 403);
  assert.equal((await invoke(allowed, { ...post({ key: "title", locale: "en", text: "Hi", expectedHash: hash }, { origin: "http://100.100.1.1:5173", host: "100.100.1.1:5173" }),
    address: "100.100.1.2" })).status, 200, "a write from an allowed laptop to the dev server's address");

  // Same origin: the page's Origin must name the dev server, and Sec-Fetch-Site must not say cross-site.
  assert.equal((await invoke(middleware, { ...read, headers: { origin: "http://evil.example" } })).status, 403);
  assert.equal((await invoke(middleware, { ...read, headers: { origin: "http://localhost:5174" } })).status, 403, "another localhost port");
  assert.equal((await invoke(middleware, { ...read, headers: { "sec-fetch-site": "cross-site" } })).status, 403);
  assert.equal((await invoke(middleware, { ...read, headers: { "sec-fetch-site": "same-site" } })).status, 403);
  assert.equal((await invoke(middleware, { ...read, headers: { origin: "null" } })).status, 403);
  assert.equal((await invoke(middleware, post({ key: "title", locale: "en", text: "Hi", expectedHash: hash }, { origin: undefined }))).status, 403, "a write needs Origin");
  assert.equal((await invoke(middleware, post({ key: "title", locale: "en", text: "Hi", expectedHash: hash }, { origin: "http://evil.example" }))).status, 403);
  assert.equal(api.calls.length, 3, "only the allowed requests reached the compiler");
});

test("the plugin validates the editing options and the environment variable", async () => {
  assert.throws(() => runicTranslations({ inPageEditing: "yes" }), /inPageEditing must be a boolean/);
  assert.throws(() => runicTranslations({ inPageEditing: { allow: "100.64.0.0/10" } }), /inPageEditing must be a boolean/);
  assert.throws(() => runicTranslations({ inPageEditing: { allow: ["100.64.0.0/40"] } }), /inPageEditing\.allow: '100\.64\.0\.0\/40' is not a network/);

  const root = await mkdtemp(join(tmpdir(), "runic-vite-edit-"));
  const previous = process.env.RUNIC_TRANSLATIONS_EDIT_ALLOW;
  try {
    const { plugin, server, used } = await fakeServer(root, {});
    process.env.RUNIC_TRANSLATIONS_EDIT_ALLOW = "10.0.0.0/8 nope/1";
    assert.throws(() => plugin.configureServer(server), /RUNIC_TRANSLATIONS_EDIT_ALLOW: 'nope\/1' is not a network/);
    process.env.RUNIC_TRANSLATIONS_EDIT_ALLOW = "10.0.0.0/8";
    const next = await fakeServer(root, {});
    next.plugin.configureServer(next.server);
    assert.equal(next.used.length, 1, "mounted under vite dev");
    assert.match(next.infos.join("\n"), /accepts requests from loopback and 10\.0\.0\.0\/8/);
    const off = await fakeServer(root, { inPageEditing: false });
    off.plugin.configureServer(off.server);
    assert.equal(off.used.length, 0, "inPageEditing: false mounts nothing");
    assert.equal(used.length, 0);
  } finally {
    if (previous === undefined) delete process.env.RUNIC_TRANSLATIONS_EDIT_ALLOW; else process.env.RUNIC_TRANSLATIONS_EDIT_ALLOW = previous;
    await rm(root, { recursive: true, force: true });
  }
});

async function fakeServer(root, options) {
  const project = join(root, "translations");
  await mkdir(project, { recursive: true });
  await writeFile(join(project, "runic.json"), JSON.stringify({ schemaVersion: 1, catalog: "app" }));
  const plugin = runicTranslations({ project, output: join(root, "generated"), persistentCompiler: false, ...options });
  const used = [], infos = [];
  const watcher = { add() {}, on() {}, off() {} };
  const server = { watcher, httpServer: { once() {} }, ws: { send() {} }, middlewares: { use: handler => used.push(handler) },
    config: { base: "/", logger: { info: line => infos.push(line), warn() {} } } };
  return { plugin, server, used, infos };
}
