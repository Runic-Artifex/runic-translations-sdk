<script lang="ts">
  import { tick } from "svelte";
  import type { EditorMessageProjection, EditorMessageOperation } from "#lib/contracts.js";
  import WandSparklesIcon from "@lucide/svelte/icons/wand-sparkles";
  import { Button } from "#lib/components/ui/button/index.js";
  import * as Field from "#lib/components/ui/field/index.js";
  import { Textarea } from "#lib/components/ui/textarea/index.js";
  import MessageComposer from "#lib/MessageComposer.svelte";
  import type { EditorMode } from "#lib/EditorModeSwitcher.svelte";
  import type { ResourceValue } from "#lib/resource-model.js";
  import { localeDirection } from "#lib/locale-text.js";
  import { getUiText } from "#lib/ui-text.js";

  interface Props {
    identity: string;
    mode: EditorMode;
    locale: string;
    label: string;
    value: string;
    resourceValue: ResourceValue | undefined;
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
  }

  let {
    identity,
    mode,
    locale,
    label,
    value,
    resourceValue,
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
  }: Props = $props();

  const ui = getUiText();
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

<section class="mx-auto mt-4 w-full max-w-[1000px]" lang={locale} dir={localeDirection(locale)}>
  <Field.Field data-invalid={invalid} class="gap-2">
    <div class="flex min-w-0 items-center justify-between gap-4">
      <Field.Label
        for={mode === "raw" ? "translation-value" : undefined}
        class="min-w-0 truncate text-xs font-semibold text-foreground/80"
      >
        {label}
      </Field.Label>
      <span class="shrink-0 text-[0.65rem] tabular-nums text-muted-foreground">
        {ui.text("ui_count_characters", { count: value.length })}
      </span>
    </div>

    {#if mode === "translation"}
      <div inert={disabled} aria-disabled={disabled} class:opacity-60={disabled}>
        {#key identity}
          <MessageComposer value={resourceValue} {locale} {authoring} {authoringBusy} {onoperation} onchange={onresourcechange} />
        {/key}
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
