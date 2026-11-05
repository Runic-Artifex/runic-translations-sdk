<script lang="ts">
	import { describeStorage, storageTitle } from '@acme/widgets';
	import { gotoLocale } from '@runic-artifex/translations-sveltekit/navigation';
	import { m } from 'virtual:runic-translations/shop';
	import { routing } from '#lib/i18n.ts';
	import { localeContext } from '#lib/locale.ts';
	import type { PageProps } from './$types';

	let { data }: PageProps = $props();
	const locale = localeContext.use();
</script>

<h1>{m.title()}</h1>
<section>
	<h2>{storageTitle()}</h2>
	<p id="empty">{describeStorage({ files: 0, used: 0, total: 15 })}</p>
	<p id="usage">{data.usage}</p>
</section>
<p id="price">{m.price({ amount: '4.99' })}</p>

<nav>
	{#each routing.locales as next (next)}
		<button disabled={next === locale.locale} onclick={() => gotoLocale(next, routing)}>
			{next}
		</button>
	{/each}
</nav>
