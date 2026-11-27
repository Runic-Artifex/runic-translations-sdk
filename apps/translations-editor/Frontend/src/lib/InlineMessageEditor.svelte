<script lang="ts">
  import ArrowLeftIcon from "@lucide/svelte/icons/arrow-left";
  import ArrowRightIcon from "@lucide/svelte/icons/arrow-right";
  import GripVerticalIcon from "@lucide/svelte/icons/grip-vertical";
  import PlusIcon from "@lucide/svelte/icons/plus";
  import Trash2Icon from "@lucide/svelte/icons/trash-2";
  import XIcon from "@lucide/svelte/icons/x";
  import VariableIcon from "@lucide/svelte/icons/variable";
  import { Button } from "#lib/components/ui/button/index.js";
  import * as DropdownMenu from "#lib/components/ui/dropdown-menu/index.js";
  import * as Field from "#lib/components/ui/field/index.js";
  import { Input } from "#lib/components/ui/input/index.js";
  import * as Select from "#lib/components/ui/select/index.js";
  import {
    inputTypes,
    nextIdentifier,
    parseMf2Slots,
    serializeMf2Slots,
    mf2VariableSyntax,
    type Mf2EditorSlot,
    type InputType,
    type MessageInput,
  } from "#lib/message-composer.js";
  import { tick } from "svelte";
  import SpellingMarks from "#lib/SpellingMarks.svelte";
  import SpellingSuggestions from "#lib/SpellingSuggestions.svelte";
  import { localeDirection } from "#lib/locale-text.js";
  import { getSpelling, markAt, marksWithin } from "#lib/state/spelling.svelte.js";
  import { getUiText } from "#lib/ui-text.js";

  type EditorSlot = Mf2EditorSlot;

  let {
    value,
    locale,
    inputs,
    label,
    onchange,
    onensureinput,
    onupdateformat,
    compact = false,
  }: {
    value: string;
    locale: string;
    inputs: Record<string, MessageInput>;
    label: string;
    onchange: (value: string) => void;
    onensureinput?: (name: string, type: InputType) => void;
    onupdateformat?: (name: string, format: string) => void;
    /** One line high, for a row of the list view (D074.1); the composer's box is taller. */
    compact?: boolean;
  } = $props();

  const ui = getUiText();

  let selectedToken = $state<{ name: string; slot: number }>();
  let dropSlot = $state<number>();
  let dropBoundary = $state<string>();
  let editingSlot = $state<number>();
  let activeSlot = 0;
  let caret = 0;
  let textareas: HTMLTextAreaElement[] = [];
  let slots = $derived(parseMf2Slots(value));
  let inputNames = $derived(Object.keys(inputs));
  // The editor's own spell checking (D074.10): the pattern is checked as a whole, in its language, without its
  // placeholders and markup, and each text segment shows the marks that fall in it.
  const spelling = getSpelling();
  let patternMarks = $derived(spelling?.marks(locale, value) ?? []);
  let caretAt = $state<{ slot: number; position: number }>();
  let activeMark = $derived.by(() => {
    if (caretAt === undefined || slots[caretAt.slot] === undefined) return undefined;
    return markAt(slotMarks(caretAt.slot), caretAt.position);
  });

  function slotMarks(index: number) {
    const slot = slots[index];
    return slot === undefined ? [] : marksWithin(patternMarks, slotStart(index), slot.text.length);
  }

  function trackCaret(index: number, target: HTMLTextAreaElement): void {
    caretAt = target.selectionStart === target.selectionEnd ? { slot: index, position: target.selectionStart } : undefined;
  }

  function replaceMark(replacement: string): void {
    const mark = activeMark;
    const at = caretAt;
    if (mark === undefined || at === undefined) return;
    const text = slots[at.slot].text;
    const next = text.slice(0, mark.start) + replacement + text.slice(mark.start + mark.length);
    const position = mark.start + replacement.length;
    updateText(at.slot, next, position);
    caretAt = { slot: at.slot, position };
    void focusSlot(at.slot, position);
  }

  function emitChange(next: string): void {
    onchange(next);
  }

  function serialize(next: EditorSlot[]): string {
    return serializeMf2Slots(next);
  }

  function tokenSyntax(index: number): string {
    const slot = slots[index];
    return slot?.token === undefined ? "" : slot.syntax ?? mf2VariableSyntax(slot.token);
  }

  function slotStart(index: number): number {
    let position = 0;
    for (let slotIndex = 0; slotIndex < index; slotIndex += 1) {
      const slot = slots[slotIndex];
      position += slot.text.length + (slot.token === undefined ? 0 : (slot.syntax ?? mf2VariableSyntax(slot.token)).length);
    }
    return position;
  }

  function tokenStart(index: number): number {
    return slotStart(index) + (slots[index]?.text.length ?? 0);
  }

  function moveToken(sourceSlot: number, targetPosition: number): void {
    const source = serialize(slots);
    const name = slots[sourceSlot]?.token;
    if (name === undefined) return;
    const syntax = tokenSyntax(sourceSlot);
    const sourceStart = tokenStart(sourceSlot);
    const sourceEnd = sourceStart + syntax.length;
    if (targetPosition >= sourceStart && targetPosition <= sourceEnd) return;
    const withoutToken = source.slice(0, sourceStart) + source.slice(sourceEnd);
    const adjustedTarget = targetPosition > sourceEnd ? targetPosition - syntax.length : targetPosition;
    emitChange(withoutToken.slice(0, adjustedTarget) + syntax + withoutToken.slice(adjustedTarget));
    selectedToken = undefined;
  }

  function startVariableDrag(event: DragEvent, name: string, sourceSlot: number): void {
    event.dataTransfer?.setData("application/x-runic-variable", JSON.stringify({ name, sourceSlot }));
    event.dataTransfer?.setData("text/plain", mf2VariableSyntax(name));
    if (event.dataTransfer !== null) event.dataTransfer.effectAllowed = "move";
  }

  function allowVariableDrop(event: DragEvent, targetSlot: number): void {
    if (!event.dataTransfer?.types.includes("application/x-runic-variable")) return;
    event.preventDefault();
    event.dataTransfer.dropEffect = "move";
    dropSlot = targetSlot;
  }

  function dropVariable(event: DragEvent, targetSlot: number, target: HTMLTextAreaElement): void {
    const payload = event.dataTransfer?.getData("application/x-runic-variable");
    dropSlot = undefined;
    if (payload === undefined || payload === "") return;
    event.preventDefault();
    try {
      const { sourceSlot } = JSON.parse(payload) as { sourceSlot: number };
      const targetPosition = slotStart(targetSlot) + (target.selectionStart ?? target.value.length);
      moveToken(sourceSlot, targetPosition);
    } catch {
      // Ignore drag data that did not originate from this editor.
    }
  }

  function allowBoundaryDrop(event: DragEvent, boundary: string): void {
    if (!event.dataTransfer?.types.includes("application/x-runic-variable")) return;
    event.preventDefault();
    event.dataTransfer.dropEffect = "move";
    dropBoundary = boundary;
  }

  function dropVariableAtBoundary(event: DragEvent, targetSlot: number, targetCaret: number): void {
    const payload = event.dataTransfer?.getData("application/x-runic-variable");
    dropBoundary = undefined;
    if (payload === undefined || payload === "") return;
    event.preventDefault();
    try {
      const { sourceSlot } = JSON.parse(payload) as { sourceSlot: number };
      moveToken(sourceSlot, slotStart(targetSlot) + targetCaret);
    } catch {
      // Ignore drag data that did not originate from this editor.
    }
  }

  function moveSelected(direction: "earlier" | "later"): void {
    if (selectedToken === undefined) return;
    const sourceSlot = selectedToken.slot;
    const sourceEnd = tokenStart(sourceSlot) + tokenSyntax(selectedToken.slot).length;
    if (direction === "earlier") {
      const target = slots[sourceSlot].text.length > 0
        ? slotStart(sourceSlot)
        : sourceSlot > 0 ? tokenStart(sourceSlot - 1) : tokenStart(sourceSlot);
      moveToken(sourceSlot, target);
      return;
    }
    const following = slots[sourceSlot + 1];
    const target = following === undefined
      ? sourceEnd
      : sourceEnd + following.text.length + (following.text.length === 0 && following.token !== undefined ? (following.syntax ?? mf2VariableSyntax(following.token)).length : 0);
    moveToken(sourceSlot, target);
  }

  function canMoveSelected(direction: "earlier" | "later"): boolean {
    if (selectedToken === undefined) return false;
    if (direction === "earlier") return tokenStart(selectedToken.slot) > 0;
    return tokenStart(selectedToken.slot) + tokenSyntax(selectedToken.slot).length < serialize(slots).length;
  }

  function updateText(index: number, text: string, selection: number | null): void {
    activeSlot = index;
    caret = selection ?? text.length;
    const next = structuredClone(slots);
    next[index].text = text;
    const insertedToken = parseMf2Slots(text).some((slot) => slot.token !== undefined);
    emitChange(serialize(next));
    if (insertedToken) void focusSlot(index + 1, 0);
  }

  function rememberCaret(index: number, target: HTMLTextAreaElement): void {
    activeSlot = index;
    caret = target.selectionStart ?? target.value.length;
    trackCaret(index, target);
  }

  function inspectToken(name: string | undefined, slot: number): void {
    if (name !== undefined) selectedToken = { name, slot };
  }

  function registerTextarea(index: number): (element: HTMLTextAreaElement) => () => void {
    return (element) => {
      textareas[index] = element;
      return () => {
        if (textareas[index] === element) delete textareas[index];
      };
    };
  }

  function insertVariableAt(index: number, position: number, name: string): void {
    const next = structuredClone(slots);
    const slot = next[index] ?? next[next.length - 1];
    const insertionPosition = Math.min(position, slot.text.length);
    const trailingText = slot.text.slice(insertionPosition);
    const previousToken = slot.token;
    const previousSyntax = slot.syntax;
    slot.text = slot.text.slice(0, insertionPosition);
    slot.token = name;
    slot.syntax = mf2VariableSyntax(name);
    next.splice(index + 1, 0, { text: trailingText, token: previousToken, syntax: previousSyntax });
    emitChange(serialize(next));
    selectedToken = { name, slot: index };
    editingSlot = undefined;
  }

  function insertNewVariableAt(index: number, position: number): void {
    insertVariableAt(index, position, nextIdentifier("value", inputNames));
  }

  function editTextAt(index: number, position: number): void {
    editingSlot = index;
    void focusSlot(index, position);
  }

  function removeSelectedToken(): void {
    if (selectedToken === undefined) return;
    const next = structuredClone(slots);
    const slot = next[selectedToken.slot];
    const following = next[selectedToken.slot + 1];
    if (slot === undefined || slot.token === undefined) return;
    slot.token = following?.token;
    slot.syntax = following?.syntax;
    slot.text += following?.text ?? "";
    if (following !== undefined) next.splice(selectedToken.slot + 1, 1);
    emitChange(serialize(next));
    selectedToken = undefined;
    void focusSlot(Math.min(activeSlot, next.length - 1), slot.text.length);
  }

  // The pattern box reads as one text field: a click on its empty area puts the caret at the end of the last text
  // segment, opening an empty one after a trailing variable. Text segments, chips and insertion points keep their own
  // clicks, and keyboard users reach the same segments with Tab.
  function focusEnd(event: MouseEvent): void {
    if (event.target !== event.currentTarget || slots.length === 0) return;
    event.preventDefault();
    const last = slots.length - 1;
    editingSlot = last;
    void focusSlot(last, slots[last].text.length);
  }

  async function focusSlot(index: number, position: number): Promise<void> {
    await tick();
    const target = textareas[index];
    if (target === undefined) return;
    target.focus();
    target.setSelectionRange(position, position);
    activeSlot = index;
    caret = position;
  }
