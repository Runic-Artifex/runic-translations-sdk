import { test, expect } from 'bun:test';
import { mkdtempSync, readFileSync, rmSync } from 'node:fs';
import { join } from 'node:path';
import { tmpdir } from 'node:os';
import { execFileSync, spawnSync } from 'node:child_process';
import { createHash } from 'node:crypto';
import { fileURLToPath } from 'node:url';
import { REPOSITORY, describe } from './ci-artifact.mjs';
import { writeVsixFixtures } from './vsix-fixtures.mjs';
const VERSION = '1.2.3-preview.1';
const root = fileURLToPath(new URL('../..', import.meta.url));
const python = process.platform === 'win32' ? 'python' : 'python3';
const source = execFileSync('git', ['rev-parse', 'HEAD'], {cwd: root, encoding: 'utf8'}).trim();
const sha256 = path => createHash('sha256').update(readFileSync(path)).digest('hex');
// Writes small but real .nupkg, npm .tgz and .vsix archives.
const fixtures = String.raw`
import io, json, sys, tarfile, zipfile, base64, os
out, version, source = sys.argv[1:]
def nupkg(name, nuspec_extra='', files={}):
    os.makedirs(f'{out}/nuget', exist_ok=True)
    with zipfile.ZipFile(f'{out}/nuget/{name}.{version}.nupkg', 'w') as z:
        z.writestr(f'{name}.nuspec', f'''<?xml version="1.0" encoding="utf-8"?>
<package xmlns="http://schemas.microsoft.com/packaging/2012/06/nuspec.xsd"><metadata>
<id>{name}</id><version>{version}</version><license type="expression">MIT</license>
<repository type="git" url="https://github.com/Runic-Artifex/runic-translations-sdk" commit="{source}" />{nuspec_extra}</metadata></package>''')
        for path, content in files.items(): z.writestr(path, content)
nupkg('Runic.Fixture.Core')
nupkg('Runic.Fixture', f'<dependencies><group targetFramework="net10.0"><dependency id="Runic.Fixture.Core" version="[{version}]" /><dependency id="Third.Party" version="1.2.3" /></group></dependencies>')
deps = {'runtimeTarget': {'name': '.NETCoreApp,Version=v10.0'},
        'targets': {'.NETCoreApp,Version=v10.0': {f'dotnet-fixture/{version}': {'dependencies': {'Fixture.Helper': '1.0.0', 'Runic.Fixture.Core': version}},
                                                  'Fixture.Helper/1.0.0': {'dependencies': {'Bundled.Lib': '2.0.0'}},
                                                  'Bundled.Lib/2.0.0': {}, f'Runic.Fixture.Core/{version}': {}}},
        'libraries': {f'dotnet-fixture/{version}': {'type': 'project'}, 'Fixture.Helper/1.0.0': {'type': 'project'},
                      'Bundled.Lib/2.0.0': {'type': 'package', 'sha512': 'sha512-' + base64.b64encode(bytes(range(64))).decode()},
                      f'Runic.Fixture.Core/{version}': {'type': 'project'}, 'Microsoft.NETCore.App/10.0.0': {'type': 'runtimepack'}}}
nupkg('dotnet-fixture', '<packageTypes><packageType name="DotnetTool" /></packageTypes>',
      {'tools/net10.0/any/dotnet-fixture.deps.json': '﻿' + json.dumps(deps)})
os.makedirs(f'{out}/npm', exist_ok=True)
with tarfile.open(f'{out}/npm/runic-artifex-fixture-{version}.tgz', 'w:gz') as t:
    data = json.dumps({'name': '@runic-artifex/fixture', 'version': version, 'license': 'MIT', 'gitHead': source,
                       'repository': {'type': 'git', 'url': 'git+https://github.com/Runic-Artifex/runic-translations-sdk.git'},
                       'dependencies': {'@runic-artifex/other': '^1.0.0'}, 'peerDependencies': {'svelte': '>=5 <6'}}).encode()
    info = tarfile.TarInfo('package/package.json'); info.size = len(data); t.addfile(info, io.BytesIO(data))
os.makedirs(f'{out}/vsix', exist_ok=True)
with zipfile.ZipFile(f'{out}/vsix/fixture.vsix', 'w') as z:
    z.writestr('extension.vsixmanifest', '<PackageManifest Version="2.0.0" xmlns="http://schemas.microsoft.com/developer/vsx-schema/2011"><Metadata><Identity Id="fixture" Version="1.2.3001" Publisher="runic-artifex" /></Metadata></PackageManifest>')
    z.writestr('extension/package.json', json.dumps({'name': 'fixture', 'license': 'MIT', 'dependencies': {'vscode-languageclient': '10.1.2', 'ranged': '^2.0.0'}}))
`;
function withFixtures(check) {
  const directory = mkdtempSync(join(tmpdir(), 'runic-sbom-test-'));
  try {
    execFileSync(python, ['-c', fixtures, join(directory, 'packages'), VERSION, source]);
    check(directory);
  } finally { rmSync(directory, {recursive: true, force: true}); }
}
const sbom = (directory, files, output = join(directory, 'sbom.json'), version = VERSION, artifactVersions = {}) => {
  const result = spawnSync(python, [join(root, 'eng/release/sbom.py'), '--repository', REPOSITORY, '--version', version,
    '--source', source, '--epoch', '1790000000', '--output', output,
    ...Object.entries(artifactVersions).flatMap(([file, value]) => ['--artifact-version', `${file}=${value}`]),
    ...files.map(file => join(directory, 'packages', file))], {encoding: 'utf8'});
  return {result, bom: result.status === 0 ? JSON.parse(readFileSync(output, 'utf8')) : undefined};
};
const nuget = name => `nuget/${name}.${VERSION}.nupkg`;
const npmFile = `npm/runic-artifex-fixture-${VERSION}.tgz`;

