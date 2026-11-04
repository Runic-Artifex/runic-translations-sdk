/**
 * The editor's interface locale: one of the editor catalog's own locales. The first start negotiates it from the
 * operating system's preference list as the browser engine reports it (`navigator.languages`); the user's choice is
 * then kept in the native local state and set as the window's locale, so native surfaces follow it.
 */
export const uiLocaleStateKey = "runic-translations.ui-locale";

/** Whether `value` is one of `available`, compared without regard to case; returns the catalog's spelling. */
export function matchUiLocale(value: string | null | undefined, available: readonly string[]): string | undefined {
  if (typeof value !== "string" || value.length === 0) return undefined;
  const lower = value.toLowerCase();
  return available.find(locale => locale.toLowerCase() === lower);
}

/**
 * Picks the first preference the catalog provides, truncating each preference subtag by subtag (`de-CH-1996` →
 * `de-CH` → `de`) before moving on to the next one, and otherwise the catalog's base locale.
 *
 * TODO(tr-0.9 TB): replace the truncation with the runtime's CLDR `negotiateLocale` (likely subtags and parent
 * locales, runic-translations-sdk#131) once it is merged, so `zh-TW` reaches `zh-Hant` and `nb` reaches `no`.
 */
export function negotiateUiLocale(
  preferences: readonly string[],
  available: readonly string[],
  baseLocale: string,
): string {
  for (const preference of preferences) {
    if (typeof preference !== "string") continue;
    const subtags = preference.replaceAll("_", "-").split("-").filter(subtag => subtag.length > 0);
    for (let length = subtags.length; length > 0; length -= 1) {
      // A single-letter subtag introduces an extension or private use, which never ends a usable prefix.
      if (subtags[length - 1].length === 1) continue;
      const match = matchUiLocale(subtags.slice(0, length).join("-"), available);
      if (match !== undefined) return match;
    }
  }
  return matchUiLocale(baseLocale, available) ?? available[0] ?? baseLocale;
}

/** The operating system's preference list as the page sees it, most preferred first. */
export function preferredLanguages(): readonly string[] {
  if (typeof navigator === "undefined") return [];
  if (Array.isArray(navigator.languages) && navigator.languages.length > 0) return navigator.languages;
  return typeof navigator.language === "string" && navigator.language.length > 0 ? [navigator.language] : [];
}

/** A locale's name in its own language, as language pickers list it ("English", "Deutsch"). */
export function uiLocaleName(locale: string): string {
  try {
    return new Intl.DisplayNames([locale], { type: "language" }).of(locale) ?? locale;
  } catch {
    return locale;
  }
}
