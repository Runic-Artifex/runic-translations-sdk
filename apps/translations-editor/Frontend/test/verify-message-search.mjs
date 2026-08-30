import { readFile } from "node:fs/promises";
import { performance } from "node:perf_hooks";
import ts from "typescript";

const sourceUrl = new URL("../src/lib/message-search.ts", import.meta.url);
const source = (await readFile(sourceUrl, "utf8"))
  .replace(/import \{ preview, type TranslationRow \} from "\.\/resource-model";\n/, `
    const preview = (entry) => entry === undefined ? "Not translated" : typeof entry.value === "string" ? entry.value : "Structured message";
  `);
const transpiled = ts.transpileModule(source, {
  compilerOptions: { module: ts.ModuleKind.ESNext, target: ts.ScriptTarget.ES2022 },
  fileName: sourceUrl.pathname,
});
const search = await import(`data:text/javascript;base64,${Buffer.from(transpiled.outputText).toString("base64")}`);

await verifyScale("10,000", 10_000, search.indexedSearchBudgets.tenThousand);
await verifyScale("50,000", 50_000, search.indexedSearchBudgets.fiftyThousand);

async function verifyScale(name, count, budget) {
  const rows = Array.from({ length: count }, (_, index) => row(index));
  const started = performance.now();
  const index = search.createMessageSearchIndex(rows);
  const buildElapsed = performance.now() - started;
  const queryStarted = performance.now();
  const matches = index.matchingRows("locale-0");
  const firstQueryElapsed = performance.now() - queryStarted;
  const repeatedStarted = performance.now();
  for (let attempt = 0; attempt < 30; attempt += 1) index.matchingRows(`locale-${attempt % 5}`);
  const repeatedQueryElapsed = (performance.now() - repeatedStarted) / 30;
  assert(matches.size === Math.ceil(count / 5), `${name} indexed search returned an unexpected result count.`);
  assert(buildElapsed <= budget.buildMilliseconds,
    `${name} index build took ${buildElapsed.toFixed(1)}ms (budget ${budget.buildMilliseconds}ms).`);
  assert(Math.max(firstQueryElapsed, repeatedQueryElapsed) <= budget.queryMilliseconds,
    `${name} indexed query took ${Math.max(firstQueryElapsed, repeatedQueryElapsed).toFixed(1)}ms (budget ${budget.queryMilliseconds}ms).`);
  console.log(`PASS: ${name} multi-locale index ${buildElapsed.toFixed(1)}ms; first query ${firstQueryElapsed.toFixed(1)}ms; repeated query ${repeatedQueryElapsed.toFixed(1)}ms (recorded budgets ${budget.buildMilliseconds}ms/${budget.queryMilliseconds}ms).`);
}

function row(index) {
  const key = `Group.Message${String(index).padStart(5, "0")}`;
  return {
    key,
    description: `Source description ${index}`,
    tags: [`locale-${index % 5}`, `section-${index % 19}`],
    structured: false,
    cells: Object.fromEntries(["en", "de", "ar"].map((locale) => [locale, {
      entry: { key, value: `${locale} source message ${index}`, tags: [], structured: false },
    }])),
  };
}

function assert(condition, message) {
  if (!condition) throw new Error(message);
}
