<script lang="ts">
	import ChevronDownIcon from "@lucide/svelte/icons/chevron-down";
	import Settings2Icon from "@lucide/svelte/icons/settings-2";
	import { Badge } from "#lib/components/ui/badge/index.js";
	import * as Collapsible from "#lib/components/ui/collapsible/index.js";
	import * as Item from "#lib/components/ui/item/index.js";
	import * as ScrollArea from "#lib/components/ui/scroll-area/index.js";
	import * as Sidebar from "#lib/components/ui/sidebar/index.js";
	import { onMount } from "svelte";
	import { getLocalEditorState, setLocalEditorState, subscribeLocalEditorState } from "./local-state";
	import { getUiText } from "#lib/ui-text.js";

	export interface LocaleSummary {
		tag: string;
		name: string;
		fallback?: string;
		translated: number;
		/** Messages a fallback locale other than the base serves (runic-translations status). */
		fallbackCount?: number;
		missing?: number;
		/** Translations whose source changed since they were reviewed; absent without a review file. */
		stale?: number;
		/** validation.translationCompleteness: how validate reports the locale's missing messages. */
		completeness?: string;
		total: number;
		/** Coverage: translated plus fallback, as a percentage, as runic-translations status reports it. */
		percent: number;
		isSource: boolean;
	}

	function coverageDetails(locale: LocaleSummary): string {
		const parts = [ui.text("ui_locale_status_translated", { count: String(locale.translated) })];
		if (locale.fallbackCount) parts.push(ui.text("ui_locale_status_fallback", { count: String(locale.fallbackCount), locale: locale.fallback ?? "" }));
		if (locale.missing !== undefined) parts.push(ui.text("ui_locale_status_missing", { count: String(locale.missing) }));
		if (locale.stale !== undefined) parts.push(ui.text("ui_locale_status_stale", { count: String(locale.stale) }));
		if (locale.completeness !== undefined) parts.push(ui.text(locale.completeness === "error" ? "ui_locale_status_policy_error" : locale.completeness === "warning" ? "ui_locale_status_policy_warning" : "ui_locale_status_policy_allow"));
		return `${locale.percent}% · ${parts.join(" · ")}`;
	}

	let {
		locales,
		selectedLocale,
		onselect,
		onmanage,
		pseudoLocales = [],
		activePseudoLocale,
		onselectpseudo,
		open = $bindable(true),
	}: {
		locales: LocaleSummary[];
		selectedLocale: string;
		onselect: (locale: string) => void;
		onmanage: () => void;
		/** runic.json pseudoLocales: generated from the base locale, previewed but never edited. */
		pseudoLocales?: string[];
		/** The pseudo-locale the preview currently shows, if any. */
		activePseudoLocale?: string;
		onselectpseudo?: (locale: string) => void;
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
										<Badge variant={locale.completeness === "error" && locale.missing ? "destructive" : "outline"} aria-label={`${locale.percent}% ${ui.text("ui_locale_translated")}`} title={coverageDetails(locale)}>{locale.percent}%</Badge>
									</Item.Actions>
								</button>
							{/snippet}
						</Item.Root>
					{/each}
					{#each pseudoLocales as locale (locale)}
						<Item.Root
							variant={activePseudoLocale === locale ? "muted" : "default"}
							size="xs"
							aria-pressed={activePseudoLocale === locale}
							onclick={() => onselectpseudo?.(locale)}
							class="cursor-pointer"
							aria-label={`${locale}, ${ui.text("ui_locale_pseudo_description")}`}
						>
							{#snippet child({ props })}
								<button type="button" {...props}>
									<Item.Media>
										<Badge variant={activePseudoLocale === locale ? "default" : "outline"} class="min-w-8">
											<code>{locale}</code>
										</Badge>
									</Item.Media>
									<Item.Content class="min-w-0">
										<Item.Title class="min-w-0">
											<span class="truncate">{ui.text("ui_locale_pseudo")}</span>
											<Badge variant="ghost" aria-hidden="true" title={ui.text("ui_locale_pseudo_description")}>{ui.text("ui_locale_generated")}</Badge>
										</Item.Title>
									</Item.Content>
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
