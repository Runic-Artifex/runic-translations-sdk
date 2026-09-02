<script lang="ts">
	import ChevronDownIcon from "@lucide/svelte/icons/chevron-down";
	import Settings2Icon from "@lucide/svelte/icons/settings-2";
	import { Badge } from "$lib/components/ui/badge/index.js";
	import * as Collapsible from "$lib/components/ui/collapsible/index.js";
	import * as Item from "$lib/components/ui/item/index.js";
	import * as ScrollArea from "$lib/components/ui/scroll-area/index.js";
	import * as Sidebar from "$lib/components/ui/sidebar/index.js";
	import { onMount } from "svelte";
	import { getLocalEditorState, setLocalEditorState, subscribeLocalEditorState } from "./local-state";
	import { getUiText } from "$lib/ui-text";

	export interface LocaleSummary {
		tag: string;
		name: string;
		fallback?: string;
		translated: number;
		total: number;
		percent: number;
		isSource: boolean;
	}

	let {
		locales,
		selectedLocale,
		onselect,
		onmanage,
		open = $bindable(true),
	}: {
		locales: LocaleSummary[];
		selectedLocale: string;
		onselect: (locale: string) => void;
		onmanage: () => void;
		open?: boolean;
	} = $props();

	const sidebar = Sidebar.useSidebar();
	const ui = getUiText();

	onMount(() => {
		const refresh = (): void => {
			open = getLocalEditorState("runic.sidebar.languages") !== "closed";
		};
		refresh();
		return subscribeLocalEditorState(refresh);
	});

	function persistOpen(value: boolean): void {
		setLocalEditorState("runic.sidebar.languages", value ? "open" : "closed");
	}

	function selectLocale(locale: string): void {
		onselect(locale);
		if (sidebar.isMobile) sidebar.setOpenMobile(false);
	}
</script>

<Collapsible.Root bind:open onOpenChange={persistOpen} class={["group/languages", open && "min-h-0 flex flex-1 flex-col"]}>
	<Sidebar.Group aria-label={ui.text("ui_locale_coverage")} class={["py-1", open && "min-h-0 flex-1"]}>
		<Sidebar.GroupLabel class="pr-10">
			<Collapsible.Trigger class="flex min-w-0 flex-1 items-center gap-2 text-left">
				<span>{ui.text("ui_locale_languages")}</span>
				<Badge variant="secondary">{locales.length}</Badge>
				<ChevronDownIcon class="ml-auto transition-transform group-data-[state=open]/languages:rotate-180" />
			</Collapsible.Trigger>
		</Sidebar.GroupLabel>
		<Sidebar.GroupAction aria-label={ui.text("ui_locale_manage")} title={ui.text("ui_locale_manage")} onclick={onmanage}>
			<Settings2Icon />
		</Sidebar.GroupAction>
		<Collapsible.Content class="min-h-0 flex-1 overflow-hidden">
			<Sidebar.GroupContent class="min-h-0 flex-1">
				<ScrollArea.Root class="h-full min-h-0">
					<Item.Group class="gap-1 pr-2">
					{#each locales as locale (locale.tag)}
						<Item.Root
							variant={selectedLocale === locale.tag ? "muted" : "default"}
							size="xs"
							aria-pressed={selectedLocale === locale.tag}
							aria-current={selectedLocale === locale.tag ? "true" : undefined}
							onclick={() => selectLocale(locale.tag)}
							class="cursor-pointer"
							aria-label={`${locale.tag} ${locale.name}, ${locale.isSource ? ui.text("ui_locale_source_language") : `${ui.text("ui_locale_falls_back_to")} ${locale.fallback ?? ui.text("ui_locale_no_language")}`}, ${locale.percent}% ${ui.text("ui_locale_translated")}`}
						>
							{#snippet child({ props })}
								<button type="button" {...props}>
									<Item.Media>
										<Badge variant={selectedLocale === locale.tag ? "default" : "outline"} class="min-w-8">
											<code>{locale.tag}</code>
										</Badge>
									</Item.Media>
									<Item.Content class="min-w-0">
										<Item.Title class="min-w-0">
											<span class="truncate">{locale.name}</span>
										<Badge variant="ghost" aria-hidden="true" title={locale.isSource ? ui.text("ui_locale_source_language") : `${ui.text("ui_locale_falls_back_to")} ${locale.fallback ?? ui.text("ui_locale_no_language")}`}>
											{locale.isSource ? ui.text("ui_locale_source") : `← ${locale.fallback ?? ui.text("ui_locale_none")}`}
										</Badge>
										</Item.Title>
									</Item.Content>
									<Item.Actions>
										<Badge variant="outline" aria-label={`${locale.percent}% ${ui.text("ui_locale_translated")}`}>{locale.translated}/{locale.total}</Badge>
									</Item.Actions>
								</button>
							{/snippet}
						</Item.Root>
					{/each}
					</Item.Group>
				</ScrollArea.Root>
			</Sidebar.GroupContent>
		</Collapsible.Content>
	</Sidebar.Group>
</Collapsible.Root>
