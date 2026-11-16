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
import { AccessList, allowListFromEnvironment, createEditMiddleware as createMiddleware, isLoopback, isTrustedHost, normalizeAddress, parseAllowList } from "../dist/edit.js";

// The middleware with the test's client counted as the server's own user: the requests here have no real socket for the
// owner check (local-user.ts), which its own test covers.
const createEditMiddleware = (api, access, base, logger, guards = {}) => createMiddleware(api, access, base, logger, { localUser: async () => true, ...guards });

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
    assert.match(refused.body.detail, /inPageEditing: \{ allow: \[\.\.\.\] \} \}\) or RUNIC_DEV_ALLOW\./);
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

test("a write from loopback needs a client of the server's own user; reads and allowed machines do not", async () => {
  const api = backend();
  const owners = [];
  const middleware = createEditMiddleware(api, new AccessList(parseAllowList(["100.64.0.0/10"], "test")), "/", undefined, {
    localUser: async socket => { owners.push(socket.remoteAddress); return false; },
  });
  const edit = { key: "title", locale: "en", text: "Hi", expectedHash: hash };
  const refused = await invoke(middleware, post(edit));
  assert.equal(refused.status, 403);
  assert.match(refused.body.detail, /another account on this machine/);
  assert.equal((await invoke(middleware, { ...post(edit), address: "::ffff:127.0.0.1" })).status, 403);
  assert.equal((await invoke(middleware, { url: "/__runic-translations/message?key=title" })).status, 200, "a read");
  assert.equal((await invoke(middleware, { ...post(edit, { origin: "http://100.100.1.1:5173", host: "100.100.1.1:5173" }), address: "100.100.1.2" })).status, 200,
    "an allowed machine is judged by its address; the owner check is for loopback");
  assert.deepEqual(owners, ["127.0.0.1", "::ffff:127.0.0.1"]);
  assert.deepEqual(api.calls.map(([method]) => method), ["message", "set-message"]);
});

test("with server.allowedHosts: true, the API answers IP literals, localhost and the allow list's names only", async () => {
  const access = new AccessList(parseAllowList(["laptop.tailnet.ts.net", "100.64.0.0/10"], "test"));
  for (const host of ["127.0.0.1:5173", "[::1]:5173", "100.100.1.2", "localhost:5173", "app.localhost", "Laptop.Tailnet.TS.net.:5173", undefined])
    assert.equal(isTrustedHost(host, access), true, String(host));
  for (const host of ["rebind.attacker.example:5173", "localhost.attacker.example", "127.0.0.1.nip.io", "", "[::1", "[127.0.0.1]"])
    assert.equal(isTrustedHost(host, access), false, host);

  const api = backend();
  const checked = createEditMiddleware(api, access, "/", undefined, { checkHost: true });
  const unchecked = createEditMiddleware(api, access, "/");
  const rebound = { origin: "http://rebind.attacker.example:5173", host: "rebind.attacker.example:5173", "sec-fetch-site": "same-origin" };
  const edit = { key: "title", locale: "en", text: "Written through DNS rebinding", expectedHash: hash };
  const refused = await invoke(checked, post(edit, rebound));
  assert.equal(refused.status, 403);
  assert.match(refused.body.detail, /allowedHosts: true/);
  assert.equal((await invoke(checked, { url: "/__runic-translations/message?key=title", headers: rebound })).status, 403);
  assert.equal((await invoke(checked, post(edit))).status, 200, "the dev server's own localhost page");
  assert.equal((await invoke(unchecked, post(edit, rebound))).status, 200, "without allowedHosts: true, Vite's host check refuses it first");
});

test("the owner of a loopback client comes from /proc/net/tcp and tcp6", async () => {
  const { addressKey, createLocalUserCheck, parseProcNetTcp } = await import("../dist/local-user.js");
  const header = "  sl  local_address rem_address   st tx_queue rx_queue tr tm->when retrnsmt   uid  timeout inode\n";
  const row = (local, remote, uid) => `   0: ${local} ${remote} 01 00000000:00000000 00:00000000 00000000 ${String(uid).padStart(5)}        0 4242 1\n`;
  const tables = {
    "/proc/net/tcp": header + row("0100007F:A0F2", "0100007F:1435", 1001) + row("0100007F:1435", "0100007F:A0F2", 1000),
    "/proc/net/tcp6": header + row("00000000000000000000000001000000:A0F3", "00000000000000000000000001000000:1435", 1000) +
      row("0000000000000000FFFF00000100007F:A0F4", "0000000000000000FFFF00000100007F:1435", 1000),
  };
  assert.equal(parseProcNetTcp(tables["/proc/net/tcp"], true)[0].localAddress, addressKey("127.0.0.1"));
  assert.equal(parseProcNetTcp(header + row("7F000001:A0F2", "7F000001:1435", 7), false)[0].localAddress, addressKey("::ffff:127.0.0.1"));
  const read = async path => { if (path in tables) return tables[path]; throw new Error("ENOENT"); };
  const check = createLocalUserCheck({ platform: "linux", uid: 1000, read, littleEndian: true });
  const socket = (remoteAddress, remotePort) => ({ remoteAddress, remotePort, localAddress: remoteAddress, localPort: 5173 });
  assert.equal(await check(socket("127.0.0.1", 41_202)), false, "another user's client");
  assert.equal(await check(socket("::1", 41_203)), true);
  assert.equal(await check(socket("::ffff:127.0.0.1", 41_204)), true, "an IPv4 client of a dual-stack server");
  assert.equal(await check(socket("127.0.0.1", 40_000)), false, "not in the tables");
  assert.equal(await check({ remoteAddress: "127.0.0.1" }), false, "no endpoints");
  assert.equal(await createLocalUserCheck({ platform: "linux", uid: 1000, read: async () => { throw new Error("ENOENT"); } })(socket("127.0.0.1", 41_202)), true,
    "no tables: the check is not available");
  assert.equal(await createLocalUserCheck({ platform: "darwin", uid: 1000, read })(socket("127.0.0.1", 41_202)), true);
  if (process.platform === "linux") {
    // A real connection of this process: its own user.
    const { createServer } = await import("node:http");
    const { connect } = await import("node:net");
    const { server, client } = await new Promise(resolve => {
      const listening = createServer().on("connection", accepted => resolve({ server: listening, client: accepted }));
      listening.listen(0, "127.0.0.1", () => connect(listening.address().port, "127.0.0.1"));
    });
    try {
      assert.equal(await createLocalUserCheck()(client), true);
      assert.equal(await createLocalUserCheck({ uid: process.getuid() + 1 })(client), false);
    } finally { client.destroy(); server.close(); }
  }
});

