import { createRequire } from 'node:module';
import { dirname, join } from 'node:path';
import { copyFileSync, mkdirSync, readFileSync, readdirSync, writeFileSync, existsSync } from 'node:fs';
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
writeFileSync(join(import.meta.dirname, 'THIRD-PARTY-NOTICES.md'), notices.join('').trimEnd() + '\n');

// The runic.json schema is copied from the specification on every build, so members that a
// release adds to specs/translations/schemas/project-v1.schema.json reach the VSIX without an
// extension change. package.json's jsonValidation points at this copy.
const projectSchema = join('dist', 'schemas', 'project-v1.schema.json');
mkdirSync(join(import.meta.dirname, 'dist', 'schemas'), { recursive: true });
copyFileSync(join(import.meta.dirname, '..', '..', 'specs', 'translations', 'schemas', 'project-v1.schema.json'), join(import.meta.dirname, projectSchema));
