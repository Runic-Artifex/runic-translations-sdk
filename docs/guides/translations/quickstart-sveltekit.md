# SvelteKit quick start

This workflow adds localized routes and request-scoped server rendering to a
SvelteKit 3 application. The Vite plugin compiles your RMF2 sources, and the
SvelteKit adapter resolves each request's locale from its URL. The workflow
requires the .NET 10 SDK and Node 24.18 or later in the Node 24 series.
Use one exact published Translations version for the tool and all three npm
packages; replace `<VERSION>` below with that release. No SDK source checkout is
needed.

By the end, `/` renders English, `/de` renders German, and a language switcher
moves between them on the same page.

## 1. Create the app and install the tools

```bash
npx sv create my-app --template minimal --types ts \
  --add sveltekit-adapter="adapter:node" --install npm
cd my-app
dotnet new tool-manifest --output .config
dotnet tool install dotnet-runic-translations --version <VERSION>
npm install --save-dev --save-exact \
  @runic-artifex/vite-plugin-runic-translations@<VERSION> \
  @runic-artifex/translations-sveltekit@<VERSION> \
  @runic-artifex/translations-svelte@<VERSION>
```

This guide uses `adapter-node` so you can run the production server locally.
Other adapters work if the server runtime provides `node:async_hooks`. The
generated server module uses its `AsyncLocalStorage` to scope each request's
locale. Edge platforms such as Cloudflare need Node.js compatibility enabled
(`nodejs_compat`), and runtimes without `AsyncLocalStorage` are not supported.
Commit `.config/dotnet-tools.json` and the npm
lockfile. A clean checkout then restores the same compiler with
`dotnet tool restore`.

## 2. Add translations

Create `translations/runic.json`:

```json
{
  "schemaVersion": 1,
  "catalog": "app",
  "code": { "namespace": "Example", "className": "AppText" },
  "baseLocale": "en",
  "locales": ["en", "de"]
}
```

Create `translations/en.rmf2`:

```rmf2
home {
  title = Welcome to Runic
  greeting = Hello, {$name}!
}
```

Create `translations/de.rmf2`:

```rmf2
home {
  title = Willkommen bei Runic
  greeting = Hallo, {$name}!
}
```

Add `.runic/` to `.gitignore`. See the [RMF2 guide](rmf2.md) for the full
authoring syntax.

## 3. Configure Vite and TypeScript

Add the Runic plugin before `sveltekit()` in `vite.config.ts`:

```ts
import adapter from '@sveltejs/adapter-node';
import { runicTranslations } from '@runic-artifex/vite-plugin-runic-translations';
import { sveltekit } from '@sveltejs/kit/vite';
import { defineConfig } from 'vite';

export default defineConfig({
	plugins: [
		runicTranslations(),
		sveltekit({
			adapter: adapter()
		})
	]
});
```

Keep the `compilerOptions` block that `sv create` generated. It is omitted here
for brevity.

The plugin writes typed declarations for the `virtual:runic-translations/app`
modules to `.runic/translations/virtual.d.ts`. Add that file to the `include`
list in `tsconfig.json`:

```json
"include": ["src", "vite.config.ts", ".runic/translations/virtual.d.ts"]
```

The declarations exist only after Vite has run once. On a clean checkout, run
`npm run build` or `npm run dev` before `npm run check`.

## 4. Define locale routing

Create `src/lib/i18n.ts`. Use the same locales as `runic.json`:

```ts
import { createRunicLocaleRouting } from '@runic-artifex/translations-sveltekit/translations';

export const routing = createRunicLocaleRouting({
	locales: ['en', 'de'] as const,
	baseLocale: 'en',
	baseLocalePath: 'unprefixed'
});

export type Locale = (typeof routing.locales)[number];
```

`baseLocalePath` selects the URL shape:

| Setting | English URLs | German URLs | Bare `/about` |
| --- | --- | --- | --- |
| `'unprefixed'` (default) | `/`, `/about` | `/de`, `/de/about` | Renders English. `/en/about` redirects to `/about`. |
| `'prefixed'` | `/en`, `/en/about` | `/de`, `/de/about` | Redirects to the visitor's locale. |

With `'prefixed'`, an unprefixed request is resolved in this order:

1. The `runic_locale` cookie.
2. The application locale, if you pass `applicationLocale` to the handle.
3. `Accept-Language`.
4. `baseLocale`.

