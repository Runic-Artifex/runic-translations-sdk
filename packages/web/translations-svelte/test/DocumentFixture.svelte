<script lang="ts">
  import { LocalizedDocument, type DocumentNode } from "../src/document/index.js";
  import { LocalizedInline, type InlineElement } from "../src/inline/index.js";
  import { createLocaleContext, type LocaleSource } from "../src/translations/index.js";
  let { nodes, headingBase = 2, pageDirection, localeSource }: { nodes: readonly DocumentNode[]; headingBase?: number; pageDirection?: "ltr" | "rtl"; localeSource?: LocaleSource } = $props();
  // svelte-ignore state_referenced_locally
  if (localeSource) createLocaleContext<string>().provide(localeSource);
</script>
{#snippet markup(node: InlineElement)}<span data-runic-markup={node.name} data-runic-occurrence={node.occurrence}><LocalizedInline nodes={node.children} /></span>{/snippet}
<LocalizedDocument {nodes} {headingBase} {pageDirection} custom={{ "shop:term": markup, "shop:hidden": markup }} />