test('the SBOM describes each artifact by hash with its declared and bundled dependencies', () => withFixtures(directory => {
  const {result, bom} = sbom(directory, [nuget('Runic.Fixture'), nuget('Runic.Fixture.Core'), nuget('dotnet-fixture'), npmFile, 'vsix/fixture.vsix'],
    undefined, VERSION, {'fixture.vsix': '1.2.3001'});
  expect(result.stderr).toBe('');
  expect(bom).toMatchObject({bomFormat: 'CycloneDX', specVersion: '1.6', version: 1});
  expect(bom.serialNumber).toMatch(/^urn:uuid:[0-9a-f-]{36}$/);
  expect(bom.metadata.timestamp).toBe('2026-09-21T14:13:20Z');
  expect(bom.metadata.component).toMatchObject({'bom-ref': 'release', name: 'runic-translations-sdk', version: VERSION,
    purl: `pkg:github/runic-artifex/runic-translations-sdk@v${VERSION}`});
  const components = Object.fromEntries(bom.components.map(c => [c['bom-ref'], c]));
  const edges = Object.fromEntries(bom.dependencies.map(d => [d.ref, d.dependsOn]));
  const core = `pkg:nuget/Runic.Fixture.Core@${VERSION}`, fixture = `pkg:nuget/Runic.Fixture@${VERSION}`, tool = `pkg:nuget/dotnet-fixture@${VERSION}`;
  const npm = `pkg:npm/%40runic-artifex/fixture@${VERSION}`, vsix = 'vsix:runic-artifex/fixture@1.2.3001';
  expect(edges.release).toEqual([npm, tool, core, fixture, vsix].sort());
  expect(components[core].hashes).toEqual([{alg: 'SHA-256', content: sha256(join(directory, 'packages', nuget('Runic.Fixture.Core')))}]);
  expect(components[npm].hashes[0].content).toBe(sha256(join(directory, 'packages', npmFile)));
  expect(components[fixture]).toMatchObject({type: 'library', licenses: [{expression: 'MIT'}]});
  expect(components[fixture].properties).toContainEqual({name: 'runic:source-commit', value: source});
  expect(components[tool].type).toBe('application');
  expect(components[npm]).toMatchObject({group: '@runic-artifex', name: 'fixture'});
  // An exact pin on another artifact links to it; other ranges stay requested ranges.
  expect(edges[fixture]).toEqual([core, 'pkg:nuget/Third.Party#1.2.3']);
  expect(components['pkg:nuget/Third.Party#1.2.3']).toMatchObject({name: 'Third.Party', purl: 'pkg:nuget/Third.Party',
    properties: [{name: 'runic:requested-range', value: '1.2.3'}]});
  expect(edges[npm]).toEqual(['pkg:npm/%40runic-artifex/other#^1.0.0', 'pkg:npm/svelte#>=5 <6']);
  // Bundled libraries come from deps.json, with the NuGet package SHA-512; runtime packs are left out.
  expect(components['pkg:nuget/Bundled.Lib@2.0.0'].hashes).toEqual([{alg: 'SHA-512', content: Buffer.from([...Array(64).keys()]).toString('hex')}]);
  // The tool's own project is the tool; other projects built into it are components of their own.
  expect(edges[tool]).toEqual(['pkg:nuget/Bundled.Lib@2.0.0', core, 'project:Fixture.Helper@1.0.0']);
  expect(components['project:Fixture.Helper@1.0.0']).toMatchObject({name: 'Fixture.Helper', version: '1.0.0'});
  expect(edges['project:Fixture.Helper@1.0.0']).toEqual(['pkg:nuget/Bundled.Lib@2.0.0']);
  expect(Object.keys(components).some(ref => ref.includes('Microsoft.NETCore.App'))).toBe(false);
  // A VS Code extension bundles its exact runtime dependencies.
  expect(edges[vsix]).toEqual(['pkg:npm/ranged#^2.0.0', 'pkg:npm/vscode-languageclient@10.1.2']);
  const refs = new Set(['release', ...Object.keys(components)]);
  for (const {ref, dependsOn} of bom.dependencies) for (const target of [ref, ...dependsOn]) expect(refs.has(target)).toBe(true);
}));

