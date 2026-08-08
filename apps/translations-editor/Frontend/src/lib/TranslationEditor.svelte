<script lang="ts">
  import BracesIcon from "@lucide/svelte/icons/braces";
  import WandSparklesIcon from "@lucide/svelte/icons/wand-sparkles";
  import { Button } from "$lib/components/ui/button/index.js";
  import * as Field from "$lib/components/ui/field/index.js";
  import { Textarea } from "$lib/components/ui/textarea/index.js";
  import MessageComposer from "$lib/MessageComposer.svelte";
  import type { EditorMode } from "$lib/EditorModeSwitcher.svelte";
  import type { StructuredMessage } from "$lib/message-composer";
  import type { ResourceValue } from "$lib/resource-model";

  interface Props {
    mode: EditorMode;
    label: string;
    value: string;
    structuredValue: ResourceValue | undefined;
    missing: boolean;
    invalid: boolean;
    onchange: (value: string) => void;
    onstructuredchange: (value: StructuredMessage) => void;
    onformatraw: () => void;
  }

  let {
    mode,
    label,
    value,
    structuredValue,
    missing,
    invalid,
    onchange,
    onstructuredchange,
    onformatraw,
  }: Props = $props();
</script>

<section class="mx-auto mt-4 w-full max-w-[1000px]">
  <Field.Field data-invalid={invalid} class="gap-2">
    <div class="flex min-w-0 items-center justify-between gap-4">
      <Field.Label
        for={mode === "advanced" ? undefined : "translation-value"}
        class="min-w-0 truncate text-xs font-semibold text-foreground/80"
      >
        {label}
      </Field.Label>
      <span class="shrink-0 text-[0.65rem] tabular-nums text-muted-foreground">
        {value.length.toLocaleString()} characters
      </span>
    </div>

    {#if mode === "advanced"}
      <MessageComposer value={structuredValue} onchange={onstructuredchange} />
    {:else}
      <Textarea
        id="translation-value"
        class={`field-sizing-fixed min-h-60 resize-y bg-card/70 px-5 py-4 text-base leading-7 shadow-inner ${mode === "raw" ? "min-h-96 font-mono text-xs" : ""}`}
        value={value}
        placeholder={missing ? "Add this translation…" : undefined}
        spellcheck={mode === "simple"}
        aria-invalid={invalid}
        oninput={(event) => onchange(event.currentTarget.value)}
      />
    {/if}

    <div class="flex flex-wrap items-center justify-between gap-x-4 gap-y-2 px-1">
      <Field.Description class="flex items-center gap-2 text-xs">
        <BracesIcon class="size-3.5 shrink-0 text-primary/70" aria-hidden="true" />
        {#if mode === "simple"}
          <span>Use <code>{"{name}"}</code> for inputs. Literal braces use <code>{"{{braces}}"}</code>.</span>
        {:else if mode === "advanced"}
          <span>Edit selectors, variants, formats, and semantic markup as a schema-v2 value.</span>
        {:else}
          <span>Changes here affect the complete resource document.</span>
        {/if}
      </Field.Description>
      {#if mode === "raw"}
        <Button variant="ghost" size="xs" onclick={onformatraw}>
          <WandSparklesIcon data-icon="inline-start" />
          Format JSON
        </Button>
      {/if}
    </div>
  </Field.Field>
</section>

<style>
  code {
    border-radius: var(--radius-sm);
    padding: 0.08rem 0.25rem;
    color: color-mix(in oklab, var(--primary) 72%, var(--foreground));
    background: var(--muted);
    font: 0.67rem ui-monospace, monospace;
  }
</style>
