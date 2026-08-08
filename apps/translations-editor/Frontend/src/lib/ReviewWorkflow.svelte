<script lang="ts">
  import BookOpenIcon from "@lucide/svelte/icons/book-open";
  import CheckCircle2Icon from "@lucide/svelte/icons/check-circle-2";
  import ClipboardListIcon from "@lucide/svelte/icons/clipboard-list";
  import ListChecksIcon from "@lucide/svelte/icons/list-checks";
  import SparklesIcon from "@lucide/svelte/icons/sparkles";
  import TriangleAlertIcon from "@lucide/svelte/icons/triangle-alert";
  import { Badge } from "$lib/components/ui/badge/index.js";
  import { Button } from "$lib/components/ui/button/index.js";
  import * as Card from "$lib/components/ui/card/index.js";
  import * as Field from "$lib/components/ui/field/index.js";
  import * as Select from "$lib/components/ui/select/index.js";
  import { Textarea } from "$lib/components/ui/textarea/index.js";
  import type { EditorReviewState } from "$lib/contracts";
  import type { QualityIssue, TranslationSuggestion } from "$lib/review-model";

  const states: { value: EditorReviewState; label: string }[] = [
    { value: "draft", label: "Draft" },
    { value: "translated", label: "Translated" },
    { value: "needs-review", label: "Needs review" },
    { value: "approved", label: "Approved" },
  ];

  let {
    state,
    dirty,
    message,
    disabled,
    stale,
    terminologyCount,
    qualityCount,
    note,
    qualityIssues,
    suggestions,
    onstatechange,
    onnotechange,
    onterminology,
    onreport,
    onqualityfilter,
    onsuggestion,
  }: {
    state: EditorReviewState;
    dirty: boolean;
    message: string;
    disabled: boolean;
    stale: boolean;
    terminologyCount: number;
    qualityCount: number;
    note: string;
    qualityIssues: QualityIssue[];
    suggestions: TranslationSuggestion[];
    onstatechange: (state: EditorReviewState) => void;
    onnotechange: (note: string) => void;
    onterminology: () => void;
    onreport: () => void;
    onqualityfilter: () => void;
    onsuggestion: (translation: string) => void;
  } = $props();

  let stateLabel = $derived(states.find((option) => option.value === state)?.label ?? state);
</script>

<Card.Root size="sm" class="mx-auto mb-4 max-w-[1000px] gap-0 py-0 shadow-none">
  <Card.Header class="grid-cols-1 gap-3 border-b py-3 xl:grid-cols-[1fr_auto]">
    <Card.Title class="flex items-center gap-2 text-sm">
      <ListChecksIcon />
      Workflow
      <Badge variant="secondary">{stateLabel}</Badge>
      {#if stale}<Badge variant="destructive">Source changed</Badge>{/if}
    </Card.Title>
    <Card.Description>{dirty ? "Unsaved sidecar changes" : message}</Card.Description>
    <div class="flex flex-wrap items-end gap-2 justify-self-stretch xl:col-start-2 xl:row-span-2 xl:row-start-1 xl:justify-self-end">
      <Field.Field class="gap-1">
        <Field.Label for="workflow-status">Status</Field.Label>
        <Select.Root
          type="single"
          value={state}
          disabled={disabled}
          onValueChange={(value) => {
            onstatechange(value as EditorReviewState);
          }}
        >
          <Select.Trigger id="workflow-status" size="sm" class="min-w-36">{stateLabel}</Select.Trigger>
          <Select.Content>
            <Select.Group>
              <Select.Label>Workflow status</Select.Label>
              {#each states as option (option.value)}
                <Select.Item value={option.value} label={option.label}>{option.label}</Select.Item>
              {/each}
            </Select.Group>
          </Select.Content>
        </Select.Root>
      </Field.Field>
      <Button variant="outline" size="sm" onclick={onterminology}>
        <BookOpenIcon data-icon="inline-start" />
        Terminology · {terminologyCount}
      </Button>
      <Button variant="outline" size="sm" onclick={onreport}>
        <ClipboardListIcon data-icon="inline-start" />
        Quality report · {qualityCount}
      </Button>
    </div>
  </Card.Header>

  <Card.Content class="grid grid-cols-1 gap-3 py-3 xl:grid-cols-[1.25fr_1fr]">
    <Field.Field>
      <Field.Label for="review-note">Translator / reviewer note</Field.Label>
      <Textarea
        id="review-note"
        class="min-h-20 resize-y"
        value={note}
        placeholder="Optional context for the next reviewer…"
        oninput={(event) => onnotechange(event.currentTarget.value)}
      />
    </Field.Field>

    <section class="flex flex-col gap-2" aria-label="Quality checks">
      <strong class="text-sm font-medium">Quality checks</strong>
      {#if qualityIssues.length === 0}
        <div class="flex items-center gap-2 text-sm text-muted-foreground">
          <CheckCircle2Icon class="text-primary" />
          No local quality findings
        </div>
      {:else}
        {#each qualityIssues as issue (`${issue.kind}:${issue.message}`)}
          <Button variant="outline" size="xs" class="h-auto justify-start whitespace-normal" onclick={onqualityfilter}>
            <TriangleAlertIcon class="text-primary" data-icon="inline-start" />
            <span class="text-left">{issue.message}</span>
          </Button>
        {/each}
      {/if}
    </section>
  </Card.Content>

  {#if suggestions.length > 0}
    <Card.Footer class="flex-col items-stretch gap-2 border-t py-3">
      <strong class="flex items-center gap-2 text-sm font-medium"><SparklesIcon />Local translation memory</strong>
      <div class="grid gap-1.5">
        {#each suggestions as suggestion (suggestion.key)}
          <Button
            variant="ghost"
            size="sm"
            class="h-auto justify-start whitespace-normal"
            title={suggestion.source}
            onclick={() => onsuggestion(suggestion.translation)}
          >
            <Badge variant="secondary">{Math.round(suggestion.score * 100)}%</Badge>
            <span class="text-left"><code>{suggestion.key}</code> · {suggestion.translation}</span>
          </Button>
        {/each}
      </div>
    </Card.Footer>
  {/if}
</Card.Root>
