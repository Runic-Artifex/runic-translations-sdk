<script lang="ts">
  import DownloadIcon from "@lucide/svelte/icons/download";
  import SpellCheckIcon from "@lucide/svelte/icons/spell-check";
  import { Button } from "#lib/components/ui/button/index.js";
  import { localeDisplayName } from "./locale-text";
  import { getSpelling } from "./state/spelling.svelte";
  import { getUiText } from "./ui-text";

  /**
   * What the composer says about the dictionary of the language being edited (D086.2): an offer to download it, which
   * the user answers, for any language but the system's; the progress of a download, also of the system language's,
   * which starts on its own; and a failed download.
   */
  let { locale }: { locale: string } = $props();

  const ui = getUiText();
  const spelling = getSpelling();
  let notice = $derived(spelling?.notice(locale));
  let uiLocale = $derived(spelling?.uiLocale ?? "en");
  let language = $derived(localeDisplayName(locale, uiLocale));
  let busy = $state(false);

  function megabytes(bytes: number): string {
    return new Intl.NumberFormat(uiLocale, { maximumFractionDigits: 1 }).format(bytes / 1_000_000);
  }

  async function download(): Promise<void> {
    busy = true;
    try {
      await spelling?.download(locale);
    } finally {
      busy = false;
    }
  }
</script>

{#if notice !== undefined}
  <div class="flex flex-wrap items-center gap-2 rounded-pane border bg-muted/30 px-3 py-2 text-sm" role="status" data-spelling-notice={notice.kind}>
    <SpellCheckIcon class="size-4 shrink-0 text-muted-foreground" aria-hidden="true" />
    {#if notice.kind === "offer"}
      <span class="min-w-0 flex-1">{ui.text("ui_spelling_offer", { language, size: megabytes(notice.size) })}</span>
      <Button size="xs" disabled={busy} onclick={() => void download()}>
        <DownloadIcon data-icon="inline-start" aria-hidden="true" />
        {ui.text("ui_spelling_download")}
      </Button>
      <Button variant="ghost" size="xs" onclick={() => spelling?.decline(locale)}>{ui.text("ui_spelling_not_now")}</Button>
    {:else if notice.kind === "downloading"}
      <span class="min-w-0 flex-1">{ui.text(notice.automatic ? "ui_spelling_downloading_automatic" : "ui_spelling_downloading", { language, percent: notice.percent })}</span>
    {:else}
      <span class="min-w-0 flex-1">{ui.text("ui_spelling_failed", { language })}</span>
      <Button variant="secondary" size="xs" disabled={busy} onclick={() => void download()}>{ui.text("ui_spelling_retry")}</Button>
    {/if}
  </div>
{/if}
