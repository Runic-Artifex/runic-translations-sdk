<script lang="ts">
  import AlertCircleIcon from "@lucide/svelte/icons/circle-alert";
  import CheckCircle2Icon from "@lucide/svelte/icons/circle-check-big";
  import ChevronRightIcon from "@lucide/svelte/icons/chevron-right";
  import TriangleAlertIcon from "@lucide/svelte/icons/triangle-alert";
  import * as Alert from "#lib/components/ui/alert/index.js";
  import { Badge } from "#lib/components/ui/badge/index.js";
  import { Button } from "#lib/components/ui/button/index.js";
  import { Spinner } from "#lib/components/ui/spinner/index.js";
  import type { EditorDiagnostic, EditorDiagnosticQuickFix } from "#lib/contracts.js";
  import {
    errorsFirst, groupDiagnostics, messageDisplayName, placeDiagnostics,
    type DiagnosticSelection, type DiagnosticSources, type PlacedDiagnostic,
  } from "#lib/diagnostic-scope.js";
  import { diagnosticText } from "#lib/review-model.js";
  import { getUiText, displayNotice } from "#lib/ui-text.js";

  interface Props {
    busy: boolean;
    diagnostics: EditorDiagnostic[];
    clientError: string | undefined;
    errorCount: number;
    warningCount: number;
    validLabel: string;
    invalidLabel: string;
    diagnosticsLabel: string;
    schemaVersion: number;
    /** The documents the diagnostics point into, to name a diagnostic's message and its line in it. */
    sources?: DiagnosticSources;
    /**
     * The message being translated: its diagnostics are listed first, then every other error (always shown, as each
     * blocks saving), then the other warnings under a collapsed disclosure. Without one, every diagnostic is listed,
     * errors first.
     */
    selection?: DiagnosticSelection;
    onselect: (diagnostic: EditorDiagnostic) => void;
    onfix?: (diagnostic: EditorDiagnostic, fix: EditorDiagnosticQuickFix) => void;
  }

  let {
    busy,
    diagnostics,
    clientError,
    errorCount,
    warningCount,
    validLabel,
    invalidLabel,
    diagnosticsLabel,
    schemaVersion,
    sources,
    selection,
    onselect,
    onfix,
  }: Props = $props();

  const ui = getUiText();
  const ids = $props.id();
  let otherExpanded = $state(false);

  let invalid = $derived(errorCount > 0 || clientError !== undefined);
  let placed = $derived(placeDiagnostics(diagnostics, sources));
  let groups = $derived(groupDiagnostics(placed, selection));
  let flat = $derived(selection === undefined ? errorsFirst(placed, (item) => item.diagnostic) : []);
  let selectionName = $derived(selection === undefined ? "" : messageDisplayName(selection.key, sources));
  let messageErrors = $derived(groups.message.filter((item) => item.diagnostic.severity === "error").length);
  let messageWarnings = $derived(groups.message.filter((item) => item.diagnostic.severity === "warning").length);

  // Parser diagnostics may legitimately share code and source coordinates,
  // including identical messages. Occurrences distinguish rows without dropping
  // diagnostics, while stable identities preserve focus across equivalent loads.
  function rowsOf(items: readonly PlacedDiagnostic[]): { key: string; item: PlacedDiagnostic }[] {
    const occurrences = new Map<string, number>();
    return items.map((item) => {
      const { diagnostic } = item;
      const identity = JSON.stringify([
        diagnostic.path, diagnostic.id, diagnostic.severity,
        diagnostic.line, diagnostic.column, diagnostic.endLine, diagnostic.endColumn,
        diagnostic.message, diagnostic.notice,
      ]);
      const occurrence = occurrences.get(identity) ?? 0;
      occurrences.set(identity, occurrence + 1);
      return { key: `${identity}:${occurrence}`, item };
    });
  }

  /** Where a diagnostic is, for a translator: the message and the line in it. The file position comes second. */
  function place(item: PlacedDiagnostic, inSelection: boolean): string | undefined {
    if (item.message === undefined || item.locale === undefined) return undefined;
    if (inSelection) return item.messageLine === undefined ? undefined : ui.text("ui_validation_place_line", { line: item.messageLine });
    return item.messageLine === undefined
      ? ui.text("ui_validation_place_message", { message: item.message, locale: item.locale })
      : ui.text("ui_validation_place_message_line", { message: item.message, locale: item.locale, line: item.messageLine });
  }

  const filePosition = (diagnostic: EditorDiagnostic): string =>
    ui.text("ui_validation_file_position", { path: diagnostic.path, line: diagnostic.line, column: diagnostic.column });
</script>

