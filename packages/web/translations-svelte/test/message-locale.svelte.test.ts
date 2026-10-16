// @vitest-environment happy-dom

import { flushSync, mount, unmount } from "svelte";
import { describe, expect, test } from "vitest";
import { createSvelteLocale, useMessageLocale, type MutableLocaleSource } from "../src/translations/index.js";
import MessageLocaleFixture from "./MessageLocaleFixture.svelte";
import { configureLocaleResolver, getLocale } from "./message-runtime.js";

describe("useMessageLocale", () => {
  test("re-renders message calls when the application's locale changes, without keying the page", async () => {
    const state = $state({ locale: "en", count: 2 });
    const component = mount(MessageLocaleFixture, { target: document.body, props: { state } });
    flushSync();
    const heading = document.querySelector("h1")!;
    expect(heading.textContent).toBe("Title");
    expect(document.querySelector("p")?.textContent).toBe("2 entries");

    state.locale = "de";
    flushSync();
    // The same element: the page was updated, not mounted again.
    expect(document.querySelector("h1")).toBe(heading);
    expect(heading.textContent).toBe("Titel");
    expect(document.querySelector("p")?.textContent).toBe("2 Einträge");

    // A locale the catalog does not declare goes through its unsupported-locale policy.
    state.locale = "fr";
    flushSync();
    expect(heading.textContent).toBe("Title");

    await unmount(component);
  });

  test("installs the resolver for the first render and removes it on destroy", async () => {
    const restore = configureLocaleResolver(() => "en");
    try {
      const state = $state({ locale: "de", count: 1 });
      const component = mount(MessageLocaleFixture, { target: document.body, props: { state } });
      flushSync();
      expect(document.querySelector("h1")?.textContent).toBe("Titel");
      expect(getLocale()).toBe("de");
      await unmount(component);
      expect(getLocale()).toBe("en");
    } finally {
      restore();
    }
  });

  test("follows a SvelteLocale", async () => {
    let current = "de";
    const listeners = new Set<(locale: string) => void>();
    const source: MutableLocaleSource<string> = {
      getLocale: () => current,
      setLocale(locale) { current = locale; for (const listener of listeners) listener(locale); },
      subscribe(listener) { listeners.add(listener); return () => listeners.delete(listener); },
    };
    const locale = createSvelteLocale(source);
    const component = mount(MessageLocaleFixture, { target: document.body, props: { state: { locale: "en", count: 1 }, locale } });
    flushSync();
    expect(document.querySelector("h1")?.textContent).toBe("Titel");
    source.setLocale("en");
    flushSync();
    expect(document.querySelector("h1")?.textContent).toBe("Title");
    await unmount(component);
    locale.dispose();
  });

  test("rejects other values, and installs nothing outside a component", () => {
    expect(() => useMessageLocale(undefined as never, () => "en")).toThrow(TypeError);
    expect(() => useMessageLocale(configureLocaleResolver, "en" as never)).toThrow(TypeError);
    let installed = false;
    expect(() => useMessageLocale(() => { installed = true; return () => {}; }, () => "de")).toThrow();
    expect(installed).toBe(false);
    expect(getLocale()).toBe("en");
  });
});
