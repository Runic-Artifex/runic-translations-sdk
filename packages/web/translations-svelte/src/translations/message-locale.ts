import { onDestroy } from "svelte";

/** The generated runtime's `configureLocaleResolver`: installs a resolver and returns the function that removes it. */
export type ConfigureLocaleResolver = (resolver: () => string) => () => void;

/**
 * Makes the generated message functions (`m.*()`) follow a locale that the application owns, such as a ViewModel's
 * language setting.
 *
 * `locale` must read Svelte state: a getter over `$state` or a ViewModel's reactive state, or a {@link SvelteLocale}.
 * The generated functions read the locale through the resolver while they format, so a template, `$derived` or
 * `$effect` that calls `m.*()` runs again when the locale changes, without keying the page on it. A locale the
 * catalog does not declare goes through the catalog's unsupported-locale policy, as an explicit `locale` option
 * does.
 *
 * Call it while a component initialises, usually in the root component. It installs the resolver at once, so the
 * first render already uses the locale, and removes it when the component is destroyed (the previous resolver then
 * applies again). The resolver is module state of the generated runtime and serves every component in the page;
 * with server-side rendering, which formats several requests in one process, pass `{ locale }` to the message calls
 * or use the request-scoped `/server` context instead.
 *
 * @example
 * ```svelte
 * <script lang="ts">
 *   import { m } from "virtual:runic-translations/app";
 *   import { configureLocaleResolver } from "virtual:runic-translations/app/runtime";
 *   import { useMessageLocale } from "@runic-artifex/translations-svelte";
 *
 *   const settings = useView(() => ({ connect: connectSettings }));
 *   useMessageLocale(configureLocaleResolver, () => settings.state?.locale ?? "en");
 * </script>
 *
 * <h1>{m.app_title()}</h1>
 * ```
 */
export function useMessageLocale(
  configureLocaleResolver: ConfigureLocaleResolver,
  locale: (() => string) | { readonly locale: string },
): void {
  if (typeof configureLocaleResolver !== "function")
    throw new TypeError("Pass the generated runtime's configureLocaleResolver.");
  if (typeof locale !== "function" && (typeof locale !== "object" || locale === null || !("locale" in locale)))
    throw new TypeError("The locale must be a function or an object with a locale property, such as a SvelteLocale.");
  const read = typeof locale === "function" ? locale : () => locale.locale;
  // onDestroy first: outside a component it throws before anything is installed.
  let remove: (() => void) | undefined;
  onDestroy(() => remove?.());
  remove = configureLocaleResolver(read);
}
