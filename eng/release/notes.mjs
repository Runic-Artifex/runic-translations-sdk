#!/usr/bin/env bun
// Release-note fragments (D066.33). Each pull request writes its notes as files of
// their own under eng/release/notes/<version>/, so open pull requests never edit the
// same file; the release pull request assembles them into <version>.md, which the
// publish workflow reads. eng/release/README.md, "Release notes", describes the layout.
//
//   bun eng/release/notes.mjs check [<version>...]   validate fragments (all versions by default)
//   bun eng/release/notes.mjs assemble <version>     write <version>.md and remove <version>/
//   bun eng/release/notes.mjs split <version> [<file>] [--keep]
//                                                    turn a <version>.md into fragments
import { execFileSync } from 'node:child_process';
import { existsSync, mkdirSync, readFileSync, readdirSync, rmSync, statSync, writeFileSync } from 'node:fs';
import { dirname, join, posix, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const here = dirname(fileURLToPath(import.meta.url));
export const root = resolve(here, '../..');
export const notesDirectory = resolve(here, 'notes');

// The sections in the order <version>.md lists them. `heading` is the default heading;
// a section's _intro.md that starts with a heading of the same level replaces it (for
// example "## Upgrading from 0.19"). `match` recognizes the section when splitting.
// Sections with a `parent` are subsections of it. `loose` lists separate entries with
// a blank line. Every other `##` section is a feature section (features/).
// Runic Translations lists its feature sections before Upgrading, and CI under
// "Release engineering"; runic-sdk's copy of this file differs only there.
export const SECTIONS = [
  {name: 'highlights', heading: '## Highlights', match: /^Highlights$/},
  {name: 'features'},
  {name: 'upgrading', heading: '## Upgrading', match: /^Upgrading\b/},
  {name: 'breaking', heading: '### Breaking changes', parent: 'upgrading', loose: true, match: /^Breaking changes$/},
  {name: 'behaviour', heading: '### Behaviour and output changes', parent: 'upgrading', loose: true, match: /^Behaviou?r (and output )?changes$/},
  {name: 'experimental', heading: '### Experimental features and how to opt in', parent: 'upgrading', loose: true, match: /^Experimental\b/},
  {name: 'fixes', heading: '## Fixes', match: /^Fixes$/},
  {name: 'ci', heading: '## Release engineering', match: /^(CI and tests|Release engineering)$/},
  {name: 'known-issues', heading: '## Known issues', match: /^Known issues$/},
];
const byName = new Map(SECTIONS.map(section => [section.name, section]));
const level = section => section.heading?.match(/^#+/)[0].length ?? 2;
const children = name => SECTIONS.filter(section => section.parent === name);

// ---------------------------------------------------------------------------
// Markdown helpers. Lines inside fenced code blocks are never headings or entries.

function fencedLines(lines) {
  let fence = null;
  return lines.map(line => {
    const marker = line.match(/^ {0,3}(`{3,}|~{3,})/)?.[1];
    if (fence) {
      if (marker?.[0] === fence[0] && marker.length >= fence.length) fence = null;
      return true;
    }
    if (marker) { fence = marker; return true; }
    return false;
  });
}

// Splits a document at its headings of `depth` hashes: the text before the first one,
// and each heading with the text up to the next.
function sectionsAt(text, depth) {
  const lines = text.split('\n');
  const fenced = fencedLines(lines);
  const pattern = new RegExp(`^#{${depth}} (.*)$`);
  const parts = [];
  let current = {heading: null, lines: []};
  lines.forEach((line, i) => {
    const heading = !fenced[i] && line.match(pattern);
    if (heading) { parts.push(current); current = {heading: heading[1].trim(), lines: []}; }
    else current.lines.push(line);
  });
  parts.push(current);
  return {before: parts[0].lines.join('\n').trim(), sections: parts.slice(1).map(part => ({heading: part.heading, body: part.lines.join('\n').trim()}))};
}

function headings(text) {
  const lines = text.split('\n');
  const fenced = fencedLines(lines);
  return lines.flatMap((line, i) => {
    const match = !fenced[i] && line.match(/^(#{1,6}) (.*)$/);
    return match ? [{depth: match[1].length, text: match[2].trim()}] : [];
  });
}

const isItem = line => /^([-*+]|\d+[.)]) /.test(line);
const isRow = line => line.startsWith('|');

// The blocks of a list section's body: list items (with their indented continuation),
// table rows (the header and delimiter row of a table are not entries) and other text.
function blocks(body) {
  const lines = body.split('\n');
  const fenced = fencedLines(lines);
  const result = [];
  let i = 0;
  while (i < lines.length) {
    const line = lines[i];
    if (!line.trim()) { i++; continue; }
    if (!fenced[i] && isItem(line)) {
      let end = i + 1;
      while (end < lines.length && (fenced[end] || !lines[end].trim() || /^\s/.test(lines[end]))) end++;
      while (end > i + 1 && !lines[end - 1].trim()) end--;
      result.push({entry: true, text: lines.slice(i, end).join('\n')});
      i = end;
    } else if (!fenced[i] && isRow(line)) {
      let end = i;
      while (end < lines.length && isRow(lines[end])) end++;
      const delimiter = end > i + 1 && /^\|[\s:|-]+\|?\s*$/.test(lines[i + 1]);
      if (delimiter) result.push({entry: false, text: lines.slice(i, i + 2).join('\n')});
      for (const row of lines.slice(delimiter ? i + 2 : i, end)) result.push({entry: true, row: true, text: row});
      i = end;
    } else {
      let end = i + 1;
      while (end < lines.length && (fenced[end] || (lines[end].trim() && !isItem(lines[end]) && !isRow(lines[end])))) end++;
      result.push({entry: false, text: lines.slice(i, end).join('\n')});
      i = end;
    }
  }
  return result;
}

// A list section's body as text before its entries, the entries, and text after them.
function listParts(body) {
  const all = blocks(body);
  const first = all.findIndex(block => block.entry);
  if (first < 0) return {intro: body.trim(), entries: [], end: ''};
  let last = first;
  while (last + 1 < all.length && all[last + 1].entry) last++;
  const join = list => list.map(block => block.text).join('\n\n');
  return {intro: join(all.slice(0, first)), entries: all.slice(first, last + 1).map(block => block.text), end: join(all.slice(last + 1))};
}

const plain = text => text
  .replace(/!?\[([^\]]*)\]\([^)]*\)/g, '$1')
  .replace(/<[^>]+>/g, '')
  .replace(/[`*_~]/g, '');

// GitHub's anchor for a heading.
export function anchor(text) {
  return plain(text).trim().toLowerCase().replace(/[^\p{L}\p{M}\p{N}\p{Pc}\- ]/gu, '').replace(/ /g, '-');
}

function slug(text) {
  const words = plain(text).replace(/^([-*+]|\d+[.)]|\|)\s*/, '').replace(/['’]/g, '').toLowerCase().match(/[\p{L}\p{N}]+/gu) ?? [];
  return words.slice(0, 6).join('-').slice(0, 48).replace(/-+$/, '') || 'note';
}

// The pull request or issue number an entry names: `(#951)` in a feature heading, the
// `([#176](...))` line under it, or the first `#NNN` of this repository in an entry.
function issueOf(text, feature) {
  if (feature) {
    const [heading, ...rest] = text.split('\n');
    const body = rest.find(line => line.trim()) ?? '';
    return heading.match(/\(#(\d+)\b/)?.[1] ?? body.match(/^\(\[#(\d+)\]/)?.[1];
  }
  return text.match(/(?<![\w/#-])#(\d+)\b/)?.[1];
}

const normalize = text => text.replace(/[ \t]+$/gm, '').replace(/\n{3,}/g, '\n\n').trim();

// ---------------------------------------------------------------------------
// Fragment files.

function parseFragment(text) {
  const match = text.match(/^---\n([\s\S]*?)\n---\n?/);
  const meta = {};
  const problems = [];
  if (match) for (const line of match[1].split('\n').filter(line => line.trim())) {
    const field = line.match(/^(\w+):\s*(.*)$/);
    if (field?.[1] === 'order' && /^-?\d+$/.test(field[2].trim())) meta.order = Number(field[2]);
    else problems.push(`unknown front matter "${line.trim()}" (only "order: <integer>")`);
  }
  return {meta, body: (match ? text.slice(match[0].length) : text).trim(), problems};
}

const issueOfName = name => name.match(/^(\d+)(?:-|\.md$)/)?.[1];

// The fragments of one section, in the order the assembled notes list them: those
// with `order` by it, then the others by issue number, then by file name.
function readSection(directory, name) {
  const path = join(directory, name);
  const read = file => existsSync(join(path, file)) ? parseFragment(readFileSync(join(path, file), 'utf8')) : undefined;
  if (!existsSync(path)) return {intro: undefined, end: undefined, entries: []};
  const entries = readdirSync(path).filter(file => file.endsWith('.md') && !file.startsWith('_')).map(file => {
    const fragment = read(file);
    return {file, ...fragment, issue: issueOfName(file)};
  });
  const key = entry => [entry.meta.order ?? Infinity, entry.issue === undefined ? Infinity : Number(entry.issue)];
  entries.sort((a, b) => {
    const [ka, kb] = [key(a), key(b)];
    return ka[0] - kb[0] || (ka[1] === kb[1] ? 0 : ka[1] < kb[1] ? -1 : 1) || (a.file < b.file ? -1 : a.file > b.file ? 1 : 0);
  });
  return {intro: read('_intro.md'), end: read('_end.md'), entries};
}

function joinEntries(texts, loose) {
  return texts.reduce((all, text, i) => i === 0 ? text
    : all + (isRow(texts[i - 1]) && isRow(text) ? '\n' : loose ? '\n\n' : '\n') + text, '');
}

// The assembled notes of a fragment directory.
export function assemble(directory) {
  const parts = [readFileSync(join(directory, '_intro.md'), 'utf8').trim()];
  const content = new Map(SECTIONS.map(section => [section.name, readSection(directory, section.name)]));
  const hasContent = name => {
    const section = content.get(name);
    return Boolean(section.intro?.body || section.end?.body || section.entries.length);
  };
  for (const section of SECTIONS) {
    const {intro, end, entries} = content.get(section.name);
    if (section.name === 'features') { parts.push(...entries.map(entry => entry.body)); continue; }
    if (!hasContent(section.name) && !children(section.name).some(child => hasContent(child.name))) continue;
    let heading = section.heading, introText = intro?.body ?? '';
    const own = introText.match(new RegExp(`^#{${level(section)}} .*`))?.[0];
    if (own) { heading = own; introText = introText.slice(own.length).trim(); }
    const listed = joinEntries(entries.map(entry => entry.body), section.loose);
    const body = introText && listed && isRow(introText.split('\n').at(-1)) && isRow(listed)
      ? [`${introText}\n${listed}`] : [introText, listed];
    parts.push([heading, ...body, end?.body ?? ''].filter(Boolean).join('\n\n'));
  }
  return parts.join('\n\n') + '\n';
}

// ---------------------------------------------------------------------------
// check

// Removes fenced and indented code blocks and inline code (as
// tests/engineering/markdown-links.test.mjs does in runic-sdk).
function prose(markdown) {
  const kept = [];
  let fence = null, blank = true, code = false, list = false;
  for (const line of markdown.split('\n')) {
    const indent = line.match(/^[ \t]*/)[0].replace(/\t/g, '    ').length;
    const marker = line.trimStart().match(/^(`{3,}|~{3,})/)?.[1];
    if (fence) {
      if (marker?.[0] === fence[0] && marker.length >= fence.length && indent < 4) fence = null;
      kept.push('');
      continue;
    }
    if (marker && indent < 4) { fence = marker; kept.push(''); continue; }
    if (!line.trim()) { blank = true; kept.push(line); continue; }
    code = indent >= 4 && !list && (blank || code);
    if (!code && indent < 4) list = /^\s*([-*+]|\d+[.)])\s/.test(line) || (list && indent > 0);
    blank = false;
    kept.push(code ? '' : line);
  }
  return kept.join('\n').replace(/(`+)[\s\S]*?\1/g, '');
}

function linkTargets(markdown) {
  const text = prose(markdown);
  const destination = String.raw`(?:<([^<>\n]*)>|((?:[^()\s]|\([^()\s]*\))+))`;
  return [
    ...[...text.matchAll(new RegExp(String.raw`!?\[(?:[^\][]|\[[^\]]*\])*\]\(\s*${destination}(?:\s+(?:"[^"]*"|'[^']*'|\([^()]*\)))?\s*\)`, 'g'))]
      .map(match => match[1] ?? match[2]),
    ...[...text.matchAll(new RegExp(String.raw`^ {0,3}\[(?!\^)[^\]]+\]:[ \t]*${destination}`, 'gm'))].map(match => match[1] ?? match[2]),
    ...[...text.matchAll(/<(?:a|img)\s[^>]*?\b(?:href|src)="([^"]+)"/g)].map(match => match[1]),
  ].filter(Boolean);
}

// Problems of the assembled notes as eng/release/notes/<file> would have them: broken
// relative links (against `exists`, a repository-relative path test), same-document
// anchors without a heading, and repeated `##` headings.
export function documentProblems(markdown, file, exists) {
  const problems = [];
  const anchors = new Set(), seen = new Map(), counts = new Map();
  for (const {depth, text} of headings(markdown)) {
    const base = anchor(text);
    const count = counts.get(base) ?? 0;
    counts.set(base, count + 1);
    anchors.add(count ? `${base}-${count}` : base);
    if (depth !== 2) continue;
    if (seen.has(text)) problems.push(`the heading "## ${text}" appears twice`);
    seen.set(text, true);
  }
  for (const target of linkTargets(markdown)) {
    if (target.startsWith('#')) {
      const name = decodeURIComponent(target.slice(1));
      if (name && !anchors.has(name.toLowerCase())) problems.push(`the link ${target} names no heading of the notes`);
      continue;
    }
    if (/^[a-z][a-z\d+.-]*:/i.test(target) || target.startsWith('//')) continue;
    const path = decodeURIComponent(target.replace(/[?#].*$/, ''));
    if (!path) continue;
    const resolved = posix.normalize(path.startsWith('/') ? path.slice(1) : posix.join('eng/release/notes', posix.dirname(file), path)).replace(/\/+$/, '');
    if (resolved !== '.' && !exists(resolved)) problems.push(`the link ${target} names a file that does not exist (${resolved})`);
  }
  return problems;
}

// Files Git tracks or would add, and their directories, as repository-relative paths.
export function repositoryPaths(repository = root) {
  let files;
  try {
    files = execFileSync('git', ['ls-files', '-z', '--cached', '--others', '--exclude-standard'], {cwd: repository, encoding: 'utf8', maxBuffer: 1 << 28})
      .split('\0').filter(Boolean);
  } catch {
    return path => existsSync(resolve(repository, path));
  }
  const paths = new Set();
  for (const file of files) for (let path = file; path && path !== '.'; path = posix.dirname(path)) paths.add(path);
  return path => paths.has(path);
}

const fragmentName = /^[a-z0-9][a-z0-9.-]*\.md$/;

// The problems of one version's fragments, as "<version>/<file>: <problem>".
export function check(directory, {exists = repositoryPaths()} = {}) {
  const version = directory.split(/[\\/]/).filter(Boolean).at(-1);
  const problems = [];
  const report = (file, problem) => problems.push(`${version}/${file}: ${problem}`);
  if (existsSync(`${directory}.md`)) report('', `${version}.md exists too; a release keeps either its fragments or its assembled notes`);
  if (!existsSync(join(directory, '_intro.md'))) report('_intro.md', 'missing: the title, install lines and hand-written introduction');
  for (const entry of readdirSync(directory)) {
    const path = join(directory, entry);
    if (statSync(path).isDirectory()) {
      if (!byName.has(entry)) report(`${entry}/`, `unknown section; use one of ${SECTIONS.map(section => section.name).join(', ')}`);
    } else if (entry !== '_intro.md') report(entry, `fragments go in a section directory (${SECTIONS.map(section => section.name).join(', ')})`);
  }
  const intro = existsSync(join(directory, '_intro.md')) ? readFileSync(join(directory, '_intro.md'), 'utf8') : '';
  if (intro && !/^# \S/.test(intro)) report('_intro.md', 'must start with the "# " title');
  if (headings(intro).some(heading => heading.depth > 1)) report('_intro.md', 'has a section heading; sections are fragments of their own');
  const featureHeadings = new Map();
  for (const section of SECTIONS) {
    const path = join(directory, section.name);
    if (!existsSync(path) || !statSync(path).isDirectory()) continue;
    for (const file of readdirSync(path)) {
      const where = `${section.name}/${file}`;
      if (statSync(join(path, file)).isDirectory()) { report(`${where}/`, 'a section has no subdirectories'); continue; }
      if (!['_intro.md', '_end.md'].includes(file) && !fragmentName.test(file)) {
        report(where, 'name fragments <issue>.md, <issue>-<words>.md or <words>.md in lower case');
        continue;
      }
      const {meta, body, problems: front} = parseFragment(readFileSync(join(path, file), 'utf8'));
      for (const problem of front) report(where, problem);
      if (!body) { report(where, 'is empty'); continue; }
      const found = headings(body);
      if (file.startsWith('_')) {
        if (meta.order !== undefined) report(where, '"order" applies to entries, not to _intro.md or _end.md');
        if (section.name === 'features') report(where, 'features/ has no _intro.md or _end.md; put text in the feature fragment');
        const own = file === '_intro.md' && found[0] && body.startsWith('#') && found[0].depth === level(section) ? 1 : 0;
        if (found.length > own) report(where, `has a heading other than the section's own (${section.heading})`);
      } else if (section.name === 'features') {
        if (!body.startsWith('## ')) report(where, 'a feature fragment starts with its "## Title (#issue)" heading');
        const top = found.filter(heading => heading.depth <= 2);
        if (top.length !== 1) report(where, `has ${top.length} "#"/"##" headings; a feature fragment has exactly one "##" heading`);
        const title = top[0]?.depth === 2 ? top[0].text : undefined;
        if (title) {
          const fixed = SECTIONS.find(other => other.match?.test(title));
          if (fixed) report(where, `"## ${title}" is the ${fixed.name} section; put its entries in ${fixed.name}/`);
          if (featureHeadings.has(title)) report(where, `repeats the heading "## ${title}" of features/${featureHeadings.get(title)}`);
          else featureHeadings.set(title, file);
        }
      } else {
        if (found.length) report(where, `has a heading; entries of ${section.name}/ are list items (or table rows) under "${section.heading}"`);
        else if (!isItem(body) && !isRow(body)) report(where, 'an entry is a list item ("- ...") or a table row ("| ... |")');
      }
    }
  }
  if (!problems.length) for (const problem of documentProblems(assemble(directory), `${version}.md`, exists)) report('', problem);
  return problems;
}

// Every version that has a fragment directory.
export function fragmentVersions(directory = notesDirectory) {
  return existsSync(directory) ? readdirSync(directory).filter(entry => statSync(join(directory, entry)).isDirectory()).sort() : [];
}

// ---------------------------------------------------------------------------
// split

function sectionEntries(name, body) {
  const section = byName.get(name);
  const {intro, entries, end} = listParts(body);
  return {intro, entries, end, loose: section.loose};
}

// The fragments of a notes document: {path: content} in document order, relative to
// the version's fragment directory.
export function parseNotes(markdown) {
  const {before, sections} = sectionsAt(markdown.replace(/\r\n/g, '\n'), 2);
  const units = [{kind: 'intro', text: before}];
  for (const {heading, body} of sections) {
    const fixed = SECTIONS.find(section => !section.parent && section.match?.test(heading));
    if (!fixed) { units.push({kind: 'entry', section: 'features', text: `## ${heading}${body ? `\n\n${body}` : ''}`}); continue; }
    const pieces = [{section: fixed, heading, body}];
    if (children(fixed.name).length) {
      const nested = sectionsAt(body, 3);
      pieces[0].body = nested.before;
      for (const sub of nested.sections) {
        const child = children(fixed.name).find(section => section.match.test(sub.heading));
        if (!child) throw new Error(`"### ${sub.heading}" under "## ${heading}" is not one of ${children(fixed.name).map(section => section.heading).join(', ')}`);
        pieces.push({section: child, heading: sub.heading, body: sub.body});
      }
    }
    for (const piece of pieces) {
      const {intro, entries, end} = sectionEntries(piece.section.name, piece.body);
      const marker = `${'#'.repeat(level(piece.section))} ${piece.heading}`;
      const ownHeading = marker !== piece.section.heading ? marker : '';
      const introText = [ownHeading, intro].filter(Boolean).join('\n\n');
      if (introText) units.push({kind: 'part', section: piece.section.name, file: '_intro.md', text: introText});
      for (const text of entries) units.push({kind: 'entry', section: piece.section.name, text});
      if (end) units.push({kind: 'part', section: piece.section.name, file: '_end.md', text: end});
    }
  }
  return units;
}

// Writes the fragments of `markdown` into `directory`, skipping what is a fragment
// already: the intro and a section's _intro.md/_end.md when present, a feature with
// the same heading, and an entry with the same text. Returns what it did, line by line.
export function split(markdown, directory) {
  const log = [];
  const units = parseNotes(markdown);
  const existing = new Map(SECTIONS.map(section => [section.name, readSection(directory, section.name)]));
  const taken = new Map(SECTIONS.map(section => [section.name, new Set(existsSync(join(directory, section.name)) ? readdirSync(join(directory, section.name)) : [])]));
  const identity = (section, text) => section === 'features' ? text.split('\n')[0].trim() : normalize(text);
  const writes = new Map();
  const write = (path, text) => {
    mkdirSync(dirname(join(directory, path)), {recursive: true});
    writeFileSync(join(directory, path), text.trim() + '\n');
  };
  for (const unit of units) {
    if (unit.kind === 'intro' || unit.kind === 'part') {
      const path = unit.kind === 'intro' ? '_intro.md' : `${unit.section}/${unit.file}`;
      if (!unit.text) continue;
      if (existsSync(join(directory, path))) {
        const current = parseFragment(readFileSync(join(directory, path), 'utf8')).body;
        log.push(normalize(current) === normalize(unit.text) ? `kept ${path}` : `kept ${path}; it differs from the document's, merge by hand if needed`);
      } else { write(path, unit.text); log.push(`wrote ${path}`); }
      continue;
    }
    const known = existing.get(unit.section).entries.find(entry => identity(unit.section, entry.body) === identity(unit.section, unit.text));
    if (known) { log.push(`skipped ${unit.section}/${known.file} (already a fragment)`); continue; }
    const names = taken.get(unit.section);
    const issue = issueOf(unit.text, unit.section === 'features');
    const words = slug(unit.section === 'features' ? unit.text.split('\n')[0].replace(/^## /, '').replace(/\s*\(#[^)]*\)\s*$/, '') : unit.text);
    let name = issue && !names.has(`${issue}.md`) ? `${issue}.md` : issue ? `${issue}-${words}.md` : `${words}.md`;
    for (let n = 2; names.has(name); n++) name = name.replace(/(-\d+)?\.md$/, `-${n}.md`);
    names.add(name);
    const clash = issue && name !== `${issue}.md` && existing.get(unit.section).entries.some(entry => entry.issue === issue);
    if (!writes.has(unit.section)) writes.set(unit.section, []);
    writes.get(unit.section).push({name, text: unit.text, clash});
  }
  // Keep the document's order: when the default order (issue number, then file name)
  // would change it, number the new entries of a section that had none.
  for (const [section, entries] of writes) {
    const defaultOrder = [...entries].sort((a, b) => {
      const [ia, ib] = [issueOfName(a.name), issueOfName(b.name)].map(issue => issue === undefined ? Infinity : Number(issue));
      return ia === ib ? (a.name < b.name ? -1 : a.name > b.name ? 1 : 0) : ia < ib ? -1 : 1;
    });
    const numbered = !existing.get(section).entries.length && defaultOrder.some((entry, i) => entry !== entries[i]);
    entries.forEach((entry, i) => {
      write(`${section}/${entry.name}`, numbered ? `---\norder: ${(i + 1) * 10}\n---\n${entry.text}` : entry.text);
      log.push(`wrote ${section}/${entry.name}${entry.clash ? `; ${section}/ has another fragment for #${issueOfName(entry.name)}, check whether this one replaces it` : ''}`);
    });
  }
  return log;
}

// ---------------------------------------------------------------------------

function main(args) {
  const [command, ...rest] = args;
  const versionDirectory = version => {
    if (!/^\d+\.\d+\.\d+(-[0-9A-Za-z.]+)?$/.test(version ?? '')) throw new Error(`Not a version: ${version ?? '(none)'}`);
    return join(notesDirectory, version);
  };
  if (command === 'check') {
    const versions = rest.length ? rest : fragmentVersions();
    const exists = repositoryPaths();
    const problems = versions.flatMap(version => {
      const directory = versionDirectory(version);
      return existsSync(directory) ? check(directory, {exists}) : [`${version}: no fragment directory eng/release/notes/${version}/`];
    });
    for (const problem of problems) console.error(problem);
    if (problems.length) return 1;
    console.log(versions.length ? `Release-note fragments of ${versions.join(', ')} are valid.` : 'No release-note fragment directories.');
    return 0;
  }
  if (command === 'assemble' && rest.length === 1) {
    const directory = versionDirectory(rest[0]);
    if (!existsSync(directory)) throw new Error(`No fragment directory eng/release/notes/${rest[0]}/`);
    const problems = check(directory);
    if (problems.length) { for (const problem of problems) console.error(problem); return 1; }
    writeFileSync(`${directory}.md`, assemble(directory));
    rmSync(directory, {recursive: true});
    console.log(`Wrote eng/release/notes/${rest[0]}.md and removed its fragments. Review it and commit both.`);
    return 0;
  }
  if (command === 'split' && rest.length >= 1) {
    const keep = rest.includes('--keep');
    const [version, file] = rest.filter(arg => arg !== '--keep');
    const directory = versionDirectory(version);
    const source = file ? resolve(file) : `${directory}.md`;
    if (!existsSync(source)) throw new Error(`No ${source}`);
    for (const line of split(readFileSync(source, 'utf8'), directory)) console.log(line);
    if (!file && !keep) { rmSync(source); console.log(`removed eng/release/notes/${version}.md`); }
    const problems = check(directory);
    for (const problem of problems) console.error(problem);
    return problems.length ? 1 : 0;
  }
  console.error('Use notes.mjs check [<version>...] | assemble <version> | split <version> [<file>] [--keep]');
  return 2;
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) process.exitCode = main(process.argv.slice(2));
