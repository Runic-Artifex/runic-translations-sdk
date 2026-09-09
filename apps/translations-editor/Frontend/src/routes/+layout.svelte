<script lang="ts">
	import './layout.css';
	import { onMount } from 'svelte';
	import { observeDesktopAppearance } from '$lib/appearance';
	onMount(observeDesktopAppearance);

	const { children } = $props();
</script>

<div class="contents text-foreground">
	{@render children()}
</div>
