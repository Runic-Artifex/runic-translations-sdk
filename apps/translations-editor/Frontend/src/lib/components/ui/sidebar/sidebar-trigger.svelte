<script lang="ts">
	import { getUiText } from "$lib/ui-text.js";
	const ui = getUiText();
	import PanelLeftIcon from '@lucide/svelte/icons/panel-left';
	import { Button } from "$lib/components/ui/button/index.js";
	import { cn } from "$lib/utils.js";
	import { useSidebar } from "./context.svelte.js";
	import type { ComponentProps } from "svelte";

	let {
		ref = $bindable(null),
		class: className,
		onclick,
		...restProps
	}: ComponentProps<typeof Button> & {
		onclick?: (e: MouseEvent) => void;
	} = $props();

	const sidebar = useSidebar();
</script>

<Button
	bind:ref
	data-sidebar="trigger"
	data-slot="sidebar-trigger"
	variant="ghost"
	size="icon-sm"
	class={cn("cn-sidebar-trigger", className)}
	type="button"
	onclick={(e) => {
		onclick?.(e);
		sidebar.toggle();
	}}
	{...restProps}
>
	<PanelLeftIcon  />
	<span class="sr-only">{ui.text("ui_a11y_toggle_sidebar")}</span>
</Button>
