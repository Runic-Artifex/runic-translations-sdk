#!/usr/bin/env bun
// The Translations Editor's releases (D078.2-D078.5, .github/workflows/release-editor.yml). The editor has its own
// version (apps/translations-editor/Directory.Build.props), tags editor-v<version> and notes in
// eng/release/notes/editor/. Every command runs on Linux, macOS and Windows.
//
//   bun eng/release/editor-release.mjs version                       print the editor's version
//   bun eng/release/editor-release.mjs check-tag <tag>               the tag must be editor-v<the editor's version>
//   bun eng/release/editor-release.mjs notes <output> [--release]    write the release's notes to <output>: for a
//                                                                    release only the assembled notes; otherwise the
//                                                                    fragments assembled for a preview
//   bun eng/release/editor-release.mjs manifest <directory> <rid>    write <directory>/package-manifest.json
//   bun eng/release/editor-release.mjs verify-manifest <directory>   check every file against it, and that no file is missing or extra
//   bun eng/release/editor-release.mjs checksum <file>...            write <file>.sha256 ("<sha256>  <name>")
//   bun eng/release/editor-release.mjs verify-checksum <file>...     check <file> against <file>.sha256
import { execFileSync } from "node:child_process";
import { createHash } from "node:crypto";
import { existsSync, readFileSync, readdirSync, statSync, writeFileSync } from "node:fs";
import { basename, dirname, join, relative, resolve, sep } from "node:path";
import { fileURLToPath } from "node:url";
import { assemble, check, editorNotesDirectory, repositoryPaths } from "./notes.mjs";

const root = resolve(dirname(fileURLToPath(import.meta.url)), "../..");
export const MANIFEST = "package-manifest.json";
export const MANIFEST_SCHEMA = "runic.translations.editor-package/1";
export const REPOSITORY = "Runic-Artifex/runic-translations-sdk";

// apps/translations-editor/Directory.Build.props, RunicTranslationsEditorVersion: plain x.y.z (D026, D078.3).
export function editorVersion(repository = root) {
  const props = readFileSync(join(repository, "apps/translations-editor/Directory.Build.props"), "utf8");
  const version = /<RunicTranslationsEditorVersion>([^<]+)<\/RunicTranslationsEditorVersion>/.exec(props)?.[1];
  if (!version || !/^\d+\.\d+\.\d+$/.test(version)) throw new Error(`The editor's version must be a plain x.y.z, not ${version ?? "(none)"}.`);
  return version;
}

export function checkTag(tag, version = editorVersion()) {
  if (tag !== `editor-v${version}`)
    throw new Error(`The tag ${tag} does not name the editor's version: tag editor-v${version}, or raise RunicTranslationsEditorVersion first.`);
}

// The notes of the editor's release: eng/release/notes/editor/<version>.md, which the release pull request assembles
// from the fragments in eng/release/notes/editor/<version>/ (bun eng/release/notes.mjs --editor assemble <version>).
// A release stops while fragments are unassembled; a dry run checks them and assembles a preview.
export function releaseNotes(version, { release = false, directory = editorNotesDirectory, exists = repositoryPaths() } = {}) {
  const fragments = join(directory, version);
  const assembled = join(directory, `${version}.md`);
  if (existsSync(fragments)) {
    if (release)
      throw new Error(`eng/release/notes/editor/${version}/ holds unassembled fragments; run bun eng/release/notes.mjs --editor assemble ${version} in the release pull request.`);
    const problems = check(fragments, { exists });
    if (problems.length) throw new Error(`The editor's release-note fragments have problems:\n${problems.join("\n")}`);
    return assemble(fragments);
  }
  if (!existsSync(assembled)) throw new Error(`The editor has no notes for ${version}: write eng/release/notes/editor/${version}/.`);
  return readFileSync(assembled, "utf8");
}

export const sha256 = path => createHash("sha256").update(readFileSync(path)).digest("hex");

// Every file under a directory, as sorted "/"-separated relative paths. A symbolic link is refused: the archives
// hold plain files only.
export function listFiles(directory) {
  const files = [];
  const walk = current => {
    for (const entry of readdirSync(current, { withFileTypes: true })) {
      const path = join(current, entry.name);
      if (entry.isSymbolicLink()) throw new Error(`${relative(directory, path)} is a symbolic link.`);
      if (entry.isDirectory()) walk(path);
      else files.push(relative(directory, path).split(sep).join("/"));
    }
  };
  walk(directory);
  return files.sort();
}

// The identity the editor reports itself: the first line names its version (with the source revision as build
// metadata), then Channel:, Commit: and Runtime:.
export function parseVersionText(text) {
  const lines = text.split(/\r?\n/);
  const head = /^Runic Translations Editor (\d+\.\d+\.\d+)(?:\+([0-9a-f]{40}))?$/.exec(lines[0] ?? "");
  const field = name => lines.find(line => line.startsWith(`${name}: `))?.slice(name.length + 2);
  if (!head) throw new Error(`Not the editor's version output: ${lines[0]}`);
  return { version: head[1], channel: field("Channel"), commit: field("Commit"), runtimeIdentifier: field("Runtime") };
}

