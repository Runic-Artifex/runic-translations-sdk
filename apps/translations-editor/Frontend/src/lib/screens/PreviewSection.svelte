<script lang="ts">
  import ArtifactPreviewPanel from "#lib/ArtifactPreviewPanel.svelte";
  import { Button } from "#lib/components/ui/button/index.js";
  import { Input } from "#lib/components/ui/input/index.js";
  import type { EditorLocaleCatalog } from "#lib/contracts.js";
  import DocumentPreview from "#lib/DocumentPreview.svelte";
  import InlinePreview from "#lib/InlinePreview.svelte";
  import { localeDirection } from "#lib/locale-text.js";
  import type { MessageArtifact, PreviewFormatting, PreviewNode } from "#lib/message-composer.js";
  import { previewSampleOr } from "#lib/message-preview.js";
  import type { UiDirection } from "#lib/simulation.js";
  import type { PreviewController } from "#lib/state/preview-controller.svelte.js";
  import type { SelectionController } from "#lib/state/selection-controller.svelte.js";
  import { displayNotice, getUiText } from "#lib/ui-text.js";

  let {
    preview,
    selection,
    localeCatalog,
    pseudoLocalization,
    uiDirection,
    artifactPreviewOpen = $bindable(false),
  }: {
    preview: PreviewController;
    selection: Pick<SelectionController, "selectedLocale" | "currentExamples" | "selectPreviewExample" | "changePreviewFormatting">;
    /** The SDK's CLDR locales: the regional variants offered as formatting locales. */
    localeCatalog: EditorLocaleCatalog | undefined;
    pseudoLocalization: boolean;
    uiDirection: UiDirection;
    /** Whether the compiled artifact of the preview is shown. */
    artifactPreviewOpen?: boolean;
  } = $props();
  const ui = getUiText();

  const selectedLocale = $derived(selection.selectedLocale);
  let previewDirection = $derived(localeDirection(pseudoLocalization ? preview.pseudoLocale : selectedLocale));
  // The time zones the preview offers: the host's, UTC, then the engine's IANA zones.
  const previewTimeZones = typeof Intl.supportedValuesOf === "function" ? Intl.supportedValuesOf("timeZone") : [];
  // Regional variants of the selected locale's language that CLDR has data for (de → de-AT, de-CH, …).
  let regionalLocaleSuggestions = $derived.by(() => {
    const language = selectedLocale.split("-")[0]?.toLowerCase() ?? "";
    return (localeCatalog?.locales ?? []).filter((tag) => tag.toLowerCase() === language || tag.toLowerCase().startsWith(`${language}-`));
  });

  function previewFormattingSummary(formatting: PreviewFormatting | undefined): string | undefined {
    if (formatting === undefined) return undefined;
    const parts: string[] = [];
    if (formatting.requestedLocale !== undefined && formatting.requestedLocale !== selectedLocale) {
      parts.push(ui.text("ui_preview_requested_summary", {
        requested: formatting.requestedLocale,
        resolution: ui.text(formatting.resolution === "parent" ? "ui_preview_resolution_parent" : formatting.resolution === "related" ? "ui_preview_resolution_related" : "ui_preview_resolution_exact"),
      }));
    }
    if (formatting.formattingLocale !== undefined) parts.push(ui.text("ui_preview_formats_in", { locale: formatting.formattingLocale }));
    parts.push(formatting.timeZone === undefined ? ui.text("ui_preview_time_zone_summary_host") : ui.text("ui_preview_time_zone_summary", { zone: formatting.timeZone }));
    return parts.join(" · ");
  }

  function previewInputEntries(ast: MessageArtifact): Array<[string, { type: string }]> {
    return ast.inputs.map((input) => [input.name, input]);
  }
</script>