test('the SBOM is deterministic and rejects artifacts of another version', () => withFixtures(directory => {
  const files = [nuget('Runic.Fixture.Core'), npmFile];
  sbom(directory, files, join(directory, 'a.json')); sbom(directory, [...files].reverse(), join(directory, 'b.json'));
  expect(readFileSync(join(directory, 'a.json'), 'utf8')).toBe(readFileSync(join(directory, 'b.json'), 'utf8'));
  const {result} = sbom(directory, files, join(directory, 'c.json'), '9.9.9-preview.9');
  expect(result.status).not.toBe(0);
  expect(result.stderr).toContain(`is version ${VERSION}, not 9.9.9-preview.9`);
}));

test('every artifact declares the release version unless --artifact-version names its own', () => withFixtures(directory => {
  const files = [nuget('Runic.Fixture.Core'), 'vsix/fixture.vsix'];
  // A VSIX is not exempt: without its mapped version it must declare the release version.
  expect(sbom(directory, files).result.stderr).toContain(`fixture.vsix is version 1.2.3001, not ${VERSION}`);
  expect(sbom(directory, files, undefined, VERSION, {'fixture.vsix': '0.0.1'}).result.stderr).toContain('fixture.vsix is version 1.2.3001, not 0.0.1');
  expect(sbom(directory, files, undefined, VERSION, {'fixture.vsix': '1.2.3001'}).result.status).toBe(0);
  expect(sbom(directory, files, undefined, VERSION, {'fixture.vsix': '1.2.3001', 'other.vsix': '1'}).result.stderr).toContain('--artifact-version names no artifact: other.vsix');
  const malformed = spawnSync(python, [join(root, 'eng/release/sbom.py'), '--repository', REPOSITORY, '--version', VERSION, '--source', source,
    '--epoch', '1', '--output', join(directory, 'x.json'), '--artifact-version', 'fixture.vsix', join(directory, 'packages', files[1])], {encoding: 'utf8'});
  expect(malformed.stderr).toContain('expects one FILE_NAME=VERSION');
}));

