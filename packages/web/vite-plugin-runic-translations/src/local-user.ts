// Whether a loopback client is a program of the user who runs the development server. Loopback admits every account of
// the machine, and a program, unlike a browser, sends whatever `Origin` it likes, so the in-page editing API's writes,
// which change the project's translation sources, also ask who owns the client's socket. vite-plugin-runic has the same
// check for its open action.
//
// Linux reports the owner of every TCP socket of the network namespace in /proc/net/tcp and /proc/net/tcp6, readable by
// every user. The client's socket is the entry whose local endpoint is the connection's remote one and whose remote
// endpoint is the connection's local one. macOS and Windows have no such table for an unprivileged process, so there the
// check is not made and the development server does not tell local users apart (README, "Security").

import { readFile } from "node:fs/promises";
import { endianness } from "node:os";
import { isIP } from "node:net";

/** The endpoints of an accepted connection, as `net.Socket` has them. */
export interface PeerSocket {
  readonly remoteAddress?: string | undefined;
  readonly remotePort?: number | undefined;
  readonly localAddress?: string | undefined;
  readonly localPort?: number | undefined;
}

/** One socket of /proc/net/tcp or /proc/net/tcp6: addresses as 32 hex digits of their IPv6 (IPv4-mapped) form. */
export interface TcpSocketEntry {
  readonly localAddress: string;
  readonly localPort: number;
  readonly remoteAddress: string;
  readonly remotePort: number;
  readonly uid: number;
}

/** Decides whether a loopback client belongs to this server's user; see `createLocalUserCheck`. */
export type LocalUserCheck = (socket: PeerSocket | undefined) => Promise<boolean>;

/** Whether this platform can tell the owner of a loopback client (Linux). */
export function localUserCheckAvailable(platform: NodeJS.Platform = process.platform): boolean {
  return platform === "linux";
}

/**
 * Parses /proc/net/tcp or /proc/net/tcp6. The kernel prints each 32-bit word of an address in the machine's byte order,
 * so a little-endian machine prints 127.0.0.1 as `0100007F`.
 */
export function parseProcNetTcp(text: string, littleEndian: boolean = endianness() === "LE"): TcpSocketEntry[] {
  const entries: TcpSocketEntry[] = [];
  for (const line of text.split("\n").slice(1)) {
    const fields = line.trim().split(/\s+/u);
    if (fields.length < 8) continue;
    const local = procEndpoint(fields[1]!, littleEndian), remote = procEndpoint(fields[2]!, littleEndian);
    const uid = /^\d+$/u.test(fields[7]!) ? Number(fields[7]) : NaN;
    if (local === undefined || remote === undefined || !Number.isSafeInteger(uid)) continue;
    entries.push({ localAddress: local.address, localPort: local.port, remoteAddress: remote.address, remotePort: remote.port, uid });
  }
  return entries;
}

function procEndpoint(text: string, littleEndian: boolean): { address: string; port: number } | undefined {
  const match = /^([0-9A-Fa-f]{8}|[0-9A-Fa-f]{32}):([0-9A-Fa-f]{4})$/u.exec(text);
  if (match === null) return undefined;
  const hex = match[1]!.toLowerCase();
  let bytes = "";
  for (let word = 0; word < hex.length; word += 8) {
    const group = hex.slice(word, word + 8);
    bytes += littleEndian ? group.match(/../gu)!.reverse().join("") : group;
  }
  return { address: hex.length === 8 ? `00000000000000000000ffff${bytes}` : bytes, port: Number.parseInt(match[2]!, 16) };
}

/** An IP address as 32 hex digits of its IPv6 form (IPv4 as IPv4-mapped), or undefined when it is not one. */
export function addressKey(address: string | undefined): string | undefined {
  if (typeof address !== "string") return undefined;
  const text = address.replace(/%.*$/u, "");
  const version = isIP(text);
  if (version === 4) return `00000000000000000000ffff${ipv4Hex(text)}`;
  if (version !== 6) return undefined;
  let groupsText = text;
  const dotted = /(\d{1,3}(?:\.\d{1,3}){3})$/u.exec(groupsText);
  if (dotted !== null) {
    const hex = ipv4Hex(dotted[1]!);
    groupsText = `${groupsText.slice(0, dotted.index)}${hex.slice(0, 4)}:${hex.slice(4)}`;
  }
  const [head, tail] = groupsText.split("::") as [string, string | undefined];
  const left = head === "" ? [] : head.split(":");
  const right = tail === undefined || tail === "" ? [] : tail.split(":");
  const fill = tail === undefined ? [] : Array<string>(8 - left.length - right.length).fill("0");
  return [...left, ...fill, ...right].map((group) => group.padStart(4, "0")).join("").toLowerCase();
}

function ipv4Hex(address: string): string {
  return address.split(".").map((octet) => Number(octet).toString(16).padStart(2, "0")).join("");
}

/**
 * The owner check for loopback clients. On Linux it resolves true only when the client's socket is in the namespace's
 * TCP tables and belongs to `uid` (this process's user); a socket without endpoints or not found resolves false. Where
 * the tables cannot be read at all (another platform, or a /proc without them), it resolves true: the check is not
 * available there, which `localUserCheckAvailable` and the README say.
 */
export function createLocalUserCheck(options: {
  readonly platform?: NodeJS.Platform;
  readonly uid?: number;
  readonly read?: (path: string) => Promise<string>;
  readonly littleEndian?: boolean;
} = {}): LocalUserCheck {
  const platform = options.platform ?? process.platform;
  const uid = options.uid ?? process.getuid?.();
  const read = options.read ?? ((path: string) => readFile(path, "utf8"));
  return async (socket) => {
    if (!localUserCheckAvailable(platform) || uid === undefined) return true;
    const client = addressKey(socket?.remoteAddress), server = addressKey(socket?.localAddress);
    const clientPort = socket?.remotePort, serverPort = socket?.localPort;
    if (client === undefined || server === undefined || typeof clientPort !== "number" || typeof serverPort !== "number") return false;
    const tables = await Promise.all(["/proc/net/tcp", "/proc/net/tcp6"].map((path) => read(path).catch(() => undefined)));
    if (tables.every((table) => table === undefined)) return true;
    for (const table of tables) {
      if (table === undefined) continue;
      for (const entry of parseProcNetTcp(table, options.littleEndian)) {
        if (entry.localAddress === client && entry.localPort === clientPort && entry.remoteAddress === server && entry.remotePort === serverPort)
          return entry.uid === uid;
      }
    }
    return false;
  };
}

/** This process's check: the real tables and user. */
export const isLocalUser: LocalUserCheck = createLocalUserCheck();
