<script lang="ts">
	import {
		createLocaleNavigation,
		synchronizeLocaleWithNavigation
	} from '@runic-artifex/translations-sveltekit/translations/navigation';
	import { createLocaleSource } from 'virtual:runic-translations/app/runtime';
	import { routing } from '#lib/i18n.ts';
	import { localeContext } from '#lib/locale.ts';
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

{@render children()}
