<script lang="ts">
	import { Badge } from "$lib/components/ui/badge/index.js";
	import * as Item from "$lib/components/ui/item/index.js";
	import * as ScrollArea from "$lib/components/ui/scroll-area/index.js";
	import { Separator } from "$lib/components/ui/separator/index.js";

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
	}: {
		locales: LocaleSummary[];
		selectedLocale: string;
		onselect: (locale: string) => void;
	} = $props();

	let scrollHeight = $derived(`${Math.min(Math.max(locales.length, 1) * 64, 256)}px`);
</script>

<section aria-label="Locale coverage" class="px-2 py-1">
	<header class="flex h-8 items-center justify-between px-2">
		<span class="text-xs font-medium text-muted-foreground">Languages</span>
		<Badge variant="secondary">{locales.length}</Badge>
	</header>
	<ScrollArea.Root style={`height: ${scrollHeight}`}>
		<Item.Group class="gap-1 pr-2">
			{#each locales as locale (locale.tag)}
				<Item.Root
					variant={selectedLocale === locale.tag ? "muted" : "default"}
					size="xs"
					aria-pressed={selectedLocale === locale.tag}
					aria-current={selectedLocale === locale.tag ? "true" : undefined}
					onclick={() => onselect(locale.tag)}
					class="cursor-pointer"
				>
					{#snippet child({ props })}
						<button type="button" {...props}>
							<Item.Media>
								<Badge variant={selectedLocale === locale.tag ? "default" : "outline"} class="min-w-8">
									<code>{locale.tag}</code>
								</Badge>
							</Item.Media>
							<Item.Content class="min-w-0 gap-0.5">
								<Item.Title>{locale.name}</Item.Title>
								<Item.Description class="truncate">
									{locale.translated}/{locale.total} translated · {locale.isSource ? "source" : `fallback ${locale.fallback ?? "none"}`}
								</Item.Description>
							</Item.Content>
							<Item.Actions>
								<Badge variant="outline">{locale.percent}%</Badge>
							</Item.Actions>
						</button>
					{/snippet}
				</Item.Root>
			{/each}
		</Item.Group>
	</ScrollArea.Root>
	<Separator class="mt-2" />
</section>
