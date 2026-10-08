<script lang="ts">
  import LocalizedInline from "../inline/LocalizedInline.svelte";
  import type { InlineNode, InlineSnippets } from "../inline/types.js";
  import type { DocumentBlock, DocumentNode } from "./types.js";
  let { nodes, custom = {}, headingBase = 2 }: { nodes: readonly DocumentNode[]; custom?: InlineSnippets; headingBase?: number } = $props();
  const leaves = new Set(["runic:p", "runic:h", "runic:li"]);
  const known = new Set([...leaves, "runic:ul", "runic:ol"]);
  const markerTypes: Readonly<Record<string, string>> = { "lower-alpha": "a", "upper-alpha": "A", "lower-roman": "i", "upper-roman": "I" };
  function blocks(items: readonly DocumentNode[]): readonly DocumentBlock[] {
    for (const item of items)
      if (item.kind !== "block" || !known.has(item.name)) throw new TypeError(`No Svelte document renderer linked for '${item.kind === "block" ? item.name : item.kind}'.`);
    return items as readonly DocumentBlock[];
  }
  function inlines(block: DocumentBlock): readonly InlineNode[] {
    if (block.children.some(child => child.kind === "block")) throw new TypeError(`'${block.name}' at '${block.occurrence}' must hold inline content.`);
    return block.children as readonly InlineNode[];
  }
  // Heading levels are relative to the host's base (default 2); beyond h6 the element stays h6 with an explicit aria-level.
  function level(block: DocumentBlock): number {
    if (!Number.isInteger(headingBase) || headingBase < 1 || headingBase > 9) throw new RangeError("headingBase must be an integer from 1 to 9.");
    return headingBase + Number(block.options.level) - 1;
  }
</script>

<!-- Leaves hold their inline content with no surrounding template whitespace: runic-leaf renders text verbatim. -->
{#snippet renderBlocks(items: readonly DocumentNode[], top: boolean)}
  {#each blocks(items) as block (block.occurrence)}
    {#if block.name === "runic:p"}
      <p class="runic-leaf" data-runic-occurrence={block.occurrence} lang={top ? block.locale : undefined}><LocalizedInline nodes={inlines(block)} {custom} /></p>
    {:else if block.name === "runic:h"}
      {@const effective = level(block)}
      <svelte:element this={effective <= 6 ? `h${effective}` : "h6"} class="runic-leaf" role={effective > 6 ? "heading" : undefined} aria-level={effective > 6 ? effective : undefined} data-runic-occurrence={block.occurrence} lang={top ? block.locale : undefined}><LocalizedInline nodes={inlines(block)} {custom} /></svelte:element>
    {:else if block.name === "runic:ul"}
      <ul data-runic-occurrence={block.occurrence} lang={top ? block.locale : undefined}>{@render renderBlocks(block.children, false)}</ul>
    {:else if block.name === "runic:ol"}
      <ol data-runic-occurrence={block.occurrence} lang={top ? block.locale : undefined} start={block.options.start === "1" ? undefined : Number(block.options.start)} type={markerTypes[block.options.marker] as "a" | "A" | "i" | "I" | undefined}>{@render renderBlocks(block.children, false)}</ol>
    {:else}
      <li class="runic-leaf" data-runic-occurrence={block.occurrence}><LocalizedInline nodes={inlines(block)} {custom} /></li>
    {/if}
  {/each}
{/snippet}

{@render renderBlocks(nodes, true)}

<style>
  .runic-leaf { white-space: pre-wrap; }
</style>
