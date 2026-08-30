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
    `${added} ${ui.text("Ui.Interchange.Added")} · ${changed} ${ui.text("Ui.Interchange.Changed")} · ${removed} ${ui.text("Ui.Interchange.AbsentFromImport")}`;
</script>

<AppDialog
  {open}
  title={ui.text("Ui.Interchange.Title")}
  description={ui.text("Ui.Interchange.Description")}
  class="sm:max-w-5xl"
  onopenchange={(next) => open = next}
>
  <div class="grid gap-6 lg:grid-cols-2">
    <section class="grid content-start gap-3 rounded-lg border p-4">
      <header>
        <h3 class="font-medium">{ui.text("Ui.Interchange.XliffTitle")}</h3>
        <p class="text-sm text-muted-foreground">{ui.text("Ui.Interchange.XliffDescription")}</p>
      </header>
      <Field.Field>
        <Field.Label for="xliff-export-directory">{ui.text("Ui.Interchange.ExportDirectory")}</Field.Label>
        <Input id="xliff-export-directory" bind:value={xliffDirectory} placeholder="interchange/xliff (default)" autocomplete="off" />
      </Field.Field>
      <Button variant="outline" disabled={busy} onclick={onexportxliff}>{ui.text("Ui.Interchange.ExportXliff")}</Button>
      {#if xliffExport !== undefined}
        <section class="grid gap-2" aria-live="polite">
          <p class={xliffExport.ok ? "text-sm text-emerald-700 dark:text-emerald-400" : "text-sm text-destructive"}>{xliffExport.message ?? (xliffExport.ok ? `${ui.text("Ui.Interchange.Exported")} ${xliffExport.documents.length} ${ui.text("Ui.Interchange.Files")}.` : ui.text("Ui.Interchange.XliffExportFailed"))}</p>
          {#if xliffExport.documents.length > 0}
            <ul class="text-sm">{#each xliffExport.documents as file (file.path)}<li><code>{file.path}</code> <span class="text-muted-foreground">· {file.locale} · {file.byteCount} {ui.text("Ui.Interchange.Bytes")}</span></li>{/each}</ul>
          {/if}
          {#if xliffExport.losses.length > 0}
            <Alert.Root variant={xliffExport.lossless ? "default" : "destructive"}>
              <Alert.Title>{xliffExport.lossless ? ui.text("Ui.Interchange.NonSemanticNotes") : ui.text("Ui.Interchange.SemanticLoss")}</Alert.Title>
              <Alert.Description><ul>{#each xliffExport.losses as loss (`${loss.code}:${loss.location}`)}<li><Badge variant="outline">{loss.code}</Badge> {loss.message} <span class="text-muted-foreground">({loss.location})</span></li>{/each}</ul></Alert.Description>
            </Alert.Root>
          {/if}
        </section>
      {/if}
      <Field.Field>
        <Field.Label for="xliff-import-path">{ui.text("Ui.Interchange.XliffFileToImport")}</Field.Label>
        <Input id="xliff-import-path" bind:value={xliffImportPath} placeholder="interchange/xliff/catalog.fr.xlf" autocomplete="off" />
      </Field.Field>
      <Button disabled={busy || xliffImportPath.trim() === ""} onclick={onpreviewxliff}>{#if busy}<Spinner data-icon="inline-start" />{/if}{ui.text("Ui.Interchange.PreviewXliffImport")}</Button>
      {#if xliffPreview !== undefined}
        <section class="grid gap-2" aria-live="polite">
          <p class={xliffPreview.ok ? "text-sm text-muted-foreground" : "text-sm text-destructive"}>{xliffPreview.message ?? (xliffPreview.ok ? `${xliffPreview.targetLocale ?? ui.text("Ui.Interchange.Target")} · ${xliffPreview.layer ?? ui.text("Ui.Interchange.DefaultLayer")} · ${summary(xliffPreview.addedCount, xliffPreview.changedCount, xliffPreview.removedCount)}` : ui.text("Ui.Interchange.XliffImportRefused"))}</p>
          {#if xliffPreview.refusals.length > 0}
            <Alert.Root variant="destructive"><Alert.Title>{ui.text("Ui.Interchange.ImportRefusal")}</Alert.Title><Alert.Description><ul>{#each xliffPreview.refusals as refusal (refusal.code)}<li><Badge variant="destructive">{refusal.code}</Badge> {refusal.message}</li>{/each}</ul></Alert.Description></Alert.Root>
          {/if}
          {#if xliffPreview.changes.length > 0}
            <div class="max-h-48 overflow-auto rounded border text-sm"><table><thead><tr><th>{ui.text("Ui.Interchange.Key")}</th><th>{ui.text("Ui.Interchange.Change")}</th><th>{ui.text("Ui.Interchange.BeforeAfter")}</th></tr></thead><tbody>{#each xliffPreview.changes as change (`${change.key}:${change.kind}`)}<tr><td><code>{change.key}</code></td><td>{change.kind}</td><td>{change.kind === "state-change" ? `${change.stateBefore ?? "draft"} → ${change.stateAfter ?? "draft"}` : `${change.before ?? "—"} → ${change.after ?? "—"}`}</td></tr>{/each}</tbody></table></div>
          {/if}
          {#if xliffPreview.changesOverflowed}<p class="text-sm text-muted-foreground">{ui.text("Ui.Interchange.FirstChangesOnly")}</p>{/if}
          {#if xliffPreview.ok}<Button disabled={busy || xliffPreview.confirmationToken === undefined} onclick={onapplyxliff}>{ui.text("Ui.Interchange.ApplyXliffOnce")}</Button>{/if}
        </section>
      {/if}
    </section>

    <section class="grid content-start gap-3 rounded-lg border p-4">
      <header>
        <h3 class="font-medium">{ui.text("Ui.Interchange.ReviewTitle")}</h3>
        <p class="text-sm text-muted-foreground">{ui.text("Ui.Interchange.ReviewDescription")}</p>
      </header>
      <Field.Field>
        <Field.Label for="review-export-path">{ui.text("Ui.Interchange.ReviewExportPath")}</Field.Label>
        <Input id="review-export-path" bind:value={reviewPath} placeholder="interchange/review.json (default)" autocomplete="off" />
      </Field.Field>
      <Button variant="outline" disabled={busy} onclick={onexportreview}>{ui.text("Ui.Interchange.ExportReviewJson")}</Button>
      {#if reviewExport !== undefined}<p class={reviewExport.ok ? "text-sm text-emerald-700 dark:text-emerald-400" : "text-sm text-destructive"}>{reviewExport.message ?? (reviewExport.ok ? `${ui.text("Ui.Interchange.Exported")} ${reviewExport.entryCount} ${ui.text("Ui.Interchange.ReviewEntries")} ${ui.text("Ui.Interchange.To")} ${reviewExport.path}.` : ui.text("Ui.Interchange.ReviewExportFailed"))}</p>{/if}
      <Field.Field>
        <Field.Label for="review-import-path">{ui.text("Ui.Interchange.ReviewFileToImport")}</Field.Label>
        <Input id="review-import-path" bind:value={reviewImportPath} placeholder="interchange/review.json" autocomplete="off" />
      </Field.Field>
      <Button disabled={busy || reviewImportPath.trim() === ""} onclick={onpreviewreview}>{#if busy}<Spinner data-icon="inline-start" />{/if}{ui.text("Ui.Interchange.PreviewReviewImport")}</Button>
      {#if reviewPreview !== undefined}
        <section class="grid gap-2" aria-live="polite">
          <p class={reviewPreview.ok ? "text-sm text-muted-foreground" : "text-sm text-destructive"}>{reviewPreview.message ?? (reviewPreview.ok ? summary(reviewPreview.addedCount, reviewPreview.changedCount, reviewPreview.removedCount) : ui.text("Ui.Interchange.ReviewImportRefused"))}</p>
          {#if reviewPreview.refusals.length > 0}
            <Alert.Root variant="destructive"><Alert.Title>{ui.text("Ui.Interchange.ImportRefusal")}</Alert.Title><Alert.Description><ul>{#each reviewPreview.refusals as refusal (refusal.code)}<li><Badge variant="destructive">{refusal.code}</Badge> {refusal.message}</li>{/each}</ul></Alert.Description></Alert.Root>
          {/if}
          {#if reviewPreview.changes.length > 0}
            <div class="max-h-48 overflow-auto rounded border text-sm"><table><thead><tr><th>{ui.text("Ui.Interchange.Key")}</th><th>{ui.text("Ui.Interchange.Locale")}</th><th>{ui.text("Ui.Interchange.Change")}</th><th>{ui.text("Ui.Interchange.State")}</th></tr></thead><tbody>{#each reviewPreview.changes as change (`${change.key}:${change.locale}:${change.kind}`)}<tr><td><code>{change.key}</code></td><td>{change.locale}</td><td>{change.kind}</td><td>{change.stateBefore ?? "—"} → {change.stateAfter ?? "—"}</td></tr>{/each}</tbody></table></div>
          {/if}
          {#if reviewPreview.changesOverflowed}<p class="text-sm text-muted-foreground">{ui.text("Ui.Interchange.FirstChangesOnly")}</p>{/if}
          {#if reviewPreview.ok}<Button disabled={busy || reviewPreview.confirmationToken === undefined} onclick={onapplyreview}>{ui.text("Ui.Interchange.ApplyReviewOnce")}</Button>{/if}
        </section>
      {/if}
    </section>
  </div>
  {#snippet footer()}<Button variant="outline" disabled={busy} onclick={() => open = false}>{ui.text("Ui.Common.Close")}</Button>{/snippet}
</AppDialog>

<style>
  table { width: 100%; border-collapse: collapse; }
  th, td { padding: 0.35rem 0.5rem; text-align: left; vertical-align: top; }
  th { position: sticky; top: 0; background: var(--background); font-weight: 600; }
  tr + tr td { border-top: 1px solid var(--border); }
</style>
