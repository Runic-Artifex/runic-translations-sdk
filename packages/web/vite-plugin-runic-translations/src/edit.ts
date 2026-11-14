import { lookup as dnsLookup } from "node:dns/promises";
import type { IncomingMessage, ServerResponse } from "node:http";
import { BlockList, isIP } from "node:net";
import type { CompilerDiagnostic } from "./compiler-service.js";

/** The URL path of the in-page editing API, below Vite's `base`. */
export const editRoute = "__runic-translations/message";
/** Hosts and networks, besides loopback, that may use the in-page editing API: comma- or space-separated. */
export const allowEnvironmentVariable = "RUNIC_TRANSLATIONS_EDIT_ALLOW";
/** The same list, shared with vite-plugin-runic's dev endpoints (Runic Inspect), so one setting covers both plugins. */
export const sharedAllowEnvironmentVariable = "RUNIC_DEV_ALLOW";
/** The largest request body the API reads (the compiler's request limit is 1 MiB). */
const maximumBody = 512 * 1024;
/** How long a resolved host name of the allow list is trusted. */
const hostTtl = 30_000;

/** In-page translation editing in the dev server; see the plugin README, "In-page editing API". */
export interface InPageEditingOptions {
  /**
   * Hosts and networks besides loopback whose requests the API accepts: IPv4 or IPv6 addresses (`192.168.1.20`,
   * `fd7a:115c:a1e0::1`), CIDR networks (`100.64.0.0/10`, `fd7a:115c:a1e0::/48`) and host names (`laptop.tailnet.ts.net`),
   * resolved when a request arrives. The `RUNIC_DEV_ALLOW` and `RUNIC_TRANSLATIONS_EDIT_ALLOW` environment variables add to them.
   */
  readonly allow?: readonly string[];
}

/** A message as `GET` returns it; see the serve protocol schema (`$defs/message`). */
export type EditableMessage = Readonly<Record<string, unknown>>;

/** The serve protocol's reply to `message` and `set-message`. */
export interface EditReply {
  readonly ok: boolean;
  readonly exitCode: number;
  readonly message?: string;
  readonly diagnostics?: readonly CompilerDiagnostic[];
  readonly result?: { readonly message?: EditableMessage; readonly written?: readonly string[]; readonly error?: string };
}

/** What the API needs from the plugin. */
export interface EditBackend {
  /** Why editing is not available (manifest mode), or undefined. */
  unavailable(): string | undefined;
  /** The catalogs this dev server serves: the application's, then the regenerated library catalogs. */
  catalogs(): { readonly application: string | undefined; readonly libraries: readonly string[] };
  /** Sends a `message` or `set-message` request to `runic-translations serve`; rejects when the compiler cannot serve it. */
  request(method: "message" | "set-message", body: Readonly<Record<string, unknown>>): Promise<EditReply>;
}

/** One entry of the allow list, validated. */
export type AccessRule =
  | { readonly kind: "address"; readonly text: string; readonly address: string; readonly family: "ipv4" | "ipv6" }
  | { readonly kind: "network"; readonly text: string; readonly address: string; readonly prefix: number; readonly family: "ipv4" | "ipv6" }
  | { readonly kind: "host"; readonly text: string; readonly name: string };

const hostName = /^(?=.{1,253}$)(?:[A-Za-z0-9](?:[A-Za-z0-9-]{0,61}[A-Za-z0-9])?)(?:\.[A-Za-z0-9](?:[A-Za-z0-9-]{0,61}[A-Za-z0-9])?)*\.?$/;

