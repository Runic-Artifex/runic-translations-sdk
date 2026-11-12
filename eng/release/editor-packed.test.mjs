import { expect, test } from "bun:test";
import { editorPackedVersion } from "../run.mjs";
import { readFileSync } from "node:fs";

const semver = /^[0-9]+\.[0-9]+\.[0-9]+(?:-[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?$/;

test("the local-only editor-packed version is SemVer for plain and prerelease versions", () => {
  expect(editorPackedVersion("0.8.0")).toBe("0.8.0-editor-packed");
  expect(editorPackedVersion("1.0.0-rc.2")).toBe("1.0.0-rc.2.editor-packed");
  expect(editorPackedVersion("1.0.0-preview.7")).toBe("1.0.0-preview.7.editor-packed");
  for (const release of ["0.8.0", "0.8.1", "1.0.0-rc.2", "1.0.0-preview.7"]) {
    expect(editorPackedVersion(release)).toMatch(semver);
  }
});

// R5 of the train (COMPATIBILITY.md): the editor's tool manifest names this release, like the other literal pins.
test("the editor's tool manifest pins this release of dotnet-runic-translations", () => {
  const manifest = JSON.parse(readFileSync(new URL("../../apps/translations-editor/.config/dotnet-tools.json", import.meta.url), "utf8"));
  const { version } = JSON.parse(readFileSync(new URL("../workspace.json", import.meta.url), "utf8"));
  expect(manifest.tools["dotnet-runic-translations"].version).toBe(version);
});
