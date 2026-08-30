<script lang="ts">
  import WandSparklesIcon from "@lucide/svelte/icons/wand-sparkles";
  import { Button } from "$lib/components/ui/button/index.js";
  import * as Field from "$lib/components/ui/field/index.js";
  import { Textarea } from "$lib/components/ui/textarea/index.js";
  import MessageComposer from "$lib/MessageComposer.svelte";
  import type { EditorMode } from "$lib/EditorModeSwitcher.svelte";
  import type { ResourceValue } from "$lib/resource-model";
  import { localeDirection } from "$lib/locale-text";
  import { getUiText } from "$lib/ui-text";

  interface Props {
    mode: EditorMode;
    locale: string;
    label: string;
    value: string;
    resourceValue: ResourceValue | undefined;
    missing: boolean;
    invalid: boolean;
    disabled: boolean;
    onresourcechange: (value: ResourceValue) => void;
    onrawchange: (value: string) => void;
    onformatraw: () => void;
  }

  let {
    mode,
    locale,
    label,
    value,
    resourceValue,
    missing,
    invalid,
    disabled,
    onresourcechange,
    onrawchange,
    onformatraw,
  }: Props = $props();

  const ui = getUiText();
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
        {value.length.toLocaleString()} {ui.text("Ui.TranslationEditor.Characters")}
      </span>
    </div>

    {#if mode === "translation"}
      <div inert={disabled} aria-disabled={disabled} class:opacity-60={disabled}>
        <MessageComposer value={resourceValue} {locale} onchange={onresourcechange} />
      </div>
    {:else}
      <Textarea
        id="translation-value"
        class="field-sizing-fixed min-h-96 resize-y bg-card/70 px-5 py-4 font-mono text-xs leading-7 shadow-inner"
        value={value}
        placeholder={missing ? ui.text("Ui.TranslationEditor.AddTranslation") : undefined}
        spellcheck={false}
        lang={locale}
        dir={localeDirection(locale)}
        aria-invalid={invalid}
        {disabled}
        oninput={(event) => onrawchange(event.currentTarget.value)}
      />
      <div class="flex flex-wrap items-center justify-between gap-x-4 gap-y-2 px-1">
        <Field.Description class="text-xs">{ui.text("Ui.TranslationEditor.RawChangeDescription")}</Field.Description>
        <Button variant="ghost" size="xs" {disabled} onclick={onformatraw}>
          <WandSparklesIcon data-icon="inline-start" />
          {ui.text("Ui.TranslationEditor.FormatJson")}
        </Button>
      </div>
    {/if}
  </Field.Field>
</section>
