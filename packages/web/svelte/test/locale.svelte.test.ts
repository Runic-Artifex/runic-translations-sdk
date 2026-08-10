// @vitest-environment happy-dom

import { mount, tick, unmount } from "svelte";
import { describe, expect, test, vi } from "vitest";
import { createSvelteLocale, type MutableLocaleSource } from "../src/translations/index.js";
import LocaleTreeFixture from "./LocaleTreeFixture.svelte";
import type { TestLocale } from "./locale-context.js";

function source(initialLocale: TestLocale) {
  let locale = initialLocale;
  const listeners = new Set<(value: TestLocale) => void>();
  const unsubscribe = vi.fn((listener: (value: TestLocale) => void) => listeners.delete(listener));
  const value: MutableLocaleSource<TestLocale> = {
    getLocale: () => locale,
    setLocale(next) {
      if (next === locale) return;
      locale = next;
      for (const listener of listeners) listener(locale);
    },
    subscribe(listener) {
      listeners.add(listener);
      return () => { unsubscribe(listener); };
    },
  };
  return { value, listeners, unsubscribe };
}

describe("Svelte locale adapter", () => {
  test("projects source changes into generated message calls without a reload", async () => {
    const outer = source("en");
    const inner = source("de");
    const component = mount(LocaleTreeFixture, {
      target: document.body,
      props: { outer: outer.value, inner: inner.value },
    });

    expect(document.querySelector('[data-locale-consumer="outer"]')?.textContent).toBe("Hello");
    expect(document.querySelector('[data-locale-consumer="inner"]')?.textContent).toBe("Hallo");

    outer.value.setLocale("de");
    await tick();
    expect(document.querySelector('[data-locale-consumer="outer"]')?.textContent).toBe("Hallo");
    expect(document.querySelector('[data-locale-consumer="inner"]')?.textContent).toBe("Hallo");

    await unmount(component);
    expect(outer.listeners.size).toBe(0);
    expect(inner.listeners.size).toBe(0);
    expect(outer.unsubscribe).toHaveBeenCalledOnce();
    expect(inner.unsubscribe).toHaveBeenCalledOnce();
  });

  test("uses a separate async callback for navigation-owned changes", async () => {
    const host = source("en");
    const requestLocale = vi.fn(async (locale: TestLocale) => host.value.setLocale(locale));
    const state = createSvelteLocale(host.value, { requestLocale });
    await state.setLocale("de");
    expect(requestLocale).toHaveBeenCalledWith("de");
    expect(state.locale).toBe("de");
    state.dispose();
    state.dispose();
    expect(host.unsubscribe).toHaveBeenCalledOnce();
  });

  test("keeps independently created request states isolated", () => {
    const englishSource = source("en");
    const germanSource = source("de");
    const english = createSvelteLocale(englishSource.value);
    const german = createSvelteLocale(germanSource.value);

    germanSource.value.setLocale("en");
    expect(english.locale).toBe("en");
    expect(german.locale).toBe("en");
    englishSource.value.setLocale("de");
    expect(english.locale).toBe("de");
    expect(german.locale).toBe("en");

    english.dispose();
    german.dispose();
    expect(englishSource.listeners.size).toBe(0);
    expect(germanSource.listeners.size).toBe(0);
  });
});
