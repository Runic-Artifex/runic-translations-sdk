<script lang="ts">
  import { Badge } from "$lib/components/ui/badge/index.js";
  import type { SimulationPreviewNode, UiDirection } from "$lib/simulation";
  import { getUiText } from "$lib/ui-text";

  let {
    open,
    baseResult,
    simulatedResult,
    pseudoLocalization,
    direction,
    busy,
    onclose,
  }: {
    open: boolean;
    baseResult:
      | { kind: "text"; value: string }
      | { kind: "content"; nodes: SimulationPreviewNode[] }
      | undefined;
    simulatedResult:
      | { kind: "text"; value: string }
      | { kind: "content"; nodes: SimulationPreviewNode[] }
      | undefined;
    pseudoLocalization: boolean;
    direction: UiDirection;
    busy: boolean;
    onclose: () => void;
  } = $props();

  const ui = getUiText();

  const simulationLabel = $derived(
    [pseudoLocalization ? ui.text("ui_artifact_preview_pseudo_localization") : null, direction === "rtl" ? ui.text("ui_artifact_preview_rtl") : null]
      .filter((part) => part !== null)
      .join(" · ") || ui.text("ui_artifact_preview_no_simulation"),
  );
</script>

{#snippet artifactNodes(nodes: SimulationPreviewNode[])}
  {#each nodes as node, index (index)}
    {#if node.kind === "text"}
      <span class="artifact-text">{node.value}</span>
    {:else}
      <span class="artifact-element">
        <span class="artifact-element-label">{node.name}</span>
        {#if Object.keys(node.attributes).length > 0}
          <span class="artifact-attributes">{Object.entries(node.attributes).map(([name, value]) => name + "=" + value).join(" · ")}</span>
        {/if}
        <span class="artifact-children">{@render artifactNodes(node.children)}</span>
      </span>
    {/if}
  {/each}
{/snippet}

{#if open}
  <section class="artifact-preview" aria-label={ui.text("ui_artifact_preview_aria_label")}>
    <header>
      <div>
        <strong>{ui.text("ui_artifact_preview_title")}</strong>
        <span>{ui.text("ui_artifact_preview_description")}</span>
      </div>
      <button class="artifact-close" onclick={onclose} aria-label={ui.text("ui_artifact_preview_close")}>×</button>
    </header>
    <div class="artifact-columns">
      <div class="artifact-column">
        <div class="artifact-column-header">
          <Badge variant="outline">{ui.text("ui_artifact_preview_as_generated")}</Badge>
          <small dir="ltr">{ui.text("ui_artifact_preview_untransformed")}</small>
        </div>
        <div class="artifact-canvas" dir="ltr">
          {#if busy}
            <span class="artifact-placeholder">{ui.text("ui_artifact_preview_compiling")}</span>
          {:else if baseResult?.kind === "text"}
            <p>{baseResult.value}</p>
          {:else if baseResult?.kind === "content"}
            {@render artifactNodes(baseResult.nodes)}
          {:else}
            <span class="artifact-placeholder">{ui.text("ui_artifact_preview_edit_to_preview")}</span>
          {/if}
        </div>
      </div>
      <div class="artifact-column">
        <div class="artifact-column-header">
          <Badge variant="secondary">{ui.text("ui_artifact_preview_simulated")}</Badge>
          <small>{simulationLabel}</small>
        </div>
        <div class="artifact-canvas simulated" dir={direction}>
          {#if busy}
            <span class="artifact-placeholder">{ui.text("ui_artifact_preview_compiling")}</span>
          {:else if simulatedResult?.kind === "text"}
            <p>{simulatedResult.value}</p>
          {:else if simulatedResult?.kind === "content"}
            {@render artifactNodes(simulatedResult.nodes)}
          {:else}
            <span class="artifact-placeholder">{ui.text("ui_artifact_preview_edit_to_preview")}</span>
          {/if}
        </div>
      </div>
    </div>
    <p class="artifact-note">{ui.text("ui_artifact_preview_note")}</p>
  </section>
{/if}

<style>
  .artifact-preview {
    max-width: 1000px;
    margin: 0.8rem auto 0;
    border: 1px solid var(--border);
    border-radius: 0.65rem;
    overflow: hidden;
    background: var(--card);
  }
  .artifact-preview > header {
    display: flex;
    align-items: center;
    justify-content: space-between;
    gap: 1rem;
    padding: 0.7rem 0.9rem;
    border-block-end: 1px solid var(--border);
    background: color-mix(in oklch, var(--muted) 82%, var(--primary));
  }
  .artifact-preview > header > div { display: grid; gap: 0.15rem; min-width: 0; }
  .artifact-preview strong { color: var(--foreground); font-size: 0.68rem; }
  .artifact-preview > header span:not(.artifact-element-label):not(.artifact-attributes) {
    color: var(--muted-foreground);
    font-size: 0.56rem;
  }
  .artifact-close {
    border: 1px solid var(--border);
    border-radius: 50%;
    inline-size: 1.5rem;
    block-size: 1.5rem;
    color: var(--muted-foreground);
    background: transparent;
    cursor: pointer;
    line-height: 1;
  }
  .artifact-close:hover { color: var(--foreground); background: var(--accent); }
  .artifact-columns {
    display: grid;
    grid-template-columns: repeat(auto-fit, minmax(min(100%, 18rem), 1fr));
  }
  .artifact-column + .artifact-column { border-inline-start: 1px solid var(--border); }
  .artifact-column-header {
    display: flex;
    align-items: center;
    justify-content: space-between;
    gap: 0.6rem;
    padding: 0.45rem 0.9rem;
    border-block-end: 1px solid var(--border);
    background: color-mix(in oklch, var(--background) 60%, var(--card));
  }
  .artifact-column-header small { color: var(--muted-foreground); font-size: 0.55rem; }
  .artifact-canvas {
    min-height: 4rem;
    padding: 0.9rem;
    font-size: 0.85rem;
    line-height: 1.7;
    text-align: start;
    background: radial-gradient(circle at 90% 0, color-mix(in oklch, var(--primary) 10%, transparent), transparent 45%), var(--background);
  }
  .artifact-canvas p { margin: 0; white-space: pre-wrap; overflow-wrap: anywhere; }
  .artifact-placeholder { color: var(--muted-foreground); font-size: 0.65rem; }
  .artifact-text, .artifact-children { display: inline; }
  .artifact-element {
    display: inline-flex;
    flex-wrap: wrap;
    align-items: baseline;
    gap: 0.25rem;
    border: 1px solid color-mix(in oklch, var(--primary) 40%, var(--border));
    border-radius: 0.35rem;
    padding: 0.24rem 0.35rem;
    background: color-mix(in oklch, var(--primary) 8%, var(--card));
  }
  .artifact-element-label { color: var(--primary); font: 0.52rem ui-monospace, monospace; }
  .artifact-attributes { color: var(--muted-foreground); font: 0.48rem ui-monospace, monospace; }
  .artifact-note {
    margin: 0;
    padding: 0.55rem 0.9rem;
    border-block-start: 1px solid var(--border);
    color: var(--muted-foreground);
    font-size: 0.55rem;
  }
</style>
