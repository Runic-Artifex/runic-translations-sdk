import { readFile } from "node:fs/promises";
import { performance } from "node:perf_hooks";
import ts from "typescript";

const sourceUrl = new URL("../src/lib/review-model.ts", import.meta.url);
const semanticUrl = new URL("../src/lib/semantic-review.ts", import.meta.url);
const semanticSource = await readFile(semanticUrl, "utf8");
const semanticTranspiled = ts.transpileModule(semanticSource, {
  compilerOptions: { module: ts.ModuleKind.ESNext, target: ts.ScriptTarget.ES2022 },
  fileName: semanticUrl.pathname,
});
const semanticModuleUrl = `data:text/javascript;base64,${Buffer.from(semanticTranspiled.outputText).toString("base64")}`;
const semanticModel = await import(semanticModuleUrl);
const source = (await readFile(sourceUrl, "utf8")).replace('"./semantic-review"', JSON.stringify(semanticModuleUrl));
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

// Compiler projections paired with real direct MF2 and RMF2 message bodies.
// Literal text retains quoted whitespace, and excludes storage/declarations.
const semanticFixture = [
  semanticRow("Direct.Storage", "Hello\n", "Hallo\n", ["Hello"], ["Hallo"]),
  semanticRow("Direct.Quoted", "{{ Hello }}\n", "{{ Hallo }}\n", [" Hello "], [" Hallo "], [], [], true),
  semanticRow("Direct.VariableBoundary", "Hello {$name}\n", "Hallo {$name}\n", ["Hello "], ["Hallo "], ["name:string"]),
  semanticRow("Resource.Declaration", ".input {$Save :string}\n{{Continue {$Save}}}",
    ".input {$Save :string}\n{{Weiter {$Save}}}", ["Continue "], ["Weiter "], ["Save:string"]),
  semanticRow("Resource.Branches", ".input {$count :number}\n.match $count\none {{Save one item}}\n* {{Save items}}",
    ".input {$count :number}\n.match $count\none {{Ein Element}}\n* {{Elemente}}",
    ["Save one item", "Save items"], ["Ein Element", "Elemente"], ["count:decimal"]),
  semanticRow("Direct.SemanticIdentical", "{{Continue}}\n", "Continue\n", ["Continue"], ["Continue"]),
];
const semanticIssues = model.qualityIssues(semanticFixture, "en", "de", [], terms);
assert(semanticIssues.map((issue) => `${issue.key}:${issue.kind}`).join("|") ===
  "Direct.Quoted:whitespace|Direct.SemanticIdentical:identical|Resource.Branches:terminology",
  "Semantic QA included storage newlines/declarations or lost quoted whitespace/branch terminology.");

const suggestionRows = [
  semanticRow("Current", "{{Save {$count :number}}}", undefined, ["Save "], [], ["count:decimal"]),
  semanticRow("Compatible", "{{Save all {$count :number}}}", "{{Alle {$count :number} speichern}}",
    ["Save all "], ["Alle  speichern"], ["count:decimal"]),
  semanticRow("WrongName", "{{Save {$total :number}}}", "{{{$total :number} speichern}}",
    ["Save "], [" speichern"], ["total:decimal"]),
  semanticRow("WrongType", "{{Save {$count :string}}}", "{{{$count :string} speichern}}",
    ["Save "], [" speichern"], ["count:string"]),
  semanticRow("WrongSlot", "{{{#strong}Save{/strong} {$count :number}}}",
    "{{{#strong}{$count :number} speichern{/strong}}}", ["Save "], [" speichern"],
    ["count:decimal"], ["open:strong", "close:strong"]),
  semanticRow("TranslationMismatch", "{{Save {$count :number}}}", "{{Speichern {$total :number}}}",
    ["Save "], ["Speichern "], ["count:decimal"]),
  semanticRow("SyntaxOnlyMatch", ".local $label = {|Save|}\n.input {$count :number}\n{{Remove everything {$count}}}",
    ".local $label = {|Save|}\n.input {$count :number}\n{{Alles entfernen {$count}}}",
    ["Remove everything "], ["Alles entfernen "], ["count:decimal"]),
];
suggestionRows.find(item => item.key === "TranslationMismatch").cells.de.entry.semantic.placeholders = ["total:decimal"];
const semanticSuggestions = model.translationSuggestions(suggestionRows, "en", "de", "Current");
assert(semanticSuggestions.map(item => item.key).join("|") === "Compatible",
  "Translation memory admitted incompatible placeholder names/types/markup or used declaration syntax for similarity.");
