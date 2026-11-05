import { getContext, hasContext, setContext } from "svelte";
import type { TextDirection } from "./direction.js";

/** What this package's components read from the nearest provided locale state. */
export interface ActiveLocale {
  readonly direction: TextDirection;
}

// The nearest locale state any application context provided, for components of this package that cannot know the
// application's typed context, such as LocalizedDocument's default page direction. Not exported from the package.
const activeLocaleKey = Symbol("runic-translations-svelte.active-locale");

export function setActiveLocale(state: ActiveLocale): void {
  setContext(activeLocaleKey, state);
}

/** The nearest provided locale state, or undefined. Call it during component initialisation. */
export function activeLocale(): ActiveLocale | undefined {
  return hasContext(activeLocaleKey) ? getContext<ActiveLocale>(activeLocaleKey) : undefined;
}
