// @vitest-environment happy-dom

import { hydrate, tick, unmount } from "svelte";
import { resolve } from "node:path";
import { describe, expect, test } from "vitest";
import { createServer } from "vite";
import type { MutableLocaleSource } from "../src/translations/index.js";
import LocaleHydrationFixture from "./LocaleHydrationFixture.svelte";

function source(initial: "en" | "de") {
  let locale = initial;
  const listeners = new Set<(locale: "en" | "de") => void>();
  const value: MutableLocaleSource<"en" | "de"> = {
    getLocale: () => locale,
    setLocale(next) {
      locale = next;
      for (const listener of listeners) listener(next);
    },
    subscribe(listener) {
      listeners.add(listener);
      return () => { listeners.delete(listener); };
    },
  };
  return { value, listeners };
}

describe("Svelte locale hydration", () => {
  test("hydrates an SSR locale projection without leaking the server subscription", async () => {
    const host = source("en");
    const vite = await createServer({
      root: resolve("."),
      configFile: resolve("vite.config.ts"),
      appType: "custom",
      server: { middlewareMode: true },
    });
    const serverModule = await vite.ssrLoadModule("/test/LocaleHydrationFixture.svelte");
    const { render } = await vite.ssrLoadModule("svelte/server");
    const ssr = render(serverModule.default, { props: { source: host.value } });
    await vite.close();

    expect(ssr.body).toContain("en");
    expect(host.listeners.size).toBe(0);

    document.body.innerHTML = ssr.body;
    const component = hydrate(LocaleHydrationFixture, {
      target: document.body,
      props: { source: host.value },
    });
    expect(host.listeners.size).toBe(1);

    host.value.setLocale("de");
    await tick();
    expect(document.querySelector("[data-locale-hydration]")?.textContent).toBe("de");

    await unmount(component);
    expect(host.listeners.size).toBe(0);
  });
});
