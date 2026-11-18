<script lang="ts">
  import { sourceTextView, variantTitle, type SourceSegment } from "./message-composer";
  import type { ResourceEntry } from "./resource-model";
  import { localeDirection } from "./locale-text";
  import { getUiText } from "./ui-text";

  // The base locale's text of the selected message, read-only above the target editor (#225 P1). Placeholders are
  // chips as in the composer; a message with selectors shows one block per variant, titled as the composer titles it.
  let { entry, locale, localeName }: {
    entry: Pick<ResourceEntry, "value" | "authoring"> | undefined;
    locale: string;
    localeName: string;
  } = $props();

  const ui = getUiText();
  const headingId = $props.id();
  let view = $derived(sourceTextView(entry));
  let direction = $derived(localeDirection(locale));
  let labels = $derived({
    defaultTranslation: ui.text("ui_composer_default_translation"),
    fallback: ui.text("ui_composer_fallback"),
  });
</script>

{#snippet segments(items: SourceSegment[])}{#each items as segment, index (index)}{#if segment.kind === "text"}{segment.text}{:else}<span class="mx-0.5 inline-flex h-6 items-center rounded-full bg-secondary px-2 align-baseline font-mono text-xs text-secondary-foreground" data-placeholder={segment.kind}>{segment.syntax}</span>{/if}{/each}{/snippet}

<section class="grid gap-2 rounded-xl border bg-muted/30 px-4 py-3" aria-labelledby={headingId} data-testid="source-text" data-locale={locale}>
  <header class="flex flex-wrap items-baseline justify-between gap-x-4 gap-y-1">
    <h3 id={headingId} class="text-xs font-semibold text-foreground/80">{ui.text("ui_source_panel_title", { language: localeName })}</h3>
    <span class="text-[0.65rem] text-muted-foreground">{ui.text("ui_source_panel_read_only")}</span>
  </header>
  {#if view === undefined}
    <p class="text-sm text-muted-foreground">{ui.text("ui_source_panel_missing")}</p>
  {:else if view.kind === "source"}
    <pre class="overflow-x-auto font-mono text-xs leading-6 whitespace-pre-wrap" lang={locale} dir={direction}>{view.text}</pre>
  {:else if view.selectors.length === 0}
    <p class="text-base leading-7 whitespace-pre-wrap" lang={locale} dir={direction}>{@render segments(view.blocks[0]?.segments ?? [])}</p>
  {:else}
    <dl class="grid gap-2">
      {#each view.blocks as block (block.id)}
        <div class="grid gap-0.5">
          <dt class="text-xs text-muted-foreground">{variantTitle(block.keys, view.selectors, labels)}</dt>
          <dd class="text-base leading-7 whitespace-pre-wrap" lang={locale} dir={direction}>{@render segments(block.segments)}</dd>
        </div>
      {/each}
    </dl>
  {/if}
</section>
