// Every diagnostic ID that shipped code reports has an entry in the diagnostics
// catalog, which help links name at the release tag. The C# generator's
// descriptors are also checked by GeneratorDiagnosticsTests.
import { expect, test } from "bun:test";
import { readFileSync, readdirSync } from "node:fs";
import { join, relative, resolve } from "node:path";

const root = resolve(import.meta.dir, "../..");
const catalog = readFileSync(join(root, "docs/guides/translations/diagnostics.md"), "utf8");
// Shipped sources: the packages and the dotnet-runic-translations tool with its compiler.
// The Translations Editor is not distributed, so its REDIT and RCLI9050 codes are not catalogued.
const shipped = ["packages/dotnet", "tools/Runic.Translations.Compiler", "tools/dotnet-runic-translations"];
// Recognized as a tool diagnostic but not reported by this release.
const unreported = new Set(["RCLI9010"]);
// RCLI1xxx are Runic.CommandLine parser codes, documented with Runic.CommandLine.
const idPattern = /\b(RTR\d{4}|RCLI9\d{3})\b/g;
const skipped = new Set(["bin", "obj", "node_modules", "dist"]);

function* sources(directory) {
  for (const entry of readdirSync(directory, { withFileTypes: true })) {
    const path = join(directory, entry.name);
    if (entry.isDirectory()) { if (!skipped.has(entry.name)) yield* sources(path); }
    else if (/\.(cs|targets|props)$/.test(entry.name)) yield path;
  }
}

const reports = new Map();
for (const directory of shipped)
  for (const file of sources(join(root, directory)))
    for (const [, id] of readFileSync(file, "utf8").matchAll(idPattern))
      if (!unreported.has(id)) reports.set(id, reports.get(id) ?? relative(root, file));

test("the scan finds the RTR and tool code families", () => {
  expect([...reports.keys()].some(id => id.startsWith("RTR"))).toBe(true);
  expect([...reports.keys()].some(id => id.startsWith("RCLI9"))).toBe(true);
});

test("every reported diagnostic ID has a catalog entry", () => {
  const missing = [...reports].filter(([id]) => !catalog.includes(`\n## ${id}\n`)).map(([id, file]) => `${id} (${file})`);
  expect(missing).toEqual([]);
});

test("help links are built from the release tag", () => {
  const generator = readFileSync(join(root, "packages/dotnet/Runic.Translations.Generator/TranslationsDiagnostics.cs"), "utf8");
  expect(generator).toContain("HelpLinkBase = RunicReleaseLinks.DiagnosticsHelpBase;");
  expect(generator).not.toMatch(/blob\/main/);
  expect(readFileSync(join(root, "Directory.Build.props"), "utf8"))
    .toContain("<RunicDiagnosticsCatalog>docs/guides/translations/diagnostics.md</RunicDiagnosticsCatalog>");
});
