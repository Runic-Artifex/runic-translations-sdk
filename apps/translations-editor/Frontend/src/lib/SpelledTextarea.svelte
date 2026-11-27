<script lang="ts">
  import { Textarea } from "#lib/components/ui/textarea/index.js";
  import SpellingMarks from "./SpellingMarks.svelte";
  import SpellingSuggestions from "./SpellingSuggestions.svelte";
  import { localeDirection } from "./locale-text";
  import { getSpelling, markAt, type SpellingTextKind } from "./state/spelling.svelte";

  /**
   * An MF2 source field with the editor's own spell checking (D074.10): the webview's checker is off, the words of the
   * text (not its placeholders, markup or, for a whole message, its syntax) are marked in the field's language, and the
   * word at the caret offers corrections.
   */
  let { value, locale, kind, label, class: className = "", rows, onchange }: {
    value: string;
    locale: string;
    kind: SpellingTextKind;
    label: string;
    /** The field's classes; the marks copy its font size and family. */
    class?: string;
    rows?: number;
    onchange: (value: string) => void;
  } = $props();

  const spelling = getSpelling();
  let marks = $derived(spelling?.marks(locale, value, kind) ?? []);
  let caret = $state<number>();
  let scrollTop = $state(0);
  let field = $state<HTMLTextAreaElement | null>(null);
  let active = $derived(caret === undefined ? undefined : markAt(marks, caret));

  function track(target: HTMLTextAreaElement): void {
    caret = target.selectionStart === target.selectionEnd ? target.selectionStart : undefined;
  }

  function replace(replacement: string): void {
    if (active === undefined || field === null) return;
    const next = value.slice(0, active.start) + replacement + value.slice(active.start + active.length);
    const position = active.start + replacement.length;
    onchange(next);
    queueMicrotask(() => {
      field?.focus();
      field?.setSelectionRange(position, position);
      caret = position;
    });
  }
</script>

<div class="grid gap-1" onfocusout={(event) => { if (!event.currentTarget.contains(event.relatedTarget as Node | null)) caret = undefined; }}>
  <div class="relative">
    <Textarea
      bind:ref={field}
      {value}
      {rows}
      class={className}
      data-draft-field
      aria-label={label}
      spellcheck={false}
      lang={locale}
      dir={localeDirection(locale)}
      oninput={(event) => { onchange(event.currentTarget.value); track(event.currentTarget); }}
      onkeyup={(event) => track(event.currentTarget)}
      onclick={(event) => track(event.currentTarget)}
      onselect={(event) => track(event.currentTarget)}
      onscroll={(event) => scrollTop = event.currentTarget.scrollTop}
    />
    <SpellingMarks text={value} {marks} {scrollTop} class={["absolute inset-0 rounded-2xl border border-transparent px-3 py-3 text-base md:text-sm", className].join(" ")} />
  </div>
  {#if active !== undefined}
    <SpellingSuggestions {locale} mark={active} onreplace={replace} />
  {/if}
</div>
