<script lang="ts">
  import type {
    EditorReviewFileResult,
    EditorReviewImportPreview,
    EditorXliffExportResult,
    EditorXliffImportPreview,
  } from "$lib/contracts";
  import AppDialog from "$lib/AppDialog.svelte";
  import * as Alert from "$lib/components/ui/alert/index.js";
  import { Badge } from "$lib/components/ui/badge/index.js";
  import { Button } from "$lib/components/ui/button/index.js";
  import * as Field from "$lib/components/ui/field/index.js";
  import { Input } from "$lib/components/ui/input/index.js";
  import { Spinner } from "$lib/components/ui/spinner/index.js";
  import { getUiText } from "$lib/ui-text";

  let {
    open = $bindable(false),
    busy = false,
    xliffDirectory = $bindable(""),
    xliffImportPath = $bindable(""),
    reviewPath = $bindable(""),
    reviewImportPath = $bindable(""),
    xliffExport,
    xliffPreview,
    reviewExport,
    reviewPreview,
    onexportxliff,
    onpreviewxliff,
    onapplyxliff,
    onexportreview,
    onpreviewreview,
    onapplyreview,
  }: {
    open?: boolean;
    busy?: boolean;
    xliffDirectory?: string;
    xliffImportPath?: string;
    reviewPath?: string;
    reviewImportPath?: string;
    xliffExport?: EditorXliffExportResult;
    xliffPreview?: EditorXliffImportPreview;
    reviewExport?: EditorReviewFileResult;
    reviewPreview?: EditorReviewImportPreview;
    onexportxliff(): void;
    onpreviewxliff(): void;
    onapplyxliff(): void;
    onexportreview(): void;
    onpreviewreview(): void;
    onapplyreview(): void;
  } = $props();

  const ui = getUiText();

  const summary = (added: number, changed: number, removed: number): string =>
    `${added} ${ui.text("ui_interchange_added")} · ${changed} ${ui.text("ui_interchange_changed")} · ${removed} ${ui.text("ui_interchange_absent_from_import")}`;
</script>

<AppDialog
  {open}
  title={ui.text("ui_interchange_title")}
  description={ui.text("ui_interchange_description")}
  class="sm:max-w-5xl"
  onopenchange={(next) => open = next}