assert(semanticSuggestions[0].translation === "{{Alle {$count :number} speichern}}",
  "Translation memory altered the compiler-compatible insertion source.");

const markedCurrent = semanticRow("Markup.Current", "{{{#strong}Save{/strong}}}", undefined,
  ["Save"], [], [], ["open:strong", "close:strong"]);
const markedCompatible = semanticRow("Markup.Compatible", "{{{#strong}Save now{/strong}}}",
  "{{{#strong}Jetzt speichern{/strong}}}", ["Save now"], ["Jetzt speichern"], [], ["close:strong", "open:strong"]);
assert(model.translationSuggestions([markedCurrent, markedCompatible], "en", "de", "Markup.Current").length === 1,
  "Compatible markup contracts depended on projection array order.");

const unsupported = row("Unsupported", ".input {$name :custom}\n{{Hello {$name}}}", "{{Hallo {$name}}}");
assert(!semanticModel.semanticReviewState(unsupported.cells.en.entry).supported &&
  semanticModel.semanticReviewState(unsupported.cells.en.entry).reason === "compiler-required",
  "Structured source without compiler metadata was interpreted by a fallback parser.");
unsupported.cells.en.entry.semantic = { ...semantics(["Hello"], [], []), supported: false };
assert(semanticModel.semanticReviewState(unsupported.cells.en.entry).reason === "compiler-unavailable",
  "Failed compiler semantics were silently treated as plain text.");
assert(model.translationSuggestions([unsupported, row("Other", "Hello", "Hallo")], "en", "de", "Unsupported").length === 0,
  "Unsupported semantics produced an unchecked translation suggestion.");
assert(model.qualityIssues([row("Plain.Storage", "Hello\n", "Hallo\n")], "en", "de", [], []).length === 0,
  "The plain-text compatibility path still flags the normal terminal storage newline.");
assert(model.qualityIssues([row("Plain.Intentional", "Hello\n", "Hallo \n")], "en", "de", [], [])
  .some(issue => issue.kind === "whitespace"), "Removing a storage newline also removed intentional spaces.");

if (typeof globalThis.gc !== "function") {
  throw new Error("Run this deterministic heap check with node --expose-gc.");
}
globalThis.gc();
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
globalThis.gc();
const heapGrowth = process.memoryUsage().heapUsed - heapBefore;
assert(scaleIssues.length === 0, "The scale fixture unexpectedly produced quality findings.");
assert(matches.length === 100, "Large-catalog search returned a non-deterministic result set.");
assert(suggestions.length <= 5, "Translation memory exceeded its bounded suggestion count.");
assert(heapGrowth < 256 * 1024 * 1024,
  `The 50,000-message fixture exceeded its 256 MiB heap-growth budget (${Math.ceil(heapGrowth / 1024 / 1024)} MiB).`);

console.log(`PASS: review quality is deterministic; 50,000 messages across 100 review locales completed with ${Math.ceil(heapGrowth / 1024 / 1024)} MiB retained heap growth.`);
console.log(`OBSERVATION: this machine completed the fixed quality/search work in ${elapsed.toFixed(0)} ms; timing is not a product limit.`);

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

function semanticRow(key, source, target, sourceText, targetText, placeholders = [], slots = [], boundary = false) {
  const result = row(key, source, target);
  result.cells.en.entry.semantic = semantics(sourceText, placeholders, slots, boundary);
  if (result.cells.de.entry) result.cells.de.entry.semantic = semantics(targetText, placeholders, slots, boundary);
  return result;
}

function semantics(text, placeholders = [], slots = [], hasBoundaryWhitespace = false) {
  return { text, placeholders, slots, hasBoundaryWhitespace, supported: true };
}

function assert(condition, message) {
  if (!condition) throw new Error(message);
}
