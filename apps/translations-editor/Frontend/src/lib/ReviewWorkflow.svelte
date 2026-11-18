<script lang="ts">
  import BookOpenIcon from "@lucide/svelte/icons/book-open";
  import CheckCircle2Icon from "@lucide/svelte/icons/check-circle-2";
  import CircleCheckBigIcon from "@lucide/svelte/icons/circle-check-big";
  import FlagIcon from "@lucide/svelte/icons/flag";
  import ClipboardListIcon from "@lucide/svelte/icons/clipboard-list";
  import ChevronDownIcon from "@lucide/svelte/icons/chevron-down";
  import ListChecksIcon from "@lucide/svelte/icons/list-checks";
  import SparklesIcon from "@lucide/svelte/icons/sparkles";
  import TriangleAlertIcon from "@lucide/svelte/icons/triangle-alert";
  import Undo2Icon from "@lucide/svelte/icons/undo-2";
  import { Badge } from "#lib/components/ui/badge/index.js";
  import StateMark from "#lib/StateMark.svelte";
  import { reviewStateMark } from "#lib/state-mark.js";
  import { Button } from "#lib/components/ui/button/index.js";
  import * as Card from "#lib/components/ui/card/index.js";
  import * as Collapsible from "#lib/components/ui/collapsible/index.js";
  import * as Field from "#lib/components/ui/field/index.js";
  import * as Select from "#lib/components/ui/select/index.js";
  import { Textarea } from "#lib/components/ui/textarea/index.js";
  import type { EditorDiagnostic, EditorDiagnosticQuickFix, EditorReviewState } from "#lib/contracts.js";
  import type { QualityIssue, TranslationSuggestion } from "#lib/review-model.js";
  import { localeDirection } from "#lib/locale-text.js";
  import type { SourceDiff, SourceDiffPart } from "#lib/source-diff.js";
  import { getUiText } from "#lib/ui-text.js";

  const ui = getUiText();

  // Derived, so the labels follow a change of the interface language (#225 P23).
  const states: { value: EditorReviewState; label: string }[] = $derived([
    { value: "draft", label: ui.text("ui_workflow_draft") },
    { value: "translated", label: ui.text("ui_workflow_translated") },
    { value: "needs-review", label: ui.text("ui_workflow_needs_review") },
    { value: "approved", label: ui.text("ui_workflow_approved") },
  ]);

  let {
    state: reviewState,
    notTranslated = false,
    dirty,
    message,
    disabled,
    stale,
    sourceDiff,
    sourceLocale = "",
    terminologyCount,
    qualityCount,
    note,
    qualityIssues,
    suggestions,
    onstatechange,
    onconfirm,
    onnotechange,
    ondiscard,
    onterminology,
    onreport,
    onqualityfilter,
    onsuggestion,
    onfix,
  }: {
    state: EditorReviewState;
    /** The locale has no text for the message and no review state was recorded for it. */
    notTranslated?: boolean;
    dirty: boolean;
    message: string;
    disabled: boolean;
    stale: boolean;
    /** A stale state's recorded base text compared with the current one; absent when no writer recorded the text. */
    sourceDiff?: SourceDiff;
    /** The base locale, for the language and direction of the compared texts. */
    sourceLocale?: string;
    terminologyCount: number;
    qualityCount: number;
    note: string;
    qualityIssues: QualityIssue[];
    suggestions: TranslationSuggestion[];
    onstatechange: (state: EditorReviewState) => void;
    /** Records the current state again for the current source text: the translator confirms a stale state. */
    onconfirm: () => void;
    onnotechange: (note: string) => void;
    /** Discards the unsaved review changes of the workspace. */
    ondiscard: () => void;
    onterminology: () => void;
    onreport: () => void;
    onqualityfilter: () => void;
    onsuggestion: (translation: string) => void;
    onfix?: (diagnostic: EditorDiagnostic, fix: EditorDiagnosticQuickFix) => void;
  } = $props();

  let stateLabel = $derived(states.find((option) => option.value === reviewState)?.label ?? reviewState);
  // A translation that does not exist yet is not a draft of one: the badge says so until text is typed (#225 P2).
  let badgeLabel = $derived(notTranslated ? ui.text("ui_resource_not_translated") : stateLabel);
  let open = $state(false);
  let sourceDirection = $derived(localeDirection(sourceLocale));
  let mark = $derived(reviewStateMark(reviewState, notTranslated, stale));
  const chip = "placeholder-chip mx-0.5 align-baseline";
</script>

