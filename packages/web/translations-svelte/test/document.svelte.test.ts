// @vitest-environment happy-dom

import { flushSync, mount, unmount } from "svelte";
import { resolve } from "node:path";
import { afterAll, beforeAll, describe, expect, test } from "vitest";
import { createServer, type ViteDevServer } from "vite";
import DocumentFixture from "./DocumentFixture.svelte";
import { canonicalHtml, corpusNodes, executions, expectedHtml } from "./document-corpus.js";
import { documentProps } from "./document-props.svelte.js";
import { documentFactory, type DocumentNode } from "../src/document/index.js";
import { LocalizedInline } from "../src/inline/index.js";

let vite: ViteDevServer;
let ssr: (props: Record<string, unknown>) => string;

beforeAll(async () => {
  // Own cacheDir and no WebSocket server: Vitest runs test files in parallel, and test-started Vite servers
  // sharing node_modules/.vite force-re-optimise each other's pre-bundles mid-load (I-219).
  vite = await createServer({
    root: resolve("."),
    configFile: resolve("vite.config.ts"),
    cacheDir: resolve("node_modules/.vite/test-document"),
    appType: "custom",
    server: { middlewareMode: true, hmr: false, ws: false },
  });
  const fixture = await vite.ssrLoadModule("/test/DocumentFixture.svelte");
  const { render } = await vite.ssrLoadModule("svelte/server");
  ssr = props => render(fixture.default, { props }).body;
});
afterAll(async () => { await vite.close(); });

