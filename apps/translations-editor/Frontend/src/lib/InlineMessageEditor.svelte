<script lang="ts">
  import ArrowLeftIcon from "@lucide/svelte/icons/arrow-left";
  import ArrowRightIcon from "@lucide/svelte/icons/arrow-right";
  import GripVerticalIcon from "@lucide/svelte/icons/grip-vertical";
  import PlusIcon from "@lucide/svelte/icons/plus";
  import Trash2Icon from "@lucide/svelte/icons/trash-2";
  import XIcon from "@lucide/svelte/icons/x";
  import VariableIcon from "@lucide/svelte/icons/variable";
  import { Button } from "$lib/components/ui/button/index.js";
  import * as DropdownMenu from "$lib/components/ui/dropdown-menu/index.js";
  import * as Field from "$lib/components/ui/field/index.js";
  import { Input } from "$lib/components/ui/input/index.js";
  import * as Select from "$lib/components/ui/select/index.js";
  import {
    inputTypes,
    nextIdentifier,
    type InputType,
    type MessageInput,
  } from "$lib/message-composer";
  import { tick } from "svelte";

  interface EditorSlot {
    text: string;
    token?: string;
  }

  let {
    value,
    inputs,
    label,
    onchange,
    onensureinput,
    onupdateformat,
  }: {
    value: string;
    inputs: Record<string, MessageInput>;
    label: string;
    onchange: (value: string) => void;
    onensureinput: (name: string, type: InputType) => void;
    onupdateformat: (name: string, format: string) => void;
  } = $props();

  let selectedToken = $state<{ name: string; slot: number }>();
  let dropSlot = $state<number>();
  let dropBoundary = $state<string>();
  let editingSlot = $state<number>();
  let activeSlot = 0;
  let caret = 0;
  let textareas: HTMLTextAreaElement[] = [];
  let slots = $derived(parseSlots(value));
  let inputNames = $derived(Object.keys(inputs));

  function parseSlots(source: string): EditorSlot[] {
    const result: EditorSlot[] = [{ text: "" }];
    for (let index = 0; index < source.length;) {
      if (source.startsWith("{{", index) || source.startsWith("}}", index)) {
        result[result.length - 1].text += source.slice(index, index + 2);
        index += 2;
        continue;
      }
      if (source[index] === "{") {
        const end = source.indexOf("}", index + 1);
        const name = end < 0 ? "" : source.slice(index + 1, end);
        if (/^[A-Za-z_][A-Za-z0-9_]*$/.test(name)) {
          result[result.length - 1].token = name;
          result.push({ text: "" });
          index = end + 1;
          continue;
        }
      }
      result[result.length - 1].text += source[index];
      index += 1;
    }
    return result;
  }

  function serialize(next: EditorSlot[]): string {
    return next.map((slot) => `${slot.text}${slot.token === undefined ? "" : `{${slot.token}}`}`).join("");
  }

  function slotStart(index: number): number {
    let position = 0;
    for (let slotIndex = 0; slotIndex < index; slotIndex += 1) {
      const slot = slots[slotIndex];
      position += slot.text.length + (slot.token === undefined ? 0 : slot.token.length + 2);
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
    const syntax = `{${name}}`;
    const sourceStart = tokenStart(sourceSlot);
    const sourceEnd = sourceStart + syntax.length;
    if (targetPosition >= sourceStart && targetPosition <= sourceEnd) return;
    const withoutToken = source.slice(0, sourceStart) + source.slice(sourceEnd);
    const adjustedTarget = targetPosition > sourceEnd ? targetPosition - syntax.length : targetPosition;
    onchange(withoutToken.slice(0, adjustedTarget) + syntax + withoutToken.slice(adjustedTarget));
    selectedToken = undefined;
  }

  function startVariableDrag(event: DragEvent, name: string, sourceSlot: number): void {
    event.dataTransfer?.setData("application/x-runic-variable", JSON.stringify({ name, sourceSlot }));
    event.dataTransfer?.setData("text/plain", `{${name}}`);
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
    const sourceEnd = tokenStart(sourceSlot) + selectedToken.name.length + 2;
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
      : sourceEnd + following.text.length + (following.text.length === 0 && following.token !== undefined ? following.token.length + 2 : 0);
    moveToken(sourceSlot, target);
  }

  function canMoveSelected(direction: "earlier" | "later"): boolean {
    if (selectedToken === undefined) return false;
    if (direction === "earlier") return tokenStart(selectedToken.slot) > 0;
    return tokenStart(selectedToken.slot) + selectedToken.name.length + 2 < serialize(slots).length;
  }

  function updateText(index: number, text: string, selection: number | null): void {
    activeSlot = index;
    caret = selection ?? text.length;
    const next = structuredClone(slots);
    next[index].text = text;
    const insertedToken = /\{[A-Za-z_][A-Za-z0-9_]*\}/.test(text);
    onchange(serialize(next));
    if (insertedToken) void focusSlot(index + 1, 0);
  }

  function rememberCaret(index: number, target: HTMLTextAreaElement): void {
    activeSlot = index;
    caret = target.selectionStart ?? target.value.length;
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
    slot.text = slot.text.slice(0, insertionPosition);
    slot.token = name;
    next.splice(index + 1, 0, { text: trailingText, token: previousToken });
    onchange(serialize(next));
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
    slot.text += following?.text ?? "";
    if (following !== undefined) next.splice(selectedToken.slot + 1, 1);
    onchange(serialize(next));
    selectedToken = undefined;
    void focusSlot(Math.min(activeSlot, next.length - 1), slot.text.length);
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
          aria-label={`Insert ${description}`}
          title={`Insert ${description}`}
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
      <DropdownMenu.Label>Insert</DropdownMenu.Label>
      <DropdownMenu.Item onclick={() => editTextAt(index, position)}>
        <span class="grid size-4 place-items-center font-serif text-base" aria-hidden="true">T</span>
        Text
      </DropdownMenu.Item>
      <DropdownMenu.Sub>
        <DropdownMenu.SubTrigger>
          <VariableIcon />
          Variable
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
            Create new variable
          </DropdownMenu.Item>
        </DropdownMenu.SubContent>
      </DropdownMenu.Sub>
    </DropdownMenu.Content>
  </DropdownMenu.Root>
{/snippet}

<div class="overflow-hidden rounded-2xl border bg-card/70 shadow-inner focus-within:ring-2 focus-within:ring-ring">
  <div
    class="flex min-h-32 flex-wrap content-start items-start gap-x-1 gap-y-2 px-4 py-3"
    role="group"
    aria-label={label}
  >
    {#each slots as slot, index (`${index}-${slot.token ?? "text"}`)}
      {#if index === 0}
        {@render insertionPoint(index, 0, "at the beginning")}
      {/if}
      {#if slot.text !== "" || editingSlot === index}
        <textarea
          {@attach registerTextarea(index)}
          class={[
            "field-sizing-content min-h-7 max-w-full min-w-[3ch] flex-none resize-none overflow-hidden rounded-sm bg-transparent p-0 font-sans text-base leading-7 outline-none transition-[width,background-color,box-shadow] placeholder:text-muted-foreground",
            slot.text === "" && "min-w-[8ch]",
            dropSlot === index && "bg-primary/10 ring-2 ring-primary/50",
          ]}
          rows="1"
          value={slot.text}
          aria-label={`${label}, text ${index + 1}`}
          placeholder="Write text…"
          spellcheck="true"
          onfocus={(event) => rememberCaret(index, event.currentTarget)}
          onselect={(event) => rememberCaret(index, event.currentTarget)}
          oninput={(event) => updateText(index, event.currentTarget.value, event.currentTarget.selectionStart)}
          onblur={(event) => { if (event.currentTarget.value === "") editingSlot = undefined; }}
          ondragenter={(event) => allowVariableDrop(event, index)}
          ondragover={(event) => allowVariableDrop(event, index)}
          ondragleave={(event) => {
            if (!event.currentTarget.contains(event.relatedTarget as Node | null)) dropSlot = undefined;
          }}
          ondrop={(event) => dropVariable(event, index, event.currentTarget)}
        ></textarea>
        {@render insertionPoint(index, slot.text.length, `after text ${index + 1}`)}
      {/if}
      {#if slot.token !== undefined}
        <Button
          variant="secondary"
          size="sm"
          class="h-7 shrink-0 rounded-full px-2 font-mono text-xs"
          aria-label={`Variable ${slot.token}. Open settings.`}
          title={`Variable ${slot.token}. Click to inspect.`}
          draggable="true"
          ondragstart={(event) => startVariableDrag(event, slot.token!, index)}
          ondragend={() => {
            dropSlot = undefined;
            dropBoundary = undefined;
          }}
          onclick={() => inspectToken(slot.token, index)}
        >
          <GripVerticalIcon data-icon="inline-start" aria-hidden="true" />
          {slot.token}
        </Button>
        {@render insertionPoint(index + 1, 0, `after variable ${slot.token}`)}
      {/if}
    {/each}
  </div>

  {#if selectedToken !== undefined}
    <div class="grid gap-3 border-t bg-muted/30 px-3 py-3 sm:grid-cols-[minmax(0,1fr)_minmax(0,1fr)_auto_auto_auto_auto] sm:items-end">
      <Field.Field class="gap-1">
        <Field.Label for={`inline-token-type-${selectedToken.name}`}>
          <span class="font-mono">{selectedToken.name}</span> type
        </Field.Label>
        <Select.Root
          type="single"
          value={inputs[selectedToken.name]?.type ?? "string"}
          onValueChange={(type) => onensureinput(selectedToken!.name, type as InputType)}
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
      <Field.Field class="gap-1">
        <Field.Label for={`inline-token-format-${selectedToken.name}`}>Default format</Field.Label>
        <Input
          id={`inline-token-format-${selectedToken.name}`}
          value={inputs[selectedToken.name]?.format ?? ""}
          placeholder="Compiler default"
          oninput={(event) => onupdateformat(selectedToken!.name, event.currentTarget.value)}
        />
      </Field.Field>
      <Button variant="ghost" size="icon" disabled={!canMoveSelected("earlier")} aria-label={`Move ${selectedToken.name} earlier`} title="Move variable earlier" onclick={() => moveSelected("earlier")}>
        <ArrowLeftIcon />
      </Button>
      <Button variant="ghost" size="icon" disabled={!canMoveSelected("later")} aria-label={`Move ${selectedToken.name} later`} title="Move variable later" onclick={() => moveSelected("later")}>
        <ArrowRightIcon />
      </Button>
      <Button variant="ghost" size="icon" aria-label={`Remove ${selectedToken.name} from this translation`} title="Remove variable from this translation" onclick={removeSelectedToken}>
        <Trash2Icon />
      </Button>
      <Button variant="ghost" size="icon" aria-label={`Close ${selectedToken.name} settings`} title="Close variable settings" onclick={() => selectedToken = undefined}>
        <XIcon />
      </Button>
    </div>
  {/if}
</div>