/** Validates allow-list entries; throws a TypeError naming the entry and `source` for an invalid one. */
export function parseAllowList(entries: readonly unknown[], source: string): AccessRule[] {
  return entries.map(entry => {
    if (typeof entry !== "string" || entry.trim().length === 0) throw new TypeError(`${source}: every entry must be a non-empty string.`);
    const text = entry.trim();
    const slash = text.indexOf("/");
    if (slash >= 0) {
      const address = unbracket(text.slice(0, slash)), prefixText = text.slice(slash + 1);
      const version = isIP(address);
      const prefix = /^\d{1,3}$/.test(prefixText) ? Number(prefixText) : NaN;
      if (version === 0 || !Number.isInteger(prefix) || prefix > (version === 4 ? 32 : 128))
        throw new TypeError(`${source}: '${text}' is not a network; use an IPv4 or IPv6 CIDR such as 100.64.0.0/10 or fd7a:115c:a1e0::/48.`);
      return { kind: "network", text, address, prefix, family: version === 4 ? "ipv4" : "ipv6" };
    }
    const address = unbracket(text);
    const version = isIP(address);
    if (version !== 0) return { kind: "address", text, address, family: version === 4 ? "ipv4" : "ipv6" };
    if (!hostName.test(text) || /^[\d.]+$/.test(text))
      throw new TypeError(`${source}: '${text}' is not an IP address, a CIDR network or a host name.`);
    return { kind: "host", text, name: text.toLowerCase().replace(/\.$/, "") };
  });
}

/** The rules of both environment variables (`RUNIC_DEV_ALLOW`, then `RUNIC_TRANSLATIONS_EDIT_ALLOW`), each validated under its name. */
export function allowRulesFromEnvironment(environment: Readonly<Record<string, string | undefined>> = process.env): AccessRule[] {
  return [sharedAllowEnvironmentVariable, allowEnvironmentVariable]
    .flatMap(name => parseAllowList(allowListFromEnvironment(environment[name]), name));
}

/** The entries of the environment variable: separated by commas or whitespace. */
export function allowListFromEnvironment(value: string | undefined): string[] {
  return (value ?? "").split(/[\s,]+/).filter(entry => entry.length > 0);
}

function unbracket(value: string): string {
  return value.startsWith("[") && value.endsWith("]") ? value.slice(1, -1) : value;
}

/** An IPv4-mapped IPv6 address (`::ffff:192.0.2.1`) as its IPv4 address; any other address unchanged, without a zone. */
export function normalizeAddress(address: string): string {
  const unzoned = address.replace(/%.*$/, "");
  const mapped = /^::ffff:(\d{1,3}(?:\.\d{1,3}){3})$/i.exec(unzoned);
  return mapped ? mapped[1]! : unzoned;
}

/** Whether a client address is loopback: 127.0.0.0/8 or ::1 (also IPv4-mapped). */
export function isLoopback(address: string): boolean {
  const normalized = normalizeAddress(address);
  if (isIP(normalized) === 4) return normalized.startsWith("127.");
  return isIP(normalized) === 6 && loopback6.check(normalized, "ipv6");
}

const loopback6 = new BlockList();
loopback6.addAddress("::1", "ipv6");

/** Decides whether a client address may use the API: loopback, or one of the rules. */
export class AccessList {
  readonly rules: readonly AccessRule[];
  readonly #addresses = new BlockList();
  readonly #hosts: readonly string[];
  readonly #resolved = new Map<string, { readonly at: number; readonly addresses: Promise<readonly string[]> }>();
  readonly #lookup: (name: string) => Promise<readonly string[]>;

  constructor(rules: readonly AccessRule[], lookup?: (name: string) => Promise<readonly string[]>) {
    this.rules = rules;
    for (const rule of rules) {
      if (rule.kind === "address") this.#addresses.addAddress(rule.address, rule.family);
      else if (rule.kind === "network") this.#addresses.addSubnet(rule.address, rule.prefix, rule.family);
    }
    this.#hosts = rules.flatMap(rule => rule.kind === "host" ? [rule.name] : []);
    this.#lookup = lookup ?? (async name => (await dnsLookup(name, { all: true, verbatim: true })).map(item => item.address));
  }

