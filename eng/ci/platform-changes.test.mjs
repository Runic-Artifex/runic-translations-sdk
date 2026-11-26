// eng/ci/platform-changes.sh against eng/ci/macos-paths.txt, eng/ci/windows-paths.txt and eng/ci/full-run-paths.txt
// (CONTRIBUTING.md, "CI triggers and platform jobs"): which pull requests and pushes to main run CI's macOS and Windows
// jobs, and which runs are full.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { execFileSync } from 'node:child_process';
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { root } from '../run.mjs';

const run = (paths, event) => execFileSync('bash', [resolve(root, 'eng/ci/platform-changes.sh'), ...(event ? ['--event', event] : []), '--stdin'],
  { input: paths.join('\n'), encoding: 'utf8', env: { ...process.env, GITHUB_OUTPUT: '' } }).trim().split('\n');
// The three decision lines, as `macos windows full`, each true or false.
const decide = (paths, event) => run(paths, event).slice(-3).map(line => line.split('=')[1]).join(' ');
// Every macOS path is also a Windows path here, so no change runs macOS alone; one platform alone is not a full run.
const NONE = 'false false false', WINDOWS = 'false true false', BOTH = 'true true true';
const ordinaryMerge = ['README.md', 'packages/web/translations-svelte/src/translations/direction.ts', 'eng/release/notes/0.16.0/fixes/312.md'];

test('platform-neutral changes skip both platforms', () => {
  for (const paths of [
    ['README.md', 'docs/guides/translations/diagnostics.md', 'packages/dotnet/Runic.Translations/Formatting/README.md',
      'apps/translations-editor/docs/architecture.md', 'packages/dotnet/Runic.Translations.Authoring/PublicAPI.Unshipped.txt'],
    ['packages/web/translations-svelte/src/translations/direction.ts', 'bun.lock', '.github/workflows/publish-preview.yml',
      'eng/release/ci-artifact.mjs', '.github/workflows/dependency-audit.yml'],
    ['apps/translations-editor/Frontend/src/lib/coverage-format.ts', 'apps/translations-editor/tests/HostedBrowserE2E/run-hosted-rmf2-ui.mjs',
      'apps/translations-editor/Workspace/WorkspaceLoader.cs', 'apps/translations-editor/EditorViewModel.cs'],
    // Formatting logic without platform data, the compiler, the corpora, the conformance driver, CI scripts of Linux jobs.
    ['packages/dotnet/Runic.Translations/Formatting/PluralOperands.cs', 'packages/dotnet/Runic.Translations/Formatting/Rmf2MarkupContract.cs',
      'packages/dotnet/Runic.Translations/Formatting/TextMessageSelector.cs', 'tools/Runic.Translations.Compiler/Generation/Rmf2EsmOutputRendererV5.cs',
      'specs/translations/corpus/rmf2-markup-contract-v3.json', 'specs/translations/corpus/compiled/x.json', 'eng/conformance/core/driver.ts',
      'eng/ci/apt-install.sh', 'eng/ci/platform-changes.sh', 'eng/ci/macos-paths.txt'],
    // Lock files follow Directory.Packages.props; a test project's own lock file alone changes no platform behaviour.
    ['tests/dotnet/Runic.Translations.Compiler.Tests/packages.lock.json'],
    [],
  ]) assert.equal(decide(paths), NONE, paths.join(', '));
});

test('platform globalization, the native host, CI wiring and toolchain pins run both platforms', () => {
  for (const path of [
    'packages/dotnet/Runic.Translations/Formatting/Rmf2NumberFormats.cs',
    'packages/dotnet/Runic.Translations/Formatting/DefaultTextValueFormatter.cs',
    'packages/dotnet/Runic.Translations/Formatting/GeneratedLocaleData.bin',
    'tools/Runic.Translations.Compiler/Generation/Rmf2EsmRuntimeV5.js',
    'specs/translations/corpus/localized-format-divergences.json',
    'tests/dotnet/Runic.Translations.Runtime.Tests/LocalizedFormattingTests.cs',
    'apps/translations-editor/EditorWindow.cs',
    '.github/workflows/ci.yml',
    '.github/actions/train-feed/action.yml',
    'eng/ci/localized-formatting.sh',
    'eng/ci/editor-native-canary.sh',
    'global.json',
    'Directory.Packages.props',
    'eng/Versions.props',
  ]) assert.equal(decide(['README.md', path]), BOTH, path);
});

