import { textDirection } from "@runic-artifex/translations-svelte";
import type { UiDirection } from "./simulation";

/**
 * The writing direction of a locale: its explicit script, else its likely script, is one of CLDR's right-to-left
 * scripts. This is the public direction of Runic Translations for the web, which agrees with the runtime's
 * `localeDirection` and .NET's `LocaleDirection`: `ar`, `fa`, `pa-PK` and `ku-Arab` are right to left; `ku`,
 * `he-Latn` and `sd-Deva` left to right.
 */
export function localeDirection(locale: string): UiDirection {
  return textDirection(locale.trim());
}

/** The name of the language `tag` in the interface locale, or the tag itself where the runtime has no name for it. */
export function localeDisplayName(tag: string, uiLocale: string): string {
  try {
    return new Intl.DisplayNames([uiLocale], { type: "language" }).of(tag) ?? tag;
  } catch {
    return tag;
  }
}
