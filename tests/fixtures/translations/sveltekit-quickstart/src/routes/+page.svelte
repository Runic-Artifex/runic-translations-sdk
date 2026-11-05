<script lang="ts">
	import { gotoLocale } from '@runic-artifex/translations-sveltekit/navigation';
	import { m } from 'virtual:runic-translations/app';
	import { routing } from '#lib/i18n.ts';
	import { localeContext } from '#lib/locale.ts';

	const locale = localeContext.use();
</script>

<h1>{m.home_title()}</h1>
<p>{m.home_greeting({ name: 'Ada' })}</p>
<p>{m.home_total({ amount: '1234.5' })}</p>

<nav>
	{#each routing.locales as next (next)}
		<button disabled={next === locale.locale} onclick={() => gotoLocale(next, routing)}>
			{next}
		</button>
	{/each}
	<a href={routing.localizeUrl('/about', locale.locale)}>About</a>
</nav>