</script>

{#snippet insertionPoint(index: number, position: number, description: string)}
  {@const boundary = `${index}:${position}`}
  <DropdownMenu.Root>
    <DropdownMenu.Trigger>
      {#snippet child({ props })}
        <Button
          {...props}
          variant="ghost"
          size="icon-xs"
          class={[
            "shrink-0 rounded-full text-muted-foreground hover:text-foreground",
            dropBoundary === boundary && "bg-primary text-primary-foreground ring-2 ring-primary/40",
          ]}
          aria-label={ui.text("ui_inline_insert_at", { position: description })}
          title={ui.text("ui_inline_insert_at", { position: description })}
          ondragenter={(event) => allowBoundaryDrop(event, boundary)}
          ondragover={(event) => allowBoundaryDrop(event, boundary)}
          ondragleave={() => dropBoundary = undefined}
          ondrop={(event) => dropVariableAtBoundary(event, index, position)}
        >
          <PlusIcon />
        </Button>
      {/snippet}
    </DropdownMenu.Trigger>
    <DropdownMenu.Content align="start" class="w-52">
      <DropdownMenu.Label>{ui.text("ui_inline_insert")}</DropdownMenu.Label>
      <DropdownMenu.Item onclick={() => editTextAt(index, position)}>
        <span class="grid size-4 place-items-center text-base font-semibold" aria-hidden="true">T</span>
        {ui.text("ui_inline_text")}
      </DropdownMenu.Item>
      <DropdownMenu.Sub>
        <DropdownMenu.SubTrigger>
          <VariableIcon />
          {ui.text("ui_inline_variable")}
        </DropdownMenu.SubTrigger>
        <DropdownMenu.SubContent class="w-52">
          {#each inputNames as name (name)}
            <DropdownMenu.Item onclick={() => insertVariableAt(index, position, name)}>
              <VariableIcon />
              <span class="font-mono">{name}</span>
            </DropdownMenu.Item>
          {/each}
          {#if inputNames.length > 0}<DropdownMenu.Separator />{/if}
          <DropdownMenu.Item onclick={() => insertNewVariableAt(index, position)}>
            <PlusIcon />
            {ui.text("ui_inline_create_variable")}
          </DropdownMenu.Item>
        </DropdownMenu.SubContent>
      </DropdownMenu.Sub>
    </DropdownMenu.Content>
  </DropdownMenu.Root>
{/snippet}

<div
  class="overflow-hidden rounded-pane border border-(--field-border) bg-card focus-within:border-ring focus-within:ring-1 focus-within:ring-ring"
  onfocusout={(event) => { if (!event.currentTarget.contains(event.relatedTarget as Node | null)) caretAt = undefined; }}
>
  <!-- svelte-ignore a11y_no_noninteractive_element_interactions -->
  <div
    class="flex cursor-text flex-wrap content-start items-start gap-x-1 gap-y-2"
    role="group"
    class:min-h-32={!compact}
    class:px-4={!compact}
    class:py-3={!compact}
    class:min-h-9={compact}
    class:px-2={compact}
    class:py-1={compact}
    aria-label={label}
    data-pattern-box
    onmousedown={focusEnd}
  >
    {#each slots as slot, index (`${index}-${slot.token ?? "text"}`)}
      {#if index === 0}
        {@render insertionPoint(index, 0, ui.text("ui_inline_at_beginning"))}
      {/if}
      <!-- An empty pattern (a starter's variant) shows its empty text field, ready for typing. -->
      {#if slot.text !== "" || editingSlot === index || (slots.length === 1 && slot.token === undefined)}
        <span class="relative inline-flex max-w-full flex-none">
        <textarea
          {@attach registerTextarea(index)}
          class={[
            "field-sizing-content min-h-7 max-w-full min-w-[3ch] flex-none resize-none overflow-hidden rounded-sm bg-transparent p-0 font-sans text-message leading-7 outline-none transition-[width,background-color,box-shadow] placeholder:text-muted-foreground",
            slot.text === "" && "min-w-[8ch]",
            dropSlot === index && "bg-primary/10 ring-2 ring-primary/50",
          ]}
          rows="1"
          data-draft-field
          value={slot.text}
          aria-label={ui.text("ui_inline_text_index", { label: label, index: index + 1 })}
          placeholder={ui.text("ui_inline_write_text")}
          spellcheck="false"
          lang={locale}
          dir={localeDirection(locale)}
          onfocus={(event) => rememberCaret(index, event.currentTarget)}
          onselect={(event) => rememberCaret(index, event.currentTarget)}
          onkeyup={(event) => trackCaret(index, event.currentTarget)}
          onclick={(event) => trackCaret(index, event.currentTarget)}
          oninput={(event) => { updateText(index, event.currentTarget.value, event.currentTarget.selectionStart); trackCaret(index, event.currentTarget); }}
          onblur={(event) => { if (event.currentTarget.value === "") editingSlot = undefined; }}
          ondragenter={(event) => allowVariableDrop(event, index)}
          ondragover={(event) => allowVariableDrop(event, index)}
          ondragleave={(event) => {
            if (!event.currentTarget.contains(event.relatedTarget as Node | null)) dropSlot = undefined;
          }}
          ondrop={(event) => dropVariable(event, index, event.currentTarget)}
        ></textarea>
        <SpellingMarks text={slot.text} marks={slotMarks(index)} class="absolute inset-0 p-0 font-sans text-message leading-7" />
        </span>
        {@render insertionPoint(index, slot.text.length, ui.text("ui_inline_after_text_index", { index: index + 1 }))}
      {/if}
      {#if slot.token !== undefined}
        <Button
          variant="secondary"
          size="sm"
          class="placeholder-chip h-7 shrink-0 px-2"
          aria-label={ui.text("ui_inline_variable_settings", { name: slot.token })}
          title={ui.text("ui_inline_variable_inspect", { name: slot.token })}
          draggable="true"
          ondragstart={(event) => startVariableDrag(event, slot.token!, index)}
          ondragend={() => {
            dropSlot = undefined;
            dropBoundary = undefined;
          }}
          onclick={() => inspectToken(slot.token, index)}
        >
          <GripVerticalIcon data-icon="inline-start" aria-hidden="true" />
          {mf2VariableSyntax(slot.token)}
        </Button>
        {@render insertionPoint(index + 1, 0, ui.text("ui_inline_after_variable_name", { name: slot.token }))}
      {/if}
    {/each}
  </div>
  {#if activeMark !== undefined}
    <div class="border-t bg-muted/30 px-3 py-2">
      <SpellingSuggestions {locale} mark={activeMark} onreplace={replaceMark} />
    </div>
  {/if}

  {#if selectedToken !== undefined}
    <div class="grid gap-3 border-t bg-muted/30 px-3 py-3 sm:grid-cols-[minmax(0,1fr)_minmax(0,1fr)_auto_auto_auto_auto] sm:items-end">
      {#if onensureinput !== undefined}
      <Field.Field class="gap-1">
        <Field.Label for={`inline-token-type-${selectedToken.name}`}>
          <span class="font-mono">{selectedToken.name}</span> {ui.text("ui_inline_type")}
        </Field.Label>
        <Select.Root
          type="single"
          value={inputs[selectedToken.name]?.type ?? "string"}
          onValueChange={(type) => onensureinput?.(selectedToken!.name, type as InputType)}
        >
          <Select.Trigger id={`inline-token-type-${selectedToken.name}`} class="w-full">
            {inputs[selectedToken.name]?.type ?? "string"}
          </Select.Trigger>
          <Select.Content>
            <Select.Group>
              {#each inputTypes as type (type)}
                <Select.Item value={type} label={type}>{type}</Select.Item>
              {/each}
            </Select.Group>
          </Select.Content>
        </Select.Root>
      </Field.Field>
      {/if}
      {#if onupdateformat !== undefined}
      <Field.Field class="gap-1">
        <Field.Label for={`inline-token-format-${selectedToken.name}`}>{ui.text("ui_inline_default_format")}</Field.Label>
        <Input
          id={`inline-token-format-${selectedToken.name}`}
          value={inputs[selectedToken.name]?.format ?? ""}
          placeholder={ui.text("ui_inline_compiler_default")}
          oninput={(event) => onupdateformat?.(selectedToken!.name, event.currentTarget.value)}
        />
      </Field.Field>
      {/if}
      <Button variant="ghost" size="icon" disabled={!canMoveSelected("earlier")} aria-label={ui.text("ui_inline_move_name_earlier", { name: selectedToken.name })} title={ui.text("ui_inline_move_variable_earlier")} onclick={() => moveSelected("earlier")}>
        <ArrowLeftIcon />
      </Button>
      <Button variant="ghost" size="icon" disabled={!canMoveSelected("later")} aria-label={ui.text("ui_inline_move_name_later", { name: selectedToken.name })} title={ui.text("ui_inline_move_variable_later")} onclick={() => moveSelected("later")}>
        <ArrowRightIcon />
      </Button>
      <Button variant="ghost" size="icon" aria-label={ui.text("ui_inline_remove_name", { name: selectedToken.name })} title={ui.text("ui_inline_remove_variable")} onclick={removeSelectedToken}>
        <Trash2Icon />
      </Button>
      <Button variant="ghost" size="icon" aria-label={ui.text("ui_inline_close_name_settings", { name: selectedToken.name })} title={ui.text("ui_inline_close_variable_settings")} onclick={() => selectedToken = undefined}>
        <XIcon />
      </Button>
    </div>
  {/if}
</div>

<style>
  /* Keyboard focus in the visual text box shows as the box's ring (focus-within above); the caret marks the segment. The
     page's global textarea:focus-visible outline would draw a second ring around the focused segment inside it (#225
     R9). Forced colours drop the box's ring (a box-shadow), so there the segment keeps its outline. */
  @media not (forced-colors: active) {
    [data-pattern-box] textarea:focus-visible {
      outline: none;
    }
  }
</style>
