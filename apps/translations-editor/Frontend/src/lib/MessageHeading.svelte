<script lang="ts">
  import CopyIcon from "@lucide/svelte/icons/copy";
  import PencilIcon from "@lucide/svelte/icons/pencil";
  import Trash2Icon from "@lucide/svelte/icons/trash-2";
  import { Badge } from "#lib/components/ui/badge/index.js";
  import { Button } from "#lib/components/ui/button/index.js";
  import { getUiText } from "#lib/ui-text.js";

  let {
    messageKey,
    messagePath = [],
    description,
    tags,
    locale,
    overrideLayer,
    inheritedFrom,
    onrename,
    onduplicate,
    ondelete,
  }: {
    /** The flattened code identifier (`common_cancel`); Rename, Duplicate and Delete work on it. */
    messageKey: string;
    /** The logical path (`["common", "cancel"]`), shown as the title `common.cancel`. */
    messagePath?: readonly string[];
    description?: string;
    tags: string[];
    locale: string;
    /** A library override layer the message's document belongs to; absent for the catalog's own (base) layer. */
    overrideLayer?: string;
    inheritedFrom?: string;
    onrename: () => void;
    onduplicate: () => void;
    ondelete: () => void;
  } = $props();

  const ui = getUiText();
  let dottedPath = $derived(messagePath.length > 0 ? messagePath.join(".") : messageKey);
</script>

<header class="mx-auto mb-6 flex max-w-[1000px] flex-col items-start justify-between gap-4 xl:flex-row xl:gap-8">
  <div class="min-w-0">
    <h2 class="break-words font-serif text-3xl tracking-tight sm:text-4xl" data-testid="message-title">{dottedPath}</h2>
    {#if dottedPath !== messageKey}
      <p class="mt-1 flex flex-wrap items-baseline gap-1.5 text-xs text-muted-foreground" data-testid="message-code-identifier">
        <span>{ui.text("ui_message_heading_code_identifier")}</span>
        <code class="font-mono">{messageKey}</code>
      </p>
    {/if}
    {#if description}<p class="mt-2 max-w-2xl text-sm text-muted-foreground">{description}</p>{/if}
    {#if tags.length > 0}
      <div class="mt-3 flex flex-wrap gap-1.5">
        {#each tags as tag (tag)}<Badge variant="outline">{tag}</Badge>{/each}
      </div>
    {/if}
  </div>

  <div class="flex shrink-0 flex-col items-start gap-2 xl:items-end">
    <div class="flex flex-wrap gap-1.5 xl:justify-end">
      <Badge variant="outline">{locale}</Badge>
      {#if overrideLayer}
        <Badge variant="outline" title={ui.text("ui_message_heading_override_layer_title")}>{ui.text("ui_message_heading_override_layer", { layer: overrideLayer })}</Badge>
      {/if}
      {#if inheritedFrom}<Badge variant="secondary">{ui.text("ui_message_heading_falls_back_to")} {inheritedFrom}</Badge>{/if}
    </div>
    <div class="flex gap-1.5">
      <Button variant="outline" size="xs" aria-label={ui.text("ui_message_heading_rename")} title={ui.text("ui_message_heading_rename_title")} onclick={onrename}>
        <PencilIcon data-icon="inline-start" />
        <span class="hidden min-[360px]:inline">{ui.text("ui_message_heading_rename")}</span>
      </Button>
      <Button variant="outline" size="xs" aria-label={ui.text("ui_message_heading_duplicate")} title={ui.text("ui_message_heading_duplicate_title")} onclick={onduplicate}>
        <CopyIcon data-icon="inline-start" />
        <span class="hidden min-[360px]:inline">{ui.text("ui_message_heading_duplicate")}</span>
      </Button>
      <Button variant="destructive" size="xs" aria-label={ui.text("ui_message_heading_delete")} title={ui.text("ui_message_heading_delete_title")} onclick={ondelete}>
        <Trash2Icon data-icon="inline-start" />
        <span class="hidden min-[360px]:inline">{ui.text("ui_message_heading_delete")}</span>
      </Button>
    </div>
  </div>
</header>
