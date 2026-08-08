import { readFile } from "node:fs/promises";
import { performance } from "node:perf_hooks";
import ts from "typescript";

const sourceUrl = new URL("../src/lib/review-model.ts", import.meta.url);
const source = await readFile(sourceUrl, "utf8");
const transpiled = ts.transpileModule(source, {
  compilerOptions: { module: ts.ModuleKind.ESNext, target: ts.ScriptTarget.ES2022 },
  fileName: sourceUrl.pathname,
});
const model = await import(`data:text/javascript;base64,${Buffer.from(transpiled.outputText).toString("base64")}`);

const ordered = model.sourceFingerprint({ second: "b", first: "a" });
const reversed = model.sourceFingerprint({ first: "a", second: "b" });
assert(ordered === reversed, "Source fingerprints depend on object property order.");

const fixture = [
  row("Action.Save", "Save", " Save "),
  row("Action.Cancel", "Cancel", "Cancel"),
  row("Action.Missing", "Missing", undefined),
];
const reviews = [{
  key: "Action.Save", locale: "de", state: "approved",
  sourceFingerprint: "outdated", samples: {},
}];
const terms = [{ source: "Save", preferred: "Speichern", locale: "de" }];
const issues = model.qualityIssues(fixture, "en", "de", reviews, terms);
assert(issues.map((issue) => `${issue.key}:${issue.kind}`).join("|") ===
  "Action.Cancel:identical|Action.Missing:missing|Action.Save:stale|Action.Save:terminology|Action.Save:whitespace",
  "Quality findings were incomplete or non-deterministically ordered.");
assert(model.qualityReportCsv(issues).startsWith('"key","locale","kind","message"\n'),
  "The quality report is not a deterministic quoted CSV document.");

const heapBefore = process.memoryUsage().heapUsed;
const scaleRows = Array.from({ length: 50_000 }, (_, index) =>
  row(`Group.Message${String(index).padStart(5, "0")}`, `Source message ${index}`, `Zieltext ${index}`));
const scaleReviews = scaleRows.map((item, index) => ({
  key: item.key,
  locale: `x-${String(index % 100).padStart(3, "0")}`,
  state: "translated",
  sourceFingerprint: model.sourceFingerprint(item.cells.en.entry.value),
  samples: {},
}));
const started = performance.now();
const scaleIssues = model.qualityIssues(scaleRows, "en", "de", scaleReviews, []);
const matches = scaleRows.filter((item) => item.key.includes("Message499"));
const suggestions = model.translationSuggestions(scaleRows, "en", "de", "Group.Message49999");
const elapsed = performance.now() - started;
const heapGrowth = process.memoryUsage().heapUsed - heapBefore;
assert(scaleIssues.length === 0, "The scale fixture unexpectedly produced quality findings.");
assert(matches.length === 100, "Large-catalog search returned a non-deterministic result set.");
assert(suggestions.length <= 5, "Translation memory exceeded its bounded suggestion count.");
assert(elapsed < 10_000, `The 50,000-message quality/search pass exceeded 10 seconds (${elapsed.toFixed(0)} ms).`);
assert(heapGrowth < 256 * 1024 * 1024,
  `The 50,000-message fixture exceeded its 256 MiB heap-growth budget (${Math.ceil(heapGrowth / 1024 / 1024)} MiB).`);

console.log(`PASS: review quality is deterministic; 50,000 messages across 100 review locales completed in ${elapsed.toFixed(0)} ms with ${Math.ceil(heapGrowth / 1024 / 1024)} MiB heap growth.`);

function row(key, source, target) {
  return {
    key,
    tags: [],
    structured: false,
    cells: {
      en: { entry: { key, value: source, tags: [], structured: false } },
      de: target === undefined ? {} : { entry: { key, value: target, tags: [], structured: false } },
    },
  };
}

function assert(condition, message) {
  if (!condition) throw new Error(message);
}
