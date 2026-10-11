// Links in packed READMEs name the release tag, v<version>, never the main
// branch. eng/build/release-links.targets applies the same rules to NuGet
// packages; keep the two in step. runic-sdk keeps the same file.

const escape = text => text.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');

// A main-branch link to any Runic Artifex repository. Links to another
// repository cannot be versioned here and must use its package or the portal.
const mainBranchLink = /github\.com\/Runic-Artifex\/[\w.-]+\/(?:blob|tree)\/main(?=[/#?)\s>"]|$)[^\s)]*/;

// Rewrites this repository's blob/main and tree/main links to the tag, and
// resolves relative links, which registries cannot follow, against the tag.
// `directory` is the README's folder relative to the repository root.
export function releaseLinks(text, { repository, tag, directory }) {
  const base = `https://github.com/${repository}`;
  const folder = directory.replace(/\\/g, '/').replace(/^\.?\/?/, '').replace(/([^/])$/, '$1/');
  return text
    .replace(new RegExp(`${escape(base)}/(blob|tree)/main(?=[/#?)\\s>"]|$)`, 'g'), `${base}/$1/${tag}`)
    .replace(/\]\((?![A-Za-z][A-Za-z0-9+.-]*:|#|\/)([^)\s]+)\)/g, `](${base}/blob/${tag}/${folder}$1)`);
}

// Throws when a packed README still links to a main branch.
export function assertReleaseLinks(text, where) {
  const link = mainBranchLink.exec(text)?.[0];
  if (link) throw new Error(`${where} links to a main branch (${link}). Link to a file of this repository, which packing rewrites to the release tag, or to the package registry or documentation portal.`);
}
