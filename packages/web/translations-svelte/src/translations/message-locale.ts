import { BROWSER } from "esm-env";
import { onDestroy } from "svelte";
import { createSubscriber } from "svelte/reactivity";

/** The generated runtime's `configureLocaleResolver`: installs a resolver and returns the function that removes it. */
export type ConfigureLocaleResolver = (resolver: () => string) => () => void;

/** What {@link useMessageLocale} follows: a getter over Svelte state, or a {@link SvelteLocale}-like object. */
export type MessageLocaleInput = (() => string) | { readonly locale: string; readonly requestedLocale?: string };

// The shared locale channel of the generated runtimes (Runic Translations 0.9 or later): every catalog on the page without
// a resolver of its own reads `resolver`, the last of `resolvers` (the installed ones in installation order, each removed by
// identity). The shape is the runtimes' protocol; members are only ever added.
interface LocaleChannel {
  version: number;
  resolver: (() => string) | null;
  storage: unknown;
  resolvers: (() => string)[];
}

const localeChannelKey = Symbol.for("runic.translations.locale");

function localeChannel(): LocaleChannel {
  const host = globalThis as unknown as Record<symbol, LocaleChannel | undefined>;
  let channel = host[localeChannelKey];
  if (channel === undefined || channel === null || typeof channel !== "object")
    host[localeChannelKey] = channel = { version: 1, resolver: null, storage: null, resolvers: [] };
  if (!Array.isArray(channel.resolvers)) channel.resolvers = typeof channel.resolver === "function" ? [channel.resolver] : [];
  return channel;
}

// The text-change channel (Runic Translations 0.12 or later): in development, the Vite plugin increments `revision` and
// calls every listener once per update in which message text changed in place. The shape is a protocol between
// packages; members are only ever added, and whoever comes first creates it.
interface TextChannel {
  version: number;
  revision: number;
  listeners: Set<() => void>;
}

const textChannelKey = Symbol.for("runic.translations.text");

function textChannel(): TextChannel {
  const host = globalThis as unknown as Record<symbol, TextChannel | undefined>;
  let channel = host[textChannelKey];
  if (channel === undefined || channel === null || typeof channel !== "object")
    host[textChannelKey] = channel = { version: 1, revision: 0, listeners: new Set() };
  if (typeof channel.revision !== "number") channel.revision = 0;
  if (!(channel.listeners instanceof Set)) channel.listeners = new Set();
  return channel;
}

// One listener for the page while any template, $derived or $effect reads a message through a resolver installed here:
// reading subscribes it, so it runs again when edited text is swapped in. Nothing listens otherwise, so the plugin
// reloads a page that would not show the new text.
const followTextChanges = createSubscriber(update => {
  const listeners = textChannel().listeners;
  listeners.add(update);
  return () => { listeners.delete(update); };
});

/**
 * Makes the generated message functions (`m.*()`) of every catalog on the page, the application's and its libraries',
 * follow a locale that the application owns, such as a {@link SvelteLocale} or a ViewModel's language setting.
 *
 * `locale` must read Svelte state: a getter over `$state` or a ViewModel's reactive state, or a {@link SvelteLocale},
 * whose `requestedLocale` (such as `de-CH` for `de` text) is used so numbers and dates follow the requested region.
 * The generated functions read the locale while they format, so a template, `$derived` or `$effect` that calls `m.*()`
 * runs again when the locale changes, without keying the page on it. Each catalog resolves the tag under its own locales
 * and unsupported-locale policy, so a library that lacks the locale shows its own fallback.
 *
 * Call it while a component initialises, usually in the root layout. It installs the resolver at once, so the first
 * render already uses the locale, and removes it when the component is destroyed. Resolvers are kept in installation
 * order and removed by identity, so the latest one that is still mounted applies, whatever order components are
 * destroyed in. With one argument it sets the generated runtimes' shared locale channel, which every catalog generated
 * by Runic Translations 0.9 or later reads when it has no resolver of its own.
 *
 * During server rendering the one-argument form installs nothing: a shared resolver would be process-wide, so concurrent
 * requests would read each other's locale. The server's locale is the request's, from the generated `runWithLocale`
 * (`createLocaleHandle` in `@runic-artifex/translations-sveltekit` sets it); a server without it renders in the base
 * locale unless a call passes `{ locale }`.
 *
 * With the generated `configureLocaleResolver` as the first argument, it sets that catalog's own resolver only, as
 * before 0.9.
 *
 * In development with `@runic-artifex/vite-plugin-runic-translations` 0.12 or later, an edited message updates in place:
 * whatever reads `m.*()` through the resolver runs again with the new text, and component state is kept. It follows the
 * plugin's text-change channel (`Symbol.for("runic.translations.text")`) in the browser; production builds never write it.
 *
 * @example
 * ```svelte
 * <script lang="ts">
 *   import { m } from "virtual:runic-translations/app";
 *   import { useMessageLocale } from "@runic-artifex/translations-svelte";
 *
 *   const locale = localeContext.provide(source);
 *   useMessageLocale(locale);
 * </script>
 *
 * <h1>{m.app_title()}</h1>
 * ```
 */
export function useMessageLocale(locale: MessageLocaleInput): void;
export function useMessageLocale(configureLocaleResolver: ConfigureLocaleResolver, locale: MessageLocaleInput): void;
export function useMessageLocale(
  first: ConfigureLocaleResolver | MessageLocaleInput,
  second?: MessageLocaleInput,
): void {
  const configure = second === undefined ? undefined : first as ConfigureLocaleResolver;
  const locale = second === undefined ? first as MessageLocaleInput : second;
  if (second !== undefined && typeof configure !== "function")
    throw new TypeError("Pass the generated runtime's configureLocaleResolver.");
  if (typeof locale !== "function" && (typeof locale !== "object" || locale === null || !("locale" in locale)))
    throw new TypeError("The locale must be a function or an object with a locale property, such as a SvelteLocale.");
  // A SvelteLocale reports the requested tag (de-CH for de), so numbers and dates follow the requested region.
  const requested = typeof locale === "function" ? locale
    : () => "requestedLocale" in locale && typeof locale.requestedLocale === "string" ? locale.requestedLocale : locale.locale;
  // In the browser, reading the locale also follows text changes, so messages format again with edited text.
  const read = BROWSER ? () => { followTextChanges(); return requested(); } : requested;
  // onDestroy first: outside a component it throws before anything is installed.
  let remove: (() => void) | undefined;
  onDestroy(() => remove?.());
  if (configure !== undefined) remove = configure(read);
  else if (BROWSER) remove = configureShared(read);
}

function configureShared(resolver: () => string): () => void {
  const channel = localeChannel();
  const list = channel.resolvers;
  // A fresh entry per call, so a resolver installed twice is removed once per call.
  const entry = () => resolver();
  list.push(entry);
  channel.resolver = entry;
  let active = true;
  return () => {
    if (!active) return;
    active = false;
    const index = list.indexOf(entry);
    if (index >= 0) list.splice(index, 1);
    channel.resolver = list.length === 0 ? null : list[list.length - 1]!;
  };
}
