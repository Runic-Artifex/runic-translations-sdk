// Loads the editor's rune modules (`*.svelte.ts`) under Bun as Vite does: TypeScript is stripped, then the Svelte
// compiler turns the runes into its client runtime. The generated UI catalog is a Vite virtual module; the stores only
// create notices from it, so an empty catalog stands in.
import { readFile } from "node:fs/promises";
import { plugin } from "bun";
import { compileModule } from "svelte/compiler";
import ts from "typescript";

plugin({
  name: "svelte-rune-modules",
  setup(build) {
    build.module("virtual:runic-translations/editor", () => ({ exports: { m: {} }, loader: "object" }));
    build.onLoad({ filter: /\.svelte\.ts$/ }, async ({ path }) => {
      const source = await readFile(path, "utf8");
      const javascript = ts.transpileModule(source, {
        compilerOptions: { module: ts.ModuleKind.ESNext, target: ts.ScriptTarget.ES2022, verbatimModuleSyntax: false },
        fileName: path,
      }).outputText;
      const compiled = compileModule(javascript, { filename: path, generate: "client", dev: false });
      return { contents: compiled.js.code, loader: "js" };
    });
  },
});

/** Lets queued promise callbacks and zero-delay timers run. */
export const settle = (rounds = 5) => new Promise((resolve) => {
  const step = (left) => left === 0 ? resolve() : setTimeout(() => step(left - 1), 0);
  step(rounds);
});

/** A promise with its resolver, for host replies a test releases in a chosen order. */
export function deferred() {
  let resolve;
  let reject;
  const promise = new Promise((resolvePromise, rejectPromise) => {
    resolve = resolvePromise;
    reject = rejectPromise;
  });
  return { promise, resolve, reject };
}
