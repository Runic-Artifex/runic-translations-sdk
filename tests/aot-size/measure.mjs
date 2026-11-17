// The Translations NativeAOT size budgets (tests/aot-size/README.md): publishes the probe (Probe/AotSizeProbe.csproj) for
// linux-x64 with each catalog that tests/aot-size/budgets.json names, reads each mstat with MstatReader, runs the
// executable, and checks the budgets.
//   bun tests/aot-size/measure.mjs [--update] [--keep]
// --update rewrites each budget's measured value and limit from this run. --keep keeps the published probes and their
// records under artifacts/aot-size/ (by default only results.json stays). The report goes to standard output and, in
// GitHub Actions, to the job summary.
import { spawnSync } from "node:child_process";
import { appendFileSync, copyFileSync, mkdirSync, readFileSync, rmSync, statSync, writeFileSync } from "node:fs";
import { resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { allLocales, writeFixture } from "./fixture.mjs";

const root = resolve(fileURLToPath(import.meta.url), "../../..");
const budgetsFile = resolve(root, "tests/aot-size/budgets.json");
const probe = resolve(root, "tests/aot-size/Probe/AotSizeProbe.csproj");
const reader = resolve(root, "tests/aot-size/MstatReader/MstatReader.csproj");
const output = resolve(root, "artifacts/aot-size");
const rid = "linux-x64";
const mstat = resolve(root, `tests/aot-size/Probe/obj/Release/net10.0/${rid}/native/AotSizeProbe.mstat`);

// The measurements of one build from its executable size and its mstat records (MstatReader: kind, size, owner, name).
// The probe's own records (owner AotSizeProbe) are its generated catalog: the locale data and the message table
// (RvaFields), and the code.
export function measurement(executableBytes, records) {
  const result = { executableBytes, reflectionRuntimeBytes: 0, catalogDataBytes: 0, catalogCodeBytes: 0, catalogBytes: 0, overlayProviderBytes: 0, largeStringBytes: 0 };
  for (const line of records.split("\n")) {
    const [kind, size, owner, name = ""] = line.split("\t");
    const bytes = Number(size);
    // DeduplicatedMethods records carry a node id, not a size.
    if (kind === "DeduplicatedMethods" || !Number.isFinite(bytes) || bytes < 0) continue;
    // System.Reflection.Runtime is the reflection stack in System.Private.CoreLib and System.Private.Reflection.Execution
    // that Delegate.Method, MethodInfo and Type members need.
    if (name.startsWith("System.Reflection.Runtime.")) result.reflectionRuntimeBytes += bytes;
    // The overlay composition, which only an application that creates an overlay needs.
    if (name.includes("Runic.Translations.OverlayTranslationProvider")) result.overlayProviderBytes += bytes;
    // Frozen strings of 4 KB or more: a catalog's markup contract as a UTF-16 literal would be one.
    if (kind === "FrozenObjects" && name === "System.String" && bytes >= 4096) result.largeStringBytes += bytes;
    if (owner === "AotSizeProbe") {
      result.catalogBytes += bytes;
      if (kind === "RvaFields") result.catalogDataBytes += bytes;
      else if (kind === "Methods") result.catalogCodeBytes += bytes;
    }
  }
  return result;
}

// The value of each metric; `builds` maps a build name of budgets.json to its measurement.
export const metrics = {
  // The executable without the catalog's own records: the runtime, the framework and what they need.
  "aot/translations/fixed-bytes": (entry, builds) => [builds[entry.build].executableBytes - builds[entry.build].catalogBytes],
  // The catalog's records (locale data and generated code) at two message counts in two locales.
  "aot/translations/per-message-bytes": (entry, builds) => entry.builds.map(name => builds[name].catalogBytes),
  // The catalog's RvaFields at two locale counts with the same messages: the locale data, and the message table, which
  // does not grow with the locales.
  "aot/translations/per-locale-message-bytes": (entry, builds) => entry.builds.map(name => builds[name].catalogDataBytes),
  "aot/translations/reflection-runtime-bytes": (entry, builds) => [builds[entry.build].reflectionRuntimeBytes],
  "aot/translations/overlay-provider-bytes": (entry, builds) => [builds[entry.build].overlayProviderBytes],
  "aot/translations/large-string-bytes": (entry, builds) => [builds[entry.build].largeStringBytes],
};

// Checks one metric. A "total" has one value and its limit. An "O(n)" has a value at each of its two sizes: the larger
// may be at most the smaller times the size ratio, plus margin, slack and noise, and at most the limit.
export function check(name, entry, values, updating) {
  const { class: growth, sizes = [], limit, margin = 0, slack = 0, noise = 0 } = entry;
  const large = values.at(-1);
  let failure = null;
  if (growth === "O(n)") {
    const upper = (values[0] + noise) * (sizes[1] / sizes[0]) * (1 + margin) + slack + noise;
    if (large > upper) failure = `${name} grows faster than O(n): ${values[0]} at ${sizes[0]}, ${large} at ${sizes[1]} (at most ${Math.round(upper)}).`;
  } else if (growth !== "total") failure = `${name}: unknown class '${growth}' in budgets.json`;
  if (!failure && !updating) {
    if (limit === undefined) failure = `${name} has no limit in budgets.json; run bun tests/aot-size/measure.mjs --update.`;
    else if (large > limit) failure = `${name} is ${large}, over its limit ${limit}.`;
  }
  return { values, limit: limit ?? null, status: failure ? "failed" : updating ? "recorded" : "ok", failure };
}

// The limit --update records: the measured value plus the entry's margin, and its noise.
function updatedLimit(entry, values) {
  const large = values.at(-1);
  return Math.ceil(large * (1 + (entry.margin ?? 0)) + (entry.noise ?? 0));
}

function run(command, args, options = {}) {
  const result = spawnSync(command, args, { cwd: root, stdio: ["ignore", "inherit", "inherit"], ...options });
  if (result.error) throw result.error;
  if (result.status !== 0) throw new Error(`${command} ${args.join(" ")} exited with ${result.status}.`);
  return result;
}

// Publishes the probe with one build's catalog, runs it and measures it.
function publish(name, { messages, locales }, keep) {
  const localeList = allLocales.slice(0, locales);
  const catalog = resolve(output, "catalog");
  writeFixture(catalog, messages, localeList);
  const directory = resolve(output, name);
  rmSync(directory, { recursive: true, force: true });
  rmSync(mstat, { force: true });
  console.log(`Publishing the Translations NativeAOT probe ${name} (${messages} messages × ${localeList.join(", ")})…`);
  const started = Date.now();
  run("dotnet", ["publish", probe, "-c", "Release", "-r", rid, "--self-contained", "true", "-p:PublishAot=true",
    "-p:IlcTreatWarningsAsErrors=true",
    // As a CI (Verification) build: a catalog diagnostic such as RTR0095 fails the publish locally too.
    "-p:TreatWarningsAsErrors=true", `-p:AotSizeCatalogDirectory=${catalog}`, "-o", resolve(directory, "bin"), "--nologo", "-v:q"]);
  const seconds = Math.round((Date.now() - started) / 1000);
  // Every locale formats every message: the probe must work, not only link.
  for (const locale of localeList) {
    const result = spawnSync(resolve(directory, "bin/AotSizeProbe"), [], { encoding: "utf8", env: { ...process.env, PROBE_LOCALE: locale } });
    if (result.status !== 0 || result.stdout.split("\n").length !== messages + 1)
      throw new Error(`The probe ${name} failed in ${locale} (${result.status}): ${result.stderr}${result.stdout.slice(0, 2000)}`);
  }
  const records = spawnSync("dotnet", [resolve(root, "tests/aot-size/MstatReader/bin/Release/net10.0/MstatReader.dll"), mstat],
    { cwd: root, encoding: "utf8", maxBuffer: 1 << 28 });
  if (records.status !== 0) throw new Error(`MstatReader failed on ${mstat}: ${records.stderr}`);
  const build = { ...measurement(statSync(resolve(directory, "bin/AotSizeProbe")).size, records.stdout), publishSeconds: seconds };
  if (keep) {
    copyFileSync(mstat, resolve(directory, "AotSizeProbe.mstat"));
    writeFileSync(resolve(directory, "records.tsv"), records.stdout);
  } else rmSync(directory, { recursive: true, force: true });
  console.log(`  ${build.executableBytes} bytes; catalog ${build.catalogBytes} mstat bytes (data ${build.catalogDataBytes}, code ${build.catalogCodeBytes}); System.Reflection.Runtime ${build.reflectionRuntimeBytes} (${seconds} s)`);
  return build;
}

function table(results) {
  const lines = ["## Translations NativeAOT size budgets", "", "| Budget | Value | Limit | Status |", "| --- | ---: | ---: | --- |"];
  for (const [name, result] of Object.entries(results))
    lines.push(`| \`${name}\` | ${result.values.map(value => value.toLocaleString("en-US")).join(" → ")} | ${result.limit?.toLocaleString("en-US") ?? "–"} | ${result.status} |`);
  return `${lines.join("\n")}\n`;
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  const args = process.argv.slice(2);
  const updating = args.includes("--update");
  const keep = args.includes("--keep");
  const budgets = JSON.parse(readFileSync(budgetsFile, "utf8"));
  for (const name of Object.keys(budgets.cases)) if (!metrics[name]) throw new Error(`budgets.json has ${name}, which measure.mjs does not measure.`);
  for (const name of Object.keys(metrics)) if (!budgets.cases[name]) throw new Error(`${name} has no entry in budgets.json.`);

  mkdirSync(output, { recursive: true });
  run("dotnet", ["build", reader, "-c", "Release", "--nologo", "-v:q"]);
  const builds = Object.fromEntries(Object.entries(budgets.builds).map(([name, build]) => [name, publish(name, build, keep)]));
  rmSync(resolve(output, "catalog"), { recursive: true, force: true });

  const results = {};
  const failures = [];
  for (const [name, entry] of Object.entries(budgets.cases)) {
    const values = metrics[name](entry, builds);
    results[name] = check(name, entry, values, updating);
    if (results[name].failure) failures.push(results[name].failure);
    if (updating) {
      entry.measured = values;
      entry.limit = updatedLimit(entry, values);
    }
  }
  writeFileSync(resolve(output, "results.json"), `${JSON.stringify({ schema: 1, rid, cases: results, builds }, null, 2)}\n`);
  if (updating && failures.length === 0) {
    writeFileSync(budgetsFile, `${JSON.stringify(budgets, null, 2)}\n`);
    console.log(`Updated ${budgetsFile}.`);
  }
  const summary = table(results);
  console.log(summary);
  if (process.env.GITHUB_STEP_SUMMARY) appendFileSync(process.env.GITHUB_STEP_SUMMARY, summary);
  for (const failure of failures) console.error(failure);
  process.exit(failures.length ? 1 : 0);
}
