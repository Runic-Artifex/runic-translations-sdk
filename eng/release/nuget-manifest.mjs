// nuget.org's version rules for a pushed .nupkg or .snupkg (NuGetGallery ManifestValidator), checked before a push:
// nuget.org answers 400 "The package manifest contains an invalid Version" otherwise, after earlier packages of the
// same release may already be published. 0.9.0 stopped that way: the Build package stamped the upper bound
// 0.10.0-0 into Runic.Translations.CommandLine.
//
// The package version and each dependency's lower and upper bound must be SemVer 2.0 (a dotted prerelease label or,
// for the package version only, build metadata) or match the NuGet 2.x pattern, whose prerelease label starts with a
// letter. A dependency bound must not carry build metadata, and a floating range is rejected.
import { readFileSync, readdirSync } from "node:fs";
import { join } from "node:path";
import { inflateRawSync } from "node:zlib";

// NuGetGallery NuGetVersionExtensions.IsValidVersionForLegacyClients (case-insensitive).
const legacyVersion = /^\d+(\s*\.\s*\d+){0,3}(-[a-z][0-9a-z-]*)?$/i;
// NuGet.Versioning's parse: 1 to 4 numeric parts, an optional prerelease and optional metadata.
const nugetVersion = /^(?<numbers>\d+(?:\.\d+){0,3})(?:-(?<release>[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?(?:\+(?<metadata>[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?$/;

// Why nuget.org rejects this version, or undefined when it accepts it.
export function nugetVersionProblem(value, { dependency = false } = {}) {
  const version = value.trim();
  const match = nugetVersion.exec(version);
  if (!match) return `'${value}' is not a NuGet version`;
  const { release, metadata } = match.groups;
  if (dependency && metadata !== undefined) return `'${value}' has build metadata, which a dependency version must not carry`;
  // NuGetVersion.IsSemVer2: more than one prerelease label, or build metadata.
  const semVer2 = (release?.includes(".") ?? false) || metadata !== undefined;
  if (semVer2 || legacyVersion.test(version)) return undefined;
  return `'${value}' is neither SemVer 2.0 (a dotted prerelease label) nor a NuGet 2.x version, whose prerelease label must start with a letter`;
}

// The lower and upper bound of a nuspec version range ("1.0", "[1.0]", "[1.0, 2.0)", "(, 2.0]"); undefined bounds are open.
export function rangeBounds(value) {
  const range = value.trim();
  if (!/^[[(]/.test(range)) return { floating: range.includes("*"), bounds: [range] };
  const inner = range.slice(1, -1);
  const parts = inner.split(",").map(part => part.trim());
  if (!/[\])]$/.test(range) || parts.length > 2) return { invalid: true, bounds: [] };
  const bounds = parts.filter(part => part !== "");
  // nuget.org checks the upper bound only when it differs from the lower one.
  return { floating: range.includes("*"), bounds: [...new Set(bounds)] };
}

const attribute = (element, name) => new RegExp(`\\s${name}\\s*=\\s*(["'])(.*?)\\1`, "s").exec(element)?.[2];

// Every reason nuget.org would reject this nuspec's package version or dependency versions.
export function nuspecVersionProblems(nuspec) {
  const problems = [];
  const metadata = /<metadata(?:\s[^>]*)?>([\s\S]*?)<\/metadata>/.exec(nuspec)?.[1] ?? "";
  // The package's own <version>, outside <dependencies> (which has none of that element).
  const version = /<version>\s*([^<]*?)\s*<\/version>/.exec(metadata.replace(/<dependencies[\s\S]*?<\/dependencies>/, ""))?.[1];
  if (version === undefined) problems.push("the package has no <version>");
  else {
    const problem = nugetVersionProblem(version);
    if (problem) problems.push(`package version ${problem}`);
  }
  for (const [element] of metadata.matchAll(/<dependency\b[^>]*>/g)) {
    const id = attribute(element, "id") ?? "(no id)";
    const range = attribute(element, "version");
    if (range === undefined) continue;
    const { floating, invalid, bounds } = rangeBounds(range);
    if (invalid || floating) {
      problems.push(`dependency ${id} has the ${invalid ? "invalid" : "floating"} range '${range}'`);
      continue;
    }
    for (const bound of bounds) {
      const problem = nugetVersionProblem(bound, { dependency: true });
      if (problem) problems.push(`dependency ${id} version range '${range}': ${problem}`);
    }
  }
  return problems;
}

// Dependencies released in lockstep with this repository (COMPATIBILITY.md R2/R3, D061.13, D061.16): the Translations
// runtime and Hosting, and Runic Command Line. Each must be [F, U) with U the next minor before 1.0 (the next major from
// 1.0) in the -0.0 form, so one range form covers every cross-repository dependency and the next line's previews.
const lockstepDependency = /^(Runic\.Translations|Runic\.Translations\.Hosting|Runic\.CommandLine(\..+)?)$/i;

// The upper bound of the version rule for a floor: 0.12.0 gives 0.13.0-0.0, 1.2.0 gives 2.0.0-0.0.
export function lockstepUpperBound(floor) {
  const match = /^(\d+)\.(\d+)\.\d+(?:-[0-9A-Za-z.-]+)?$/.exec(floor);
  if (!match) return undefined;
  const [major, minor] = [Number(match[1]), Number(match[2])];
  return major === 0 ? `0.${minor + 1}.0-0.0` : `${major + 1}.0.0-0.0`;
}

// Each lockstep dependency whose range is not [F, next minor-0.0) before 1.0 or [F, next major-0.0) from 1.0.
export function lockstepRangeProblems(nuspec) {
  const metadata = /<metadata(?:\s[^>]*)?>([\s\S]*?)<\/metadata>/.exec(nuspec)?.[1] ?? "";
  const problems = [];
  for (const [element] of metadata.matchAll(/<dependency\b[^>]*>/g)) {
    const id = attribute(element, "id");
    const range = attribute(element, "version")?.trim();
    if (!id || !lockstepDependency.test(id) || range === undefined) continue;
    const match = /^\[\s*([^,\s]+)\s*,\s*([^\s)]+)\s*\)$/.exec(range);
    const expected = match && lockstepUpperBound(match[1]);
    if (!match || !expected || match[2] !== expected)
      problems.push(`dependency ${id} has the range '${range}'; a lockstep dependency must be [F, ${expected ?? "next minor-0.0"}) (one bound form, D061.13)`);
  }
  return problems;
}

// Reads the root .nuspec of a .nupkg or .snupkg (a zip archive) through its central directory.
export function packageNuspec(path) {
  const zip = readFileSync(path);
  let end = zip.length - 22;
  while (end >= 0 && zip.readUInt32LE(end) !== 0x06054b50) end--;
  if (end < 0) throw new Error(`${path} is not a zip archive`);
  let offset = zip.readUInt32LE(end + 16);
  for (let index = zip.readUInt16LE(end + 10); index > 0; index--) {
    const method = zip.readUInt16LE(offset + 10), size = zip.readUInt32LE(offset + 20);
    const nameLength = zip.readUInt16LE(offset + 28), extraLength = zip.readUInt16LE(offset + 30), commentLength = zip.readUInt16LE(offset + 32);
    const local = zip.readUInt32LE(offset + 42);
    const name = zip.toString("utf8", offset + 46, offset + 46 + nameLength);
    if (!name.includes("/") && name.endsWith(".nuspec")) {
      const start = local + 30 + zip.readUInt16LE(local + 26) + zip.readUInt16LE(local + 28);
      const data = zip.subarray(start, start + size);
      return (method === 8 ? inflateRawSync(data) : data).toString("utf8");
    }
    offset += 46 + nameLength + extraLength + commentLength;
  }
  throw new Error(`${path} has no .nuspec`);
}

// Fails, naming each package and problem, unless nuget.org would accept the version and dependency versions of every
// given .nupkg and .snupkg. A directory checks every package in it.
export function checkNugetManifests(paths) {
  const files = [paths].flat().flatMap(path => /\.s?nupkg$/i.test(path) ? [path]
    : readdirSync(path).filter(name => /\.s?nupkg$/i.test(name)).sort().map(name => join(path, name)));
  if (!files.length) throw new Error(`No .nupkg or .snupkg to check in ${[paths].flat().join(", ")}`);
  const problems = files.flatMap(file => nuspecVersionProblems(packageNuspec(file)).map(problem => `- ${file}: ${problem}`));
  if (problems.length) throw new Error([
    "nuget.org would reject these packages (\"The package manifest contains an invalid Version\"):",
    ...problems,
    "A version or dependency bound must be SemVer 2.0 (for example 0.10.0-0.0) or have a prerelease label that starts with a letter (0.10.0-a); a dependency bound must not carry build metadata.",
  ].join("\n"));
  const ranges = files.flatMap(file => lockstepRangeProblems(packageNuspec(file)).map(problem => `- ${file}: ${problem}`));
  if (ranges.length) throw new Error(["These packages break the lockstep range rule:", ...ranges].join("\n"));
  return files;
}
