// The vendored conformance driver core (eng/conformance/README.md): the copy in this repository matches its stamp, and
// the stamp check, the upstream comparison and the update work, against a local stand-in for runic-sdk. No network.
import assert from "node:assert/strict";
import { execFileSync } from "node:child_process";
import { appendFileSync, cpSync, mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { dirname, join } from "node:path";
import test from "node:test";
import { fileURLToPath } from "node:url";
import { check, compareUpstream, fileHash, vendor } from "./vendor.mjs";

const here = dirname(fileURLToPath(import.meta.url));

function scratch(body) {
  const dir = mkdtempSync(join(tmpdir(), "rax-conformance-vendor-"));
  try {
    return body(dir);
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
}

// A copy of eng/conformance's core and stamp, which a test may edit.
function copy(dir) {
  const target = join(dir, "conformance");
  cpSync(join(here, "core"), join(target, "core"), { recursive: true });
  cpSync(join(here, "core.vendor.json"), join(target, "core.vendor.json"));
  return target;
}

// A git repository with runic-sdk's layout: each commit holds the given eng/conformance/core files.
function fakeSdk(dir, ...versions) {
  const repo = join(dir, "sdk");
  const git = (...args) => execFileSync("git", ["-C", repo, "-c", "user.name=t", "-c", "user.email=t@t", "-c", "commit.gpgsign=false", ...args]).toString().trim();
  mkdirSync(repo);
  git("init", "-q");
  const commits = [];
  for (const files of versions) {
    rmSync(join(repo, "eng"), { recursive: true, force: true });
    for (const [name, text] of Object.entries(files)) {
      mkdirSync(dirname(join(repo, "eng/conformance/core", name)), { recursive: true });
      writeFileSync(join(repo, "eng/conformance/core", name), text);
    }
    git("add", "-A");
    git("commit", "-q", "-m", `core ${commits.length}`);
    commits.push(git("rev-parse", "HEAD"));
  }
  return { repo, commits };
}

test("the vendored core matches its stamp", () => {
  assert.deepEqual(check(here), []);
  const stamp = JSON.parse(readFileSync(join(here, "core.vendor.json"), "utf8"));
  assert.match(stamp.commit, /^[0-9a-f]{40}$/);
  assert.ok(stamp.files["driver.ts"] && stamp.files["index.ts"], "the stamp lists the driver and its export manifest");
});

test("a one-byte edit, an added file and a removed file fail the check", () =>
  scratch((dir) => {
    const target = copy(dir);
    assert.deepEqual(check(target), []);
    appendFileSync(join(target, "core/results.ts"), " ");
    writeFileSync(join(target, "core/local.ts"), "export {};\n");
    rmSync(join(target, "core/names.ts"));
    assert.deepEqual(check(target).map((p) => p.split(":")[0]), [
      "core/local.ts is not in core.vendor.json",
      "core/results.ts differs from runic-sdk " + JSON.parse(readFileSync(join(target, "core.vendor.json"), "utf8")).commit.slice(0, 12),
      "core/names.ts is missing",
    ]);
  }));

test("line endings do not count, other bytes do", () => {
  assert.equal(fileHash(Buffer.from("a\r\nb\n")), fileHash(Buffer.from("a\nb\n")));
  assert.notEqual(fileHash(Buffer.from("a\nb\n")), fileHash(Buffer.from("a\nc\n")));
});

test("update vendors a commit, and the upstream comparison finds a stamp edited by hand", () =>
  scratch((dir) => {
    const { repo, commits } = fakeSdk(dir, { "a.ts": "export const a = 1;\n", "sub/b.ts": "b\n" }, { "a.ts": "export const a = 2;\n" });
    const target = join(dir, "conformance");
    mkdirSync(target);
    assert.equal(vendor(target, commits[0], { sdk: repo }), commits[0]);
    assert.deepEqual(check(target), []);
    assert.deepEqual(compareUpstream(target, undefined, { sdk: repo }), []);
    assert.equal(readFileSync(join(target, "core/sub/b.ts"), "utf8"), "b\n");

    // A newer commit differs; updating to it removes the file it dropped.
    assert.ok(compareUpstream(target, commits[1], { sdk: repo }).some((p) => p.includes("the pin is")));
    vendor(target, "HEAD", { sdk: repo });
    assert.deepEqual(compareUpstream(target, undefined, { sdk: repo }), []);
    assert.deepEqual(Object.keys(JSON.parse(readFileSync(join(target, "core.vendor.json"), "utf8")).files), ["a.ts"]);

    // An edit with a matching stamp passes offline, but not against upstream.
    writeFileSync(join(target, "core/a.ts"), "export const a = 3;\n");
    const stamp = JSON.parse(readFileSync(join(target, "core.vendor.json"), "utf8"));
    stamp.files["a.ts"] = fileHash(readFileSync(join(target, "core/a.ts")));
    writeFileSync(join(target, "core.vendor.json"), JSON.stringify(stamp));
    assert.deepEqual(check(target), []);
    assert.deepEqual(compareUpstream(target, undefined, { sdk: repo }), [`core/a.ts: the stamp differs from runic-sdk ${commits[1].slice(0, 12)}`]);
  }));