describe("Svelte LocalizedDocument", () => {
  // Hydration is verified in Chromium (verify-document-hydration.mjs): happy-dom cannot hydrate Svelte SSR output.
  test("SSR and client rendering produce the canonical HTML oracle for every shared corpus execution", () => {
    expect(executions.map(item => item.id).sort()).toEqual(Object.keys(expectedHtml).sort());
    for (const execution of executions) {
      let calls = 0;
      const host = document.createElement("div");
      host.innerHTML = ssr({ nodes: corpusNodes(execution.id, () => calls++) });
      expect(canonicalHtml(host), execution.id).toBe(expectedHtml[execution.id]);
      const target = document.createElement("div");
      const component = mount(DocumentFixture, { target, props: { nodes: corpusNodes(execution.id, () => calls++) } });
      flushSync();
      expect(canonicalHtml(target), `${execution.id} client`).toBe(expectedHtml[execution.id]);
      expect(calls, "rendering must not activate actions").toBe(0);
      unmount(component);
    }
  });

  test("heading levels are relative to headingBase and stay valid past h6", () => {
    const host = document.createElement("div");
    host.innerHTML = ssr({ nodes: corpusNodes("headings-integer-literals"), headingBase: 6 });
    expect(canonicalHtml(host)).toContain('<h6 class="runic-leaf" data-runic-occurrence="h[1]" lang="en">Title</h6><h6 aria-level="7" class="runic-leaf" data-runic-occurrence="h[2]" lang="en" role="heading">Section</h6>');
    // Validated eagerly: content without headings rejects an invalid base too.
    for (const headingBase of [0, 10, 1.5]) {
      expect(() => ssr({ nodes: corpusNodes("headings-integer-literals"), headingBase })).toThrow(/headingBase/);
      expect(() => ssr({ nodes: corpusNodes("files-one-de"), headingBase })).toThrow(/headingBase/);
      expect(() => ssr({ nodes: [], headingBase })).toThrow(/headingBase/);
    }
  });

  test("top-level blocks state their content direction beside lang when it differs from the page's", () => {
    const paragraph = (locale: string, text: string, occurrence = "p[1]") =>
      documentFactory.block("runic:p", {}, [documentFactory.text(text)], { occurrence, locale });
    const html = (nodes: DocumentNode[], pageDirection?: "ltr" | "rtl") => {
      const host = document.createElement("div");
      host.innerHTML = ssr(pageDirection === undefined ? { nodes } : { nodes, pageDirection });
      return canonicalHtml(host);
    };
    expect(html([paragraph("ar", "مرحبا")])).toBe('<p class="runic-leaf" data-runic-occurrence="p[1]" dir="rtl" lang="ar">مرحبا</p>');
    expect(html([paragraph("en", "Hello")])).toBe('<p class="runic-leaf" data-runic-occurrence="p[1]" lang="en">Hello</p>');
    // English fallback text in an Arabic page.
    expect(html([paragraph("en", "Hello")], "rtl")).toBe('<p class="runic-leaf" data-runic-occurrence="p[1]" dir="ltr" lang="en">Hello</p>');
    expect(html([paragraph("he", "שלום")], "rtl")).toBe('<p class="runic-leaf" data-runic-occurrence="p[1]" lang="he">שלום</p>');
    // Without pageDirection, the page has the provided locale's direction, as %runic.dir% gives it: English fallback text
    // on an Arabic page states dir="ltr", and Arabic text there needs none.
    const arabic = { getLocale: () => "ar", subscribe: () => () => {} };
    const viaRuntime = { ...arabic, getLocale: () => "en", localeDirection: (tag: string) => (tag === "en" ? "rtl" : "ltr") as "ltr" | "rtl" };
    const withLocale = (nodes: DocumentNode[], localeSource: object) => {
      const host = document.createElement("div");
      host.innerHTML = ssr({ nodes, localeSource });
      return canonicalHtml(host);
    };
    expect(withLocale([paragraph("en", "Hello"), paragraph("ar", "مرحبا", "p[2]")], arabic))
      .toBe('<p class="runic-leaf" data-runic-occurrence="p[1]" dir="ltr" lang="en">Hello</p><p class="runic-leaf" data-runic-occurrence="p[2]" lang="ar">مرحبا</p>');
    // The locale source's localeDirection (the generated runtime's) decides the page direction when it has one.
    expect(withLocale([paragraph("en", "Hello")], viaRuntime)).toBe('<p class="runic-leaf" data-runic-occurrence="p[1]" dir="ltr" lang="en">Hello</p>');
    expect(html([paragraph("en", "Hello")], "ltr")).toBe('<p class="runic-leaf" data-runic-occurrence="p[1]" lang="en">Hello</p>');
    const explicit = document.createElement("div");
    explicit.innerHTML = ssr({ nodes: [paragraph("en", "Hello")], localeSource: arabic, pageDirection: "ltr" });
    expect(canonicalHtml(explicit), "an explicit pageDirection wins").toBe('<p class="runic-leaf" data-runic-occurrence="p[1]" lang="en">Hello</p>');
    // The client renders the same attributes.
    const target = document.createElement("div");
    const component = mount(DocumentFixture, { target, props: { nodes: [paragraph("ar", "مرحبا"), paragraph("en", "Hello", "p[2]")] } });
    flushSync();
    expect(canonicalHtml(target)).toBe('<p class="runic-leaf" data-runic-occurrence="p[1]" dir="rtl" lang="ar">مرحبا</p><p class="runic-leaf" data-runic-occurrence="p[2]" lang="en">Hello</p>');
    unmount(component);
  });

  test("textDirection follows CLDR's right-to-left scripts", async () => {
    const { textDirection } = await import("../src/translations/index.js");
    const subset = (await import("../../../../specs/translations/cldr/runic-subset-48.2.json", { with: { type: "json" } })).default as { rightToLeftScripts: string[] };
    const source = (await import("node:fs")).readFileSync(resolve("src/translations/direction.ts"), "utf8");
    for (const script of subset.rightToLeftScripts) {
      expect(source).toContain(`"${script}"`);
      expect(textDirection(`und-${script}`)).toBe("rtl");
    }
    // Extension and private-use subtags are never the script, as in the runtime's localeDirection and .NET's LocaleDirection.
    for (const [tag, direction] of Object.entries({ ar: "rtl", "ar-EG": "rtl", he: "rtl", fa: "rtl", "pa-PK": "rtl", ar_EG: "rtl", "ku-Arab": "rtl", en: "ltr", "he-Latn": "ltr", "sd-Deva": "ltr", "": "ltr", "not a tag": "ltr",
      "ar-u-nu-latn": "rtl", "en-u-nu-arab": "ltr", "de-x-hebr": "ltr", "x-hebr": "ltr", "zh-yue-Hebr": "rtl", "und-Arab-u-nu-latn": "rtl", "sr-Latn-RS": "ltr" }))
      expect(textDirection(tag), tag).toBe(direction);
  });

  test("rejects blocks outside the document profile and blocks in inline content", () => {
    const table = documentFactory.block("runic:table", {}, [], { occurrence: "table[1]", locale: "en" });
    expect(() => ssr({ nodes: [table] })).toThrow(/runic:table/);
    const nested = documentFactory.block("runic:p", {}, [documentFactory.block("runic:p", {}, [], { occurrence: "p[1]/p[1]", locale: "en" })], { occurrence: "p[1]", locale: "en" });
    expect(() => ssr({ nodes: [nested] })).toThrow(/inline content/);
  });

  test("documents mark bold and italic with b and i while standalone LocalizedInline keeps span.bold and span.italic", () => {
    const host = document.createElement("div");
    host.innerHTML = ssr({ nodes: corpusNodes("inline-formatting-in-document") });
    expect(canonicalHtml(host)).toContain('<b data-runic-occurrence="p[1]/0">Bold</b>, <i data-runic-occurrence="p[1]/2">italic</i>');
    const block = corpusNodes("inline-formatting-in-document")[0]! as { children: readonly import("../src/inline/index.js").InlineNode[] };
    const target = document.createElement("div");
    const component = mount(LocalizedInline, { target, props: { nodes: block.children } });
    flushSync();
    expect(canonicalHtml(target)).toContain('<span class="bold" data-runic-occurrence="p[1]/0">Bold</span>, <span class="italic" data-runic-occurrence="p[1]/2">italic</span>');
    unmount(component);
  });

  test("a different slot at the same occurrence gets a new control and retires the previous one", () => {
    const calls: string[] = [];
    const action = (ref: string) => documentFactory.block("runic:p", {}, [documentFactory.element({ name: "runic:action", options: { ref }, occurrence: "p[1]/0", locale: "en",
      children: [documentFactory.text("Run")], binding: { kind: "runic:action", onActivate: () => calls.push(ref) } })], { occurrence: "p[1]", locale: "en" });
    const target = document.createElement("div");
    document.body.append(target);
    const props = documentProps([action("first")]);
    const component = mount(DocumentFixture, { target, props });
    flushSync();
    const first = target.querySelector("button")!;
    props.nodes = [action("second")];
    flushSync();
    const second = target.querySelector("button")!;
    expect(second).not.toBe(first);
    expect(first.disabled).toBe(true);
    second.click();
    expect(calls).toEqual(["second"]);
    unmount(component);
    target.remove();
  });

  test("replacing or clearing content retires retained actions and links", () => {
    const calls = { first: 0, second: 0 };
    const target = document.createElement("div");
    document.body.append(target);
    const props = documentProps(corpusNodes("backup-en", () => calls.first++));
    const component = mount(DocumentFixture, { target, props });
    flushSync();
    const button = target.querySelector("button")!;
    const link = target.querySelector("a")!;
    button.click();
    expect(calls).toEqual({ first: 1, second: 0 });
    // Same skeleton, new callbacks: the keyed button stays in place and calls only the new callback.
    props.nodes = corpusNodes("backup-de", () => calls.second++);
    flushSync();
    expect(target.querySelector("button")).toBe(button);
    button.click();
    expect(calls).toEqual({ first: 1, second: 1 });
    // A different skeleton removes the list: the retained action and link are detached and inert.
    props.nodes = corpusNodes("files-one-de");
    flushSync();
    expect(target.querySelector("button")).toBeNull();
    button.click();
    expect(calls).toEqual({ first: 1, second: 1 });
    expect(button.disabled).toBe(true);
    // Svelte delegates clicks to the mount root, so a detached button can never fire. Re-attach the retained button
    // inside the root and re-enable it: the retired control still must not call back.
    target.append(button);
    button.disabled = false;
    button.click();
    expect(calls).toEqual({ first: 1, second: 1 });
    button.remove();
    expect(link.hasAttribute("href")).toBe(false);
    // Clearing the content leaves nothing to activate.
    props.nodes = corpusNodes("backup-en", () => calls.first++);
    flushSync();
    const replacement = target.querySelector("button")!;
    props.nodes = [];
    flushSync();
    target.append(replacement);
    replacement.disabled = false;
    replacement.click();
    expect(calls).toEqual({ first: 1, second: 1 });
    replacement.remove();
    expect(target.textContent).toBe("");
    unmount(component);
    target.remove();
  });
});