test("the plugin validates the editing options and the environment variable", async () => {
  assert.throws(() => runicTranslations({ inPageEditing: "yes" }), /inPageEditing must be a boolean/);
  assert.throws(() => runicTranslations({ inPageEditing: { allow: "100.64.0.0/10" } }), /inPageEditing must be a boolean/);
  assert.throws(() => runicTranslations({ inPageEditing: { allow: ["100.64.0.0/40"] } }), /inPageEditing\.allow: '100\.64\.0\.0\/40' is not a network/);

  const root = await mkdtemp(join(tmpdir(), "runic-vite-edit-"));
  const previous = process.env.RUNIC_TRANSLATIONS_EDIT_ALLOW;
  const previousShared = process.env.RUNIC_DEV_ALLOW;
  delete process.env.RUNIC_DEV_ALLOW;
  try {
    const { used } = await fakeServer(root, {});
    // RUNIC_TRANSLATIONS_EDIT_ALLOW was removed in 0.13: the plugin ignores it, even an invalid list, and warns.
    process.env.RUNIC_TRANSLATIONS_EDIT_ALLOW = "10.0.0.0/8 nope/1";
    const next = await fakeServer(root, {});
    next.plugin.configureServer(next.server);
    assert.equal(next.used.length, 1, "mounted under vite dev");
    assert.doesNotMatch(next.infos.join("\n"), /accepts requests from/);
    assert.match(next.warnings.join("\n"), /RUNIC_TRANSLATIONS_EDIT_ALLOW is no longer read .*Set RUNIC_DEV_ALLOW/);
    // RUNIC_DEV_ALLOW, the setting vite-plugin-runic shares, adds to the list and is validated under its own name.
    process.env.RUNIC_DEV_ALLOW = "100.64.0.0/10,laptop.tailnet.ts.net";
    const shared = await fakeServer(root, {});
    shared.plugin.configureServer(shared.server);
    assert.match(shared.infos.join("\n"), /accepts requests from loopback and 100\.64\.0\.0\/10, laptop\.tailnet\.ts\.net$/m);
    delete process.env.RUNIC_TRANSLATIONS_EDIT_ALLOW;
    assert.equal(next.warnings.length, 1);
    process.env.RUNIC_DEV_ALLOW = "100.64.0.0/10";
    const sharedOnly = await fakeServer(root, {});
    sharedOnly.plugin.configureServer(sharedOnly.server);
    assert.match(sharedOnly.infos.join("\n"), /accepts requests from loopback and 100\.64\.0\.0\/10$/m);
    process.env.RUNIC_DEV_ALLOW = "100.64.0.0/40";
    const invalid = await fakeServer(root, {});
    assert.throws(() => invalid.plugin.configureServer(invalid.server), /RUNIC_DEV_ALLOW: '100\.64\.0\.0\/40' is not a network/);
    delete process.env.RUNIC_DEV_ALLOW;
    const open = await fakeServer(root, {}, { allowedHosts: true });
    open.plugin.configureServer(open.server);
    assert.match(open.infos.join("\n"), /server\.allowedHosts is true; in-page editing still answers only IP addresses/);
    assert.doesNotMatch(next.infos.join("\n"), /allowedHosts/);
    const off = await fakeServer(root, { inPageEditing: false });
    off.plugin.configureServer(off.server);
    assert.equal(off.used.length, 0, "inPageEditing: false mounts nothing");
    assert.equal(used.length, 0);
  } finally {
    if (previous === undefined) delete process.env.RUNIC_TRANSLATIONS_EDIT_ALLOW; else process.env.RUNIC_TRANSLATIONS_EDIT_ALLOW = previous;
    if (previousShared === undefined) delete process.env.RUNIC_DEV_ALLOW; else process.env.RUNIC_DEV_ALLOW = previousShared;
    await rm(root, { recursive: true, force: true });
  }
});

async function fakeServer(root, options, serverOptions = {}) {
  const project = join(root, "translations");
  await mkdir(project, { recursive: true });
  await writeFile(join(project, "runic.json"), JSON.stringify({ schemaVersion: 1, catalog: "app" }));
  const plugin = runicTranslations({ project, output: join(root, "generated"), persistentCompiler: false, ...options });
  const used = [], infos = [], warnings = [];
  const watcher = { add() {}, on() {}, off() {} };
  const server = { watcher, httpServer: { once() {} }, ws: { send() {} }, middlewares: { use: handler => used.push(handler) },
    config: { base: "/", server: serverOptions, logger: { info: line => infos.push(line), warn: line => warnings.push(line) } } };
  return { plugin, server, used, infos, warnings };
}
