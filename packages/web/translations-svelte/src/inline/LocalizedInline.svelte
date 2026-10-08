<script lang="ts">
  import InlineContent from "./InlineContent.svelte";
  import type { InlineNode, InlineSnippets } from "./types.js";
  let { nodes, custom = {} }: { nodes: readonly InlineNode[]; custom?: InlineSnippets } = $props();
</script>

<!-- Standalone inline content marks bold and italic with span.bold and span.italic; LocalizedDocument uses b and i. -->
<InlineContent {nodes} {custom} documentMode={false} />
