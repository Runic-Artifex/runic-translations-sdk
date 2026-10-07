// Maps the release version (eng/workspace.json) onto the IDE extension version formats.
//   bun eng/release/ide-versions.mjs [version]   print the mapping as JSON (default: workspace version)
//
// Neither format takes a SemVer prerelease label, so a preview becomes a number that sorts
// below the final release of the same version and above every earlier release:
//
//   release           Visual Studio (N.N.N.N)   VS Code (x.y.z)   VS Code pre-release flag
//   0.6.0-preview.N   0.6.0.N                   0.6.N             yes
//   0.6.0             0.6.0.1000                0.6.1000          no
//   0.6.1-preview.N   0.6.1.N                   0.6.(1000 + N)    yes
//   0.6.1             0.6.1.1000                0.6.2000          no
//
// Visual Studio matches the AnalyzerReleases headers (packages/dotnet/Runic.Translations.Generator):
// the fourth component is the preview number, or 1000 for the final release. VS Code folds that
// fourth component into the patch: patch * 1000 + (N or 1000). A final x.y.z is therefore a
// multiple of 1000 and a preview never is, so a preview can never take the number of a later
// final release (0.6.N for preview N would collide with the final 0.6.N). Preview numbers run
// from 1 to 999. Only -preview.N and final versions are supported.
//
// Visual Studio reads each version component as a 16-bit System.Version part, so major, minor and
// patch are limited to 65534 here, in package.py and in the Visual Studio project. The VS Code patch
// is then at most 65534 * 1000 + 1000 = 65535000, well inside the 32-bit integer the Marketplace
// keeps per component. The VS Code "preview" field (the Marketplace "Preview" label) follows
// vscodePreRelease, so a final release is not labelled a preview; package.mjs stamps both.
import assert from "node:assert/strict";
import { execFileSync } from "node:child_process";
import { readFileSync } from "node:fs";

const RELEASE = /^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-preview\.([1-9]\d{0,2}))?$/;
export const VSCODE_PRE_RELEASE = "Microsoft.VisualStudio.Code.PreRelease";

export function ideVersions(version) {
  const match = RELEASE.exec(version ?? "");
  assert.ok(match, `${version} is not x.y.z or x.y.z-preview.N (1 <= N <= 999); IDE extension versions cannot express it`);
  const [major, minor, patch] = match.slice(1, 4).map(Number);
  // Visual Studio parses each component as a 16-bit System.Version part.
  assert.ok([major, minor, patch].every(part => part <= 65534), `${version} has a component above 65534`);
  const revision = match[4] ? Number(match[4]) : 1000;
  const vscodePatch = patch * 1000 + revision;
  assert.ok(vscodePatch <= 2 ** 31 - 1, `${version} maps to VS Code patch ${vscodePatch}, above the 32-bit Marketplace limit`);
  return { release: version, visualStudio: `${major}.${minor}.${patch}.${revision}`,
    vscode: `${major}.${minor}.${vscodePatch}`, vscodePreRelease: Boolean(match[4]) };
}

export const workspaceVersion = () => JSON.parse(readFileSync(new URL("../workspace.json", import.meta.url), "utf8")).version;

// Identity, pre-release flag, Marketplace "Preview" gallery flag and the bundled package.json
// version and preview field of a VSIX (python3, standard library only).
export function vsixMetadata(path) {
  const script = `import json,sys,zipfile,xml.etree.ElementTree as E
z=zipfile.ZipFile(sys.argv[1])
assert z.testzip() is None,'corrupt VSIX'
m=E.fromstring(z.read('extension.vsixmanifest'))
local=lambda e:e.tag.rsplit('}',1)[-1]
i=[e for e in m.iter() if local(e)=='Identity']
assert len(i)==1,'one VSIX identity'
p={e.get('Id'):e.get('Value') for e in m.iter() if local(e)=='Property'}
f=' '.join(e.text or '' for e in m.iter() if local(e)=='GalleryFlags').split()
j=json.loads(z.read('extension/package.json')) if 'extension/package.json' in z.namelist() else {}
print(json.dumps(dict(id=i[0].get('Id'),version=i[0].get('Version'),publisher=i[0].get('Publisher'),preRelease=p.get(sys.argv[2])=='true',preview='Preview' in f,packageVersion=j.get('version'),packagePreview=j.get('preview') is True)))`;
  return JSON.parse(execFileSync(process.platform === "win32" ? "python" : "python3", ["-c", script, path, VSCODE_PRE_RELEASE], { encoding: "utf8" }));
}

if (import.meta.main) console.log(JSON.stringify(ideVersions(process.argv[2] ?? workspaceVersion()), null, 2));
