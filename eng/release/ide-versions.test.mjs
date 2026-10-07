import { expect, test } from "bun:test";
import { execFileSync, spawnSync } from "node:child_process";
import { readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
import { ideVersions, workspaceVersion } from "./ide-versions.mjs";

const root = fileURLToPath(new URL("../..", import.meta.url));
const python = process.platform === "win32" ? "python" : "python3";
const project = "tools/visualstudio-runic-translations/Runic.Translations.VisualStudio.csproj";
const read = path => readFileSync(new URL(`../../${path}`, import.meta.url), "utf8");
const table = [
  ["0.6.0-preview.1", "0.6.0.1", "0.6.1", true],
  ["0.6.0-preview.2", "0.6.0.2", "0.6.2", true],
  ["0.6.0-preview.999", "0.6.0.999", "0.6.999", true],
  ["0.6.0", "0.6.0.1000", "0.6.1000", false],
  ["0.6.1-preview.1", "0.6.1.1", "0.6.1001", true],
  ["0.6.1", "0.6.1.1000", "0.6.2000", false],
  ["0.7.0-preview.1", "0.7.0.1", "0.7.1", true],
  ["1.0.0", "1.0.0.1000", "1.0.1000", false],
];
const compare = (a, b) => {
  const [x, y] = [a, b].map(version => version.split(".").map(Number));
  return x.map((part, index) => part - y[index]).find(difference => difference !== 0) ?? 0;
};

test("maps a release onto the Visual Studio and VS Code version formats", () => {
  for (const [release, visualStudio, vscode, vscodePreRelease] of table)
    expect(ideVersions(release)).toEqual({ release, visualStudio, vscode, vscodePreRelease });
});

test("IDE versions sort in release order and a preview never takes a final release's number", () => {
  for (let index = 1; index < table.length; index++) {
    expect(compare(table[index - 1][1], table[index][1])).toBeLessThan(0);
    expect(compare(table[index - 1][2], table[index][2])).toBeLessThan(0);
  }
  const finals = new Set(), previews = new Set();
  for (let patch = 0; patch < 5; patch++) {
    finals.add(ideVersions(`0.6.${patch}`).vscode);
    for (const preview of [1, 2, 500, 999]) previews.add(ideVersions(`0.6.${patch}-preview.${preview}`).vscode);
  }
  expect([...previews].filter(version => finals.has(version))).toEqual([]);
});

test("refuses versions the IDE formats cannot express", () => {
  for (const version of ["0.6.0-preview.0", "0.6.0-preview.1000", "0.6.0-rc.1", "0.6.0-preview", "0.6", "06.0.0", "0.6.0+build", ""])
    expect(() => ideVersions(version)).toThrow("IDE extension versions cannot express it");
  expect(() => ideVersions("0.65535.0")).toThrow("above 65534");
});

test("package.py and the Visual Studio project map versions like ide-versions.mjs", () => {
  for (const [release, visualStudio] of table)
    expect(execFileSync(python, [`${root}/tools/visualstudio-runic-translations/package.py`, "--print-version", "--release-version", release], { encoding: "utf8" }).trim()).toBe(visualStudio);
  const refused = spawnSync(python, [`${root}/tools/visualstudio-runic-translations/package.py`, "--print-version", "--release-version", "0.6.0-rc.1"], { encoding: "utf8" });
  expect(refused.status).not.toBe(0);
  expect(execFileSync(python, [`${root}/tools/visualstudio-runic-translations/package.py`, "--print-version"], { encoding: "utf8" }).trim())
    .toBe(ideVersions(workspaceVersion()).visualStudio);
});

const dotnet = spawnSync("dotnet", ["--version"], { encoding: "utf8" }).status === 0;
test.skipIf(!dotnet)("the Visual Studio project stamps the mapped version from eng/workspace.json", () => {
  const msbuild = (...properties) => spawnSync("dotnet", ["msbuild", project, "-nologo", "-getTargetResult:GetRunicVsixVersion", ...properties],
    { cwd: root, encoding: "utf8" });
  const stamped = result => JSON.parse(result.stdout).TargetResults.GetRunicVsixVersion;
  const current = stamped(msbuild());
  expect(current.Result).toBe("Success");
  expect(current.Items.map(item => item.Identity)).toEqual([ideVersions(workspaceVersion()).visualStudio]);
  for (const [release, visualStudio] of [table[1], table[3], table[4]])
    expect(stamped(msbuild(`-p:RunicIdeReleaseVersion=${release}`)).Items.map(item => item.Identity)).toEqual([visualStudio]);
  const refused = msbuild("-p:RunicIdeReleaseVersion=0.6.0-rc.1");
  expect(refused.status).not.toBe(0);
  expect(refused.stdout + refused.stderr).toContain("cannot be mapped to a VSIX version");
}, 120_000);

test("both manifests take their version from the build, never from a committed number", () => {
  expect(read("tools/visualstudio-runic-translations/source.extension.vsixmanifest"))
    .toContain('<Identity Id="Runic.Artifex.Translations.Rmf2" Version="|%CurrentProject%;GetRunicVsixVersion|"');
  const extension = JSON.parse(read("tools/vscode-runic-translations/package.json"));
  expect(extension.scripts.package).toBe("bun run build && node package.mjs");
  expect(read("tools/vscode-runic-translations/.vscodeignore").split("\n")).toContain("package.mjs");
});
