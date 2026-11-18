// Renders the editor's Svelte components with Svelte's server renderer under Bun: components and rune modules are
// compiled for the server, the generated UI catalog is an empty virtual module, and UI text shows its key and
// arguments, so the markup says which message a label uses.
import { readFile } from "node:fs/promises";
import { plugin } from "bun";
import { compile, compileModule } from "svelte/compiler";
import ts from "typescript";

globalThis.__runicTestUiText = {
  text: (key, values) => values === undefined ? `[${key}]` : `[${key} ${Object.entries(values).map(([name, value]) => `${name}=${value}`).join(" ")}]`,
};
plugin({
  name: "svelte-server-components",
  setup(build) {
    build.module("virtual:runic-translations/editor", () => ({ exports: { m: {} }, loader: "object" }));
    build.onLoad({ filter: /\/src\/lib\/ui-text\.ts$/ }, async ({ path }) => ({
      contents: (await readFile(path, "utf8")).replace("return useUiText();", "return globalThis.__runicTestUiText;"),
      loader: "ts",
    }));
    // Rune modules of the editor and of its component libraries.
    build.onLoad({ filter: /\.svelte\.(?:js|ts)$/ }, async ({ path }) => {
      const source = await readFile(path, "utf8");
      const javascript = path.endsWith(".ts") ? ts.transpileModule(source, {
        compilerOptions: { module: ts.ModuleKind.ESNext, target: ts.ScriptTarget.ES2022, verbatimModuleSyntax: false }, fileName: path,
      }).outputText : source;
      return { contents: compileModule(javascript, { filename: path, generate: "server", dev: false }).js.code, loader: "js" };
    });
    build.onLoad({ filter: /\.svelte$/ }, async ({ path }) => {
      const compiled = compile(await readFile(path, "utf8"), { filename: path, generate: "server", dev: false });
      return { contents: compiled.js.code, loader: "js" };
    });
  },
});

const { render } = await import("svelte/server");

/** The server-rendered markup of `component`, without Svelte's hydration comments. */
export const html = (component, props) => render(component, { props }).body.replace(/<!--[^>]*-->/g, "");
/** The visible text of `markup`, white space collapsed. */
export const text = (markup) => markup.replace(/<[^>]+>/g, "").replace(/\s+/g, " ").trim();
