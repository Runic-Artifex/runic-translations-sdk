<script lang="ts">
  import { Button } from "#lib/components/ui/button/index.js";
  import { Textarea } from "#lib/components/ui/textarea/index.js";
  import InlineMessageEditor from "./InlineMessageEditor.svelte";
  import type { InputType, MessageInput } from "./message-model";
  import { localeDirection } from "./locale-text";
  import { getUiText } from "./ui-text";

  let { pattern, inputs, locale, label, onchange, onensureinput }: {
    pattern: string;
    inputs: Record<string, MessageInput>;
    locale: string;
    label: string;
    onchange: (pattern: string) => void;
    onensureinput?: (name: string, type: InputType) => void;
  } = $props();
  const ui = getUiText();
  let sourceMode = $state(false);
  let pendingPattern = $state<string>();
  let visiblePattern = $derived(pendingPattern ?? pattern);
  $effect(() => {
    if (pendingPattern !== undefined && pendingPattern === pattern) pendingPattern = undefined;
  });
  function changePattern(next: string): void {
    pendingPattern = next;
    onchange(next);
  }
</script>

<div class="grid gap-2">
  {#if sourceMode}
    <Textarea value={visiblePattern} class="min-h-32 font-mono" aria-label={label} lang={locale} dir={localeDirection(locale)} oninput={(event) => changePattern(event.currentTarget.value)} />
  {:else}
    <InlineMessageEditor value={visiblePattern} {inputs} {locale} {label} onchange={changePattern} {onensureinput} />
  {/if}
  <Button variant="ghost" size="sm" class="justify-self-start" aria-pressed={sourceMode} onclick={() => sourceMode = !sourceMode}>{ui.text("ui_composer_message_source")}</Button>
</div>
