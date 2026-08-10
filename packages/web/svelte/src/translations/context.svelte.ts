import { createContext, onDestroy } from "svelte";
import {
  createSvelteLocale,
  type LocaleSource,
  type SvelteLocale,
  type SvelteLocaleOptions,
} from "./locale.svelte.js";

export interface LocaleContext<Locale extends string> {
  provide(
    source: LocaleSource<Locale>,
    options?: SvelteLocaleOptions<Locale> & Readonly<{ dispose?: boolean }>,
  ): SvelteLocale<Locale>;
  provideState(
    state: SvelteLocale<Locale>,
    options?: Readonly<{ dispose?: boolean }>,
  ): SvelteLocale<Locale>;
  use(): SvelteLocale<Locale>;
}

/**
 * Creates an application-owned typed context. Call this once in a normal module,
 * then provide it from a layout/component and consume it below that boundary.
 */
export function createLocaleContext<Locale extends string = string>(): LocaleContext<Locale> {
  const [use, set] = createContext<SvelteLocale<Locale>>();

  function provideState(
    state: SvelteLocale<Locale>,
    options: Readonly<{ dispose?: boolean }> = {},
  ): SvelteLocale<Locale> {
    set(state);
    onDestroy(() => {
      if (options.dispose ?? true) state.dispose();
    });
    return state;
  }

  return {
    provide(source, options = {}) {
      const state = createSvelteLocale(source, options);
      return provideState(state, options);
    },
    provideState,
    use,
  };
}
