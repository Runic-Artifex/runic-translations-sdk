<script lang="ts">
  import BookPlusIcon from "@lucide/svelte/icons/book-plus";
  import { Button } from "#lib/components/ui/button/index.js";
  import type { EditorMisspelling } from "./contracts";
  import { getSpelling } from "./state/spelling.svelte";
  import { getUiText } from "./ui-text";

  /** The corrections of the misspelled word at the caret, and adding it to the user's own word list (D074.10). */
  let { locale, mark, onreplace }: {
    locale: string;
    mark: EditorMisspelling;
    onreplace: (replacement: string) => void;
  } = $props();

  const ui = getUiText();
  const spelling = getSpelling();
  let suggestions = $state.raw<readonly string[]>([]);
  $effect(() => {
    const word = mark.word;
    let current = true;
    suggestions = [];
    void spelling?.suggest(locale, word).then((next) => { if (current) suggestions = next; });
    return () => { current = false; };
  });
</script>

<div class="flex flex-wrap items-center gap-1 text-sm" role="group" aria-label={ui.text("ui_spelling_suggestions_for", { word: mark.word })} data-spelling-suggestions={mark.word}>
  <span class="mr-1 text-muted-foreground">{ui.text("ui_spelling_misspelled", { word: mark.word })}</span>
  {#each suggestions as suggestion (suggestion)}
    <Button variant="secondary" size="xs" lang={locale} onmousedown={(event: MouseEvent) => event.preventDefault()} onclick={() => onreplace(suggestion)}>{suggestion}</Button>
  {:else}
    <span class="text-muted-foreground">{ui.text("ui_spelling_no_suggestions")}</span>
  {/each}
  <Button variant="ghost" size="xs" onmousedown={(event: MouseEvent) => event.preventDefault()} onclick={() => void spelling?.addWord(mark.word)}>
    <BookPlusIcon data-icon="inline-start" aria-hidden="true" />
    {ui.text("ui_spelling_add_word")}
  </Button>
</div>
