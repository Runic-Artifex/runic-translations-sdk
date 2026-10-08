// @vitest-environment happy-dom

import { flushSync, mount, unmount } from "svelte";
import { resolve } from "node:path";
import { afterAll, beforeAll, describe, expect, test } from "vitest";
import { createServer, type ViteDevServer } from "vite";
import DocumentFixture from "./DocumentFixture.svelte";
import { canonicalHtml, corpusNodes, executions, expectedHtml } from "./document-corpus.js";
import { documentProps } from "./document-props.svelte.js";
import { documentFactory } from "../src/document/index.js";

let vite: ViteDevServer;
let ssr: (props: Record<string, unknown>) => string;

beforeAll(async () => {
  vite = await createServer({ root: resolve("."), configFile: resolve("vite.config.ts"), appType: "custom", server: { middlewareMode: true } });
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
    for (const headingBase of [0, 10, 1.5])
      expect(() => ssr({ nodes: corpusNodes("headings-integer-literals"), headingBase })).toThrow(/headingBase/);
  });

  test("rejects blocks outside the document profile and blocks in inline content", () => {
    const table = documentFactory.block("runic:table", {}, [], { occurrence: "table[1]", locale: "en" });
    expect(() => ssr({ nodes: [table] })).toThrow(/runic:table/);
    const nested = documentFactory.block("runic:p", {}, [documentFactory.block("runic:p", {}, [], { occurrence: "p[1]/p[1]", locale: "en" })], { occurrence: "p[1]", locale: "en" });
    expect(() => ssr({ nodes: [nested] })).toThrow(/inline content/);
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
    expect(link.hasAttribute("href")).toBe(false);
    // Clearing the content leaves nothing to activate.
    props.nodes = corpusNodes("backup-en", () => calls.first++);
    flushSync();
    const replacement = target.querySelector("button")!;
    props.nodes = [];
    flushSync();
    replacement.click();
    expect(calls).toEqual({ first: 1, second: 1 });
    expect(target.textContent).toBe("");
    unmount(component);
    target.remove();
  });
});
