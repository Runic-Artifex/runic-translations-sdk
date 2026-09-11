<script lang="ts">
  import type { SimulationPreviewNode } from "$lib/simulation";
  let { nodes }: { nodes: SimulationPreviewNode[] } = $props();
</script>

{#snippet renderNodes(children: SimulationPreviewNode[])}
  {#each children as node (node)}
    {#if node.kind === "text"}
      <span class="text">{node.value}</span>
    {:else if node.name === "runic:strong"}
      <strong>{@render renderNodes(node.children)}</strong>
    {:else if node.name === "runic:em"}
      <em>{@render renderNodes(node.children)}</em>
    {:else if node.name === "runic:bold"}
      <span class="bold">{@render renderNodes(node.children)}</span>
    {:else if node.name === "runic:italic"}
      <span class="italic">{@render renderNodes(node.children)}</span>
    {:else if node.name === "runic:code"}
      <code>{@render renderNodes(node.children)}</code>
    {:else if node.name === "runic:br"}
      <br />
    {:else if node.name === "runic:link"}
      <span class="link" role="link" aria-disabled="true" title={node.attributes.ref}>{@render renderNodes(node.children)}</span>
    {:else if node.name === "runic:action"}
      <button type="button" disabled title={node.attributes.ref}>{@render renderNodes(node.children)}</button>
    {:else if node.name === "runic:icon"}
      <span role="img" aria-label={node.attributes.ref ?? node.name} title={node.attributes.ref}>◇</span>
    {:else}
      <span class="custom" data-markup={node.name} title={Object.entries(node.attributes).map(([key, value]) => `${key}=${value}`).join(" · ")}>
        <small>{node.name}</small> {@render renderNodes(node.children)}
      </span>
    {/if}
  {/each}
{/snippet}

{@render renderNodes(nodes)}

<style>
  .text { white-space: pre-wrap; }
  .bold { font-weight: bold; }
  .italic { font-style: italic; }
  .link { text-decoration: underline; color: var(--primary); }
  button, .custom { border: 1px solid var(--border); border-radius: 0.3em; padding: 0.1em 0.3em; }
  button { color: inherit; background: var(--muted); }
  small { font-size: 0.7em; color: var(--muted-foreground); }
  code { font-family: monospace; }
</style>