The handle then redirects with `307`. A cookie wins over the application
locale, even a stale one. If a user's profile locale should take precedence,
update or clear the cookie when the profile changes, or change
`resolutionOrder`. Mixed-case prefixes such as `/DE/about` redirect to the
canonical `/de/about` in both modes. If the app sets `paths.base`, pass the same
value as `basePath`.

## 5. Add the hooks

Create `src/hooks.server.ts`:

```ts
import { createRunicLocaleHandle } from '@runic-artifex/translations-sveltekit/translations';
import { runWithLocale } from 'virtual:runic-translations/app/server';
import { routing } from '#lib/i18n.ts';

export const handle = createRunicLocaleHandle(routing, { runWithLocale });
```

The handle resolves the locale and stores it in `event.locals.locale`. It
redirects non-canonical URLs and replaces `%runic.locale%` in `app.html`.
Passing the generated `runWithLocale` gives each request its own locale for
message calls in the rest of the request.

Create `src/hooks.ts`:

```ts
import { createRunicLocaleReroute } from '@runic-artifex/translations-sveltekit/translations';
import { routing } from '#lib/i18n.ts';

export const reroute = createRunicLocaleReroute(routing);
```

The reroute hook removes the locale prefix before route matching, so
`/de/about` uses the same `src/routes/about` files as `/about`. Do not create
`[locale]` route directories.

Type the local in `src/app.d.ts`:

```ts
import type { Locale } from '#lib/i18n.ts';

declare global {
	namespace App {
		interface Locals {
			locale: Locale;
		}
	}
}

export {};
```

In `src/app.html`, change `<html lang="en">` to:

```html
<html lang="%runic.locale%">
```

## 6. Provide the locale to components

Create `src/lib/locale.ts`:

```ts
import { createLocaleContext } from '@runic-artifex/translations-svelte/translations';
import type { Locale } from '#lib/i18n.ts';

export const localeContext = createLocaleContext<Locale>();
```

Read the request locale in `src/routes/+layout.server.ts`:

```ts
import { localeFromLocals } from '@runic-artifex/translations-sveltekit/translations';
import { routing } from '#lib/i18n.ts';
import type { LayoutServerLoad } from './$types';

export const load: LayoutServerLoad = ({ locals }) => ({
	locale: localeFromLocals(locals, routing)
});
```

`localeFromLocals` throws if the handle did not run or stored an unsupported
value, so a missing hook fails loudly instead of rendering the base locale.

Replace `src/routes/+layout.svelte`:

```svelte
<script lang="ts">
	import {
		createLocaleNavigation,
		synchronizeLocaleWithNavigation
	} from '@runic-artifex/translations-sveltekit/translations/navigation';
	import { createLocaleSource } from 'virtual:runic-translations/app/runtime';
	import { routing } from '#lib/i18n.ts';
	import { localeContext } from '#lib/locale.ts';
	import favicon from '#lib/assets/favicon.svg';
	import type { LayoutProps } from './$types';

	let { data, children }: LayoutProps = $props();

	// The root layout owns one locale source, seeded from the server's locale.
	// svelte-ignore state_referenced_locally
	const source = createLocaleSource({ initialLocale: data.locale });
	synchronizeLocaleWithNavigation(source, routing);
	const locale = localeContext.provide(source, {
		requestLocale: createLocaleNavigation(routing)
	});

	$effect(() => {
		document.documentElement.lang = locale.locale;
	});
</script>

<svelte:head>
	<link rel="icon" href={favicon} />
</svelte:head>

{@render children()}
```

The favicon lines come from the `sv create` template. The generated runtime
types the source with the catalog's locales (`"de" | "en"`), so it fits the
routing's `Locale` without a cast. `synchronizeLocaleWithNavigation` updates
the source after every client-side navigation. `requestLocale` makes `locale.setLocale('de')`
navigate to the German URL instead of changing state in place. The `$effect`
keeps `<html lang>` correct after client-side navigation; the handle sets it
for server-rendered responses.

## 7. Render messages and switch locales

Replace `src/routes/+page.svelte`:

```svelte
<script lang="ts">
	import { gotoLocale } from '@runic-artifex/translations-sveltekit/translations/navigation';
	import { m } from 'virtual:runic-translations/app';
	import { routing } from '#lib/i18n.ts';
	import { localeContext } from '#lib/locale.ts';

	const locale = localeContext.use();
</script>

<h1>{m.home_title(locale.messageOptions)}</h1>
<p>{m.home_greeting({ name: 'Ada' }, locale.messageOptions)}</p>

<nav>
	{#each routing.locales as next (next)}
		<button disabled={next === locale.locale} onclick={() => gotoLocale(next, routing)}>
			{next}
		</button>
	{/each}
	<a href={routing.localizeUrl('/about', locale.locale)}>About</a>
</nav>
```

