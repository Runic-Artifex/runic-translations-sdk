<script lang="ts">
  import AppDialog from "#lib/AppDialog.svelte";
  import { Button } from "#lib/components/ui/button/index.js";
  import { Textarea } from "#lib/components/ui/textarea/index.js";
  import { qualityReportCsv, type QualityIssue } from "#lib/review-model.js";
  import { getUiText } from "#lib/ui-text.js";

  let {
    open = $bindable(false),
    locale,
    findings,
    messageCount,
  }: {
    open?: boolean;
    locale: string;
    /** The quality findings of the locale, as the report lists them. */
    findings: QualityIssue[];
    /** How many messages have findings. */
    messageCount: number;
  } = $props();
  const ui = getUiText();
</script>

{#if open}
  <AppDialog
    open
    title={ui.text("ui_quality_report_locale", { locale })}
    description={ui.text("ui_count_quality_summary", { findings: findings.length, messages: messageCount })}
    class="sm:max-w-4xl"
    onopenchange={(value) => open = value}
  >
    <Textarea class="min-h-[26rem] font-mono text-xs" aria-label={ui.text("ui_page_quality_csv_aria_label")} readonly value={qualityReportCsv(findings)} />
    {#snippet footer()}<Button variant="outline" onclick={() => open = false}>{ui.text("ui_page_close")}</Button>{/snippet}
  </AppDialog>
{/if}
