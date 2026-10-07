# `@runic-artifex/translations-sveltekit`

SvelteKit locale routing, request handling, and browser-navigation helpers for
Runic Translations. It resolves each request's locale from the URL, a cookie,
the application, or `Accept-Language`. It rewrites localized paths onto your
normal routes and scopes generated message calls to the request during server
rendering.

Start with the
[SvelteKit quick start](https://github.com/Runic-Artifex/runic-translations-sdk/blob/main/docs/guides/translations/quickstart-sveltekit.md).
It takes about ten minutes and builds a two-locale app from an empty directory.

## Install

```bash
npm install --save-dev --save-exact \
  @runic-artifex/translations-sveltekit@<VERSION> \
  @runic-artifex/translations-svelte@<VERSION> \
  @runic-artifex/vite-plugin-runic-translations@<VERSION>
```

Use one exact release for all Runic packages and the `dotnet-runic-translations`
tool. The package requires `@sveltejs/kit` 3. Request-scoped rendering uses the
generated `/server` module, which imports `AsyncLocalStorage` from
`node:async_hooks`. Node adapters work as-is. Edge platforms such as Cloudflare
need Node.js compatibility enabled (`nodejs_compat`), and runtimes without
`AsyncLocalStorage` are not supported. See the
[ESM backend](https://github.com/Runic-Artifex/runic-translations-sdk/blob/main/docs/guides/translations/esm.md)
for which generated modules need it.

## Entry points

| Import | Use in | Exports |
| --- | --- | --- |
| `@runic-artifex/translations-sveltekit/translations` | Shared, server, and universal hooks | `createRunicLocaleRouting`, `createRunicLocaleHandle`, `createRunicLocaleReroute`, `localeFromLocals`, `parseAcceptLanguage` |
| `@runic-artifex/translations-sveltekit/translations/navigation` | Components (browser) | `gotoLocale`, `createLocaleNavigation`, `synchronizeLocaleWithNavigation` |

## Routing

```ts
// src/lib/i18n.ts
import { createRunicLocaleRouting } from '@runic-artifex/translations-sveltekit/translations';

export const routing = createRunicLocaleRouting({
	locales: ['en', 'de'] as const,
	baseLocale: 'en',
	baseLocalePath: 'unprefixed'
});
```

| Option | Default | Meaning |
| --- | --- | --- |
| `locales` | required | Supported locale tags. Matching ignores case and falls back from `de-AT` to `de`. |
| `baseLocale` | required | Locale used when no strategy resolves one. Must be in `locales`. |
| `baseLocalePath` | `'unprefixed'` | `'unprefixed'` serves the base locale at `/about`. `'prefixed'` serves it at `/en/about`. |
| `basePath` | `''` | Match SvelteKit's `paths.base` when the app is not served from `/`. |
| `resolutionOrder` | `['url', 'cookie', 'application', 'browser']` | Order of the strategies that resolve a locale. A cookie, even a stale one, wins over `applicationLocale` unless you reorder. |

The routing object also exposes `localizeUrl(url, locale)`,
`delocalizeUrl(url)`, `canonicalUrl(url, locale)`, `inspectUrl(url)`,
`resolveLocale(input)`, `matchLocale(value)`, and `isLocale(value)`. All of
them accept a path string or a `URL`. They return a path for a path, or an
absolute URL for an absolute input.

## Hooks

```ts
// src/hooks.server.ts
import { createRunicLocaleHandle } from '@runic-artifex/translations-sveltekit/translations';
import { runWithLocale } from 'virtual:runic-translations/app/server';
import { routing } from '#lib/i18n.ts';

export const handle = createRunicLocaleHandle(routing, { runWithLocale });
```

```ts
// src/hooks.ts
import { createRunicLocaleReroute } from '@runic-artifex/translations-sveltekit/translations';
import { routing } from '#lib/i18n.ts';

export const reroute = createRunicLocaleReroute(routing);
```

The handle resolves the locale, stores it in `event.locals.locale`, and
redirects non-canonical URLs, such as `/en/about` in unprefixed mode or
`/DE/about`, with `307`. It replaces `%runic.locale%` in `app.html`, so use
`<html lang="%runic.locale%">`. With the generated `runWithLocale`, message
calls during the request use the request's locale and concurrent renders stay
isolated.

`createRunicLocaleHandle` options:

| Option | Default | Meaning |
| --- | --- | --- |
| `runWithLocale` | none | Generated `/server` function that scopes message calls to the request. |
| `cookie` | `{ name: 'runic_locale', path: '/', httpOnly: true, sameSite: 'lax' }` | Cookie that is read for the `cookie` strategy, plus SvelteKit cookie options. Use `false` to ignore cookies. |
| `persistLocale` | `false` | Writes the resolved locale to the cookie on every resolved response (pages, data, errors), but not on the handle's canonical redirects. SvelteKit adds `Secure` by default. Pass `cookie: { secure: false }` for plain-HTTP hosts other than `localhost`. |
| `applicationLocale` | none | `(event) => locale`, for example from a user profile, for the `application` strategy. |
| `canonicalRedirect` | `true` | Redirects to the canonical localized URL. |
| `redirectStatus` | `307` | Status code for canonical redirects. |
| `localsKey` | `'locale'` | Property of `event.locals` that receives the locale. |
| `setLocale` | none | `(event, locale) => void` that replaces the `locals` assignment. |
| `htmlLanguageToken` | `'%runic.locale%'` | Placeholder replaced in `app.html`. Use `false` to disable. |

The reroute hook removes the locale prefix before route matching, so one
`src/routes/about` serves `/about` and `/de/about` without a `[locale]`
parameter.

## Server loads

```ts
// src/routes/+layout.server.ts
import { localeFromLocals } from '@runic-artifex/translations-sveltekit/translations';
import { routing } from '#lib/i18n.ts';
import type { LayoutServerLoad } from './$types';

export const load: LayoutServerLoad = ({ locals }) => ({
	locale: localeFromLocals(locals, routing)
});
```

`localeFromLocals(locals, routing, key?)` returns the typed locale or throws if
the handle didn't set a supported one. Declare `locale` in `App.Locals` in
`src/app.d.ts`.

## Navigation

- `gotoLocale(locale, routing, options?)` navigates to the current page in
  another locale.
- `createLocaleNavigation(routing)` returns the `requestLocale` callback for
  `@runic-artifex/translations-svelte`'s `createLocaleContext().provide`, so
  that `setLocale` navigates.
- `synchronizeLocaleWithNavigation(source, routing)` is called during root
  layout initialization and updates a mutable locale source after every
  navigation.

The [quick start](https://github.com/Runic-Artifex/runic-translations-sdk/blob/main/docs/guides/translations/quickstart-sveltekit.md)
shows the complete root layout, a language switcher, and prerendering for each
locale.

## Links

- [SvelteKit quick start](https://github.com/Runic-Artifex/runic-translations-sdk/blob/main/docs/guides/translations/quickstart-sveltekit.md)
- [`@runic-artifex/translations-svelte`](https://github.com/Runic-Artifex/runic-translations-sdk/blob/main/packages/web/translations-svelte/README.md)
- [`@runic-artifex/vite-plugin-runic-translations`](https://github.com/Runic-Artifex/runic-translations-sdk/blob/main/packages/web/vite-plugin-runic-translations/README.md)
- [ESM backend and SSR guidance](https://github.com/Runic-Artifex/runic-translations-sdk/blob/main/docs/guides/translations/esm.md)
- [Issues and support](https://github.com/Runic-Artifex/runic-translations-sdk/issues)
