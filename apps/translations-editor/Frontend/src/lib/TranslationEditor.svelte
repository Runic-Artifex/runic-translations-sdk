<script lang="ts">
  import { tick } from "svelte";
  import type { EditorMessageProjection, EditorMessageOperation } from "#lib/contracts.js";
  import WandSparklesIcon from "@lucide/svelte/icons/wand-sparkles";
  import { Button } from "#lib/components/ui/button/index.js";
  import * as Field from "#lib/components/ui/field/index.js";
  import { Textarea } from "#lib/components/ui/textarea/index.js";
  import MessageComposer from "#lib/MessageComposer.svelte";
  import SourceTextPanel from "#lib/SourceTextPanel.svelte";
  import type { EditorMode } from "#lib/EditorModeSwitcher.svelte";
  import type { ResourceEntry, ResourceValue } from "#lib/resource-model.js";
  import { localeDirection } from "#lib/locale-text.js";
  import { lengthLimit, messageLength } from "#lib/message-starter.js";
  import { getUiText } from "#lib/ui-text.js";

  interface Props {
    identity: string;
    mode: EditorMode;
    locale: string;
    label: string;
    value: string;
    resourceValue: ResourceValue | undefined;
    /** The base locale and its entry of this message: shown read-only above the editor of every other locale. */
    sourceLocale: string;
    sourceLocaleName: string;
    sourceEntry: ResourceEntry | undefined;
    authoring?: EditorMessageProjection;
    authoringBusy?: boolean;
    onoperation?: (operation: EditorMessageOperation) => void;
    sourceSelection?: { start: number; end: number; token: number };
    missing: boolean;
    invalid: boolean;
    disabled: boolean;
    onresourcechange: (value: ResourceValue) => void;
    onrawchange: (value: string) => void;
    onformatraw: () => void;
    /** The message's `@maxLength` in this locale: the counter shows the text against it (#225 P21). */
    maxLength?: number;
    /** The compiler's RTR0098 text for this message, shown at the field while the text is over the limit. */
    lengthWarning?: string;
    /** The empty starter of a missing translation is being prepared. */
    starterLoading?: boolean;
    /** Fills the starter of a missing translation from the source text; absent for an existing translation. */
    oncopysource?: () => void;
  }

  let {
    identity,
    mode,
    locale,
    label,
    value,
    resourceValue,
    sourceLocale,
    sourceLocaleName,
    sourceEntry,
    authoring,
    authoringBusy = false,
    onoperation,
    sourceSelection,
    missing,
    invalid,
    disabled,
    onresourcechange,
    onrawchange,
    onformatraw,
    maxLength,
    lengthWarning,
    starterLoading = false,
    oncopysource,
  }: Props = $props();

  const ui = getUiText();
  // With a limit the counter measures what RTR0098 measures: the literal text of the longest variant.
  let limit = $derived(mode === "translation" ? lengthLimit(messageLength(authoring, value), maxLength) : undefined);
  let rawInput = $state<HTMLTextAreaElement | null>(null);
  $effect(() => {
    const selection = sourceSelection;
    const input = rawInput;
    if (mode !== "raw" || selection === undefined || input === null) return;
    // The source and conditional textarea must be mounted before selection.
    void tick().then(() => {
      if (rawInput !== input || sourceSelection !== selection) return;
      input.focus();
      input.setSelectionRange(selection.start, selection.end);
      const line = input.value.slice(0, selection.start).split("\n").length - 1;
      input.scrollTop = Math.max(0, line * 28 - input.clientHeight / 2);
    });
  });
</script>

{#if mode === "translation" && sourceLocale !== "" && sourceLocale !== locale}
  <div class="mx-auto mt-4 w-full max-w-[1000px]">
    <SourceTextPanel entry={sourceEntry} locale={sourceLocale} localeName={sourceLocaleName} />
  </div>
{/if}

<section class="mx-auto mt-4 w-full max-w-[1000px]" lang={locale} dir={localeDirection(locale)}>
  <Field.Field data-invalid={invalid} class="gap-2">
    <div class="flex min-w-0 items-center justify-between gap-4">
      <Field.Label
        for={mode === "raw" ? "translation-value" : undefined}
        class="min-w-0 truncate text-xs font-semibold text-foreground/80"
      >
        {label}
      </Field.Label>
      {#if limit === undefined}
        <span class="shrink-0 text-[0.65rem] tabular-nums text-muted-foreground" data-testid="character-count">
          {ui.text("ui_count_characters", { count: value.length })}
        </span>
      {:else}
        <span
          class={["shrink-0 text-[0.65rem] tabular-nums", limit.over ? "font-semibold text-amber-700 dark:text-amber-300" : "text-muted-foreground"]}
          data-testid="character-count"
          data-over-limit={limit.over}
          title={ui.text("ui_count_characters_limit_title", { count: limit.length, limit: limit.limit })}
        >
          {ui.text("ui_count_characters_limit", { count: limit.length, limit: limit.limit })}
        </span>
      {/if}
    </div>
    {#if limit?.over}
      <p class="rounded-md border border-amber-500/40 bg-amber-500/10 px-3 py-2 text-xs text-amber-800 dark:text-amber-200" role="status" data-testid="length-limit">
        {lengthWarning ?? ui.text("ui_translation_editor_over_limit", { limit: limit.limit })}
      </p>
    {/if}

    {#if mode === "translation"}
      <div inert={disabled} aria-disabled={disabled} class:opacity-60={disabled}>
        {#if starterLoading}
          <p class="rounded-lg border bg-muted/30 p-3 text-sm text-muted-foreground" role="status">{ui.text("ui_translation_editor_starter_loading")}</p>
        {:else}
          {#key identity}
            <MessageComposer value={resourceValue} {locale} {authoring} {authoringBusy} {onoperation} onchange={onresourcechange} {maxLength} {oncopysource} />
          {/key}
        {/if}
      </div>
    {:else}
      <Textarea
        bind:ref={rawInput}
        id="translation-value"
        class="field-sizing-fixed min-h-96 resize-y bg-card/70 px-5 py-4 font-mono text-xs leading-7 shadow-inner"
        value={value}
        placeholder={missing ? ui.text("ui_translation_editor_add_translation") : undefined}
        spellcheck={false}
        lang={locale}
        dir={localeDirection(locale)}
        aria-invalid={invalid}
        {disabled}
        oninput={(event) => onrawchange(event.currentTarget.value)}
      />
      <div class="flex flex-wrap items-center justify-between gap-x-4 gap-y-2 px-1">
        <Field.Description class="text-xs">{ui.text("ui_translation_editor_raw_change_description")}</Field.Description>
        <Button variant="ghost" size="xs" {disabled} onclick={onformatraw}>
          <WandSparklesIcon data-icon="inline-start" />
          {ui.text("ui_translation_editor_format_json")}
        </Button>
      </div>
    {/if}
  </Field.Field>
</section>
