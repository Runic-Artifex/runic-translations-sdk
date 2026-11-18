<script lang="ts">
  import PlusIcon from "@lucide/svelte/icons/plus";
  import Trash2Icon from "@lucide/svelte/icons/trash-2";
  import AppDialog from "#lib/AppDialog.svelte";
  import { Badge } from "#lib/components/ui/badge/index.js";
  import { Button } from "#lib/components/ui/button/index.js";
  import * as Field from "#lib/components/ui/field/index.js";
  import { Input } from "#lib/components/ui/input/index.js";
  import type { ReviewStore } from "#lib/state/review-store.svelte.js";
  import { getUiText } from "#lib/ui-text.js";

  let {
    open = $bindable(false),
    review,
    blocked,
  }: {
    open?: boolean;
    review: ReviewStore;
    /** Whether review data cannot change now: the form is shown but inert. */
    blocked: boolean;
  } = $props();
  const ui = getUiText();

  // The new term's fields stay filled while the dialog is closed, until the term is added.
  let termSource = $state("");
  let termPreferred = $state("");
  let termLocale = $state("");
  let termNote = $state("");

  function addTerm(): void {
    if (blocked) return;
    if (termSource.trim() === "" || termPreferred.trim() === "") return;
    review.addTerm({
      source: termSource.trim(),
      preferred: termPreferred.trim(),
      locale: termLocale.trim() || undefined,
      note: termNote.trim() || undefined,
    });
    termSource = "";
    termPreferred = "";
    termLocale = "";
    termNote = "";
  }

  function removeTerm(index: number): void {
    if (blocked) return;
    review.removeTerm(index);
  }
</script>

{#if open}
  <AppDialog
    open
    title={ui.text("ui_page_terminology_title")}
    description={ui.text("ui_page_terminology_description")}
    class="sm:max-w-4xl"
    onopenchange={(value) => open = value}
  >
    <div inert={blocked} aria-disabled={blocked} class:opacity-60={blocked}>
    <Field.FieldGroup class="grid gap-3 sm:grid-cols-2">
      <Field.Field><Field.Label for="term-source">{ui.text("ui_page_terminology_source_term")}</Field.Label><Input id="term-source" bind:value={termSource} placeholder={ui.text("ui_page_terminology_source_placeholder")} /></Field.Field>
      <Field.Field><Field.Label for="term-preferred">{ui.text("ui_page_terminology_preferred")}</Field.Label><Input id="term-preferred" bind:value={termPreferred} placeholder={ui.text("ui_page_terminology_preferred_placeholder")} /></Field.Field>
      <Field.Field><Field.Label for="term-locale">{ui.text("ui_page_terminology_locale")}</Field.Label><Input id="term-locale" bind:value={termLocale} placeholder={ui.text("ui_page_terminology_locale_placeholder")} /></Field.Field>
      <Field.Field><Field.Label for="term-note">{ui.text("ui_page_terminology_note")}</Field.Label><Input id="term-note" bind:value={termNote} placeholder={ui.text("ui_page_terminology_note_placeholder")} /></Field.Field>
      <Button class="justify-self-start sm:col-span-2" variant="outline" disabled={blocked || termSource.trim() === "" || termPreferred.trim() === ""} onclick={addTerm}>
        <PlusIcon data-icon="inline-start" />{ui.text("ui_page_terminology_add_term")}
      </Button>
    </Field.FieldGroup>
    <div class="mt-5 grid overflow-hidden rounded-xl border">
      {#each review.terminology as term, index (term)}
        <div class="grid grid-cols-[minmax(0,1fr)_auto] items-center gap-3 border-b px-4 py-3 last:border-b-0">
          <div class="min-w-0">
            <div class="flex min-w-0 flex-wrap items-center gap-2"><strong>{term.source}</strong><span class="text-muted-foreground">→</span><strong>{term.preferred}</strong>{#if term.locale}<Badge variant="outline">{term.locale}</Badge>{/if}</div>
            <p class="truncate text-xs text-muted-foreground">{term.note ?? ui.text("ui_page_terminology_no_note")}</p>
          </div>
          <Button variant="ghost" size="icon-xs" aria-label={ui.text("ui_terminology_remove_source", { term: term.source })} onclick={() => removeTerm(index)}><Trash2Icon /></Button>
        </div>
      {:else}
        <p class="p-6 text-center text-sm text-muted-foreground">{ui.text("ui_page_terminology_empty")}</p>
      {/each}
    </div>
    </div>
    {#snippet footer()}
      <Button variant="outline" onclick={() => open = false}>{ui.text("ui_page_done")}</Button>
    {/snippet}
  </AppDialog>
{/if}
