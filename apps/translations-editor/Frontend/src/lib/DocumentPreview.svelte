<script lang="ts">
  import InlinePreview from "#lib/InlinePreview.svelte";
  import type { SimulationPreviewBlock } from "#lib/simulation.js";
  let { blocks }: { blocks: SimulationPreviewBlock[] } = $props();

  // Fixed list numbering styles of the document profile; no translation text reaches attributes except as data.
  const listTypes: Record<string, "1" | "a" | "A" | "i" | "I"> = {
    decimal: "1", "lower-alpha": "a", "upper-alpha": "A", "lower-roman": "i", "upper-roman": "I",
  };
  const level = (block: SimulationPreviewBlock) => {
    const value = Number(block.options.level);
    return Number.isInteger(value) && value >= 1 && value <= 6 ? value : 1;
  };
</script>

{#snippet renderBlocks(children: SimulationPreviewBlock[])}
  {#each children as block (block.occurrence)}
    {#if block.name === "runic:p"}
      <p class="paragraph" data-runic-occurrence={block.occurrence}><InlinePreview nodes={block.nodes} /></p>
    {:else if block.name === "runic:h"}
      <div class="heading level-{level(block)}" role="heading" aria-level={level(block)} data-runic-occurrence={block.occurrence}><InlinePreview nodes={block.nodes} /></div>
    {:else if block.name === "runic:ul"}
      <ul data-runic-occurrence={block.occurrence}>{@render renderBlocks(block.blocks)}</ul>
    {:else if block.name === "runic:ol"}
      <ol start={Number(block.options.start ?? "1")} type={listTypes[block.options.marker ?? "decimal"] ?? "1"} data-runic-occurrence={block.occurrence}>{@render renderBlocks(block.blocks)}</ol>
    {:else if block.name === "runic:li"}
      <li data-runic-occurrence={block.occurrence}><InlinePreview nodes={block.nodes} /></li>
    {:else}
      <div class="custom-block" data-markup={block.name} data-runic-occurrence={block.occurrence}>
        <small>{block.name}</small>
        {#if block.blocks.length > 0}{@render renderBlocks(block.blocks)}{:else}<InlinePreview nodes={block.nodes} />{/if}
      </div>
    {/if}
  {/each}
{/snippet}

<div class="document-preview">{@render renderBlocks(blocks)}</div>

<style>
  .document-preview { display: grid; gap: 0.6em; }
  .paragraph { margin: 0; }
  .heading { font-weight: 650; line-height: 1.3; }
  .level-1 { font-size: 1.45em; }
  .level-2 { font-size: 1.25em; }
  .level-3 { font-size: 1.12em; }
  ul, ol { margin: 0; padding-inline-start: 1.6em; }
  ul { list-style: disc; }
  ol { list-style: decimal; }
  ol[type="a"] { list-style-type: lower-alpha; }
  ol[type="A"] { list-style-type: upper-alpha; }
  ol[type="i"] { list-style-type: lower-roman; }
  ol[type="I"] { list-style-type: upper-roman; }
  .custom-block { border: 1px dashed var(--border); border-radius: 0.3em; padding: 0.2em 0.4em; }
  small { font-size: 0.7em; color: var(--muted-foreground); }
</style>
