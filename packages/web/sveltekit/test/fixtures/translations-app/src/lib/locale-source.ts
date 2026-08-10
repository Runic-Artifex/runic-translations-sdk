import type { MutableLocaleSource } from "@runic-artifex/svelte/translations";

export function createFixtureLocaleSource(initialLocale: "en" | "de"): MutableLocaleSource<"en" | "de"> {
  let locale = initialLocale;
  const listeners = new Set<(locale: "en" | "de") => void>();
  return {
    getLocale: () => locale,
    setLocale(next) {
      if (next === locale) return;
      locale = next;
      for (const listener of listeners) listener(locale);
    },
    subscribe(listener) {
      listeners.add(listener);
      return () => { listeners.delete(listener); };
    },
  };
}
