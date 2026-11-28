<script lang="ts">
  import type { EditorMisspelling } from "./contracts";

  /**
   * The marks of misspelled words, drawn under a text field (D074.10): a copy of the field's text in transparent
   * letters, laid over the field with the same font, padding and wrapping, whose misspelled words are underlined. The
   * field stays the one the user types in; the copy takes no pointer events and is hidden from assistive technology,
   * which reads the words from the suggestion row instead.
   */
  let { text, marks, class: className = "", scrollTop = 0 }: {
    text: string;
    marks: readonly EditorMisspelling[];
    class?: string;
    /** The field's scroll position, for a field that scrolls. */
    scrollTop?: number;
  } = $props();

  let parts = $derived.by(() => {
    const result: { text: string; mark: boolean }[] = [];
    let position = 0;
    for (const mark of [...marks].sort((left, right) => left.start - right.start)) {
      if (mark.start < position || mark.start + mark.length > text.length) continue;
      if (mark.start > position) result.push({ text: text.slice(position, mark.start), mark: false });
      result.push({ text: text.slice(mark.start, mark.start + mark.length), mark: true });
      position = mark.start + mark.length;
    }
    // A trailing line break needs a character after it to take up its line, as the field's does.
    result.push({ text: text.slice(position) + "​", mark: false });
    return result;
  });
  let mirror = $state<HTMLElement>();
  $effect(() => {
    if (mirror !== undefined) mirror.scrollTop = scrollTop;
  });
</script>

<span bind:this={mirror} class={["spelling-mirror", className]} aria-hidden="true" data-spelling-marks={marks.length}>{#each parts as part, index (index)}{#if part.mark}<span class="spelling-mark" data-spelling-word={part.text}>{part.text}</span>{:else}{part.text}{/if}{/each}</span>

<style>
  .spelling-mirror {
    pointer-events: none;
    overflow: hidden;
    white-space: pre-wrap;
    overflow-wrap: break-word;
    color: transparent;
    user-select: none;
  }

  .spelling-mark {
    text-decoration-line: underline;
    text-decoration-style: wavy;
    text-decoration-color: var(--destructive);
    text-decoration-thickness: 1.5px;
    text-decoration-skip-ink: none;
    text-underline-offset: 0.2em;
  }
</style>
