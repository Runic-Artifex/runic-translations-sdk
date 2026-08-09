<script lang="ts">
	import './layout.css';

	const { children } = $props();
</script>

<div class="contents text-foreground">
	{@render children()}
</div>
