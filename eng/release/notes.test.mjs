import { expect, test } from 'bun:test';
import { existsSync, mkdirSync, mkdtempSync, readFileSync, readdirSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join } from 'node:path';
import { anchor, assemble, check, fragmentVersions, notesDirectory, repositoryPaths, split } from './notes.mjs';

// The fixture is a released notes file written before fragments existed.
const FIXTURE = '0.11.0';
const fixture = readFileSync(join(notesDirectory, `${FIXTURE}.md`), 'utf8');
const normalize = text => text.replace(/[ \t]+$/gm, '').replace(/\n{3,}/g, '\n\n').trim();
const exists = repositoryPaths();
const anywhere = () => true;

function scratch(body) {
  const base = mkdtempSync(join(tmpdir(), 'runic-notes-'));
  try { return body(join(base, '9.9.0')); } finally { rmSync(base, {recursive: true, force: true}); }
}
function files(directory, entries) {
  for (const [path, text] of Object.entries(entries)) {
    mkdirSync(dirname(join(directory, path)), {recursive: true});
    writeFileSync(join(directory, path), text);
  }
}
const intro = '# Notes 9.9.0\n\nInstall matching `9.9.0` packages.\n';

test('splitting and assembling released notes gives the same notes', () => scratch(directory => {
  split(fixture, directory);
  expect(check(directory, {exists})).toEqual([]);
  expect(normalize(assemble(directory))).toBe(normalize(fixture));
}));

test('split names fragments by section and issue', () => scratch(directory => {
  split(fixture, directory);
  const listed = section => readdirSync(join(directory, section)).sort();
  // A feature heading without (#issue) is named by its words; a cross-repository
  // reference (runic-cli-sdk#98) is not this repository's issue.
  expect(listed('features')).toEqual(['command-line-text-for-runic-command.md', 'tool.md']);
  expect(listed('upgrading')).toHaveLength(5);
  expect(readFileSync(join(directory, 'upgrading/_intro.md'), 'utf8')).toBe('## Upgrading from 0.10\n');
  expect(listed('ci')).toEqual(['ci-builds-against-runic-command-line.md']);
  expect(readFileSync(join(directory, '_intro.md'), 'utf8')).toStartWith(`# Runic Translations ${FIXTURE}\n`);
}));

test('split names a feature by the issue line under its heading', () => scratch(directory => {
  split(`${intro}\n## Edited messages update in place\n\n([#176](https://github.com/Runic-Artifex/runic-translations-sdk/issues/176))\nText.\n`, directory);
  expect(readdirSync(join(directory, 'features'))).toEqual(['176.md']);
}));

test('split skips what is a fragment already and adds the rest', () => scratch(directory => {
  files(directory, {'_intro.md': intro, 'features/951.md': '## Source maps (#951)\n\nMain text.\n', 'fixes/965.md': '- Owned windows (#965).\n'});
  const branch = `${intro}\n## Source maps (#951)\n\nMain text.\n\n## Open in editor (#952)\n\nBranch text.\n\n## Fixes\n\n- Owned windows (#965).\n- A second fix (#970).\n`;
  const log = split(branch, directory);
  expect(log).toContain('kept _intro.md');
  expect(log).toContain('skipped features/951.md (already a fragment)');
  expect(log).toContain('skipped fixes/965.md (already a fragment)');
  expect(log).toContain('wrote features/952.md');
  expect(log).toContain('wrote fixes/970.md');
  expect(normalize(assemble(directory))).toBe(normalize(branch));
  // A changed entry for the same issue is written beside the old one, with a warning.
  const again = split(`${intro}\n## Fixes\n\n- Owned windows, reworded (#965).\n`, directory);
  expect(again.find(line => line.startsWith('wrote fixes/965-'))).toContain('check whether this one replaces it');
}));

test('entries sort by order, then issue number, then file name', () => scratch(directory => {
  files(directory, {
    '_intro.md': intro,
    'fixes/30.md': '- Thirty (#30).\n', 'fixes/4.md': '- Four (#4).\n', 'fixes/words.md': '- Words.\n',
    'fixes/aaa.md': '- Aaa.\n', 'fixes/100.md': '---\norder: 5\n---\n- First by order (#100).\n',
    'features/20.md': '## Twenty (#20)\n\nText.\n', 'features/7.md': '## Seven (#7)\n\nText.\n',
  });
  expect(check(directory, {exists: anywhere})).toEqual([]);
  expect(assemble(directory)).toBe(`${intro}\n## Seven (#7)\n\nText.\n\n## Twenty (#20)\n\nText.\n\n## Fixes\n\n`
    + '- First by order (#100).\n- Four (#4).\n- Thirty (#30).\n- Aaa.\n- Words.\n');
}));

test('one pull request contributes to several sections', () => scratch(directory => {
  files(directory, {
    '_intro.md': intro,
    'features/951.md': '## Source maps (#951)\n\nText.\n',
    'breaking/951.md': '- **Rebuild** for source maps (#951).\n',
    'behaviour/951.md': '- Output changes (#951).\n',
    'fixes/951.md': '- A fix that came with it (#951).\n',
    'known-issues/951.md': '- Not on Windows yet (#951).\n',
  });
  expect(check(directory, {exists: anywhere})).toEqual([]);
  expect(assemble(directory)).toBe(`${intro}\n## Source maps (#951)\n\nText.\n\n## Upgrading\n\n### Breaking changes\n\n- **Rebuild** for source maps (#951).\n\n`
    + '### Behaviour and output changes\n\n- Output changes (#951).\n\n'
    + '## Fixes\n\n- A fix that came with it (#951).\n\n## Known issues\n\n- Not on Windows yet (#951).\n');
}));

