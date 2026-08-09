<script lang="ts">
	import { onMount, type Snippet } from "svelte";

	const storageKey = "runic.sidebar.languages-share";
	const defaultShare = 0.5;
	const minimumShare = 0.2;
	const keyboardStep = 0.05;

	let {
		languages,
		messages,
		languagesOpen = $bindable(true),
		messagesOpen = $bindable(true),
	}: {
		languages: Snippet;
		messages: Snippet;
		languagesOpen?: boolean;
		messagesOpen?: boolean;
	} = $props();

	let container = $state<HTMLDivElement>();
	let languagesShare = $state(defaultShare);
	let resizing = $state(false);

	let layout = $derived(
		languagesOpen && messagesOpen
			? "both-open"
			: languagesOpen
				? "languages-open"
				: messagesOpen
					? "messages-open"
					: "both-closed",
	);

	onMount(() => {
		const stored = Number.parseFloat(localStorage.getItem(storageKey) ?? "");
		if (Number.isFinite(stored)) languagesShare = clampShare(stored);
	});

	function clampShare(value: number): number {
		return Math.min(1 - minimumShare, Math.max(minimumShare, value));
	}

	function updateFromPointer(clientY: number): void {
		if (container === undefined) return;
		const bounds = container.getBoundingClientRect();
		if (bounds.height <= 0) return;
		languagesShare = clampShare((clientY - bounds.top) / bounds.height);
	}

	function beginResize(event: PointerEvent & { currentTarget: HTMLElement }): void {
		resizing = true;
		event.currentTarget.setPointerCapture(event.pointerId);
		updateFromPointer(event.clientY);
	}

	function continueResize(event: PointerEvent): void {
		if (resizing) updateFromPointer(event.clientY);
	}

	function finishResize(event: PointerEvent & { currentTarget: HTMLElement }): void {
		if (!resizing) return;
		resizing = false;
		if (event.currentTarget.hasPointerCapture(event.pointerId)) {
			event.currentTarget.releasePointerCapture(event.pointerId);
		}
		persistShare();
	}

	function resizeWithKeyboard(event: KeyboardEvent): void {
		let next = languagesShare;
		if (event.key === "ArrowUp") next -= keyboardStep;
		else if (event.key === "ArrowDown") next += keyboardStep;
		else if (event.key === "Home") next = minimumShare;
		else if (event.key === "End") next = 1 - minimumShare;
		else if (event.key === "Enter" || event.key === " ") next = defaultShare;
		else return;
		event.preventDefault();
		languagesShare = clampShare(next);
		persistShare();
	}

	function resetShare(): void {
		languagesShare = defaultShare;
		persistShare();
	}

	function persistShare(): void {
		localStorage.setItem(storageKey, languagesShare.toFixed(3));
	}
</script>

<div
	bind:this={container}
	class={["sidebar-section-panels", layout, resizing && "is-resizing"]}
	style:--languages-size={`${languagesShare}fr`}
	style:--messages-size={`${1 - languagesShare}fr`}
>
	<section class="sidebar-section-panel languages-panel" aria-label="Languages panel">
		{@render languages()}
	</section>

	{#if languagesOpen && messagesOpen}
		<div
			class="section-resizer"
			role="slider"
			tabindex="0"
			aria-label="Resize Languages and Messages"
			aria-orientation="vertical"
			aria-valuemin={minimumShare * 100}
			aria-valuemax={(1 - minimumShare) * 100}
			aria-valuenow={Math.round(languagesShare * 100)}
			aria-valuetext={`Languages ${Math.round(languagesShare * 100)}%, Messages ${Math.round((1 - languagesShare) * 100)}%`}
			title="Drag to resize · Double-click to reset"
			onpointerdown={beginResize}
			onpointermove={continueResize}
			onpointerup={finishResize}
			onpointercancel={finishResize}
			ondblclick={resetShare}
			onkeydown={resizeWithKeyboard}
		></div>
	{/if}

	<section class="sidebar-section-panel messages-panel" aria-label="Messages panel">
		{@render messages()}
	</section>
</div>

<style>
	.sidebar-section-panels {
		display: grid;
		min-height: 0;
		flex: 1;
		overflow: hidden;
	}

	.sidebar-section-panels.both-open {
		grid-template-rows: minmax(0, var(--languages-size)) 0.5rem minmax(0, var(--messages-size));
	}

	.sidebar-section-panels.languages-open {
		grid-template-rows: minmax(0, 1fr) auto;
	}

	.sidebar-section-panels.messages-open {
		grid-template-rows: auto minmax(0, 1fr);
	}

	.sidebar-section-panels.both-closed {
		grid-template-rows: auto auto;
		align-content: start;
	}

	.sidebar-section-panel {
		display: flex;
		min-height: 0;
		min-width: 0;
		flex-direction: column;
		overflow: hidden;
	}

	.section-resizer {
		position: relative;
		z-index: 2;
		width: 100%;
		min-height: 0.5rem;
		border: 0;
		padding: 0;
		background: transparent;
		cursor: row-resize;
		touch-action: none;
	}

	.section-resizer::before {
		position: absolute;
		top: 50%;
		right: 0.5rem;
		left: 0.5rem;
		height: 1px;
		content: "";
		background: var(--sidebar-border);
		transition: height 120ms ease, background-color 120ms ease;
		transform: translateY(-50%);
	}

	.section-resizer:hover::before,
	.section-resizer:focus-visible::before,
	.is-resizing .section-resizer::before {
		height: 2px;
		background: var(--sidebar-ring);
	}

	.section-resizer:focus-visible {
		outline: none;
	}

	.is-resizing {
		cursor: row-resize;
		user-select: none;
	}
</style>
