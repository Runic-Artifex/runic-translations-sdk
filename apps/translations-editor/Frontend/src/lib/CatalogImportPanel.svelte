<script lang="ts">
  import type { EditorCatalogImportPlan } from "#lib/contracts.js";
  import * as Alert from "#lib/components/ui/alert/index.js";
  import { Badge } from "#lib/components/ui/badge/index.js";
  import { Button } from "#lib/components/ui/button/index.js";
  import { Checkbox } from "#lib/components/ui/checkbox/index.js";
  import * as Field from "#lib/components/ui/field/index.js";
  import { Input } from "#lib/components/ui/input/index.js";
  import { Spinner } from "#lib/components/ui/spinner/index.js";
  import { getUiText, displayNotice } from "#lib/ui-text.js";

  // Import from .resx or i18next through the runic-translations importer (L5-26): the dry run lists every message's
  // outcome and the files it writes; "Import once" applies exactly that plan.
  let {
    busy = false,
    from = $bindable<"resx" | "i18next">("resx"),
    source = $bindable(""),
    overwrite = $bindable(false),
    plan,
    oncheck,
    onapply,
  }: {
    busy?: boolean;
    from?: "resx" | "i18next";
    source?: string;
    overwrite?: boolean;
    plan?: EditorCatalogImportPlan;
    oncheck(): void;
    onapply(): void;
  } = $props();

  const ui = getUiText();
</script>

<section class="grid content-start gap-3 rounded-lg border p-4" aria-label={ui.text("ui_catalog_import_title")}>
  <header>
    <h3 class="font-medium">{ui.text("ui_catalog_import_title")}</h3>
    <p class="text-sm text-muted-foreground">{ui.text("ui_catalog_import_description")}</p>
  </header>
  <div class="grid gap-3 sm:grid-cols-[minmax(0,14rem)_1fr]">
    <Field.Field>
      <Field.Label for="catalog-import-format">{ui.text("ui_catalog_import_format")}</Field.Label>
      <select id="catalog-import-format" class="h-9 rounded-md border bg-background px-2 text-sm" bind:value={from} title={from === "resx" ? ui.text("ui_catalog_import_format_resx") : ui.text("ui_catalog_import_format_i18next")}>
        <option value="resx">.resx</option>
        <option value="i18next">i18next</option>
      </select>
    </Field.Field>
    <Field.Field>
      <Field.Label for="catalog-import-source">{ui.text("ui_catalog_import_source")}</Field.Label>
      <Input id="catalog-import-source" bind:value={source} placeholder={from === "resx" ? "../App/Properties/Strings.resx" : "../web/public/locales"} autocomplete="off" />
      <Field.Description>{from === "resx" ? ui.text("ui_catalog_import_format_resx") : ui.text("ui_catalog_import_format_i18next")}</Field.Description>
    </Field.Field>
  </div>
  <Field.Field orientation="horizontal">
    <Checkbox id="catalog-import-overwrite" bind:checked={overwrite} />
    <Field.Label for="catalog-import-overwrite">{ui.text("ui_catalog_import_overwrite")}</Field.Label>
  </Field.Field>
  <Button class="justify-self-start" disabled={busy || source.trim() === ""} onclick={oncheck}>{#if busy}<Spinner data-icon="inline-start" />{/if}{ui.text("ui_catalog_import_check")}</Button>
  {#if plan !== undefined}
    <section class="grid gap-2" aria-live="polite">
      {#if !plan.ok}
        <Alert.Root variant="destructive">
          <Alert.Title>{plan.message === undefined ? ui.text("ui_backend_import_messages_invalid") : displayNotice(plan.message, ui)}</Alert.Title>
          {#if plan.diagnostics.length > 0}<Alert.Description><ul>{#each plan.diagnostics as diagnostic, index (index)}<li><Badge variant="destructive">{diagnostic.id}</Badge> {diagnostic.message} <span class="text-muted-foreground">({diagnostic.path}:{diagnostic.line})</span></li>{/each}</ul></Alert.Description>{/if}
        </Alert.Root>
      {:else}
        {#if plan.diagnostics.length > 0}
          <Alert.Root>
            <Alert.Title>{ui.text("ui_catalog_import_preexisting")}</Alert.Title>
            <Alert.Description><ul>{#each plan.diagnostics as diagnostic, index (index)}<li><Badge variant="outline">{diagnostic.id}</Badge> {diagnostic.message} <span class="text-muted-foreground">({diagnostic.path}:{diagnostic.line})</span></li>{/each}</ul></Alert.Description>
          </Alert.Root>
        {/if}
        <ul class="text-sm">
          {#each plan.locales as locale (locale.locale)}<li>{ui.text("ui_catalog_import_locale_summary", { locale: locale.locale, added: String(locale.added), changed: String(locale.changed), unchanged: String(locale.unchanged), kept: String(locale.kept) })}</li>{/each}
          <li class="text-muted-foreground">{ui.text("ui_catalog_import_totals", { skipped: String(plan.skipped), refused: String(plan.refused) })}</li>
        </ul>
        {#if plan.files.length > 0}
          <div class="text-sm"><span class="text-muted-foreground">{ui.text("ui_catalog_import_files")}:</span> {#each plan.files as file (file)}<code class="mr-2">{file}</code>{/each}</div>
        {/if}
        {#if plan.entries.length > 0}
          <div class="max-h-56 overflow-auto rounded border text-sm"><table><thead><tr><th>{ui.text("ui_interchange_key")}</th><th>{ui.text("ui_interchange_locale")}</th><th>{ui.text("ui_catalog_import_outcome")}</th><th>{ui.text("ui_catalog_import_reason")}</th></tr></thead><tbody>{#each plan.entries as entry, index (index)}<tr><td><code>{entry.key}</code></td><td>{entry.locale}</td><td><Badge variant={entry.outcome === "refused" ? "destructive" : "outline"}>{entry.outcome}</Badge></td><td>{entry.code ?? ""} {entry.message ?? ""}</td></tr>{/each}</tbody></table></div>
        {/if}
        {#if plan.entriesOverflowed}<p class="text-sm text-muted-foreground">{ui.text("ui_catalog_import_first_entries")}</p>{/if}
        {#if plan.confirmationToken === undefined}
          <p class="text-sm text-muted-foreground">{ui.text("ui_catalog_import_nothing")}</p>
        {:else}
          <Button class="justify-self-start" disabled={busy} onclick={onapply}>{ui.text("ui_catalog_import_apply")}</Button>
        {/if}
      {/if}
    </section>
  {/if}
</section>

<style>
  table { width: 100%; border-collapse: collapse; }
  th, td { padding: 0.35rem 0.5rem; text-align: left; vertical-align: top; }
  th { position: sticky; top: 0; background: var(--background); font-weight: 600; }
  tr + tr td { border-top: 1px solid var(--border); }
</style>
