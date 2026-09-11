import { createRequire } from 'node:module';
import { dirname, join } from 'node:path';
import { readFileSync, readdirSync, writeFileSync, copyFileSync, existsSync } from 'node:fs';
const require = createRequire(import.meta.url);
const visited = new Set();
const notices = ['# Bundled third-party licenses\n\nGenerated from the locked runtime dependency graph.\n'];
function visit(name, from = import.meta.dirname) {
  let folder = dirname(require.resolve(name, { paths: [from] }));
  while (!existsSync(join(folder, 'package.json')) || JSON.parse(readFileSync(join(folder, 'package.json'), 'utf8')).name !== name) {
    const parent = dirname(folder); if (parent === folder) throw new Error(`Cannot locate package metadata for ${name}`); folder = parent;
  }
  const file = join(folder, 'package.json');
  if (visited.has(file)) return;
  visited.add(file);
  const manifest = JSON.parse(readFileSync(file, 'utf8'));
  const license = readdirSync(folder).find(name => /^licen[cs]e(?:\.(?:md|txt))?$/i.test(name));
  if (!license) throw new Error(`Missing license for bundled dependency ${name}`);
  notices.push(`\n## ${manifest.name} ${manifest.version}\n\n${readFileSync(join(folder, license), 'utf8')}\n`);
  for (const child of Object.keys(manifest.dependencies ?? {})) visit(child, folder);
}
visit('vscode-languageclient');
writeFileSync(join(import.meta.dirname, 'THIRD-PARTY-NOTICES.md'), notices.join(''));
// vscode-languageclient uses this asset when a POSIX server does not shut down gracefully.
copyFileSync(join(dirname(require.resolve('vscode-languageclient/package.json')), 'lib/node/terminateProcess.sh'), join(import.meta.dirname, 'dist/terminateProcess.sh'));
