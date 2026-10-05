<script lang="ts">
  import { LocalizedInline, type InlineNode, type InlineElement } from "../src/inline/index.js";
  let { nodes }: { nodes: readonly InlineNode[] } = $props();
</script>
{#snippet badge(node: InlineElement)}
  <span class="badge" data-tone={node.options.tone} data-runic-occurrence={node.occurrence}><LocalizedInline nodes={node.children} /></span>
{/snippet}
<LocalizedInline {nodes} custom={{ "shop:badge": badge }} />
