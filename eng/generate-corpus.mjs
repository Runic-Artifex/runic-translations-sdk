// Writes specs/translations/corpus/compiled: the locale artifacts and pack contracts the compiler produces for the
// conformance corpora, which runtimes without the C# compiler read (specs/translations/corpus/README.md, "Compiled
// artifacts"). With --check it only reports stale files and fails. The compilation lives in the Compiler test suite
// (CompiledCorpusTests.Fresh), which every `bun run test` also runs as the check, so CI fails on a stale directory.
import { spawnSync } from "node:child_process";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const root = resolve(dirname(fileURLToPath(import.meta.url)), "..");
const check = process.argv.slice(2).includes("--check");

if (process.argv.length > (check ? 3 : 2))
  throw new Error("Usage: bun eng/generate-corpus.mjs [--check]");

const args = [
  "run",
  "--project",
  "tests/dotnet/Runic.Translations.Compiler.Tests",
  "--configuration",
  "Release",
  "--",
  "--treenode-filter",
  "/*/*/CompiledCorpusTests/Fresh",
  "--minimum-expected-tests",
  "1",
];
if (!check) args.push("--test-parameter", "update-compiled-corpus=true");

const result = spawnSync("dotnet", args, { cwd: root, stdio: "inherit" });
if (result.error) throw result.error;
process.exit(result.status ?? 1);