test('Windows path rules and line endings run only the Windows jobs', () => {
  for (const path of [
    'tools/Runic.Translations.Compiler/PathContainment.cs',
    'packages/dotnet/Runic.Translations.Authoring/Rmf2Workspace.cs',
    'tests/dotnet/Runic.Translations.Authoring.Tests/PathContainmentTests.cs',
    '.gitattributes',
  ]) assert.equal(decide(['README.md', path]), WINDOWS, path);
  assert.equal(decide(['packages/dotnet/Runic.Translations.Authoring/README.md']), NONE);
  assert.equal(decide(['tests/dotnet/Runic.Translations.Authoring.Tests/WorkspaceMutationTests.cs']), NONE);
});

test('every path entry of both lists names a tracked file', () => {
  const tracked = execFileSync('git', ['-C', root, 'ls-files'], { encoding: 'utf8' }).split('\n');
  for (const list of ['macos-paths.txt', 'windows-paths.txt']) {
    const patterns = readFileSync(resolve(root, 'eng/ci', list), 'utf8').split('\n').map(line => line.replace(/#.*/, '').trim())
      .filter(line => line && !line.startsWith('!'));
    for (const pattern of patterns) {
      const glob = new RegExp(`^${pattern.replace(/[.+?^${}()|[\]\\]/g, '\\$&').replaceAll('*', '.*')}$`);
      assert.ok(tracked.some(path => glob.test(path)), `${list}: ${pattern} matches no tracked file`);
    }
  }
});

test('a pull request decides by the platform lists alone, also without --event', () => {
  assert.equal(decide(ordinaryMerge, 'pull_request'), NONE);
  assert.equal(decide(['eng/release/notes/0.16.0.md', '.github/workflows/publish-preview.yml'], 'pull_request'), NONE);
  assert.equal(decide(['eng/release/notes/0.16.0.md', 'eng/Versions.props'], 'pull_request'), BOTH);
  assert.equal(decide(ordinaryMerge), NONE);
});

test('an ordinary merge to main skips both platforms; one that changes a platform path runs that platform', () => {
  assert.equal(decide(ordinaryMerge, 'push'), NONE);
  assert.equal(decide([], 'push'), NONE);
  assert.equal(decide([...ordinaryMerge, 'packages/dotnet/Runic.Translations/Formatting/Rmf2LocalizedFormats.cs'], 'push'), BOTH);
  assert.equal(decide([...ordinaryMerge, 'packages/dotnet/Runic.Translations.Authoring/Rmf2Workspace.cs'], 'push'), WINDOWS);
  const output = run([...ordinaryMerge, 'tools/Runic.Translations.Compiler/PathContainment.cs'], 'push');
  assert.ok(output.includes('Windows path: tools/Runic.Translations.Compiler/PathContainment.cs'));
  assert.ok(!output.some(line => line.startsWith('macOS path:')));
});

test('a version or release commit on main runs every job', () => {
  for (const path of [
    'eng/Versions.props',
    'eng/workspace.json',
    'eng/release/notes/0.16.0.md',
    'eng/release/notes/1.0.0-rc.1.md',
    '.github/workflows/publish-preview.yml',
  ]) {
    const output = run([...ordinaryMerge, path], 'push');
    assert.equal(decide([...ordinaryMerge, path], 'push'), BOTH, path);
    assert.ok(output.includes(`Version or release path: ${path}`), path);
  }
  // A release note fragment is an ordinary change.
  assert.equal(decide(['eng/release/notes/0.16.0/features/312.md', 'eng/release/notes/0.16.0/_intro.md'], 'push'), NONE);
});

test('nightly, manual and multi-commit push runs run every job', () => {
  for (const event of ['schedule', 'workflow_dispatch', 'push-range']) {
    assert.equal(decide(ordinaryMerge, event), BOTH, event);
    assert.ok(run(ordinaryMerge, event).includes(`A ${event} run runs every job.`), event);
  }
});

test('the changes job gates both platforms and marks only full runs outside pull requests', () => {
  const workflow = readFileSync(resolve(root, '.github/workflows/ci.yml'), 'utf8');
  assert.match(workflow, /bash eng\/ci\/platform-changes\.sh --event "\$event" HEAD\^1 HEAD/);
  assert.match(workflow, /name: ci-full-run/);
  assert.equal(workflow.match(/if: steps\.paths\.outputs\.full == 'true' && github\.event_name != 'pull_request'/g)?.length, 2);
  assert.equal(workflow.match(/if: needs\.changes\.outputs\.macos == 'true'/g)?.length, 2);
  assert.equal(workflow.match(/if: needs\.changes\.outputs\.windows == 'true'/g)?.length, 2);
  assert.match(workflow, /WINDOWS_JOBS: formatting-windows editor-windows/);
  assert.match(workflow, /MACOS_JOBS: formatting-macos editor-macos/);
});