<!-- The compared base text (#225 P20): removed words struck through in the recorded text, added ones marked in the current
     text, placeholders as chips as in the source panel. -->
{#snippet diffParts(parts: SourceDiffPart[])}{#each parts as part, index (index)}{#if part.kind === "text"}{#if part.change === "same"}{part.text}{:else if part.change === "removed"}<del class="rounded-sm bg-destructive/15 px-0.5 text-destructive decoration-destructive/70" data-change="removed"><span class="sr-only">{`${ui.text("ui_workflow_source_removed")} `}</span>{part.text}</del>{:else}<ins class="rounded-sm bg-success/15 px-0.5 font-medium text-success no-underline" data-change="added"><span class="sr-only">{`${ui.text("ui_workflow_source_added")} `}</span>{part.text}</ins>{/if}{:else if part.change === "same"}<span class={chip} data-placeholder={part.kind}>{part.syntax}</span>{:else if part.change === "removed"}<del class="{chip} ring-2 ring-destructive/60" data-placeholder={part.kind} data-change="removed"><span class="sr-only">{`${ui.text("ui_workflow_source_removed")} `}</span>{part.syntax}</del>{:else}<ins class="{chip} no-underline ring-2 ring-success/60" data-placeholder={part.kind} data-change="added"><span class="sr-only">{`${ui.text("ui_workflow_source_added")} `}</span>{part.syntax}</ins>{/if}{/each}{/snippet}

<Collapsible.Root bind:open class="group/workflow mx-auto mb-4 w-full max-w-[1000px]">
  <Card.Root size="sm" class="gap-0 py-0 shadow-none">
    <Card.Header class="p-0">
      <Collapsible.Trigger class="flex min-h-12 w-full items-center gap-2 rounded-2xl px-4 py-2 text-left outline-none focus-visible:ring-2 focus-visible:ring-ring">
        <ListChecksIcon class="size-4 shrink-0" aria-hidden="true" />
        <span class="font-medium">{ui.text("ui_workflow_title")}</span>
        <Badge variant="outline" data-testid="workflow-state">{#if mark !== undefined && !stale}<StateMark state={mark} />{/if}{badgeLabel}</Badge>
        {#if stale}<Badge variant="outline" class="border-warning/60 text-warning"><StateMark state="outdated" />{ui.text("ui_workflow_source_changed")}</Badge>{/if}
        <span class="ml-auto hidden min-w-0 truncate text-xs text-muted-foreground sm:block">
          {dirty ? ui.text("ui_workflow_unsaved_changes") : message}
        </span>
        <ChevronDownIcon class="size-4 shrink-0 transition-transform group-data-[state=open]/workflow:rotate-180" aria-hidden="true" />
      </Collapsible.Trigger>
    </Card.Header>

    {#if stale}
      <!-- The source text changed since the state was recorded: confirm it or mark it for review (#225 P20). -->
      <div class="flex flex-col gap-2 border-t px-4 py-3" role="group" aria-label={ui.text("ui_workflow_source_changed")} data-testid="workflow-stale">
        {#if sourceDiff === undefined}
          <p class="text-sm">{ui.text("ui_workflow_stale_explanation", { state: stateLabel })}</p>
        {:else}
          <p class="text-sm">{ui.text("ui_workflow_stale_diff_explanation", { state: stateLabel })}</p>
          <dl class="grid gap-2 rounded-lg border bg-muted/30 px-3 py-2" data-testid="workflow-source-diff" data-syntax={sourceDiff.syntax ? "true" : undefined}>
            <div class="grid gap-0.5" data-testid="workflow-source-before">
              <dt class="text-xs text-muted-foreground">{ui.text("ui_workflow_source_before")}</dt>
              <dd class={["leading-7 whitespace-pre-wrap", sourceDiff.syntax ? "font-mono text-xs" : "text-sm"]} lang={sourceLocale || undefined} dir={sourceDirection}>{@render diffParts(sourceDiff.before)}</dd>
            </div>
            <div class="grid gap-0.5" data-testid="workflow-source-now">
              <dt class="text-xs text-muted-foreground">{ui.text("ui_workflow_source_now")}</dt>
              <dd class={["leading-7 whitespace-pre-wrap", sourceDiff.syntax ? "font-mono text-xs" : "text-sm"]} lang={sourceLocale || undefined} dir={sourceDirection}>{@render diffParts(sourceDiff.after)}</dd>
            </div>
          </dl>
          {#if !sourceDiff.changed}
            <p class="text-xs text-muted-foreground" data-testid="workflow-source-same">{ui.text("ui_workflow_source_same_text")}</p>
          {/if}
        {/if}
        <div class="flex flex-wrap gap-2">
          <Button size="sm" {disabled} title={ui.text("ui_workflow_confirm_translation_title")} data-testid="workflow-confirm" onclick={() => { if (!disabled) onconfirm(); }}>
            <CircleCheckBigIcon data-icon="inline-start" />
            {ui.text("ui_workflow_confirm_translation")}
          </Button>
          {#if reviewState !== "needs-review"}
            <Button variant="outline" size="sm" {disabled} data-testid="workflow-mark-needs-review" onclick={() => { if (!disabled) onstatechange("needs-review"); }}>
              <FlagIcon data-icon="inline-start" />
              {ui.text("ui_workflow_mark_needs_review")}
            </Button>
          {/if}
        </div>
      </div>
    {/if}

    <Collapsible.Content>
      <Card.Content class="grid grid-cols-1 gap-4 border-t py-4 xl:grid-cols-[minmax(0,1.25fr)_minmax(17rem,1fr)]">
        <div class="grid content-start gap-4">
          <div class="flex flex-wrap items-end gap-2">
      <Field.Field class="gap-1">
        <Field.Label for="workflow-status">{ui.text("ui_workflow_status")}</Field.Label>
        <!-- A stale state is not selected: choosing it again records it anew for the current source text (#225 P20). -->
        <Select.Root
          type="single"
          value={stale ? "" : reviewState}
          disabled={disabled}
          onValueChange={(value) => {
            if (disabled) return;
            onstatechange(value as EditorReviewState);
          }}
        >
          <Select.Trigger id="workflow-status" size="sm" class="min-w-36">{stateLabel}</Select.Trigger>
          <Select.Content>
            <Select.Group>
              <Select.Label>{ui.text("ui_workflow_status")}</Select.Label>
              {#each states as option (option.value)}
                <Select.Item value={option.value} label={option.label}>{option.label}</Select.Item>
              {/each}
            </Select.Group>
          </Select.Content>
        </Select.Root>
      </Field.Field>
      <Button variant="outline" size="sm" {disabled} onclick={() => { if (!disabled) onterminology(); }}>
        <BookOpenIcon data-icon="inline-start" />
        {ui.text("ui_workflow_terminology")} · {terminologyCount}
      </Button>
      <Button variant="outline" size="sm" {disabled} onclick={() => { if (!disabled) onreport(); }}>
        <ClipboardListIcon data-icon="inline-start" />
        {ui.text("ui_workflow_quality_report")} · {qualityCount}
      </Button>
      {#if dirty}
        <Button variant="ghost" size="sm" {disabled} onclick={() => { if (!disabled) ondiscard(); }}>
          <Undo2Icon data-icon="inline-start" />
          {ui.text("ui_workflow_discard")}
        </Button>
      {/if}
          </div>

          <Field.Field>
            <Field.Label for="review-note">{ui.text("ui_workflow_reviewer_note")}</Field.Label>
            <Textarea
              id="review-note"
              class="min-h-20 resize-y"
              value={note}
              placeholder={ui.text("ui_workflow_reviewer_note_placeholder")}
              {disabled}
              oninput={(event) => { if (!disabled) onnotechange(event.currentTarget.value); }}
            />
          </Field.Field>
        </div>

        <section class="flex flex-col gap-2" aria-label={ui.text("ui_workflow_quality_checks")}>
          <strong class="text-sm font-medium">{ui.text("ui_workflow_quality_checks")}</strong>
          {#if qualityIssues.length === 0}
            <div class="flex items-center gap-2 text-sm text-muted-foreground">
              <CheckCircle2Icon class="text-success" />
              {ui.text("ui_workflow_no_issues")}
            </div>
          {:else}
            {#each qualityIssues as issue (`${issue.kind}:${issue.code ?? ""}:${issue.message}`)}
              <Button variant="outline" size="xs" class="h-auto justify-start whitespace-normal" {disabled} onclick={() => { if (!disabled) onqualityfilter(); }}>
                <TriangleAlertIcon class="text-warning" data-icon="inline-start" />
                <span class="text-left">{#if issue.code !== undefined}<strong class="mr-1 font-mono">{issue.code}</strong>{/if}{issue.message}</span>
              </Button>
              {#if onfix !== undefined && issue.diagnostic?.quickFixes !== undefined && issue.diagnostic.quickFixes.length > 0}
                <div class="flex flex-wrap gap-2 pl-6">
                  {#each issue.diagnostic.quickFixes as fix (fix.id)}
                    <Button variant="secondary" size="xs" {disabled} data-quick-fix={fix.id} onclick={() => { if (!disabled && issue.diagnostic !== undefined) onfix?.(issue.diagnostic, fix); }}>{fix.title}</Button>
                  {/each}
                </div>
              {/if}
            {/each}
          {/if}
        </section>
      </Card.Content>

      {#if suggestions.length > 0}
        <Card.Footer class="flex-col items-stretch gap-2 border-t py-3">
          <strong class="flex items-center gap-2 text-sm font-medium"><SparklesIcon />{ui.text("ui_workflow_local_translation_memory")}</strong>
          <p class="text-xs text-muted-foreground">{ui.text("ui_workflow_suggestions_privacy")}</p>
          <div class="grid gap-1.5">
            {#each suggestions as suggestion (suggestion.key)}
              <Button
                variant="ghost"
                size="sm"
                class="h-auto justify-start whitespace-normal"
                title={suggestion.source}
                {disabled}
                onclick={() => { if (!disabled) onsuggestion(suggestion.translation); }}
              >
                <Badge variant="secondary">{Math.round(suggestion.score * 100)}%</Badge>
                <span class="text-left"><code>{suggestion.key}</code> · {suggestion.translation}</span>
              </Button>
            {/each}
          </div>
        </Card.Footer>
      {:else}
        <Card.Footer class="border-t py-3 text-xs text-muted-foreground">{ui.text("ui_workflow_empty_suggestions_privacy")}</Card.Footer>
      {/if}
    </Collapsible.Content>
  </Card.Root>
</Collapsible.Root>
