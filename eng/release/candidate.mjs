import assert from "node:assert/strict";
import { existsSync, mkdirSync, mkdtempSync, readFileSync, readdirSync, renameSync, rmSync, writeFileSync } from "node:fs";
import { basename, dirname, join } from "node:path";
import { gunzipSync, gzipSync } from "node:zlib";

// A candidate is built in a sibling directory on the same filesystem and
// renamed over the target only after every package exists. An interrupted run
// leaves the previous target untouched; the next run removes its leftovers.
const stagingPrefix = name => `.${name}-staging-`;
const previousPrefix = name => `.${name}-previous-`;

export function recoverStaging(target) {
  const parent = dirname(target), name = basename(target);
  if (!existsSync(parent)) return;
  for (const entry of readdirSync(parent)) {
    const path = join(parent, entry);
    if (entry.startsWith(stagingPrefix(name))) rmSync(path, { recursive: true, force: true });
    // Only possible when a promotion stopped between its two renames.
    else if (entry.startsWith(previousPrefix(name))) {
      if (existsSync(target)) rmSync(path, { recursive: true, force: true });
      else renameSync(path, target);
    }
  }
}

export function createStaging(target) {
  recoverStaging(target);
  mkdirSync(dirname(target), { recursive: true });
  return mkdtempSync(join(dirname(target), stagingPrefix(basename(target))));
}

export function promoteStaging(staging, target) {
  const previous = join(dirname(target), `${previousPrefix(basename(target))}${process.pid}`);
  const replacing = existsSync(target);
  if (replacing) renameSync(target, previous);
  try {
    renameSync(staging, target);
  } catch (error) {
    if (replacing) renameSync(previous, target);
    throw error;
  }
  if (replacing) rmSync(previous, { recursive: true, force: true });
}

// Runs build(staging) and promotes its output, or removes it on failure.
export function stageAndPromote(target, build) {
  const staging = createStaging(target);
  try {
    build(staging);
    promoteStaging(staging, target);
  } finally {
    rmSync(staging, { recursive: true, force: true });
  }
}

const text = (header, start, length) => {
  const bytes = header.subarray(start, start + length);
  const end = bytes.indexOf(0);
  return bytes.subarray(0, end < 0 ? length : end).toString("utf8");
};

// Merges fields into package/package.json of a gzipped ustar archive and copies
// every other entry byte-for-byte, so packing never writes the source manifest.
export function stampNpmManifest(archive, fields) {
  const tar = gunzipSync(readFileSync(archive));
  const output = [];
  let offset = 0, stamped = 0, extended = false;
  while (offset + 512 <= tar.length) {
    const header = tar.subarray(offset, offset + 512);
    if (header.every(byte => byte === 0)) break;
    const prefix = text(header, 345, 155), name = text(header, 0, 100);
    const path = prefix ? `${prefix}/${name}` : name;
    const size = parseInt(text(header, 124, 12).trim() || "0", 8);
    const type = String.fromCharCode(header[156] || 48);
    const end = offset + 512 + Math.ceil(size / 512) * 512;
    if (type === "0" && path === "package/package.json") {
      assert.ok(!extended, "package.json must not use an extended tar header");
      const manifest = JSON.parse(tar.subarray(offset + 512, offset + 512 + size).toString("utf8"));
      const bytes = Buffer.from(`${JSON.stringify({ ...manifest, ...fields }, null, 2)}\n`);
      const updated = Buffer.from(header);
      updated.write(`${bytes.length.toString(8).padStart(11, "0")}\0`, 124, 12, "latin1");
      updated.fill(0x20, 148, 156);
      const checksum = updated.reduce((sum, byte) => sum + byte, 0);
      updated.write(`${checksum.toString(8).padStart(6, "0")}\0 `, 148, 8, "latin1");
      output.push(updated, bytes, Buffer.alloc((512 - bytes.length % 512) % 512));
      stamped++;
    } else {
      output.push(tar.subarray(offset, end));
    }
    extended = type === "x";
    offset = end;
  }
  assert.equal(stamped, 1, `${archive} must contain exactly one package/package.json`);
  output.push(Buffer.alloc(1024));
  writeFileSync(archive, gzipSync(Buffer.concat(output), { level: 9 }));
}
