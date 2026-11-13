import { expect, test } from "bun:test";
import { execFileSync } from "node:child_process";
import { mkdtempSync, readFileSync, rmSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { checkNugetManifests, lockstepRangeProblems, lockstepUpperBound, nugetVersionProblem, nuspecVersionProblems, rangeBounds } from "./nuget-manifest.mjs";
import { publishCandidates } from "./publish.mjs";

const nuspec = (version, dependencies = []) => `<?xml version="1.0" encoding="utf-8"?>
<package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd"><metadata><id>Acme.Grid</id><version>${version}</version>
<dependencies><group targetFramework="net10.0">${dependencies.map(([id, range]) => `<dependency id="${id}" version="${range}" exclude="Build,Analyzers" />`).join("")}</group></dependencies>
</metadata></package>`;

// Writes a package with a deflated root .nuspec, as dotnet pack does.
function zip(path, text) {
  execFileSync("python3", ["-I", "-c",
    "import sys, zipfile\nwith zipfile.ZipFile(sys.argv[1], 'w', zipfile.ZIP_DEFLATED) as z:\n    z.writestr('[Content_Types].xml', '<Types/>')\n    z.writestr('Acme.Grid.nuspec', sys.argv[2])\n    z.writestr('lib/net10.0/Acme.Grid.dll', 'x')",
    path, text]);
}

test("versions follow nuget.org's rule: SemVer 2.0 or a NuGet 2.x prerelease label that starts with a letter", () => {
  for (const accepted of ["0.9.1", "1.0", "1.2.3.4", "0.10.0-0.0", "0.10.0-preview.1", "1.0.0-rc.1", "0.10.0-a", "0.10.0-alpha", "0.10.0-beta-2", "1.0.0-0.1", "1.0.0-1.a"])
    expect([accepted, nugetVersionProblem(accepted)]).toEqual([accepted, undefined]);
  // 0.9.0 failed with exactly this bound: a numeric label is not SemVer 2.0 and does not start with a letter.
  for (const rejected of ["0.10.0-0", "2.0.0-0", "0.10.0-1", "1.0.0-2306071004-3a8d691", "1.0.0-0a", "x.y", "1.0.0.0.0", ""])
    expect(nugetVersionProblem(rejected)).toBeString();
  // A package version may carry build metadata (it is then SemVer 2.0); a dependency version may not.
  expect(nugetVersionProblem("1.0.0-0+build")).toBeUndefined();
  expect(nugetVersionProblem("1.0.0+build", { dependency: true })).toContain("build metadata");
  expect(nugetVersionProblem("1.0.0-0+build", { dependency: true })).toContain("build metadata");
});

test("both bounds of a dependency range are checked, an exact or open bound once", () => {
  expect(rangeBounds("[0.9.1, 0.10.0-0.0)").bounds).toEqual(["0.9.1", "0.10.0-0.0"]);
  expect(rangeBounds("0.9.1").bounds).toEqual(["0.9.1"]);
  expect(rangeBounds("[0.9.1]").bounds).toEqual(["0.9.1"]);
  expect(rangeBounds("[0.9.1, 0.9.1]").bounds).toEqual(["0.9.1"]);
  expect(rangeBounds("(, 2.0.0-0)").bounds).toEqual(["2.0.0-0"]);
  expect(rangeBounds("0.9.*").floating).toBe(true);
  expect(rangeBounds("[0.9.1, 1.0").invalid).toBe(true);

  expect(nuspecVersionProblems(nuspec("0.9.1", [["Runic.Translations", "[0.9.1, 0.10.0-0.0)"], ["Runic.CommandLine", "[0.9.1, 0.10.0)"]]))).toEqual([]);
  expect(nuspecVersionProblems(nuspec("0.9.1", [["Runic.Translations", "[0.9.1, 0.10.0-0)"]])))
    .toEqual([expect.stringContaining("dependency Runic.Translations version range '[0.9.1, 0.10.0-0)': '0.10.0-0'")]);
  expect(nuspecVersionProblems(nuspec("0.9.1", [["Runic.Translations", "[0.10.0-0, 0.11.0-0.0)"]]))).toHaveLength(1);
  expect(nuspecVersionProblems(nuspec("1.0.0-1", []))).toEqual([expect.stringContaining("package version '1.0.0-1'")]);
  expect(nuspecVersionProblems(nuspec("0.9.1", [["Runic.Translations", "0.9.*"]]))).toEqual([expect.stringContaining("floating")]);
  expect(nuspecVersionProblems(nuspec("0.9.1", [["Runic.Translations", "[0.9.1, 1.0.0+meta)"]]))).toEqual([expect.stringContaining("build metadata")]);
});

test("a .nupkg or .snupkg with a -0 bound fails the check, naming the file, and -0.0 passes", () => {
  const directory = mkdtempSync(join(tmpdir(), "runic-translations-manifest-"));
  try {
    zip(join(directory, "Acme.Grid.1.0.0.nupkg"), nuspec("1.0.0", [["Runic.Translations", "[0.9.1, 0.10.0-0.0)"]]));
    zip(join(directory, "Acme.Grid.1.0.0.snupkg"), nuspec("1.0.0", [["Runic.Translations", "[0.9.1, 0.10.0-0.0)"]]));
    expect(checkNugetManifests(directory)).toHaveLength(2);
    zip(join(directory, "Acme.Grid.1.0.0.snupkg"), nuspec("1.0.0", [["Runic.Translations", "[0.9.1, 0.10.0-0)"]]));
    expect(() => checkNugetManifests(directory)).toThrow(/Acme\.Grid\.1\.0\.0\.snupkg: dependency Runic\.Translations version range '\[0\.9\.1, 0\.10\.0-0\)'/);
    expect(() => checkNugetManifests(join(directory, "Acme.Grid.1.0.0.nupkg"))).not.toThrow();
    expect(() => checkNugetManifests(mkdtempSync(join(directory, "empty-")))).toThrow("No .nupkg or .snupkg");
  } finally {
    rmSync(directory, { recursive: true, force: true });
  }
});

test("publish stops on a package nuget.org would reject, before any registry read or push, also in the dry run", async () => {
  const directory = mkdtempSync(join(tmpdir(), "runic-translations-manifest-"));
  try {
    const file = join(directory, "Runic.Translations.CommandLine.0.9.1.nupkg");
    zip(file, nuspec("0.9.1", [["Runic.Translations", "[0.9.1, 0.10.0-0.0)"]]));
    // dotnet nuget push sends the .snupkg beside a .nupkg too, so its manifest counts.
    zip(file.replace(/\.nupkg$/, ".snupkg"), nuspec("0.9.1", [["Runic.Translations", "[0.9.1, 0.10.0-0)"]]));
    const candidates = [{ registry: "nuget", name: "Runic.Translations.CommandLine", file }];
    for (const dryRun of [true, false]) {
      const calls = [];
      await expect(publishCandidates(candidates, "0.9.1", { dryRun, log: () => {},
        published: async () => { calls.push("published"); return false; },
        checkPush: async () => { calls.push("checkPush"); }, push: async () => { calls.push("push"); } }))
        .rejects.toThrow("nuget.org would reject these packages");
      expect(calls).toEqual([]);
    }
    zip(file.replace(/\.nupkg$/, ".snupkg"), nuspec("0.9.1", [["Runic.Translations", "[0.9.1, 0.10.0-0.0)"]]));
    const pushed = [];
    await publishCandidates(candidates, "0.9.1", { dryRun: false, log: () => {}, published: async () => false,
      checkPush: async () => {}, push: async candidate => { pushed.push(candidate.name); } });
    expect(pushed).toEqual(["Runic.Translations.CommandLine"]);
  } finally {
    rmSync(directory, { recursive: true, force: true });
  }
});

test("CI's package verification and the publish dry run both run the manifest check", () => {
  const verify = readFileSync(new URL("../verify-packages.mjs", import.meta.url), "utf8");
  expect(verify).toContain("checkNugetManifests(nuget)");
  expect(Bun.YAML.parse(readFileSync(new URL("../../.github/workflows/ci.yml", import.meta.url), "utf8")).jobs)
    .toSatisfy(jobs => Object.values(jobs).some(job => job.steps?.some(step => step.run?.includes("bun eng/verify-packages.mjs"))));
  expect(readFileSync(new URL("./publish.mjs", import.meta.url), "utf8")).toMatch(/publishCandidates\(candidates, version, \{ dryRun, checkManifests = checkCandidateManifests/);
});

test("lockstep dependencies use one bound form: [F, next minor-0.0) before 1.0, [F, next major-0.0) from 1.0", () => {
  expect(lockstepUpperBound("0.12.0")).toBe("0.13.0-0.0");
  expect(lockstepUpperBound("0.10.0-preview.1")).toBe("0.11.0-0.0");
  expect(lockstepUpperBound("1.2.3")).toBe("2.0.0-0.0");
  expect(lockstepUpperBound("x")).toBeUndefined();
  expect(lockstepRangeProblems(nuspec("0.10.0", [["Runic.Translations", "[0.10.0, 0.11.0-0.0)"], ["Runic.Translations.Hosting", "[0.10.0, 0.11.0-0.0)"],
    ["Runic.CommandLine", "[0.12.0, 0.13.0-0.0)"], ["Microsoft.Extensions.DependencyInjection.Abstractions", "10.0.14"]]))).toEqual([]);
  // 0.9.1's Command Line bound, an open Hosting/Wpf dependency (I4), a -0 bound and a range across two minors.
  for (const [id, range] of [["Runic.CommandLine", "[0.9.1, 0.10.0)"], ["Runic.Translations", "0.9.1"], ["Runic.Translations.Hosting", "[0.10.0, 0.11.0-0)"],
    ["Runic.CommandLine.Spectre", "[0.12.0, 0.14.0-0.0)"], ["Runic.Translations", "[0.10.0, )"]])
    expect(lockstepRangeProblems(nuspec("0.10.0", [[id, range]]))).toEqual([expect.stringContaining(`dependency ${id} has the range '${range}'`)]);
});
