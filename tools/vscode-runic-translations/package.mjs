// Packages artifacts/runic-translations.vsix with the release version of eng/workspace.json.
// The committed package.json keeps a placeholder version: the files vsce would package are
// copied to a staging directory, package.json is stamped there and vsce packs the copy, so
// packaging never modifies tracked files. See ide-versions.mjs for the version mapping.
import assert from 'node:assert/strict';
import { execFileSync } from 'node:child_process';
import { cpSync, mkdirSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { createRequire } from 'node:module';
import { dirname, join } from 'node:path';
import { ideVersions, vsixMetadata, workspaceVersion } from '../../eng/release/ide-versions.mjs';

const here = import.meta.dirname;
const vsce = join(dirname(createRequire(import.meta.url).resolve('@vscode/vsce/package.json')), 'vsce');
const run = (args, cwd) => execFileSync(process.execPath, [vsce, ...args], { cwd, encoding: 'utf8', stdio: ['ignore', 'pipe', 'inherit'] });
const versions = ideVersions(workspaceVersion());
const output = join(here, 'artifacts', 'runic-translations.vsix');
const staging = join(here, 'artifacts', 'package');

const files = run(['ls', '--no-dependencies'], here).split('\n').map(line => line.trim()).filter(Boolean);
assert.ok(files.includes('package.json') && files.includes('dist/extension.cjs'), `Unexpected vsce file list: ${files.join(', ')}`);
rmSync(staging, { recursive: true, force: true });
mkdirSync(staging, { recursive: true });
try {
  // .vscodeignore only keeps vsce from warning; the staging directory holds exactly the listed files.
  for (const file of [...files, '.vscodeignore']) cpSync(join(here, file), join(staging, file));
  const manifest = JSON.parse(readFileSync(join(here, 'package.json'), 'utf8'));
  writeFileSync(join(staging, 'package.json'), `${JSON.stringify({ ...manifest, version: versions.vscode }, null, 2)}\n`);
  rmSync(output, { force: true });
  run(['package', '--no-dependencies', ...(versions.vscodePreRelease ? ['--pre-release'] : []), '--out', output], staging);
} finally {
  rmSync(staging, { recursive: true, force: true });
}

const packed = vsixMetadata(output);
assert.deepEqual(packed, { id: 'runic-translations', version: versions.vscode, publisher: 'runic-artifex',
  preRelease: versions.vscodePreRelease, packageVersion: versions.vscode },
  `${output} does not carry the ${versions.release} mapping ${JSON.stringify(versions)}`);
console.log(`Packaged ${output}: version ${versions.vscode}${versions.vscodePreRelease ? ' (pre-release)' : ''} for release ${versions.release}`);
