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
  for (const version of ["65535.0.0", "0.65535.0", "0.6.65535", "0.6.99999-preview.1"])
    expect(() => ideVersions(version)).toThrow("above 65534");
});

// The largest supported release: every Visual Studio component fits System.Version, and the
// folded VS Code patch fits the Marketplace's 32-bit component.
test("the largest components map within both IDE limits", () => {
  expect(ideVersions("65534.65534.65534")).toEqual({ release: "65534.65534.65534", visualStudio: "65534.65534.65534.1000",
    vscode: "65534.65534.65535000", vscodePreRelease: false });
  expect(ideVersions("65534.65534.65534-preview.999").vscode).toBe("65534.65534.65534999");
  expect(65534 * 1000 + 1000).toBeLessThanOrEqual(2 ** 31 - 1);
});

const limits = [["65534.65534.65534", "65534.65534.65534.1000"], ["0.6.65534-preview.3", "0.6.65534.3"]];
const tooLarge = ["65535.0.0", "0.65535.0", "0.6.65535", "0.6.99999-preview.1"];

test("package.py and the Visual Studio project map versions like ide-versions.mjs", () => {
  for (const [release, visualStudio] of table)
    expect(execFileSync(python, [`${root}/tools/visualstudio-runic-translations/package.py`, "--print-version", "--release-version", release], { encoding: "utf8" }).trim()).toBe(visualStudio);
  for (const [release, visualStudio] of limits)
    expect(execFileSync(python, [`${root}/tools/visualstudio-runic-translations/package.py`, "--print-version", "--release-version", release], { encoding: "utf8" }).trim()).toBe(visualStudio);
  for (const version of ["0.6.0-rc.1", ...tooLarge]) {
    const refused = spawnSync(python, [`${root}/tools/visualstudio-runic-translations/package.py`, "--print-version", "--release-version", version], { encoding: "utf8" });
    expect(refused.status).not.toBe(0);
    expect(refused.stderr).toContain(version.startsWith("0.6.0-rc") ? "cannot be mapped" : "above 65534");
  }
  expect(execFileSync(python, [`${root}/tools/visualstudio-runic-translations/package.py`, "--print-version"], { encoding: "utf8" }).trim())
    .toBe(ideVersions(workspaceVersion()).visualStudio);
});

test("Publish preview rejects a version the IDE formats cannot express before looking for CI artifacts", () => {
  const release = Bun.YAML.parse(read(".github/workflows/publish-preview.yml"));
  const steps = release.jobs.candidate.steps;
  const index = steps.findIndex(step => step.name === "Validate version");
  expect(index).toBeGreaterThanOrEqual(0);
  expect(index).toBeLessThan(steps.findIndex(step => step.name === "Find the successful CI run for this commit"));
  // The format checks, without the comparisons with eng/workspace.json and eng/Versions.props that follow them.
  const script = steps[index].run.split("\n");
  const formatChecks = script.slice(0, script.findIndex(line => line.startsWith("bun eng/release/ide-versions.mjs")) + 1).join("\n");
  expect(script.slice(script.indexOf(formatChecks.split("\n").at(-1)) + 1).every(line => !line || line.startsWith("test "))).toBe(true);
  const validate = version => spawnSync("bash", ["-c", formatChecks], { cwd: root, encoding: "utf8", env: { ...process.env, VERSION: version } });
  for (const version of ["0.6.0-rc.1", "0.6.0-preview.0", "0.6.0-preview.1000", "0.6.0-beta", "0.6.0+build", "06.0.0", "0.6"]) {
    const refused = validate(version);
    expect(refused.status).not.toBe(0);
    expect(refused.stdout).toContain(`::error::Version '${version}' is not x.y.z-preview.N`);
  }
  const tooLarge = validate("0.65535.0");
  expect(tooLarge.status).not.toBe(0);
  expect(tooLarge.stderr).toContain("above 65534");
  const current = validate(workspaceVersion());
  expect(current.stderr).toBe("");
  expect(current.status).toBe(0);
  expect(JSON.parse(current.stdout)).toEqual(ideVersions(workspaceVersion()));
}, 30_000);

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
  for (const [release, visualStudio] of limits)
    expect(stamped(msbuild(`-p:RunicIdeReleaseVersion=${release}`)).Items.map(item => item.Identity)).toEqual([visualStudio]);
  for (const version of ["0.6.0-rc.1", ...tooLarge]) {
    const refused = msbuild(`-p:RunicIdeReleaseVersion=${version}`);
    expect(refused.status).not.toBe(0);
    expect(refused.stdout + refused.stderr).toContain("cannot be mapped to a VSIX version");
  }
}, 120_000);

test("both manifests take their version from the build, never from a committed number", () => {
  expect(read("tools/visualstudio-runic-translations/source.extension.vsixmanifest"))
    .toContain('<Identity Id="Runic.Artifex.Translations.Rmf2" Version="|%CurrentProject%;GetRunicVsixVersion|"');
  const extension = JSON.parse(read("tools/vscode-runic-translations/package.json"));
  expect(extension.scripts.package).toBe("bun run build && node package.mjs");
  // package.mjs stamps "preview" from the release; CI also packages a final release to check it.
  expect(read("tools/vscode-runic-translations/package.mjs")).toContain("preview: versions.vscodePreRelease }");
  const steps = Bun.YAML.parse(read(".github/workflows/ci.yml")).jobs["ide-packaging"].steps;
  expect(steps.find(step => step.name === "Build, test and package the VS Code extension").run.split("\n"))
    .toContain('node package.mjs 0.6.0 "$RUNNER_TEMP/final-release-check.vsix"');
  expect(read("tools/vscode-runic-translations/.vscodeignore").split("\n")).toContain("package.mjs");
});
