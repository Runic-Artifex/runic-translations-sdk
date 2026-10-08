<script lang="ts">
  import { LocalizedDocument, type DocumentNode } from "../src/document/index.js";
  import { LocalizedInline, type InlineElement } from "../src/inline/index.js";
  let { nodes, headingBase = 2 }: { nodes: readonly DocumentNode[]; headingBase?: number } = $props();
</script>
{#snippet markup(node: InlineElement)}<span data-runic-markup={node.name} data-runic-occurrence={node.occurrence}><LocalizedInline nodes={node.children} /></span>{/snippet}
<LocalizedDocument {nodes} {headingBase} custom={{ "shop:term": markup, "shop:hidden": markup }} />
