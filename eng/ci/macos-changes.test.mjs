// eng/ci/macos-changes.sh against eng/ci/macos-paths.txt (CONTRIBUTING.md, "CI triggers and macOS jobs"): which pull
// request changes run CI's macOS jobs.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { execFileSync } from 'node:child_process';
import { resolve } from 'node:path';
import { root } from '../run.mjs';

const decide = paths => {
  const output = execFileSync('bash', [resolve(root, 'eng/ci/macos-changes.sh'), '--stdin'],
    { input: paths.join('\n'), encoding: 'utf8', env: { ...process.env, GITHUB_OUTPUT: '' } });
  return output.trim().split('\n').at(-1);
};

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
