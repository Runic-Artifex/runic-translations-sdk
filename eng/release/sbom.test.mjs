import { test, expect } from 'bun:test';
import { copyFileSync, mkdirSync, mkdtempSync, readFileSync, rmSync } from 'node:fs';
import { join } from 'node:path';
import { tmpdir } from 'node:os';
import { execFileSync, spawnSync } from 'node:child_process';
import { createHash } from 'node:crypto';
import { fileURLToPath } from 'node:url';
import { REPOSITORY, VSIX, describe } from './ci-artifact.mjs';
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
                       'dependencies': {'@runic-artifex/other': '^1.0.0'}, 'peerDependencies': {'svelte': '>=5 <6'}}).encode()
    info = tarfile.TarInfo('package/package.json'); info.size = len(data); t.addfile(info, io.BytesIO(data))
os.makedirs(f'{out}/vsix', exist_ok=True)
with zipfile.ZipFile(f'{out}/vsix/fixture.vsix', 'w') as z:
    z.writestr('extension.vsixmanifest', '<PackageManifest Version="2.0.0" xmlns="http://schemas.microsoft.com/developer/vsx-schema/2011"><Metadata><Identity Id="fixture" Version="0.0.1" Publisher="runic-artifex" /></Metadata></PackageManifest>')
    z.writestr('extension/package.json', json.dumps({'name': 'fixture', 'license': 'MIT', 'dependencies': {'vscode-languageclient': '10.1.2', 'ranged': '^2.0.0'}}))
`;
function withFixtures(check) {
  const directory = mkdtempSync(join(tmpdir(), 'runic-sbom-test-'));
  try {
    execFileSync(python, ['-c', fixtures, join(directory, 'packages'), VERSION, source]);
    check(directory);
  } finally { rmSync(directory, {recursive: true, force: true}); }
}
const sbom = (directory, files, output = join(directory, 'sbom.json'), version = VERSION) => {
  const result = spawnSync(python, [join(root, 'eng/release/sbom.py'), '--repository', REPOSITORY, '--version', version,
    '--source', source, '--epoch', '1790000000', '--output', output, ...files.map(file => join(directory, 'packages', file))], {encoding: 'utf8'});
  return {result, bom: result.status === 0 ? JSON.parse(readFileSync(output, 'utf8')) : undefined};
};
const nuget = name => `nuget/${name}.${VERSION}.nupkg`;
const npmFile = `npm/runic-artifex-fixture-${VERSION}.tgz`;

test('the SBOM describes each artifact by hash with its declared and bundled dependencies', () => withFixtures(directory => {
  const {result, bom} = sbom(directory, [nuget('Runic.Fixture'), nuget('Runic.Fixture.Core'), nuget('dotnet-fixture'), npmFile, 'vsix/fixture.vsix']);
  expect(result.stderr).toBe('');
  expect(bom).toMatchObject({bomFormat: 'CycloneDX', specVersion: '1.6', version: 1});
  expect(bom.serialNumber).toMatch(/^urn:uuid:[0-9a-f-]{36}$/);
  expect(bom.metadata.timestamp).toBe('2026-09-21T14:13:20Z');
  expect(bom.metadata.component).toMatchObject({'bom-ref': 'release', name: 'runic-translations-sdk', version: VERSION,
    purl: `pkg:github/runic-artifex/runic-translations-sdk@v${VERSION}`});
  const components = Object.fromEntries(bom.components.map(c => [c['bom-ref'], c]));
  const edges = Object.fromEntries(bom.dependencies.map(d => [d.ref, d.dependsOn]));
  const core = `pkg:nuget/Runic.Fixture.Core@${VERSION}`, fixture = `pkg:nuget/Runic.Fixture@${VERSION}`, tool = `pkg:nuget/dotnet-fixture@${VERSION}`;
  const npm = `pkg:npm/%40runic-artifex/fixture@${VERSION}`, vsix = 'vsix:runic-artifex/fixture@0.0.1';
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

test('describe writes the same SBOM of every package and both VSIX files', () => withFixtures(directory => {
  const packages = join(directory, 'packages'), vsix = join(directory, 'ide');
  for (const {artifact, file, id} of VSIX) {
    mkdirSync(join(vsix, artifact), {recursive: true});
    execFileSync(python, ['-c', `import sys,zipfile\nwith zipfile.ZipFile(sys.argv[1],'w') as z: z.writestr('extension.vsixmanifest','<PackageManifest xmlns="http://schemas.microsoft.com/developer/vsx-schema/2011"><Metadata><Identity Id="${id}" Version="0.0.1" Publisher="runic-artifex" /></Metadata></PackageManifest>')`, join(vsix, artifact, file)]);
  }
  rmSync(join(packages, 'vsix'), {recursive: true});
  const first = describe(packages, vsix, VERSION, source, join(directory, 'first'), 1790000000);
  const second = describe(packages, vsix, VERSION, source, join(directory, 'second'), 1790000000);
  expect(first).toEndWith(`runic-translations-sdk-${VERSION}.cdx.json`);
  expect(readFileSync(first, 'utf8')).toBe(readFileSync(second, 'utf8'));
  const bom = JSON.parse(readFileSync(first, 'utf8'));
  expect(bom.dependencies[0].dependsOn).toEqual(expect.arrayContaining(['vsix:runic-artifex/runic-translations@0.0.1',
    'vsix:runic-artifex/Runic.Artifex.Translations.Rmf2@0.0.1']));
  expect(bom.dependencies[0].dependsOn).toHaveLength(6);
  const files = bom.components.flatMap(c => c.properties?.filter(p => p.name === 'runic:file').map(p => p.value) ?? []);
  for (const {file} of VSIX) expect(files).toContain(file);
  // Two artifacts with one identity are rejected rather than merged.
  copyFileSync(join(vsix, VSIX[0].artifact, VSIX[0].file), join(vsix, VSIX[1].artifact, VSIX[1].file));
  expect(() => describe(packages, vsix, VERSION, source, join(directory, 'third'), 1790000000)).toThrow();
}));