test('a section intro can replace the heading and table rows join their table', () => scratch(directory => {
  files(directory, {
    '_intro.md': intro,
    'upgrading/_intro.md': '## Upgrading from 9.8\n\nUpdate everything.\n',
    'experimental/_intro.md': 'Opt in per ID:\n\n| ID | Feature |\n| --- | --- |\n',
    'experimental/104.md': '| `X104` | Side panel (#104) |\n',
    'experimental/105.md': '| `X105` | Close drain (#105) |\n',
    'experimental/_end.md': 'Everything else is stable.\n',
  });
  expect(check(directory, {exists: anywhere})).toEqual([]);
  expect(assemble(directory)).toBe(`${intro}\n## Upgrading from 9.8\n\nUpdate everything.\n\n### Experimental features and how to opt in\n\n`
    + 'Opt in per ID:\n\n| ID | Feature |\n| --- | --- |\n| `X104` | Side panel (#104) |\n| `X105` | Close drain (#105) |\n\nEverything else is stable.\n');
}));

test('check reports misplaced, malformed and duplicate fragments', () => scratch(directory => {
  files(directory, {
    '_intro.md': intro,
    'stray.md': '- Not in a section.\n',
    'bugfixes/1.md': '- Unknown section (#1).\n',
    'features/1.md': '## Same title (#1)\n\nText.\n',
    'features/2.md': '## Same title (#1)\n\nAgain.\n',
    'features/3.md': 'No heading (#3).\n',
    'features/4.md': '## One (#4)\n\nText.\n\n## Two (#4)\n\nText.\n',
    'features/5.md': '## Fixes\n\n- Belongs in fixes (#5).\n',
    'fixes/6.md': '### A heading\n\n- Fix (#6).\n',
    'fixes/7.md': 'Plain text (#7).\n',
    'fixes/8.md': '---\nsection: fixes\n---\n- Fix (#8).\n',
    'fixes/Upper.md': '- Fix.\n',
    'ci/9.md': '',
  });
  const problems = check(directory, {exists: anywhere});
  const expected = [
    ['bugfixes/', 'unknown section'],
    ['stray.md', 'fragments go in a section directory'],
    ['features/2.md', 'repeats the heading "## Same title (#1)" of features/1.md'],
    ['features/3.md', 'starts with its "## Title (#issue)" heading'],
    ['features/4.md', 'has 2 "#"/"##" headings'],
    ['features/5.md', 'is the fixes section'],
    ['fixes/6.md', 'has a heading'],
    ['fixes/7.md', 'is a list item'],
    ['fixes/8.md', 'unknown front matter "section: fixes"'],
    ['fixes/Upper.md', 'in lower case'],
    ['ci/9.md', 'is empty'],
  ];
  for (const [where, text] of expected) expect(problems.some(problem => problem.startsWith(`9.9.0/${where}`) && problem.includes(text))).toBe(true);
}));

test('check resolves links as the assembled notes have them', () => scratch(directory => {
  files(directory, {
    '_intro.md': `${intro}\nSee [the fix](#fixes), [the guide](../../../CONTRIBUTING.md) and [Upgrading](#upgrading).\n`,
    'features/1.md': '## A feature (#1)\n\nSee [missing](../../../docs/no-such-guide.md) and [nowhere](#no-such-heading).\n',
    'fixes/2.md': '- Fix (#2).\n',
  });
  const problems = check(directory, {exists});
  expect(problems).toEqual([
    '9.9.0/: the link #upgrading names no heading of the notes',
    '9.9.0/: the link ../../../docs/no-such-guide.md names a file that does not exist (docs/no-such-guide.md)',
    '9.9.0/: the link #no-such-heading names no heading of the notes',
  ]);
}));

test('check rejects fragments beside assembled notes and a missing intro', () => scratch(directory => {
  files(directory, {'fixes/1.md': '- Fix (#1).\n'});
  writeFileSync(`${directory}.md`, intro);
  const problems = check(directory, {exists: anywhere});
  expect(problems.some(problem => problem.includes('9.9.0.md exists too'))).toBe(true);
  expect(problems.some(problem => problem.startsWith('9.9.0/_intro.md: missing'))).toBe(true);
}));

test('anchors follow GitHub', () => {
  expect(anchor('Go to Definition from `m.key()` to the message\'s source')).toBe('go-to-definition-from-mkey-to-the-messages-source');
  expect(anchor('Upgrading from 0.10')).toBe('upgrading-from-010');
  expect(anchor('`@runic-artifex/angular` in the Angular Package Format (#945)')).toBe('runic-artifexangular-in-the-angular-package-format-945');
});

// The fragments of the coming releases, as the release pull request will assemble them.
test('the repository\'s release-note fragments are valid', () => {
  for (const version of fragmentVersions()) {
    expect(existsSync(join(notesDirectory, version, '_intro.md'))).toBe(true);
    expect(check(join(notesDirectory, version), {exists})).toEqual([]);
  }
});
