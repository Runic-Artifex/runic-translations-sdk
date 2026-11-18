<script lang="ts">
  import AppDialog from "#lib/AppDialog.svelte";
  import * as Alert from "#lib/components/ui/alert/index.js";
  import { Button } from "#lib/components/ui/button/index.js";
  import { Input } from "#lib/components/ui/input/index.js";
  import { Spinner } from "#lib/components/ui/spinner/index.js";
  import type { AboutController } from "#lib/state/about-controller.svelte.js";
  import { displayNotice, getUiText } from "#lib/ui-text.js";

  let { about }: { about: AboutController } = $props();
  const ui = getUiText();
</script>

{#if about.open}
  <AppDialog
    open
    title={about.info?.product ?? ui.text("ui_page_about_product")}
    description={ui.text("ui_page_about_description")}
    onopenchange={(open) => about.open = open}
  >
    <div class="grid gap-4">
      {#if about.busy}
        <div class="flex items-center gap-2 text-muted-foreground"><Spinner />{ui.text("ui_page_about_reading")}</div>
      {:else if about.info !== undefined}
        <dl class="grid overflow-hidden rounded-xl border">
          {#each [
            [ui.text("ui_page_about_version"), about.info.version],
            [ui.text("ui_page_about_update_channel"), about.info.updateChannel],
            [ui.text("ui_page_about_source_revision"), about.info.commit ?? ui.text("ui_page_about_development_build")],
            [ui.text("ui_page_about_runtime"), about.info.runtime],
            [ui.text("ui_page_about_runtime_identifier"), about.info.runtimeIdentifier],
            [ui.text("ui_page_about_system"), `${about.info.operatingSystem} · ${about.info.architecture}`],
          ] as item (item[0])}
            <div class="grid gap-1 border-b px-4 py-3 last:border-b-0 sm:grid-cols-[9rem_1fr] sm:gap-4">
              <dt class="text-muted-foreground">{item[0]}</dt><dd class="m-0 overflow-wrap-anywhere font-mono text-xs">{item[1]}</dd>
            </div>
          {/each}
        </dl>
      {/if}
      <Alert.Root>
        <Alert.Title>{ui.text("ui_page_about_diagnostic_bundle_title")}</Alert.Title>
        <Alert.Description>{ui.text("ui_page_about_diagnostic_bundle_description")}</Alert.Description>
        {#if about.diagnosticMessage}<p class="text-sm text-primary" aria-live="polite">{displayNotice(about.diagnosticMessage, ui)}</p>{/if}
      </Alert.Root>
      {#if about.diagnosticBundlePath !== undefined}
        <section class="grid gap-2 rounded-xl border p-4" aria-labelledby="diagnostic-bundle-actions-title">
          <h3 id="diagnostic-bundle-actions-title" class="font-medium">{ui.text("ui_page_about_diagnostic_bundle")}</h3>
          <Input aria-label={ui.text("ui_page_about_diagnostic_bundle_path")} readonly value={about.diagnosticBundlePath} class="font-mono text-xs" />
          <div class="flex flex-wrap gap-2">
            <Button variant="outline" size="sm" disabled={about.diagnosticBusy} onclick={() => void about.revealDiagnosticBundle()}>{ui.text("ui_page_about_reveal_location")}</Button>
            <Button variant="outline" size="sm" onclick={() => void about.copyDiagnosticBundlePath()}>{ui.text("ui_page_about_copy_path")}</Button>
            <Button variant="destructive" size="sm" disabled={about.diagnosticBusy} onclick={() => void about.deleteDiagnosticBundle()}>{ui.text("ui_page_about_delete_bundle")}</Button>
          </div>
          <p class="text-xs text-muted-foreground">{ui.text("ui_page_about_bundle_no_upload")}</p>
        </section>
      {/if}
      <section class="grid gap-3 rounded-xl border p-4" aria-labelledby="local-state-title">
        <div class="grid gap-1 sm:grid-cols-[1fr_auto] sm:items-center sm:gap-4">
          <div><h3 id="local-state-title" class="font-medium">{ui.text("ui_page_about_local_state")}</h3><p class="text-sm text-muted-foreground">{ui.text("ui_page_about_local_state_description")}</p></div>
          <Button variant="outline" size="sm" disabled={(about.localStateSummary?.entries ?? 0) === 0} onclick={() => about.clearLocalState()}>{ui.text("ui_page_about_clear_local_state")}</Button>
        </div>
        {#if about.localStateSummary !== undefined}
          <p class="text-sm text-muted-foreground">{ui.text("ui_count_state_entries", { count: about.localStateSummary.entries })} · {ui.text("ui_count_state_bytes", { count: about.localStateSummary.bytes })} · {ui.text("ui_count_state_preferences", { count: about.localStateSummary.preferenceEntries })} · {ui.text("ui_count_state_recent", { count: about.localStateSummary.recentProjectEntries })} · {ui.text("ui_count_state_drafts", { count: about.localStateSummary.draftEntries })}{about.localStateSummary.recovered ? ` · ${ui.text("ui_page_about_recovered_unreadable")}` : ""}</p>
        {/if}
        <p class="text-xs text-muted-foreground">{ui.text("ui_page_about_clear_local_state_note")}</p>
        {#if about.localStateMessage}<p class="text-sm text-primary" aria-live="polite">{displayNotice(about.localStateMessage, ui)}</p>{/if}
      </section>
      <p class="text-sm text-muted-foreground">{ui.text("ui_page_about_license_note")}</p>
    </div>
    {#snippet footer()}
      <Button variant="outline" onclick={() => about.open = false}>{ui.text("ui_page_close")}</Button>
      <Button disabled={about.diagnosticBusy || about.busy} onclick={() => void about.createDiagnosticBundle()}>
        {#if about.diagnosticBusy}<Spinner data-icon="inline-start" />{/if}
        {about.diagnosticBusy ? ui.text("ui_page_about_creating_bundle") : ui.text("ui_page_about_create_bundle")}
      </Button>
    {/snippet}
  </AppDialog>
{/if}
