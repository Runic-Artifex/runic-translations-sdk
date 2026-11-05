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
