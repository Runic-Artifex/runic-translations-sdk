<script lang="ts">
	import {
		createLocaleNavigation,
		synchronizeLocaleWithNavigation
	} from '@runic-artifex/translations-sveltekit/navigation';
	import { useMessageLocale } from '@runic-artifex/translations-svelte';
	import { createLocaleSource, localeDirection } from 'virtual:runic-translations/shop/runtime';
	import { routing } from '#lib/i18n.ts';
	import { localeContext } from '#lib/locale.ts';
	import type { LayoutProps } from './$types';

	let { data, children }: LayoutProps = $props();

	// The root layout owns one locale source, seeded from the server's requested tag.
	// svelte-ignore state_referenced_locally
	const source = createLocaleSource({ initialLocale: data.requestedLocale });
	synchronizeLocaleWithNavigation(source, routing);
	const locale = localeContext.provide(source, {
		requestLocale: createLocaleNavigation(routing)
	});
	// The shared locale channel: every catalog's m.*() follows this locale, @acme/widgets' included.
	useMessageLocale(locale);

	// The server renders <html lang dir> (%runic.locale%, %runic.dir%); after a switch on the client, set both.
	$effect(() => {
		document.documentElement.lang = locale.locale;
		document.documentElement.dir = localeDirection(locale.locale);
	});
</script>

{@render children()}
