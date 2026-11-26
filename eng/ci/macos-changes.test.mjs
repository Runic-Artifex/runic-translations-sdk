// eng/ci/macos-changes.sh against eng/ci/macos-paths.txt and eng/ci/full-run-paths.txt (CONTRIBUTING.md, "CI triggers
// and macOS jobs"): which pull requests and pushes to main run CI's macOS jobs, and which runs are full.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { execFileSync } from 'node:child_process';
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { root } from '../run.mjs';

const run = (paths, event) => execFileSync('bash', [resolve(root, 'eng/ci/macos-changes.sh'), ...(event ? ['--event', event] : []), '--stdin'],
  { input: paths.join('\n'), encoding: 'utf8', env: { ...process.env, GITHUB_OUTPUT: '' } }).trim().split('\n');
const decide = (paths, event) => run(paths, event).at(-1);
// Both decision lines: a run that runs the macOS jobs runs every job, so it is full.
const decision = (paths, event) => run(paths, event).slice(-2).join(' ');
const ordinaryMerge = ['README.md', 'packages/web/translations-svelte/src/translations/direction.ts', 'eng/release/notes/0.16.0/fixes/312.md'];

test('documentation, web packages and other workflows skip the macOS jobs', () => {
  assert.equal(decide(['README.md', 'docs/guides/translations/diagnostics.md',
    'packages/dotnet/Runic.Translations/Formatting/README.md', 'apps/translations-editor/docs/architecture.md']), 'macos=false');
  assert.equal(decide(['packages/web/translations-svelte/src/translations/direction.ts', 'bun.lock',
    '.github/workflows/publish-preview.yml', 'eng/release/ci-artifact.mjs']), 'macos=false');
  assert.equal(decide(['apps/translations-editor/Frontend/src/lib/coverage-format.ts',
    'apps/translations-editor/tests/HostedBrowserE2E/run-hosted-rmf2-ui.mjs', 'apps/translations-editor/Workspace/WorkspaceLoader.cs']), 'macos=false');
  assert.equal(decide([]), 'macos=false');
});

test('formatting, the editor host, CI files and pins run the macOS jobs', () => {
  for (const path of [
    'packages/dotnet/Runic.Translations/Formatting/Rmf2NumberFormats.cs',
    'tools/Runic.Translations.Compiler/Generation/Rmf2EsmRuntimeV5.js',
    'specs/translations/corpus/localized-format-divergences.json',
    'tests/dotnet/Runic.Translations.Runtime.Tests/LocalizedFormattingTests.cs',
    'apps/translations-editor/EditorWindow.cs',
    '.github/workflows/ci.yml',
    '.github/actions/train-feed/action.yml',
    'eng/ci/macos-paths.txt',
    'global.json',
    'eng/Versions.props',
    'tests/dotnet/Runic.Translations.Build.Tests/packages.lock.json',
  ]) assert.equal(decide(['README.md', path]), 'macos=true', path);
});

test('a pull request decides by the macOS paths alone, also without --event', () => {
  assert.equal(decision(ordinaryMerge, 'pull_request'), 'full=false macos=false');
  assert.equal(decision(['eng/release/notes/0.16.0.md', 'eng/Versions.props'], 'pull_request'), 'full=true macos=true');
  assert.equal(decision(['eng/release/notes/0.16.0.md', '.github/workflows/publish-preview.yml'], 'pull_request'), 'full=false macos=false');
  assert.equal(decision(ordinaryMerge), 'full=false macos=false');
});

test('an ordinary merge to main skips the macOS jobs; one that changes a macOS path runs them', () => {
  assert.equal(decision(ordinaryMerge, 'push'), 'full=false macos=false');
  assert.equal(decision([], 'push'), 'full=false macos=false');
  assert.equal(decision([...ordinaryMerge, 'packages/dotnet/Runic.Translations/Formatting/Rmf2NumberFormats.cs'], 'push'), 'full=true macos=true');
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
    assert.equal(output.slice(-2).join(' '), 'full=true macos=true', path);
    assert.ok(output.includes(`Version or release path: ${path}`), path);
  }
  // A release note fragment is an ordinary change.
  assert.equal(decision(['eng/release/notes/0.16.0/features/312.md', 'eng/release/notes/0.16.0/_intro.md'], 'push'), 'full=false macos=false');
});

test('nightly, manual and multi-commit push runs run every job', () => {
  for (const event of ['schedule', 'workflow_dispatch', 'push-range']) {
    const output = run(ordinaryMerge, event);
    assert.equal(output.slice(-2).join(' '), 'full=true macos=true', event);
    assert.ok(output.includes(`A ${event} run runs every job.`), event);
  }
});

test('the changes job runs the rule for every event and marks only full runs outside pull requests', () => {
  const workflow = readFileSync(resolve(root, '.github/workflows/ci.yml'), 'utf8');
  assert.match(workflow, /bash eng\/ci\/macos-changes\.sh --event "\$event" HEAD\^1 HEAD/);
  assert.match(workflow, /name: ci-full-run/);
  assert.equal(workflow.match(/if: steps\.paths\.outputs\.full == 'true' && github\.event_name != 'pull_request'/g)?.length, 2);
});
