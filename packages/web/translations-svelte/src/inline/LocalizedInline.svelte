<script lang="ts">
  import type { Snippet } from "svelte";
  import type { InlineNode, InlineElement, InlineSnippets } from "./types.js";
  let { nodes, custom = {} }: { nodes: readonly InlineNode[]; custom?: InlineSnippets } = $props();
  const iconLabels = new WeakMap<InlineElement, string>();
  function snippet(name: string): Snippet<[InlineElement]> {
    const render = custom[name];
    if (!render) throw new TypeError(`No Svelte snippet linked for '${name}'.`);
    return render;
  }
  function icon(value: unknown): Snippet {
    if (typeof value !== "function") throw new TypeError("Svelte icon assets must be text or snippets.");
    return value as Snippet;
  }
  function iconLabel(node: InlineElement): string | undefined {
    if (node.binding?.kind !== "runic:icon" || node.binding.decorative) return undefined;
    const cached = iconLabels.get(node);
    if (cached !== undefined) return cached;
    const label = node.binding.accessibleName?.(node.locale);
    if (typeof label !== "string" || !label.trim()) throw new TypeError("Meaningful icon alternate text is empty.");
    iconLabels.set(node, label);
    return label;
  }
</script>

{#snippet renderNodes(items: readonly InlineNode[])}
  {#each items as node (node.kind === "text" ? node : node.occurrence)}
    {#if node.kind === "text"}
      {node.value}
    {:else if node.name === "runic:strong"}
      <strong data-runic-occurrence={node.occurrence}>{@render renderNodes(node.children)}</strong>
    {:else if node.name === "runic:em"}
      <em data-runic-occurrence={node.occurrence}>{@render renderNodes(node.children)}</em>
    {:else if node.name === "runic:bold" || node.name === "runic:italic"}
      <span class={{ bold: node.name === "runic:bold", italic: node.name === "runic:italic" }} data-runic-occurrence={node.occurrence}>{@render renderNodes(node.children)}</span>
    {:else if node.name === "runic:code"}
      <code data-runic-occurrence={node.occurrence}>{@render renderNodes(node.children)}</code>
    {:else if node.name === "runic:br"}
      <br data-runic-occurrence={node.occurrence} />
    {:else if node.binding?.kind === "runic:link"}
      <a href={node.binding.href} data-runic-occurrence={node.occurrence}>{@render renderNodes(node.children)}</a>
    {:else if node.binding?.kind === "runic:action"}
      <button type="button" onclick={node.binding.onActivate} data-runic-occurrence={node.occurrence}>{@render renderNodes(node.children)}</button>
    {:else if node.binding?.kind === "runic:icon"}
      <span role={node.binding.decorative ? undefined : "img"} aria-hidden={node.binding.decorative ? "true" : undefined} aria-label={iconLabel(node)} data-runic-occurrence={node.occurrence}>
        {#if typeof node.binding.asset === "string"}{node.binding.asset}{:else}{@render icon(node.binding.asset)()}{/if}
      </span>
    {:else}
      {@render snippet(node.name)(node)}
    {/if}
  {/each}
{/snippet}

{@render renderNodes(nodes)}

<style>
  .bold { font-weight: var(--runic-inline-bold-weight, bold); }
  .italic { font-style: italic; }
  a { color: var(--runic-inline-link-color, LinkText); }
  button { font: inherit; }
</style>
