import { readFile } from "node:fs/promises";
import { performance } from "node:perf_hooks";
import ts from "typescript";

const sourceUrl = new URL("../src/lib/message-virtualization.ts", import.meta.url);
const source = (await readFile(sourceUrl, "utf8"))
  .replace(/import type \{ MessageListItem, MessageTreeNode \} from "\.\/MessageList\.svelte";\n/, "");
const transpiled = ts.transpileModule(source, {
  compilerOptions: { module: ts.ModuleKind.ESNext, target: ts.ScriptTarget.ES2022 },
  fileName: sourceUrl.pathname,
});
const virtualization = await import(`data:text/javascript;base64,${Buffer.from(transpiled.outputText).toString("base64")}`);

verifyScale("10,000", 10_000, 250);
verifyScale("50,000", 50_000, 900);

function verifyScale(name, count, buildBudget) {
  const items = Array.from({ length: count }, (_, index) => ({
    key: `Group${index % 100}.Section${index % 31}.Message${String(index).padStart(5, "0")}`,
    preview: `Message ${index}`,
    missing: false,
    structured: false,
    stale: false,
    needsReview: false,
  }));
  const started = performance.now();
  const rows = virtualization.virtualMessageTree(items);
  const buildElapsed = performance.now() - started;
  const interactionStarted = performance.now();
  let maximumWindow = 0;
  for (let step = 0; step < 100; step += 1) {
    const window = virtualization.virtualMessageWindow(rows.length, step * 9_847, 480);
    maximumWindow = Math.max(maximumWindow, window.end - window.start);
    assert(window.offset === window.start * virtualization.messageVirtualRowHeight,
      `${name} virtual window offset drifted from its row index.`);
  }
  const interactionElapsed = performance.now() - interactionStarted;
  const maximumAllowed = Math.ceil(480 / virtualization.messageVirtualRowHeight) + virtualization.messageVirtualOverscan * 2;
  assert(rows.length > count, `${name} tree flattening lost hierarchy rows.`);
  assert(maximumWindow <= maximumAllowed, `${name} rendered ${maximumWindow} rows instead of a bounded viewport window.`);
  assert(buildElapsed <= buildBudget, `${name} virtual tree build took ${buildElapsed.toFixed(1)}ms (budget ${buildBudget}ms).`);
  assert(interactionElapsed <= 30, `${name} 100 scroll-window interactions took ${interactionElapsed.toFixed(1)}ms (budget 30ms).`);
  console.log(`PASS: ${name} virtual tree built in ${buildElapsed.toFixed(1)}ms; 100 viewport windows in ${interactionElapsed.toFixed(1)}ms; at most ${maximumWindow}/${rows.length} rows rendered.`);
}

function assert(condition, message) {
  if (!condition) throw new Error(message);
}
