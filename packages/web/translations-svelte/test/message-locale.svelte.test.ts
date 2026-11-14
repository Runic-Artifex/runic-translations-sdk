// @vitest-environment happy-dom

import { flushSync, mount, unmount } from "svelte";
import { describe, expect, test } from "vitest";
import { createSvelteLocale, useMessageLocale, type MutableLocaleSource } from "../src/translations/index.js";
import MessageLocaleFixture from "./MessageLocaleFixture.svelte";
import SharedLocaleFixture from "./SharedLocaleFixture.svelte";
import { configureLocaleResolver, getLocale, library, texts } from "./message-runtime.js";

// The development text-change channel, as @runic-artifex/vite-plugin-runic-translations writes it after an edited
// message's table was swapped in.
function textChannel(): { version: number; revision: number; listeners: Set<() => void> } {
  return (globalThis as Record<symbol, ReturnType<typeof textChannel>>)[Symbol.for("runic.translations.text")]!;
}
function textChanged(): void {
  const channel = textChannel();
  channel.revision++;
  for (const listener of channel.listeners) listener();
}

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

  test("with the locale alone, sets the shared locale channel that every catalog reads", async () => {
    const channel = () => (globalThis as Record<symbol, { version: number; resolver: unknown } | undefined>)[Symbol.for("runic.translations.locale")];
    const state = $state({ locale: "de" });
    const component = mount(SharedLocaleFixture, { target: document.body, props: { state } });
    flushSync();
    expect(document.querySelector("p")?.textContent).toBe("Keine Zeilen");
    expect(channel()?.version).toBe(1);
    state.locale = "en";
    flushSync();
    expect(document.querySelector("p")?.textContent).toBe("No rows");

    // A nested call wins while it is mounted; the outer one applies again after it is destroyed.
    const inner = $state({ locale: "de" });
    const nested = mount(SharedLocaleFixture, { target: document.body, props: { state: inner } });
    flushSync();
    expect(library.empty()).toBe("Keine Zeilen");
    await unmount(nested);
    expect(library.empty()).toBe("No rows");

    await unmount(component);
    expect(channel()?.resolver).toBe(null);
  });

  test("removes shared resolvers by identity, in any order", async () => {
    const channel = () => (globalThis as Record<symbol, { resolver: unknown; resolvers: unknown[] } | undefined>)[Symbol.for("runic.translations.locale")];
    // A layout swapped with an outro transition: the new one mounts before the old one is destroyed.
    const first = mount(SharedLocaleFixture, { target: document.body, props: { state: { locale: "de" } } });
    const second = mount(SharedLocaleFixture, { target: document.body, props: { state: { locale: "en-GB" } } });
    flushSync();
    expect(library.requested()).toBe("en-GB");
    await unmount(first);
    expect(library.requested()).toBe("en-GB");
    await unmount(second);
    // Both are gone, so no destroyed component's getter stays installed.
    expect(channel()?.resolver).toBe(null);
    expect(channel()?.resolvers).toEqual([]);

    // Install A, B, C; remove B, then C (A applies again), then A.
    const a = mount(SharedLocaleFixture, { target: document.body, props: { state: { locale: "a" } } });
    const b = mount(SharedLocaleFixture, { target: document.body, props: { state: { locale: "b" } } });
    const c = mount(SharedLocaleFixture, { target: document.body, props: { state: { locale: "c" } } });
    flushSync();
    await unmount(b);
    expect(library.requested()).toBe("c");
    await unmount(c);
    expect(library.requested()).toBe("a");
    await unmount(a);
    expect(channel()?.resolver).toBe(null);
  });

  test("with a SvelteLocale alone, the shared channel gets the requested tag", async () => {
    let current = "de";
    const listeners = new Set<(locale: string) => void>();
    const source: MutableLocaleSource<string> = {
      getLocale: () => current,
      getRequestedLocale: () => (current === "de" ? "de-CH" : current),
      setLocale(locale) { current = locale; for (const listener of listeners) listener(locale); },
      subscribe(listener) { listeners.add(listener); return () => listeners.delete(listener); },
    };
    const locale = createSvelteLocale(source);
    const component = mount(SharedLocaleFixture, { target: document.body, props: { state: { locale: "en" }, locale } });
    flushSync();
    expect(document.querySelector("span")?.textContent).toBe("de-CH");
    source.setLocale("en");
    flushSync();
    expect(document.querySelector("span")?.textContent).toBe("en");
    await unmount(component);
    locale.dispose();
  });

  test("formats messages again, keeping the page, when the dev server swaps edited text in", async () => {
    const state = $state({ locale: "en", count: 2 });
    const component = mount(MessageLocaleFixture, { target: document.body, props: { state } });
    flushSync();
    const heading = document.querySelector("h1")!;
    expect(textChannel().version).toBe(1);
    expect(textChannel().listeners.size).toBe(1);
    try {
      texts["title.en"] = "Edited title";
      flushSync();
      expect(heading.textContent).toBe("Title");
      textChanged();
      flushSync();
      expect(document.querySelector("h1")).toBe(heading);
      expect(heading.textContent).toBe("Edited title");
      // The locale still switches afterwards.
      state.locale = "de";
      flushSync();
      expect(heading.textContent).toBe("Titel");
    } finally {
      texts["title.en"] = "Title";
    }
    await unmount(component);
    expect(textChannel().listeners.size).toBe(0);
  });

  test("with the locale alone, follows text changes too, with one listener for the page", async () => {
    const first = mount(SharedLocaleFixture, { target: document.body, props: { state: { locale: "de" } } });
    const second = mount(SharedLocaleFixture, { target: document.body, props: { state: { locale: "de" } } });
    flushSync();
    expect(textChannel().listeners.size).toBe(1);
    const paragraphs = [...document.querySelectorAll("p")];
    try {
      texts["empty.de"] = "Nichts da";
      textChanged();
      flushSync();
      expect(paragraphs.map(paragraph => paragraph.textContent)).toEqual(["Nichts da", "Nichts da"]);
      expect([...document.querySelectorAll("p")]).toEqual(paragraphs);
    } finally {
      texts["empty.de"] = "Keine Zeilen";
    }
    await unmount(second);
    await unmount(first);
    flushSync();
    expect(textChannel().listeners.size).toBe(0);
  });

  test("replaces a channel value that is not an object", async () => {
    const key = Symbol.for("runic.translations.locale");
    const host = globalThis as Record<symbol, unknown>;
    const saved = host[key];
    host[key] = 42;
    try {
      const component = mount(SharedLocaleFixture, { target: document.body, props: { state: { locale: "de" } } });
      flushSync();
      expect(library.empty()).toBe("Keine Zeilen");
      await unmount(component);
    } finally {
      host[key] = saved;
    }
  });
});