test('describe writes the same SBOM of exactly the release packages and both VSIX files at their mapped versions', () => withFixtures(directory => {
  const packages = join(directory, 'packages'), vsix = join(directory, 'vsix');
  rmSync(join(packages, 'vsix'), {recursive: true});
  writeVsixFixtures(vsix, VERSION);
  const inventory = {nuget: ['Runic.Fixture', 'Runic.Fixture.Core', 'dotnet-fixture'].map(name => ({name})), npm: [{name: '@runic-artifex/fixture'}]};
  const first = describe(packages, vsix, VERSION, source, join(directory, 'first'), 1790000000, inventory);
  const second = describe(packages, vsix, VERSION, source, join(directory, 'second'), 1790000000, inventory);
  expect(first).toEndWith(`runic-translations-sdk-${VERSION}.cdx.json`);
  expect(readFileSync(first, 'utf8')).toBe(readFileSync(second, 'utf8'));
  const released = JSON.parse(readFileSync(first, 'utf8')).dependencies[0].dependsOn;
  expect(released).toHaveLength(6);
  expect(released.filter(ref => ref.startsWith('vsix:'))).toEqual(['vsix:Runic Artifex/Runic.Artifex.Translations.Rmf2@1.2.3.1', 'vsix:runic-artifex/runic-translations@1.2.3001']);
  expect(() => describe(packages, vsix, VERSION, source, join(directory, 'third'), 1790000000, {...inventory, npm: []})).toThrow('exactly');
  writeVsixFixtures(vsix, VERSION, {visualStudio: {version: '0.0.1'}});
  expect(() => describe(packages, vsix, VERSION, source, join(directory, 'fourth'), 1790000000, inventory)).toThrow('declares version 0.0.1');
}));
// Hostile or unusual metadata: oversized entries, document type declarations and versionless dependencies.
const edgeFixtures = String.raw`
import io, sys, tarfile, zipfile, os
out, version = sys.argv[1:]
os.makedirs(f'{out}/nuget', exist_ok=True); os.makedirs(f'{out}/npm', exist_ok=True)
def nuspec(name, body='', doctype=''):
    return f'<?xml version="1.0"?>{doctype}<package><metadata><id>{name}</id><version>{version}</version>{body}</metadata></package>'
def nupkg(name, spec, files={}):
    with zipfile.ZipFile(f'{out}/nuget/{name}.{version}.nupkg', 'w', zipfile.ZIP_DEFLATED) as z:
        z.writestr(f'{name}.nuspec', spec)
        for path, content in files.items(): z.writestr(path, content)
nupkg('Large', nuspec('Large'), {'tools/Large.deps.json': ' ' * (17 * 1024 * 1024)})
nupkg('Doctype', nuspec('Doctype', doctype='<!DOCTYPE package [<!ENTITY big "x">]>'))
nupkg('Floating', nuspec('Floating', '<dependencies><group><dependency id="Any.Version" /></group></dependencies>'))
with tarfile.open(f'{out}/npm/large-{version}.tgz', 'w:gz') as t:
    data = b' ' * (17 * 1024 * 1024)
    info = tarfile.TarInfo('package/package.json'); info.size = len(data); t.addfile(info, io.BytesIO(data))
`;
test('the SBOM refuses oversized entries and document types and accepts versionless dependencies', () => {
  const directory = mkdtempSync(join(tmpdir(), 'runic-sbom-edge-'));
  try {
    execFileSync(python, ['-c', edgeFixtures, join(directory, 'packages'), VERSION]);
    for (const [file, message] of [[nuget('Large'), 'Large.deps.json is larger than 16777216 bytes'],
      [`npm/large-${VERSION}.tgz`, 'package/package.json is larger than 16777216 bytes'],
      [nuget('Doctype'), 'must not declare a document type or entities']]) {
      const {result} = sbom(directory, [file]);
      expect(result.status).not.toBe(0);
      expect(result.stderr).toContain(message);
    }
    const {result, bom} = sbom(directory, [nuget('Floating')]);
    expect(result.stderr).toBe('');
    expect(bom.components.find(c => c['bom-ref'] === 'pkg:nuget/Any.Version')).toEqual({type: 'library', 'bom-ref': 'pkg:nuget/Any.Version',
      name: 'Any.Version', purl: 'pkg:nuget/Any.Version'});
    expect(bom.dependencies.find(d => d.ref === `pkg:nuget/Floating@${VERSION}`).dependsOn).toEqual(['pkg:nuget/Any.Version']);
  } finally { rmSync(directory, {recursive: true, force: true}); }
});