{#snippet previewNodes(nodes: PreviewNode[])}
  <InlinePreview {nodes} />
{/snippet}

<!-- The compiler-rendered preview of the selected message with its formatting and samples, and its artifact. -->
<section class="message-preview" aria-live="polite">
  <header>
    <div><strong>{ui.text("ui_page_preview_title")}</strong><span>{ui.text("ui_page_preview_description")}</span></div>
    <span class="preview-state">{preview.busy ? ui.text("ui_page_preview_compiling") : preview.ast === undefined ? ui.text("ui_page_preview_unavailable") : pseudoLocalization ? ui.text("ui_preview_pseudo_locale", { locale: preview.pseudoLocale }) : selectedLocale}</span>
  </header>
  <div class="flex flex-wrap items-end gap-3 px-4 pt-3" aria-label={ui.text("ui_preview_formatting")}>
    <label class="grid gap-1 text-xs">
      <span class="text-muted-foreground">{ui.text("ui_preview_regional_locale")}</span>
      <Input class="h-8 w-40" value={preview.requestedLocale} placeholder={selectedLocale} list="preview-regional-locales" autocomplete="off"
        title={ui.text("ui_preview_regional_locale_description")}
        onchange={(event) => selection.changePreviewFormatting(event.currentTarget.value, preview.timeZone)} />
    </label>
    <datalist id="preview-regional-locales">{#each regionalLocaleSuggestions as tag (tag)}<option value={tag}></option>{/each}</datalist>
    <label class="grid gap-1 text-xs">
      <span class="text-muted-foreground">{ui.text("ui_preview_time_zone")}</span>
      <select class="h-8 max-w-56 rounded-md border bg-background px-2 text-sm" value={preview.timeZone}
        title={ui.text("ui_preview_time_zone_description")}
        onchange={(event) => selection.changePreviewFormatting(preview.requestedLocale, event.currentTarget.value)}>
        <option value="">{ui.text("ui_preview_time_zone_host")}</option>
        <option value="utc">UTC</option>
        {#each previewTimeZones as zone (zone)}<option value={zone}>{zone}</option>{/each}
      </select>
    </label>
  </div>
  {#if selection.currentExamples.length > 0}
    <div class="flex flex-wrap items-center gap-2 px-4 py-3" aria-label={ui.text("ui_preview_examples")}>
      <span class="text-xs text-muted-foreground">{ui.text("ui_preview_examples")}</span>
      {#each selection.currentExamples as example, index (index)}
        <Button variant="outline" size="xs" onclick={() => selection.selectPreviewExample(index)}>{ui.text("ui_preview_example", { number: String(index + 1) })}</Button>
      {/each}
    </div>
  {/if}
  {#if preview.ast !== undefined && previewInputEntries(preview.ast).length > 0}
    <div class="sample-inputs">
      {#each previewInputEntries(preview.ast) as [name, descriptor] (name)}
        <label><span><span class="placeholder-chip">{`{$${name}}`}</span><small>{descriptor.type}</small></span><input value={previewSampleOr(preview.samples, name, "")} oninput={(event) => preview.updateSample(name, event.currentTarget.value)} /></label>
      {/each}
    </div>
  {/if}
  <div class="preview-canvas" lang={pseudoLocalization ? preview.pseudoLocale : selectedLocale} dir={previewDirection}>
    {#if preview.busy}
      <span class="preview-placeholder">{ui.text("ui_page_preview_compiling_draft")}</span>
    {:else if preview.error}
      <span class="preview-error">{displayNotice(preview.error, ui)}</span>
    {:else if pseudoLocalization && preview.pseudoError}
      <span class="preview-error">{displayNotice(preview.pseudoError, ui)}</span>
    {:else if preview.simulatedResult?.kind === "text"}
      <p>{preview.simulatedResult.value}</p>
    {:else if preview.simulatedResult?.kind === "content"}
      <div class="safe-content">{@render previewNodes(preview.simulatedResult.nodes)}</div>
    {:else if preview.simulatedResult?.kind === "document"}
      <div class="safe-content"><DocumentPreview blocks={preview.simulatedResult.blocks} /></div>
    {:else}
      <span class="preview-placeholder">{ui.text("ui_page_preview_edit_to_build")}</span>
    {/if}
  </div>
  {#if previewFormattingSummary(preview.simulatedResult?.formatting)}<p class="safe-note">{previewFormattingSummary(preview.simulatedResult?.formatting)}</p>{/if}
  {#if pseudoLocalization || uiDirection === "rtl"}<p class="safe-note">{[pseudoLocalization ? ui.text("ui_page_preview_pseudo_active") : "", uiDirection === "rtl" ? ui.text("ui_page_preview_rtl_active") : ""].filter(Boolean).join(" ")}</p>{/if}
</section>

<ArtifactPreviewPanel
  open={artifactPreviewOpen}
  baseResult={preview.result}
  simulatedResult={preview.simulatedResult}
  {pseudoLocalization}
  direction={uiDirection}
  busy={preview.busy}
  onclose={() => artifactPreviewOpen = false}
/>

<style>
  .message-preview { width: 100%; max-width: 1000px; margin: 0 auto; border: 1px solid var(--border); border-radius: var(--radius-pane); color: var(--card-foreground); background: var(--card); overflow: hidden; }
  .message-preview > header { display: flex; align-items: center; justify-content: space-between; gap: 1rem; padding: .75rem 1rem; border-bottom: 1px solid var(--border); }
  .message-preview > header > div { display: grid; gap: .15rem; }
  .message-preview header strong { color: var(--foreground); font-size: var(--ui-text); font-weight: 620; }
  .message-preview header span { color: var(--muted-foreground); font-size: var(--ui-text-xs); }
  .preview-state { border: 1px solid var(--border); border-radius: var(--radius-control); padding: .1rem .45rem; color: var(--muted-foreground) !important; font: var(--ui-text-xs) var(--font-mono) !important; white-space: nowrap; }
  .sample-inputs { display: grid; grid-template-columns: repeat(auto-fit, minmax(10rem, 1fr)); gap: .6rem; border-top: 1px solid var(--border); padding: .75rem 1rem; }
  .sample-inputs label { display: grid; gap: .3rem; color: var(--muted-foreground); font-size: var(--ui-text-xs); }
  .sample-inputs label > span { display: flex; align-items: center; justify-content: space-between; gap: .5rem; }
  .sample-inputs small { color: var(--muted-foreground); font: var(--ui-text-xs) var(--font-mono); }
  .sample-inputs input { min-width: 0; border: 1px solid var(--field-border); border-radius: var(--radius-control); padding: .35rem .5rem; color: var(--foreground); background: var(--background); font: var(--ui-text) var(--font-mono); }
  .preview-canvas { min-height: 5rem; border-top: 1px solid var(--border); padding: 1rem; color: var(--foreground); background: var(--background); font-size: var(--message-text); line-height: 1.6; }
  .preview-canvas p { margin: 0; white-space: pre-wrap; }
  .preview-placeholder { color: var(--muted-foreground); font-size: var(--ui-text); }
  .preview-error { color: var(--destructive); font-size: var(--ui-text); }
  .safe-content { display: block; }
  .safe-note { margin: 0; border-top: 1px solid var(--border); padding: .55rem 1rem; color: var(--muted-foreground); font-size: var(--ui-text-xs); }
</style>
