#!/usr/bin/env bun
// The train feed (local-planning COMPATIBILITY.md 3.4; runic-sdk eng/train/README.md): builds this repository against
// the Runic Command Line release of its train before that release is on nuget.org.
//
//   bun eng/train-feed.mjs prepare [--output <dir>] [--from <dir>] [--commit <runic-cli-sdk sha>]
//
// When RunicCommandLineVersion (eng/Versions.props) is on nuget.org, prepare does nothing. Otherwise it puts that
// version's Command Line packages into <output>/nuget (default artifacts/train-feed/nuget), from the first of:
//   --from <dir>     packages that are already packed, such as runic-cli-sdk's `eng/pack.sh <version> <dir>`;
//   a CI artifact    the command-line-packages artifact of a successful ci.yml push run on runic-cli-sdk main that
//                    has exactly that version;
//   the source       runic-cli-sdk at --commit, or at the newest successful ci.yml push run on main, packed with its
//                    own `eng/pack.sh <version>`.
// It writes <output>/train-feed.json, which names the source and the SHA-256 of each package. Builds then use the feed
// through RUNIC_TRAIN_FEED=<output>/nuget (Directory.Build.props, and trainFeedNugetConfig() for the consumers of
// eng/verify-packages.mjs); restores run unlocked, because the candidate's bytes are not the published package's.
//
// A CI run built through the train feed uploads the train-feed artifact, and Publish preview refuses such a run
// (eng/release/ci-artifact.mjs): publish only after Command Line is published and the lock files are refreshed.
import assert from "node:assert/strict";
import { execFileSync, spawnSync } from "node:child_process";
import { createHash } from "node:crypto";
import { appendFileSync, copyFileSync, existsSync, mkdirSync, readFileSync, readdirSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const root = resolve(fileURLToPath(new URL("..", import.meta.url)));
export const TRAIN_FEED_ARTIFACT = "train-feed";
export const COMMAND_LINE_REPOSITORY = "Runic-Artifex/runic-cli-sdk";
export const COMMAND_LINE_ARTIFACT = "command-line-packages";
export const COMMAND_LINE_PACKAGES = ["Runic.CommandLine", "Runic.CommandLine.Processes", "Runic.CommandLine.Spectre", "Runic.CommandLine.Testing"];
const SCHEMA = "runic.translations.train-feed/1";

export function commandLineVersion(versionsProps = readFileSync(join(root, "eng/Versions.props"), "utf8")) {
  const version = versionsProps.match(/<RunicCommandLineVersion>([^<]+)</)?.[1];
  assert.ok(version, "eng/Versions.props has no RunicCommandLineVersion");
  return version;
}

// The feed directory of the current build, or undefined without a train feed.
export function activeTrainFeed(environment = process.env) {
  const feed = environment.RUNIC_TRAIN_FEED;
  return feed ? resolve(feed) : undefined;
}

const quoted = value => value.replaceAll("&", "&amp;").replaceAll("\"", "&quot;").replaceAll("<", "&lt;");

// The <add> source and the <packageSource> mapping that a consumer NuGet.config with package source mapping needs for
// the train feed; empty strings without one. Runic.CommandLine* is more specific than nuget.org's "*", so the candidate
// can never be replaced by a nuget.org package.
export function trainFeedNugetConfig(environment = process.env) {
  const feed = activeTrainFeed(environment);
  if (!feed) return { source: "", mapping: "" };
  return {
    source: `<add key="train-command-line" value="${quoted(feed)}"/>`,
    mapping: `<packageSource key="train-command-line"><package pattern="Runic.CommandLine*"/></packageSource>`,
  };
}

const sha256 = path => createHash("sha256").update(readFileSync(path)).digest("hex");

export function feedPackages(directory, version) {
  const files = readdirSync(directory).filter(name => name.endsWith(".nupkg")).sort();
  const expected = COMMAND_LINE_PACKAGES.map(id => `${id}.${version}.nupkg`);
  const missing = expected.filter(file => !files.includes(file));
  if (missing.length) throw new Error(`${directory} lacks ${missing.join(", ")}; pack Runic Command Line ${version} into it first.`);
  return expected.map(file => ({ file, sha256: sha256(join(directory, file)) }));
}

async function published(version, fetchImpl = fetch) {
  const response = await fetchImpl("https://api.nuget.org/v3-flatcontainer/runic.commandline/index.json");
  if (response.status === 404) return false;
  if (!response.ok) throw new Error(`nuget.org answered ${response.status} for the Runic.CommandLine versions`);
  const { versions } = await response.json();
  return versions.includes(version.toLowerCase());
}

function github(token) {
  return async path => {
    const headers = { accept: "application/vnd.github+json", "x-github-api-version": "2022-11-28" };
    if (token) headers.authorization = `Bearer ${token}`;
    const response = await fetch(`https://api.github.com/repos/${COMMAND_LINE_REPOSITORY}/${path}`, { headers });
    if (!response.ok) throw new Error(`GitHub API ${COMMAND_LINE_REPOSITORY} ${path.split("?")[0]} failed: ${response.status}`);
    return response;
  };
}

// Successful ci.yml push runs on runic-cli-sdk main, newest first.
async function greenMainRuns(api) {
  const query = new URLSearchParams({ branch: "main", event: "push", status: "success", per_page: "20" });
  const { workflow_runs: runs } = await (await api(`actions/workflows/ci.yml/runs?${query}`)).json();
  return runs.filter(run => run.head_branch === "main" && run.conclusion === "success").sort((a, b) => b.id - a.id);
}

function extractZip(archive, directory) {
  execFileSync("python3", ["-I", "-c", "import sys,zipfile\nzipfile.ZipFile(sys.argv[1]).extractall(sys.argv[2])", archive, directory]);
}

// The command-line-packages artifact of the newest green main run whose packages have exactly this version.
async function fromCiArtifact(api, runs, version, feed, work) {
  for (const run of runs.slice(0, 5)) {
    const { artifacts } = await (await api(`actions/runs/${run.id}/artifacts?per_page=100`)).json();
    const artifact = artifacts.find(entry => entry.name === COMMAND_LINE_ARTIFACT && !entry.expired);
    if (!artifact) continue;
    const archive = join(work, `${artifact.id}.zip`);
    writeFileSync(archive, Buffer.from(await (await api(`actions/artifacts/${artifact.id}/zip`)).arrayBuffer()));
    const extracted = join(work, String(artifact.id));
    extractZip(archive, extracted);
    if (!existsSync(join(extracted, `Runic.CommandLine.${version}.nupkg`))) {
      console.log(`${run.html_url}: ${COMMAND_LINE_ARTIFACT} has no Runic.CommandLine ${version}`);
      continue;
    }
    for (const id of COMMAND_LINE_PACKAGES) copyFileSync(join(extracted, `${id}.${version}.nupkg`), join(feed, `${id}.${version}.nupkg`));
    return { kind: "ci-artifact", repository: COMMAND_LINE_REPOSITORY, runId: run.id, runUrl: run.html_url, commit: run.head_sha, artifactId: artifact.id };
  }
  return undefined;
}

// runic-cli-sdk at the commit, packed at this version with its own eng/pack.sh.
function fromSource(commit, version, feed, work) {
  const checkout = join(work, "runic-cli-sdk");
  const git = (...args) => execFileSync("git", args, { stdio: "inherit" });
  git("init", "--quiet", checkout);
  git("-C", checkout, "fetch", "--quiet", "--depth", "1", `https://github.com/${COMMAND_LINE_REPOSITORY}.git`, commit);
  git("-C", checkout, "checkout", "--quiet", "FETCH_HEAD");
  const packed = join(work, "packed");
  const result = spawnSync("bash", [join(checkout, "eng/pack.sh"), version, packed], { cwd: checkout, stdio: "inherit" });
  if (result.status !== 0) throw new Error(`runic-cli-sdk eng/pack.sh ${version} failed at ${commit}`);
  for (const id of COMMAND_LINE_PACKAGES) copyFileSync(join(packed, `${id}.${version}.nupkg`), join(feed, `${id}.${version}.nupkg`));
  return { kind: "source", repository: COMMAND_LINE_REPOSITORY, commit };
}

function option(args, name) {
  const index = args.indexOf(name);
  return index < 0 ? undefined : args[index + 1];
}

function setOutput(values) {
  if (!process.env.GITHUB_OUTPUT) return;
  appendFileSync(process.env.GITHUB_OUTPUT, Object.entries(values).map(([key, value]) => `${key}=${value}\n`).join(""));
}

export async function prepare(args) {
  const version = commandLineVersion();
  const output = resolve(option(args, "--output") ?? join(root, "artifacts/train-feed"));
  const feed = join(output, "nuget");
  if (await published(version)) {
    console.log(`Runic.CommandLine ${version} is on nuget.org; no train feed.`);
    setOutput({ active: "false" });
    return undefined;
  }
  rmSync(output, { recursive: true, force: true });
  mkdirSync(feed, { recursive: true });
  const work = join(tmpdir(), `runic-train-feed-${process.pid}`);
  rmSync(work, { recursive: true, force: true });
  mkdirSync(work, { recursive: true });
  let source;
  try {
    const from = option(args, "--from");
    if (from) {
      for (const { file } of feedPackages(resolve(from), version)) copyFileSync(join(resolve(from), file), join(feed, file));
      source = { kind: "local", directory: resolve(from) };
    } else {
      const api = github(process.env.GH_TOKEN ?? process.env.GITHUB_TOKEN);
      const commit = option(args, "--commit");
      const runs = commit ? [] : await greenMainRuns(api);
      try {
        source = commit ? undefined : await fromCiArtifact(api, runs, version, feed, work);
      } catch (error) {
        // Reading another repository's artifacts may need a token with actions: read there (D061.12); the source works without.
        console.log(`::warning title=Train feed::${error.message}; packing from source instead.`);
      }
      if (!source) {
        const target = commit ?? runs[0]?.head_sha;
        assert.ok(target, `No successful ci.yml push run on ${COMMAND_LINE_REPOSITORY} main; pass --commit or --from.`);
        console.log(`No ${COMMAND_LINE_ARTIFACT} artifact has Runic.CommandLine ${version}; packing ${COMMAND_LINE_REPOSITORY} at ${target}.`);
        source = fromSource(target, version, feed, work);
      }
    }
  } finally {
    rmSync(work, { recursive: true, force: true });
  }
  const marker = {
    schema: SCHEMA,
    publishable: false,
    reason: `Runic.CommandLine ${version} is not on nuget.org; this build used a train-feed candidate.`,
    commandLine: { version, source, packages: feedPackages(feed, version) },
  };
  writeFileSync(join(output, "train-feed.json"), `${JSON.stringify(marker, null, 2)}\n`);
  console.log(`Train feed for Runic.CommandLine ${version} (${source.kind}) in ${feed}. Builds use it with RUNIC_TRAIN_FEED=${feed}.`);
  setOutput({ active: "true", feed });
  return marker;
}

if (import.meta.main) {
  const [command, ...args] = process.argv.slice(2);
  if (command !== "prepare") {
    console.error("Usage: bun eng/train-feed.mjs prepare [--output <dir>] [--from <dir>] [--commit <runic-cli-sdk sha>]");
    process.exit(2);
  }
  await prepare(args);
}
