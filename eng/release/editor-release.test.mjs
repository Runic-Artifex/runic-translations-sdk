import { expect, test } from "bun:test";
import { mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join, resolve } from "node:path";
import {
  checkTag, checksumLine, createManifest, editorVersion, manifestProblems, MANIFEST, MANIFEST_SCHEMA, parseVersionText, releaseNotes,
} from "./editor-release.mjs";
import { editorNotesDirectory } from "./notes.mjs";

const root = resolve(import.meta.dirname, "../..");
const commit = "1d3032ee95b45b5a3e4415729be136f39ca5d695";

function scratch(body) {
  const directory = mkdtempSync(join(tmpdir(), "editor-release-"));
  try { return body(directory); } finally { rmSync(directory, { recursive: true, force: true }); }
}

test("an editor release tag names the editor's own version", () => {
  expect(() => checkTag("editor-v0.1.0", "0.1.0")).not.toThrow();
  expect(() => checkTag("editor-v0.1.1", "0.1.0")).toThrow(/does not name/);
  expect(() => checkTag("v0.1.0", "0.1.0")).toThrow();
  expect(() => checkTag(`editor-v${editorVersion()}`)).not.toThrow();
});

test("the editor's --version gives its version, channel, commit and runtime", () => {
  expect(parseVersionText(`Runic Translations Editor 0.1.0+${commit}\nChannel: preview\nCommit: ${commit}\nRuntime: linux-x64\n`))
    .toEqual({ version: "0.1.0", channel: "preview", commit, runtimeIdentifier: "linux-x64" });
  expect(parseVersionText("Runic Translations Editor 0.1.0\r\nChannel: preview\r\nCommit: development\r\nRuntime: win-x64\r\n").runtimeIdentifier).toBe("win-x64");
  expect(() => parseVersionText("Runic Translations 0.15.0\n")).toThrow();
});

test("package-manifest.json lists every file, and verification finds changed, missing and extra files", () => scratch(directory => {
  mkdirSync(join(directory, "www"));
  writeFileSync(join(directory, "Runic.Translations.Editor"), "binary");
  writeFileSync(join(directory, "www", "index.html"), "<!doctype html>");
  const manifest = createManifest(directory, { version: "0.1.0", channel: "preview", runtimeIdentifier: "linux-x64", commit, tree: commit });
  expect(manifest.schema).toBe(MANIFEST_SCHEMA);
  expect(manifest.files.map(file => file.path)).toEqual(["Runic.Translations.Editor", "www/index.html"]);
  expect(manifest.files[0]).toEqual({ path: "Runic.Translations.Editor", size: 6, sha256: "9a3a45d01531a20e89ac6ae10b0b0beb0492acd7216a368aa062d1a5fecaf9cd" });
  writeFileSync(join(directory, MANIFEST), JSON.stringify(manifest));
  expect(manifestProblems(directory)).toEqual([]);
  writeFileSync(join(directory, "www", "index.html"), "changed");
  writeFileSync(join(directory, "extra.txt"), "");
  rmSync(join(directory, "Runic.Translations.Editor"));
  expect(manifestProblems(directory).sort()).toEqual([
    "Runic.Translations.Editor is missing.", `extra.txt is not in ${MANIFEST}.`, `www/index.html differs from ${MANIFEST}.`]);
}));

test("a checksum file holds the digest and the file name, as sha256sum writes it", () => scratch(directory => {
  const file = join(directory, "runic-translations-editor-0.1.0-win-x64.zip");
  writeFileSync(file, "binary");
  expect(checksumLine(file)).toBe("9a3a45d01531a20e89ac6ae10b0b0beb0492acd7216a368aa062d1a5fecaf9cd  runic-translations-editor-0.1.0-win-x64.zip\n");
}));

test("a release takes only assembled notes; a dry run previews the fragments", () => {
  const version = editorVersion();
  // The repository's own notes: assembled once a release is prepared, fragments before.
  expect(releaseNotes(version, { directory: editorNotesDirectory }).startsWith(`# Runic Translations Editor ${version}\n`)).toBe(true);
  // Fragments only, as between releases: a dry run previews them, a release refuses them.
  scratch(directory => {
    mkdirSync(join(directory, version, "features"), { recursive: true });
    writeFileSync(join(directory, version, "_intro.md"), `# Runic Translations Editor ${version}\n\nIntro.\n`);
    writeFileSync(join(directory, version, "features", "one.md"), "## One\n\nA feature.\n");
    expect(releaseNotes(version, { directory }).startsWith(`# Runic Translations Editor ${version}\n`)).toBe(true);
    expect(() => releaseNotes(version, { release: true, directory })).toThrow(/unassembled/);
  });
  scratch(directory => {
    writeFileSync(join(directory, `${version}.md`), "# Notes\n");
    expect(releaseNotes(version, { release: true, directory })).toBe("# Notes\n");
    expect(() => releaseNotes("9.9.9", { directory })).toThrow(/no notes/);
  });
});

// D078.2: an editor-v tag runs only the editor's release workflow, and the SDK's workflows never run for it.
test("editor release tags and SDK tags start different workflows", () => {
  const workflow = name => readFileSync(join(root, ".github/workflows", name), "utf8");
  const editor = workflow("release-editor.yml");
  expect(editor).toMatch(/\n {2}push:\n {4}tags: \["editor-v\*"\]\n/);
  expect(editor).not.toMatch(/\n {2}push:\n {4}branches/);
  // CI's push runs are main's commits only, so no tag push, editor-v or SDK, starts it.
  const ci = workflow("ci.yml");
  expect(ci).toMatch(/\n {2}push:\n(?: {4}#.*\n)* {4}branches: \[main\]\n {2}\S/);
  expect(ci).not.toMatch(/\n {4}tags:/);
  // Publish preview, the SDK's release, runs only when dispatched.
  expect(workflow("publish-preview.yml")).toMatch(/\non:\n {2}workflow_dispatch:\n/);
  expect(workflow("publish-preview.yml")).not.toMatch(/\n {2}push:/);
  // Only the release job writes.
  expect(editor.match(/contents: write/g)).toHaveLength(1);
});
