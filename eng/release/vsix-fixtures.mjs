// Test fixtures: the two CI VSIX artifacts as small but real archives, stamped like CI stamps them.
import { execFileSync } from "node:child_process";
import { VSIX } from "./ci-artifact.mjs";
import { VSCODE_PRE_RELEASE, ideVersions } from "./ide-versions.mjs";

const script = String.raw`
import json, os, sys, zipfile
path, ident, version, publisher, pre_release, package_version = sys.argv[1:]
os.makedirs(os.path.dirname(path), exist_ok=True)
properties = f'<Properties><Property Id="${VSCODE_PRE_RELEASE}" Value="true" /></Properties>' if pre_release == 'true' else ''
with zipfile.ZipFile(path, 'w') as z:
    z.writestr('extension.vsixmanifest', f'<PackageManifest Version="2.0.0" xmlns="http://schemas.microsoft.com/developer/vsx-schema/2011"><Metadata><Identity Id="{ident}" Version="{version}" Language="en-US" Publisher="{publisher}" />{properties}</Metadata></PackageManifest>')
    if package_version:
        z.writestr('extension/package.json', json.dumps({'name': ident, 'version': package_version, 'license': 'MIT', 'dependencies': {'vscode-languageclient': '10.1.2'}}))
`;

// Writes <directory>/<artifact>/<file> for both extensions; override fields per format to break one.
export function writeVsixFixtures(directory, version, overrides = {}) {
  const versions = ideVersions(version);
  for (const { artifact, file, id, publisher, format } of VSIX) {
    const vscode = format === "vscode";
    const fixture = { id, publisher, version: versions[format], preRelease: vscode && versions.vscodePreRelease,
      packageVersion: vscode ? versions.vscode : "", ...overrides[format] };
    execFileSync(process.platform === "win32" ? "python" : "python3", ["-c", script, `${directory}/${artifact}/${file}`, fixture.id, fixture.version,
      fixture.publisher, String(fixture.preRelease), fixture.packageVersion]);
  }
}