>
  <div class="grid gap-6 lg:grid-cols-2">
    <section class="grid content-start gap-3 rounded-lg border p-4">
      <header>
        <h3 class="font-medium">{ui.text("ui_interchange_xliff_title")}</h3>
        <p class="text-sm text-muted-foreground">{ui.text("ui_interchange_xliff_description")}</p>
      </header>
      <Field.Field>
        <Field.Label for="xliff-export-directory">{ui.text("ui_interchange_export_directory")}</Field.Label>
        <Input id="xliff-export-directory" bind:value={xliffDirectory} placeholder="interchange/xliff (default)" autocomplete="off" />
      </Field.Field>
      <Button variant="outline" disabled={busy} onclick={onexportxliff}>{ui.text("ui_interchange_export_xliff")}</Button>
      {#if xliffExport !== undefined}
        <section class="grid gap-2" aria-live="polite">
          <p class={xliffExport.ok ? "text-sm text-emerald-700 dark:text-emerald-400" : "text-sm text-destructive"}>{xliffExport.message ?? (xliffExport.ok ? `${ui.text("ui_interchange_exported")} ${xliffExport.documents.length} ${ui.text("ui_interchange_files")}.` : ui.text("ui_interchange_xliff_export_failed"))}</p>
          {#if xliffExport.documents.length > 0}
            <ul class="text-sm">{#each xliffExport.documents as file (file.path)}<li><code>{file.path}</code> <span class="text-muted-foreground">· {file.locale} · {file.byteCount} {ui.text("ui_interchange_bytes")}</span></li>{/each}</ul>
          {/if}
          {#if xliffExport.losses.length > 0}
            <Alert.Root variant={xliffExport.lossless ? "default" : "destructive"}>
              <Alert.Title>{xliffExport.lossless ? ui.text("ui_interchange_non_semantic_notes") : ui.text("ui_interchange_semantic_loss")}</Alert.Title>
              <Alert.Description><ul>{#each xliffExport.losses as loss (`${loss.code}:${loss.location}`)}<li><Badge variant="outline">{loss.code}</Badge> {loss.message} <span class="text-muted-foreground">({loss.location})</span></li>{/each}</ul></Alert.Description>
            </Alert.Root>
          {/if}
        </section>
      {/if}
      <Field.Field>
        <Field.Label for="xliff-import-path">{ui.text("ui_interchange_xliff_file_to_import")}</Field.Label>
        <Input id="xliff-import-path" bind:value={xliffImportPath} placeholder="interchange/xliff/catalog.fr.xlf" autocomplete="off" />
      </Field.Field>
      <Button disabled={busy || xliffImportPath.trim() === ""} onclick={onpreviewxliff}>{#if busy}<Spinner data-icon="inline-start" />{/if}{ui.text("ui_interchange_preview_xliff_import")}</Button>
      {#if xliffPreview !== undefined}
        <section class="grid gap-2" aria-live="polite">
          <p class={xliffPreview.ok ? "text-sm text-muted-foreground" : "text-sm text-destructive"}>{xliffPreview.message ?? (xliffPreview.ok ? `${xliffPreview.targetLocale ?? ui.text("ui_interchange_target")} · ${xliffPreview.layer ?? ui.text("ui_interchange_default_layer")} · ${summary(xliffPreview.addedCount, xliffPreview.changedCount, xliffPreview.removedCount)}` : ui.text("ui_interchange_xliff_import_refused"))}</p>
          {#if xliffPreview.refusals.length > 0}
            <Alert.Root variant="destructive"><Alert.Title>{ui.text("ui_interchange_import_refusal")}</Alert.Title><Alert.Description><ul>{#each xliffPreview.refusals as refusal (refusal.code)}<li><Badge variant="destructive">{refusal.code}</Badge> {refusal.message}</li>{/each}</ul></Alert.Description></Alert.Root>
          {/if}
          {#if xliffPreview.changes.length > 0}
            <div class="max-h-48 overflow-auto rounded border text-sm"><table><thead><tr><th>{ui.text("ui_interchange_key")}</th><th>{ui.text("ui_interchange_change")}</th><th>{ui.text("ui_interchange_before_after")}</th></tr></thead><tbody>{#each xliffPreview.changes as change (`${change.key}:${change.kind}`)}<tr><td><code>{change.key}</code></td><td>{change.kind}</td><td>{change.kind === "state-change" ? `${change.stateBefore ?? "draft"} → ${change.stateAfter ?? "draft"}` : `${change.before ?? "—"} → ${change.after ?? "—"}`}</td></tr>{/each}</tbody></table></div>
          {/if}
          {#if xliffPreview.changesOverflowed}<p class="text-sm text-muted-foreground">{ui.text("ui_interchange_first_changes_only")}</p>{/if}
          {#if xliffPreview.ok}<Button disabled={busy || xliffPreview.confirmationToken === undefined} onclick={onapplyxliff}>{ui.text("ui_interchange_apply_xliff_once")}</Button>{/if}
        </section>
      {/if}
    </section>

    <section class="grid content-start gap-3 rounded-lg border p-4">
      <header>
        <h3 class="font-medium">{ui.text("ui_interchange_review_title")}</h3>
        <p class="text-sm text-muted-foreground">{ui.text("ui_interchange_review_description")}</p>
      </header>
      <Field.Field>
        <Field.Label for="review-export-path">{ui.text("ui_interchange_review_export_path")}</Field.Label>
        <Input id="review-export-path" bind:value={reviewPath} placeholder="interchange/review.json (default)" autocomplete="off" />
      </Field.Field>
      <Button variant="outline" disabled={busy} onclick={onexportreview}>{ui.text("ui_interchange_export_review_json")}</Button>
      {#if reviewExport !== undefined}<p class={reviewExport.ok ? "text-sm text-emerald-700 dark:text-emerald-400" : "text-sm text-destructive"}>{reviewExport.message ?? (reviewExport.ok ? `${ui.text("ui_interchange_exported")} ${reviewExport.entryCount} ${ui.text("ui_interchange_review_entries")} ${ui.text("ui_interchange_to")} ${reviewExport.path}.` : ui.text("ui_interchange_review_export_failed"))}</p>{/if}
      <Field.Field>
        <Field.Label for="review-import-path">{ui.text("ui_interchange_review_file_to_import")}</Field.Label>
        <Input id="review-import-path" bind:value={reviewImportPath} placeholder="interchange/review.json" autocomplete="off" />
      </Field.Field>
      <Button disabled={busy || reviewImportPath.trim() === ""} onclick={onpreviewreview}>{#if busy}<Spinner data-icon="inline-start" />{/if}{ui.text("ui_interchange_preview_review_import")}</Button>
      {#if reviewPreview !== undefined}
        <section class="grid gap-2" aria-live="polite">
          <p class={reviewPreview.ok ? "text-sm text-muted-foreground" : "text-sm text-destructive"}>{reviewPreview.message ?? (reviewPreview.ok ? summary(reviewPreview.addedCount, reviewPreview.changedCount, reviewPreview.removedCount) : ui.text("ui_interchange_review_import_refused"))}</p>
          {#if reviewPreview.refusals.length > 0}
            <Alert.Root variant="destructive"><Alert.Title>{ui.text("ui_interchange_import_refusal")}</Alert.Title><Alert.Description><ul>{#each reviewPreview.refusals as refusal (refusal.code)}<li><Badge variant="destructive">{refusal.code}</Badge> {refusal.message}</li>{/each}</ul></Alert.Description></Alert.Root>
          {/if}
          {#if reviewPreview.changes.length > 0}
            <div class="max-h-48 overflow-auto rounded border text-sm"><table><thead><tr><th>{ui.text("ui_interchange_key")}</th><th>{ui.text("ui_interchange_locale")}</th><th>{ui.text("ui_interchange_change")}</th><th>{ui.text("ui_interchange_state")}</th></tr></thead><tbody>{#each reviewPreview.changes as change (`${change.key}:${change.locale}:${change.kind}`)}<tr><td><code>{change.key}</code></td><td>{change.locale}</td><td>{change.kind}</td><td>{change.stateBefore ?? "—"} → {change.stateAfter ?? "—"}</td></tr>{/each}</tbody></table></div>
          {/if}
          {#if reviewPreview.changesOverflowed}<p class="text-sm text-muted-foreground">{ui.text("ui_interchange_first_changes_only")}</p>{/if}
          {#if reviewPreview.ok}<Button disabled={busy || reviewPreview.confirmationToken === undefined} onclick={onapplyreview}>{ui.text("ui_interchange_apply_review_once")}</Button>{/if}
        </section>
      {/if}
    </section>
  </div>
  {#snippet footer()}<Button variant="outline" disabled={busy} onclick={() => open = false}>{ui.text("ui_common_close")}</Button>{/snippet}
</AppDialog>

<style>
  table { width: 100%; border-collapse: collapse; }
  th, td { padding: 0.35rem 0.5rem; text-align: left; vertical-align: top; }
  th { position: sticky; top: 0; background: var(--background); font-weight: 600; }
  tr + tr td { border-top: 1px solid var(--border); }
</style>
