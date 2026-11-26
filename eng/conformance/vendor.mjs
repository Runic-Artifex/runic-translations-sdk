#!/usr/bin/env bun
// The vendored conformance driver core (eng/conformance/README.md, "The vendored core"). runic-sdk owns
// eng/conformance/core; this repository keeps an unchanged copy at a pinned runic-sdk commit, stamped in
// core.vendor.json with each file's SHA-256 (R7 G11, T1-15). The same script is in runic-cli-sdk and
// runic-translations-sdk.
//
//   bun eng/conformance/vendor.mjs check              offline: the copy matches the stamp
//   bun eng/conformance/vendor.mjs upstream [<ref>]   the stamp and copy match runic-sdk at the pin (or at <ref>)
//   bun eng/conformance/vendor.mjs update <ref>       vendor the core again from runic-sdk at <ref> (a commit, a branch)
//
// upstream and update fetch runic-sdk over the network, or read a local clone with --sdk <dir>. Node runs it too.
import { createHash } from "node:crypto";
import { execFileSync } from "node:child_process";
import { existsSync, mkdirSync, mkdtempSync, readdirSync, readFileSync, rmSync, statSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { dirname, join, relative, resolve } from "node:path";
import { fileURLToPath } from "node:url";

export const FORMAT = "rax.conformance-core-vendor/1";
export const SOURCE = "https://github.com/Runic-Artifex/runic-sdk";
export const UPSTREAM_PATH = "eng/conformance/core";
const here = dirname(fileURLToPath(import.meta.url));

// Line endings do not count, so a checkout that converts them still matches; any other byte does.
export const fileHash = (bytes) => "sha256:" + createHash("sha256").update(Buffer.from(bytes).toString("latin1").replace(/\r\n/g, "\n"), "latin1").digest("hex");

function listFiles(dir, base = dir) {
  if (!existsSync(dir)) return [];
  return readdirSync(dir)
    .flatMap((name) => {
      const path = join(dir, name);
      return statSync(path).isDirectory() ? listFiles(path, base) : [relative(base, path).split("\\").join("/")];
    })
    .sort();
}

const readStamp = (dir) => JSON.parse(readFileSync(join(dir, "core.vendor.json"), "utf8"));
const update = (dir) => `update it from upstream instead: bun ${relative(process.cwd(), join(dir, "vendor.mjs")) || "eng/conformance/vendor.mjs"} update <runic-sdk commit> (eng/conformance/README.md)`;

// Offline: every file of core/ has the stamp's hash, and the stamp lists every file. Returns the problems.
export function check(dir = here) {
  const problems = [];
  const stamp = readStamp(dir);
  if (stamp.format !== FORMAT) problems.push(`core.vendor.json has format ${stamp.format}, not ${FORMAT}`);
  if (!/^[0-9a-f]{40}$/.test(stamp.commit ?? "")) problems.push(`core.vendor.json: commit ${stamp.commit} is not a full commit SHA`);
  const core = join(dir, "core");
  const files = listFiles(core);
  for (const file of files) {
    const expected = stamp.files?.[file];
    if (!expected) problems.push(`core/${file} is not in core.vendor.json: the core is runic-sdk's, so add files there`);
    else if (fileHash(readFileSync(join(core, file))) !== expected) problems.push(`core/${file} differs from runic-sdk ${stamp.commit.slice(0, 12)}: the vendored core is not edited here`);
  }
  for (const file of Object.keys(stamp.files ?? {})) if (!files.includes(file)) problems.push(`core/${file} is missing`);
  return problems;
}

// runic-sdk's core at a ref: { commit, files: { name: Buffer } }. `sdk` is a local clone that has the commit;
// without it, a shallow blobless fetch of SOURCE into a temporary repository.
export function fetchUpstream(ref, { sdk, source = process.env.RUNIC_SDK_URL ?? SOURCE } = {}) {
  const git = (repo, args, options = {}) => execFileSync("git", ["-C", repo, ...args], { stdio: ["ignore", "pipe", "pipe"], maxBuffer: 1 << 26, ...options });
  let repo = sdk && resolve(sdk);
  let temporary;
  try {
    let commit;
    if (repo) {
      try {
        commit = git(repo, ["rev-parse", "--verify", `${ref}^{commit}`]).toString().trim();
      } catch {
        throw new Error(`${repo} has no commit ${ref}: fetch it there first (git -C ${repo} fetch origin)`);
      }
    } else {
      temporary = mkdtempSync(join(tmpdir(), "rax-conformance-core-"));
      repo = temporary;
      git(repo, ["init", "-q"]);
      git(repo, ["remote", "add", "origin", source]);
      git(repo, ["fetch", "-q", "--depth", "1", "--filter=blob:none", "origin", ref]);
      commit = git(repo, ["rev-parse", "FETCH_HEAD^{commit}"]).toString().trim();
    }
    const names = git(repo, ["ls-tree", "-r", "--name-only", commit, "--", `${UPSTREAM_PATH}/`]).toString().split("\n").filter(Boolean);
    if (!names.length) throw new Error(`runic-sdk ${commit} has no ${UPSTREAM_PATH}`);
    const files = {};
    for (const name of names) files[name.slice(UPSTREAM_PATH.length + 1)] = git(repo, ["cat-file", "blob", `${commit}:${name}`]);
    return { commit, files };
  } finally {
    if (temporary) rmSync(temporary, { recursive: true, force: true });
  }
}

// The copy and its stamp against runic-sdk at `ref` (default: the pinned commit). Returns the problems.
export function compareUpstream(dir = here, ref, options) {
  const stamp = readStamp(dir);
  const upstream = fetchUpstream(ref ?? stamp.commit, options);
  const problems = [];
  if (upstream.commit !== stamp.commit) problems.push(`the pin is ${stamp.commit}, ${ref} is ${upstream.commit}`);
  const names = new Set([...Object.keys(upstream.files), ...Object.keys(stamp.files ?? {})]);
  for (const name of [...names].sort()) {
    const theirs = upstream.files[name] && fileHash(upstream.files[name]);
    if (!theirs) problems.push(`core/${name}: not in runic-sdk ${upstream.commit.slice(0, 12)}`);
    else if (stamp.files?.[name] !== theirs) problems.push(`core/${name}: the stamp differs from runic-sdk ${upstream.commit.slice(0, 12)}`);
  }
  return [...problems, ...check(dir)];
}

// Replaces core/ with runic-sdk's at `ref` and writes the stamp.
export function vendor(dir = here, ref, options) {
  const upstream = fetchUpstream(ref, options);
  const core = join(dir, "core");
  rmSync(core, { recursive: true, force: true });
  const files = {};
  for (const [name, bytes] of Object.entries(upstream.files).sort(([a], [b]) => (a < b ? -1 : a > b ? 1 : 0))) {
    mkdirSync(dirname(join(core, name)), { recursive: true });
    writeFileSync(join(core, name), bytes);
    files[name] = fileHash(bytes);
  }
  const stamp = { format: FORMAT, source: SOURCE, path: UPSTREAM_PATH, commit: upstream.commit, files };
  writeFileSync(join(dir, "core.vendor.json"), JSON.stringify(stamp, null, 2) + "\n");
  return upstream.commit;
}

function main(args) {
  const flag = (name) => {
    const index = args.indexOf(name);
    return index >= 0 ? args.splice(index, 2)[1] : undefined;
  };
  const dir = resolve(flag("--dir") ?? here);
  const sdk = flag("--sdk");
  const [command, ref] = args;
  const report = (problems, ok) => {
    for (const problem of problems) console.log(problem);
    console.log(problems.length ? update(dir) : ok);
    return problems.length ? 1 : 0;
  };
  switch (command) {
    case "check":
      return report(check(dir), `eng/conformance/core matches core.vendor.json (runic-sdk ${readStamp(dir).commit.slice(0, 12)})`);
    case "upstream":
      return report(compareUpstream(dir, ref, { sdk }), `eng/conformance/core matches runic-sdk ${ref ?? readStamp(dir).commit}`);
    case "update": {
      if (!ref) break;
      const commit = vendor(dir, ref, { sdk });
      console.log(`vendored ${UPSTREAM_PATH} from runic-sdk ${commit}; review the diff, then run the conformance tests`);
      return 0;
    }
  }
  console.log("usage: bun eng/conformance/vendor.mjs check | upstream [<ref>] | update <ref>   [--sdk <runic-sdk clone>] [--dir <eng/conformance>]");
  return 2;
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  try {
    process.exit(main(process.argv.slice(2)));
  } catch (error) {
    console.error(String(error?.stderr ?? "").trim() || (error instanceof Error ? error.message : String(error)));
    process.exit(1);
  }
}