export function createManifest(directory, { version, channel, runtimeIdentifier, commit, tree }) {
  const files = listFiles(directory).filter(file => file !== MANIFEST).map(file => {
    const path = join(directory, ...file.split("/"));
    return { path: file, size: statSync(path).size, sha256: sha256(path) };
  });
  return {
    schema: MANIFEST_SCHEMA, product: "Runic Translations Editor", version, channel, runtimeIdentifier,
    repository: REPOSITORY, repositoryCommit: commit, repositoryTree: tree, files,
  };
}

export function manifestProblems(directory) {
  const manifest = JSON.parse(readFileSync(join(directory, MANIFEST), "utf8"));
  if (manifest.schema !== MANIFEST_SCHEMA) return [`${MANIFEST} has the schema ${manifest.schema}, not ${MANIFEST_SCHEMA}.`];
  const problems = [];
  const listed = new Map(manifest.files.map(file => [file.path, file]));
  for (const file of listFiles(directory).filter(file => file !== MANIFEST)) {
    const entry = listed.get(file);
    if (!entry) { problems.push(`${file} is not in ${MANIFEST}.`); continue; }
    listed.delete(file);
    const path = join(directory, ...file.split("/"));
    if (statSync(path).size !== entry.size || sha256(path) !== entry.sha256) problems.push(`${file} differs from ${MANIFEST}.`);
  }
  for (const file of listed.keys()) problems.push(`${file} is missing.`);
  return problems;
}

export const checksumLine = path => `${sha256(path)}  ${basename(path)}\n`;

function editorExecutable(directory) {
  const windows = join(directory, "Runic.Translations.Editor.exe");
  return existsSync(windows) ? windows : join(directory, "Runic.Translations.Editor");
}

function git(...args) {
  return execFileSync("git", args, { cwd: root, encoding: "utf8" }).trim();
}

function main([command, ...rest]) {
  switch (command) {
    case "version":
      console.log(editorVersion());
      return 0;
    case "check-tag":
      checkTag(rest[0]);
      console.log(`${rest[0]} names the editor's version.`);
      return 0;
    case "notes": {
      const release = rest.includes("--release");
      const [output] = rest.filter(arg => arg !== "--release");
      if (!output) throw new Error("Name the output file.");
      writeFileSync(output, releaseNotes(editorVersion(), { release }));
      console.log(`Wrote the editor ${editorVersion()} notes to ${output}.`);
      return 0;
    }
    case "manifest": {
      const [directory, runtimeIdentifier] = rest;
      // The executable's own --version: the manifest records what the build is, and it must be this source's editor.
      const reported = parseVersionText(execFileSync(editorExecutable(directory), ["--version"], { encoding: "utf8" }));
      const commit = git("rev-parse", "HEAD");
      if (reported.version !== editorVersion()) throw new Error(`The build reports ${reported.version}, not the editor's version ${editorVersion()}.`);
      if (reported.commit !== commit) throw new Error(`The build reports the commit ${reported.commit}, not ${commit}.`);
      if (reported.runtimeIdentifier !== runtimeIdentifier) throw new Error(`The build runs as ${reported.runtimeIdentifier}, not ${runtimeIdentifier}.`);
      const manifest = createManifest(directory, { ...reported, runtimeIdentifier, commit, tree: git("rev-parse", "HEAD^{tree}") });
      writeFileSync(join(directory, MANIFEST), `${JSON.stringify(manifest, null, 2)}\n`);
      console.log(`Wrote ${join(directory, MANIFEST)} with ${manifest.files.length} files.`);
      return 0;
    }
    case "verify-manifest": {
      const problems = manifestProblems(rest[0]);
      for (const problem of problems) console.error(problem);
      if (!problems.length) console.log(`Every file of ${rest[0]} matches ${MANIFEST}.`);
      return problems.length ? 1 : 0;
    }
    case "checksum":
      for (const file of rest) writeFileSync(`${file}.sha256`, checksumLine(file));
      return 0;
    case "verify-checksum": {
      const wrong = rest.filter(file => readFileSync(`${file}.sha256`, "utf8") !== checksumLine(file));
      for (const file of wrong) console.error(`${file} does not match ${basename(file)}.sha256.`);
      return wrong.length ? 1 : 0;
    }
    default:
      console.error("Use editor-release.mjs version | check-tag <tag> | notes <output> [--release] | manifest <directory> <rid> | verify-manifest <directory> | checksum <file>... | verify-checksum <file>...");
      return 2;
  }
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  try { process.exitCode = main(process.argv.slice(2)); }
  catch (error) { console.error(error.message); process.exitCode = 1; }
}

