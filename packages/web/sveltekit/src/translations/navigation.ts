import { afterNavigate, goto } from "$app/navigation";
import { page } from "$app/state";
import type { RunicLocaleRouting } from "./routing.js";

type GotoOptions = Parameters<typeof goto>[1];

export interface MutableLocaleSource<Locale extends string = string> {
  getLocale(): Locale;
  setLocale(locale: Locale): void;
}

/** Call during root layout initialization to keep the component-tree source aligned with the URL. */
export function synchronizeLocaleWithNavigation<Locale extends string>(
  source: MutableLocaleSource<Locale>,
  routing: RunicLocaleRouting<Locale>,
): void {
  afterNavigate(({ to }) => {
    if (!to) return;
    const locale = routing.resolveLocale({
      url: to.url,
      browserLocales: typeof navigator === "undefined" ? [] : navigator.languages,
    }).locale;
    if (source.getLocale() !== locale) source.setLocale(locale);
  });
}

/** Creates the async change callback accepted by `createSvelteLocale`/locale context. */
export function createLocaleNavigation<Locale extends string>(
  routing: RunicLocaleRouting<Locale>,
  options: Readonly<{ goto?: typeof goto; getUrl?: () => URL }> = {},
): (locale: Locale) => Promise<void> {
  const navigate = options.goto ?? goto;
  const getUrl = options.getUrl ?? (() => page.url);
  return (locale) => navigate(routing.canonicalUrl(getUrl(), locale));
}

export function gotoLocale<Locale extends string>(
  locale: Locale,
  routing: RunicLocaleRouting<Locale>,
  options?: GotoOptions,
): Promise<void> {
  return goto(routing.canonicalUrl(page.url, locale), options);
}