  async allows(address: string | undefined): Promise<boolean> {
    if (!address) return false;
    const normalized = normalizeAddress(address);
    const version = isIP(normalized);
    if (version === 0) return false;
    if (isLoopback(normalized)) return true;
    if (this.#addresses.check(normalized, version === 4 ? "ipv4" : "ipv6")) return true;
    for (const name of this.#hosts) {
      let entry = this.#resolved.get(name);
      if (!entry || Date.now() - entry.at > hostTtl) {
        entry = { at: Date.now(), addresses: this.#lookup(name).catch(() => []) };
        this.#resolved.set(name, entry);
      }
      if ((await entry.addresses).some(candidate => normalizeAddress(candidate) === normalized)) return true;
    }
    return false;
  }
}

/** A refused or failed request: an HTTP status and the JSON body. */
class HttpError extends Error {
  constructor(readonly status: number, readonly error: string, message: string, readonly extra: Readonly<Record<string, unknown>> = {}) {
    super(message);
  }
}

/** The connect middleware of the API, mounted only by the dev server (`configureServer`). */
export function createEditMiddleware(backend: EditBackend, access: AccessList, base: string, logger?: { warn(message: string): void }) {
  const prefix = base.endsWith("/") ? base : `${base}/`;
  const routes = new Set([`/${editRoute}`, `${prefix}${editRoute}`]);
  return (request: IncomingMessage, response: ServerResponse, next: (error?: unknown) => void): void => {
    const url = new URL(request.url ?? "/", "http://dev.invalid");
    if (!routes.has(url.pathname) && !url.pathname.startsWith("/__runic-translations/") && !url.pathname.startsWith(`${prefix}__runic-translations/`)) return next();
    void handle(request, response, url).catch(error => {
      const failure = error instanceof HttpError ? error : new HttpError(500, "internal", (error as Error)?.message ?? String(error));
      if (failure.status === 403) logger?.warn(`[runic-translations] in-page editing refused a request from ${request.socket.remoteAddress ?? "an unknown address"}: ${failure.message}`);
      send(response, failure.status, { error: failure.error, detail: failure.message, ...failure.extra });
    });
  };

  async function handle(request: IncomingMessage, response: ServerResponse, url: URL): Promise<void> {
    // Who may ask: the client address (never a forwarded header), then the browser's own origin signals.
    if (!(await access.allows(request.socket.remoteAddress)))
      throw new HttpError(403, "forbidden", `In-page translation editing accepts requests from loopback only, unless the client's host or network is allowed: ` +
        `add it to runicTranslations({ inPageEditing: { allow: [...] } }), ${sharedAllowEnvironmentVariable} or ${allowEnvironmentVariable}.`);
    sameOrigin(request);
    if (!routes.has(url.pathname)) throw new HttpError(404, "not-found", `The in-page editing API has no ${url.pathname}.`);
    const reason = backend.unavailable();
    if (reason) throw new HttpError(501, "unavailable", reason);
    if (request.method === "GET" || request.method === "HEAD") {
      const key = url.searchParams.get("key") ?? "";
      const catalog = catalogOf(url.searchParams.get("catalog"));
      checkKey(key);
      const reply = await call("message", { ...catalog, key });
      send(response, 200, { message: reply.result?.message });
      return;
    }
    if (request.method !== "POST") {
      response.setHeader("Allow", "GET, HEAD, POST");
      throw new HttpError(405, "method-not-allowed", "Use GET to read a message and POST to write one.");
    }
    const type = (request.headers["content-type"] ?? "").split(";")[0]!.trim().toLowerCase();
    if (type !== "application/json") throw new HttpError(415, "unsupported-media-type", "Send the edit as application/json.");
    const body = await readJson(request);
    const { key, locale, text, expectedHash } = body as Record<string, unknown>;
    if (typeof key !== "string") throw new HttpError(400, "bad-request", "key must be a string.");
    checkKey(key);
    if (typeof locale !== "string" || locale.length === 0 || locale.length > 64) throw new HttpError(400, "bad-request", "locale must be a locale tag.");
    if (typeof text !== "string") throw new HttpError(400, "bad-request", "text must be a string: the locale's whole message text.");
    if (expectedHash !== null && (typeof expectedHash !== "string" || !/^(?:sha256:[a-f0-9]{64}|none)$/.test(expectedHash)))
      throw new HttpError(400, "bad-request", "expectedHash must be the file hash the edit was made against (sha256:<hex>, or none or null for a file that does not exist).");
    const catalog = catalogOf((body as { catalog?: unknown }).catalog ?? null);
    const reply = await call("set-message", { ...catalog, key, locale, text, expectedHash });
    send(response, 200, { written: reply.result?.written ?? [], message: reply.result?.message });
  }

  // The catalog the request names: the application's (no catalog member for the compiler) or a regenerated library catalog.
  function catalogOf(value: unknown): { catalog?: string } {
    const { application, libraries } = backend.catalogs();
    if (value === null || value === undefined || value === "" || value === application) return {};
    if (typeof value !== "string") throw new HttpError(400, "bad-request", "catalog must be a catalog ID.");
    if (!libraries.includes(value))
      throw new HttpError(404, "not-found", `'${value}' is not a catalog of this dev server. Its catalogs: ${[application, ...libraries].filter(Boolean).join(", ")}` +
        (libraries.length === 0 ? ` (library catalogs need dependencies: "auto").` : "."));
    return { catalog: value };
  }

  async function call(method: "message" | "set-message", body: Readonly<Record<string, unknown>>): Promise<EditReply> {
    let reply: EditReply;
    try {
      reply = await backend.request(method, body);
    } catch (error) {
      throw new HttpError(503, "unavailable", `The persistent compiler (runic-translations serve) cannot answer: ${(error as Error).message}`);
    }
    if (reply.ok) return reply;
    const error = reply.result?.error ?? (reply.exitCode === 2 ? "bad-request" : "invalid");
    const status = ({ "bad-request": 400, "not-found": 404, invalid: 422, conflict: 409, unsupported: 422 } as Record<string, number>)[error] ?? 422;
    const detail = reply.diagnostics?.find(diagnostic => !diagnostic.code.startsWith("RTR"))?.message ?? reply.message ?? "The request failed.";
    throw new HttpError(status, error, detail, {
      diagnostics: (reply.diagnostics ?? []).filter(diagnostic => diagnostic.code.startsWith("RTR")),
      ...(reply.result?.message ? { message: reply.result.message } : {}),
    });
  }
}

function checkKey(key: string): void {
  if (key.length === 0 || key.length > 512 || !/^[A-Za-z_][A-Za-z0-9_.]*$/.test(key))
    throw new HttpError(400, "bad-request", "key must be a message key, such as title, shop_cart or shop.cart.");
}

// A browser sends Origin on every POST and on cross-origin reads, and Sec-Fetch-Site on every request; both must say that
// the page came from this dev server. Vite's host check (server.allowedHosts) ran before this middleware.
function sameOrigin(request: IncomingMessage): void {
  const site = request.headers["sec-fetch-site"];
  if (site !== undefined && site !== "same-origin" && site !== "none")
    throw new HttpError(403, "forbidden", `In-page translation editing accepts same-origin requests only (Sec-Fetch-Site: ${String(site)}).`);
  const origin = request.headers.origin;
  const host = request.headers.host;
  if (origin === undefined) {
    if (request.method === "POST") throw new HttpError(403, "forbidden", "A write needs the Origin header of the dev server's page.");
    return;
  }
  let originHost: string;
  try { originHost = new URL(origin).host; } catch { originHost = ""; }
  if (!host || originHost.toLowerCase() !== host.toLowerCase())
    throw new HttpError(403, "forbidden", `In-page translation editing accepts same-origin requests only (Origin ${origin}, Host ${host ?? "missing"}).`);
}

async function readJson(request: IncomingMessage): Promise<unknown> {
  const chunks: Buffer[] = [];
  let length = 0;
  for await (const chunk of request as AsyncIterable<Buffer>) {
    length += chunk.byteLength;
    if (length > maximumBody) throw new HttpError(413, "too-large", `The request body exceeds ${maximumBody} bytes.`);
    chunks.push(chunk);
  }
  try {
    const value = JSON.parse(Buffer.concat(chunks).toString("utf8")) as unknown;
    if (!value || typeof value !== "object" || Array.isArray(value)) throw new Error("not an object");
    return value;
  } catch {
    throw new HttpError(400, "bad-request", "The request body must be a JSON object.");
  }
}

function send(response: ServerResponse, status: number, body: unknown): void {
  if (response.headersSent) { response.end(); return; }
  response.statusCode = status;
  response.setHeader("Content-Type", "application/json; charset=utf-8");
  response.setHeader("Cache-Control", "no-store");
  response.setHeader("X-Content-Type-Options", "nosniff");
  response.end(JSON.stringify(body));
}
