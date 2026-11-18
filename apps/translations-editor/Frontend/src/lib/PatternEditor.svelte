<script lang="ts">
  import { Button } from "#lib/components/ui/button/index.js";
  import { Textarea } from "#lib/components/ui/textarea/index.js";
  import InlineMessageEditor from "./InlineMessageEditor.svelte";
  import type { InputType, MessageInput } from "./message-model";
  import { localeDirection } from "./locale-text";
  import { getUiText } from "./ui-text";

  let { pattern, inputs, locale, label, onchange, onensureinput, revision = 0, sourceToggle = true }: {
    pattern: string;
    inputs: Record<string, MessageInput>;
    locale: string;
    label: string;
    onchange: (pattern: string) => void;
    onensureinput?: (name: string, type: InputType) => void;
    /** Advanced by an undo or redo: text typed before it gives way to `pattern`. */
    revision?: number;
    /** Whether the pattern offers its own MF2 source; a content-editing project (D074.2) has one link for the message. */
    sourceToggle?: boolean;
  } = $props();
  const ui = getUiText();
  let sourceMode = $state(false);
  let pendingPattern = $state<{ text: string; revision: number }>();
  let visiblePattern = $derived(pendingPattern !== undefined && pendingPattern.revision === revision ? pendingPattern.text : pattern);
  $effect(() => {
    if (pendingPattern !== undefined && (pendingPattern.text === pattern || pendingPattern.revision !== revision)) pendingPattern = undefined;
  });
  function changePattern(next: string): void {
    pendingPattern = { text: next, revision };
    onchange(next);
  }
</script>

<div class="grid gap-2">
  {#if sourceMode && sourceToggle}
    <Textarea value={visiblePattern} class="min-h-32 font-mono" data-draft-field aria-label={label} spellcheck={false} lang={locale} dir={localeDirection(locale)} oninput={(event) => changePattern(event.currentTarget.value)} />
  {:else}
    <InlineMessageEditor value={visiblePattern} {inputs} {locale} {label} onchange={changePattern} {onensureinput} />
  {/if}
  {#if sourceToggle}
    <Button variant="ghost" size="sm" class="justify-self-start" aria-pressed={sourceMode} onclick={() => sourceMode = !sourceMode}>{ui.text("ui_composer_message_source")}</Button>
  {/if}
</div>