{#snippet items(list: readonly PlacedDiagnostic[], inSelection: boolean)}
  {#each rowsOf(list) as { key, item } (key)}
    {@const diagnostic = item.diagnostic}
    {@const where = place(item, inSelection)}
    <Button
      variant="ghost"
      class="grid h-auto w-full grid-cols-[auto_minmax(0,1fr)] items-start justify-start gap-x-3 gap-y-1 rounded-none px-4 py-3 text-left whitespace-normal @md:grid-cols-[auto_minmax(0,1fr)_auto]"
      title={filePosition(diagnostic)}
      data-severity={diagnostic.severity}
      onclick={() => onselect(diagnostic)}
    >
      {#if diagnostic.severity === "error"}
        <AlertCircleIcon class="mt-0.5 size-4 text-destructive" aria-hidden="true" />
      {:else}
        <TriangleAlertIcon class="mt-0.5 size-4 text-warning" aria-hidden="true" />
      {/if}
      <span class="min-w-0 text-xs leading-5 text-muted-foreground">
        <strong class={["mr-2 font-mono", diagnostic.severity === "error" ? "text-destructive" : diagnostic.severity === "warning" ? "text-warning" : "text-foreground"]}>{diagnostic.id}</strong>{displayNotice(diagnosticText(diagnostic), ui)}
      </span>
      <span class="col-start-2 grid min-w-0 gap-0.5 text-[0.65rem] text-muted-foreground @md:col-start-auto @md:text-right" data-testid="diagnostic-place">
        {#if where !== undefined}<span class="font-medium text-foreground/80">{where}</span>{/if}
        <code class="break-all @md:whitespace-nowrap">{diagnostic.path}:{diagnostic.line}:{diagnostic.column}</code>
      </span>
    </Button>
    {#if onfix !== undefined && diagnostic.quickFixes !== undefined}
      <div class="flex flex-wrap gap-2 px-4 pb-3">
        {#each diagnostic.quickFixes as fix (fix.id)}
          <Button variant="outline" size="xs" disabled={busy} onclick={() => onfix?.(diagnostic, fix)}>{fix.title}</Button>
        {/each}
      </div>
    {/if}
  {/each}
{/snippet}

<Alert.Root
  variant={invalid ? "destructive" : "default"}
  class="@container gap-0 overflow-hidden p-0"
  aria-live="polite"
>
  <header class="flex flex-wrap items-center justify-between gap-3 px-4 py-3">
    <div class="flex min-w-0 items-center gap-3">
      {#if busy}
        <Spinner class="size-5 shrink-0 text-primary" aria-label={ui.text("ui_validation_validating")} />
      {:else if invalid}
        <AlertCircleIcon class="size-5 shrink-0" aria-hidden="true" />
      {:else}
        <CheckCircle2Icon class="size-5 shrink-0 text-success" aria-hidden="true" />
      {/if}
      <div class="min-w-0">
        <Alert.Title class="text-xs font-semibold">
          {busy ? ui.text("ui_validation_validating_with_compiler") : invalid ? invalidLabel : validLabel}
        </Alert.Title>
        <Alert.Description class="text-xs">
          {diagnosticsLabel} · {ui.text("ui_count_errors", { count: errorCount })} · {ui.text("ui_count_warnings", { count: warningCount })}{#if selection !== undefined && diagnostics.length > 0}{" · "}<span data-testid="diagnostics-message-counts">{ui.text("ui_validation_message_counts", {
              errors: ui.text("ui_count_errors", { count: messageErrors }),
              warnings: ui.text("ui_count_warnings", { count: messageWarnings }),
            })}</span>{/if}
        </Alert.Description>
      </div>
    </div>
    <Badge variant="outline" class="font-mono text-[0.7rem]">{ui.text("ui_validation_compiler")} · {ui.text("ui_validation_schema")} v{schemaVersion}</Badge>
  </header>

  {#if clientError}
    <div class="border-t border-destructive/30 bg-destructive/10 px-4 py-3 text-xs text-destructive">
      {clientError}
    </div>
  {/if}

  {#if diagnostics.length > 0}
    {#if selection === undefined}
      <div class="divide-y border-t">
        {@render items(flat, false)}
      </div>
    {:else}
      <section class="border-t" aria-labelledby="{ids}-message" data-testid="diagnostics-message">
        <h3 id="{ids}-message" class="px-4 pt-3 pb-1 text-xs font-semibold text-foreground">
          {ui.text("ui_validation_message_heading", { message: selectionName, locale: selection.locale })}
        </h3>
        {#if groups.message.length === 0}
          <p class="px-4 pb-3 text-xs text-muted-foreground">{ui.text("ui_validation_message_none")}</p>
        {:else}
          <div class="divide-y">{@render items(groups.message, true)}</div>
        {/if}
      </section>
      {#if groups.blocking.length > 0}
        <section class="border-t" aria-labelledby="{ids}-blocking" data-testid="diagnostics-blocking">
          <h3 id="{ids}-blocking" class="px-4 pt-3 pb-1 text-xs font-semibold text-foreground">{ui.text("ui_validation_blocking_heading")}</h3>
          <div class="divide-y">{@render items(groups.blocking, false)}</div>
        </section>
      {/if}
      {#if groups.other.length > 0}
        <section class="border-t" data-testid="diagnostics-other">
          <h3>
            <Button
              variant="ghost"
              class="h-auto w-full justify-start gap-2 rounded-none px-4 py-3 text-left text-xs font-semibold text-foreground hover:text-foreground"
              data-testid="diagnostics-other-toggle"
              aria-expanded={otherExpanded}
              aria-controls="{ids}-other"
              onclick={() => otherExpanded = !otherExpanded}
            >
              <ChevronRightIcon class="size-4 shrink-0 transition-transform {otherExpanded ? 'rotate-90' : ''}" aria-hidden="true" />
              {ui.text("ui_validation_other_warnings_heading", { count: groups.other.length })}
            </Button>
          </h3>
          <div id="{ids}-other" class="divide-y border-t" hidden={!otherExpanded}>
            {#if otherExpanded}{@render items(groups.other, false)}{/if}
          </div>
        </section>
      {/if}
    {/if}
  {/if}
</Alert.Root>
