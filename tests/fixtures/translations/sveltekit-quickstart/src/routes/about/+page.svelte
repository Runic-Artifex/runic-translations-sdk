<script lang="ts">
	import { routing } from '#lib/i18n.ts';
	import { localeContext } from '#lib/locale.ts';
	import type { PageProps } from './$types';

	let { data }: PageProps = $props();
	const locale = localeContext.use();
</script>

<h1>{data.title}</h1>
<a href={routing.localizeUrl('/', locale.locale)}>Home</a>