`gotoLocale` navigates to the current page in the requested locale, for example
from `/about` to `/de/about`. Components pass `locale.messageOptions` so that
the same call renders correctly on the server and in the browser.
`routing.localizeUrl` builds links that stay in the current locale.

Server-only code doesn't need explicit options. The handle wraps every request
in `runWithLocale`, so a plain call in a server `load` uses that request's
locale. Create `src/routes/about/+page.server.ts`:

```ts
import { m } from 'virtual:runic-translations/app';
import type { PageServerLoad } from './$types';

export const load: PageServerLoad = () => ({ title: m.home_title() });
```

And `src/routes/about/+page.svelte`:

```svelte
<script lang="ts">
	import { routing } from '#lib/i18n.ts';
	import { localeContext } from '#lib/locale.ts';
	import type { PageProps } from './$types';

	let { data }: PageProps = $props();
	const locale = localeContext.use();
</script>

<h1>{data.title}</h1>
<a href={routing.localizeUrl('/', locale.locale)}>Home</a>
```

The request context is isolated per request, so concurrent `/about` and
`/de/about` renders never see each other's locale. Do not store the locale in
module-level variables on the server. Read it from `locals` with
`localeFromLocals`, or let `runWithLocale` supply it.

## 8. Run and build

```bash
npm run dev
```

Open `/`, `/de`, and `/de/about`, then use the buttons to switch languages.
Saving an `.rmf2` file recompiles it while the dev server keeps running, and
text rendered by components updates in place through Vite HMR. Text computed in a
server `load` function, like the `/about` title, follows SvelteKit's rule for
server modules: reload the page to run the load again.

```bash
npm run build
node build
```

`npm run build` compiles the translations before bundling, and `node build`
serves the production SSR app on port 3000.

### Remember the visitor's choice

The handle always reads the `runic_locale` cookie, but it only writes it if you
enable `persistLocale`:

```ts
export const handle = createRunicLocaleHandle(routing, { runWithLocale, persistLocale: true });
```

The handle then sets `runic_locale` on every response it resolves, including
pages, `__data.json` requests, and error pages. Its own canonical redirects do
not set the cookie. SvelteKit serializes the cookie as
`HttpOnly; Secure; SameSite=Lax` because it adds `Secure` by default. Browsers
accept `Secure` cookies on `http://localhost`, but they drop them on other
plain-HTTP hosts. For those hosts, pass `cookie: { secure: false }`:

```ts
export const handle = createRunicLocaleHandle(routing, {
	runWithLocale,
	persistLocale: true,
	cookie: { secure: false }
});
```

With a `'prefixed'` base locale, a later visit to `/` redirects to the
remembered locale. Use the `cookie` option to rename the cookie or change its attributes,
and use `applicationLocale` to supply a locale from a user profile.

### Prerendering

Prerendering runs the same handle at build time. A prerendered page has no
request cookies or `Accept-Language`, so its locale comes only from its URL.
The crawler follows `<a>` links, but language-switch buttons are not links, so
list one entry per locale in `vite.config.ts`:

```ts
sveltekit({
	adapter: adapter(),
	prerender: { entries: ['*', '/de'] }
})
```

Then add `export const prerender = true;` to `src/routes/+layout.server.ts`.
With `'prefixed'`, list `/en` and `/de` instead of `*`, because `/` is a
redirect that depends on the request. Static hosting (`adapter-static`) works
the same way. Only the URL selects the locale there, so prefer `'unprefixed'`,
or serve `/` with a redirect configured on the host.

## 9. Validate in CI

```bash
npm ci
dotnet tool restore
dotnet tool run runic-translations -- validate --project translations
npm run build
npm run check
```

Commit `package.json`, `package-lock.json`, `.config/dotnet-tools.json`, the
Vite and TypeScript configuration, `src/`, and `translations/`. `npm run build`
generates `.runic/` before `npm run check` reads its declarations.

## Next steps

- [Vite quick start](quickstart-vite.md): retain generated output and verify it
  when another build owns the compiler.
- [`@runic-artifex/translations-sveltekit`](https://github.com/Runic-Artifex/runic-translations-sdk/blob/main/packages/web/translations-sveltekit/README.md):
  every routing, handle, and navigation option.
- [ESM backend](esm.md): the generated `/runtime` and `/server` entry points.
- [RMF2 guide](rmf2.md): plurals, selectors, and inline markup.
