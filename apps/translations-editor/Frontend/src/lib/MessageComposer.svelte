<script lang="ts">
  import CirclePlusIcon from "@lucide/svelte/icons/circle-plus";
  import CodeXmlIcon from "@lucide/svelte/icons/code-xml";
  import Trash2Icon from "@lucide/svelte/icons/trash-2";
  import { Badge } from "#lib/components/ui/badge/index.js";
  import { Button } from "#lib/components/ui/button/index.js";
  import * as Card from "#lib/components/ui/card/index.js";
  import * as Field from "#lib/components/ui/field/index.js";
  import { Input } from "#lib/components/ui/input/index.js";
  import { Textarea } from "#lib/components/ui/textarea/index.js";
  import PatternEditor from "./PatternEditor.svelte";
  import { nextIdentifier, type InputType, type MessageInput } from "./message-composer";
  import type { EditorMessageOperation, EditorMessageProjection } from "./contracts";
  import type { ResourceValue } from "./resource-model";
  import { localeDirection } from "#lib/locale-text.js";
  import { getUiText } from "#lib/ui-text.js";

  interface Props {
    value: ResourceValue | undefined;
    locale: string;
    authoring?: EditorMessageProjection;
    authoringBusy?: boolean;
    onchange: (value: ResourceValue) => void;
    onoperation?: (operation: EditorMessageOperation) => void | Promise<void>;
  }

  let { value, locale, authoring, authoringBusy = false, onchange, onoperation }: Props = $props();
  const ui = getUiText();
  let rawMode = $state(false);
  let exactCaseValue = $state("");
  let selectorInput = $state("");
  const inputFunctions = ["string", "integer", "number", "date", "time", "datetime", "uuid"];
  let pendingSource = $state<string>();
  let source = $derived(pendingSource ?? (typeof value === "string" ? value : ""));
  $effect(() => {
    if (pendingSource !== undefined && pendingSource === value) pendingSource = undefined;
  });
  function changeSource(next: string): void {
    pendingSource = next;
    onchange(next);
  }
  let canAuthor = $derived(authoring?.supported === true && onoperation !== undefined && typeof value === "string");
  let inputs = $derived.by(() => Object.fromEntries((authoring?.inputs ?? []).map(input => [input.name, {
    type: inputType(input.type, input.function),
  } satisfies MessageInput])));
  let primarySelector = $derived(authoring?.selectors[0]);
  let primaryIsNumeric = $derived(["plural", "ordinal"].includes(authoring?.selectorFunctions[0] ?? ""));
  let availableCases = $derived(primaryIsNumeric ? (authoring?.selectorPluralCategories?.[0] ?? authoring?.pluralCategories ?? []).filter(category => category !== "other" && !authoring?.variants.some(variant => variant.keys[0] === category)) : []);
  let exactCaseDuplicate = $derived(authoring?.variants.some(variant => variant.keys[0] === exactCaseValue.trim() && variant.keys.slice(1).every(key => key === "*")) ?? false);

  function inputType(type: string, fn?: string): InputType {
    if (fn === "integer") return "int64";
    if (fn === "number") return "decimal";
    if (fn === "datetime") return "instant";
    return ["string", "bool", "int64", "decimal", "date", "time", "instant", "uuid"].includes(type) ? type as InputType : "string";
  }

  function operation(next: EditorMessageOperation): void | Promise<void> {
    if (canAuthor) return onoperation?.(next);
  }

  async function enablePluralForms(): Promise<void> {
    let name = authoring?.inputs.find(input => ["integer", "number"].includes(input.function ?? ""))?.name;
    if (name === undefined) {
      name = nextIdentifier("count", authoring?.inputs.map(input => input.name) ?? []);
      await operation({ kind: "add-input", name, function: "integer" });
    }
    await operation({ kind: "set-selectors", selectors: [name] });
  }

  function addVariant(key: string): void {
    if (!authoring || key.trim() === "") return;
    const keys = authoring.selectors.map((_, index) => index === 0 ? key.trim() : "*");
    void operation({ kind: "add-variant", keys, pattern: "" });
    exactCaseValue = "";
  }

  function title(keys: string[]): string {
    if (keys.length === 0) return ui.text("ui_composer_default_translation");
    return keys.map((key, index) => `${authoring?.selectors[index]}: ${key === "*" ? ui.text("ui_composer_fallback") : key}`).join(" · ");
  }
</script>

<div class="grid gap-4">
  <header class="flex flex-wrap items-start justify-between gap-3">
    <div class="grid gap-1">
      <h3 class="text-sm font-semibold">{ui.text("ui_composer_translate_message")}</h3>
      <p class="text-xs text-muted-foreground">{ui.text("ui_composer_write_naturally")} <code>{"{$count}"}</code> {ui.text("ui_composer_variables_become_chips")}</p>
    </div>
    {#if canAuthor}
      <Button variant="outline" size="sm" onclick={() => rawMode = !rawMode} aria-pressed={rawMode}>
        <CodeXmlIcon data-icon="inline-start" />{ui.text("ui_composer_message_source")}
      </Button>
    {/if}
  </header>

  {#if rawMode || !canAuthor}
    {#if !canAuthor}
      <p class="rounded-lg border bg-muted/30 p-3 text-sm text-muted-foreground" role="status">
        {ui.text("ui_composer_source_only")}
        {#if authoring?.reason}<span class="mt-1 block text-xs">{authoring.reason}</span>{/if}
      </p>
    {/if}
    <Textarea value={source} rows={12} class="font-mono text-sm" aria-label={ui.text("ui_composer_message_source")} lang={locale} dir={localeDirection(locale)} oninput={(event) => changeSource(event.currentTarget.value)} />
  {:else if authoring}
    {#if authoring.selectors.length === 0}
      <Button variant="outline" class="justify-self-start" disabled={authoringBusy} onclick={enablePluralForms}>
        <CirclePlusIcon data-icon="inline-start" />{ui.text("ui_composer_add_plural_forms")}
      </Button>
    {/if}

    {#each authoring.variants as variant (variant.id)}
      {@const fallback = variant.keys.length > 0 && variant.keys.every(key => key === "*")}
      <Card.Root size="sm">
        <Card.Header>
          <div class="flex flex-wrap items-center gap-2">
            <Card.Title>{title(variant.keys)}</Card.Title>
            {#if fallback}<Badge variant="secondary">{ui.text("ui_composer_required_fallback")}</Badge>{/if}
          </div>
          {#if variant.keys.length > 0}
            <Card.Action><Button variant="ghost" size="icon-sm" disabled={fallback || authoringBusy} aria-label={`${ui.text("ui_composer_remove")} ${title(variant.keys)}`} onclick={() => operation({ kind: "remove-variant", variantId: variant.id })}><Trash2Icon /></Button></Card.Action>
          {/if}
        </Card.Header>
        <Card.Content>
          <PatternEditor pattern={variant.pattern} {inputs} {locale} label={`${ui.text("ui_composer_translation_for")} ${title(variant.keys)}`} onchange={(pattern) => operation({ kind: "set-pattern", variantId: variant.id, pattern })} />
        </Card.Content>
      </Card.Root>
    {/each}

    {#if authoring.selectors.length > 0}
      <div class="flex flex-wrap items-end gap-2">
        {#each availableCases as category (category)}
          <Button variant="outline" size="sm" disabled={authoringBusy} onclick={() => addVariant(category)}><CirclePlusIcon />{category}</Button>
        {/each}
        <Field.Field class="max-w-64">
          <Field.Label for="mf2-exact-case">{ui.text("ui_composer_exact_or_custom_match")}</Field.Label>
          <Input id="mf2-exact-case" bind:value={exactCaseValue} placeholder={primaryIsNumeric ? "0" : "premium"} />
        </Field.Field>
        <Button variant="outline" disabled={authoringBusy || exactCaseValue.trim() === "" || exactCaseDuplicate} onclick={() => addVariant(exactCaseValue)}>{ui.text("ui_composer_add_translation_case")}</Button>
      </div>
    {/if}

    <details class="rounded-xl border bg-card p-4">
      <summary class="cursor-pointer text-sm font-semibold">{ui.text("ui_composer_advanced_structure")}</summary>
      <div class="mt-4 grid gap-4">
        <h4 class="text-sm font-medium">{ui.text("ui_composer_inputs")}</h4>
        {#each authoring.inputs as input (input.name)}
          <div class="grid items-end gap-3 sm:grid-cols-[1fr_1fr_auto]">
            <Field.Field>
              <Field.Label for={`mf2-name-${input.name}`}>{ui.text("ui_common_name")}</Field.Label>
              <Input id={`mf2-name-${input.name}`} value={input.name} disabled={authoringBusy} onblur={(event) => { const newName = event.currentTarget.value.trim(); if (newName !== input.name) void operation({ kind: "rename-input", name: input.name, newName }); }} />
            </Field.Field>
            <label class="grid gap-2 text-sm" for={`mf2-function-${input.name}`}>{ui.text("ui_composer_formatter")}
              <select id={`mf2-function-${input.name}`} class="h-9 rounded-md border bg-background px-3 text-sm" value={input.function ?? "string"} disabled={authoringBusy} onchange={(event) => operation({ kind: "set-input", name: input.name, function: event.currentTarget.value })}>
                {#if input.function && !inputFunctions.includes(input.function)}<option value={input.function}>{input.function}</option>{/if}
                {#each inputFunctions as fn (fn)}<option value={fn}>{fn}</option>{/each}
              </select>
            </label>
            <Button variant="ghost" size="icon" disabled={authoringBusy || !input.declared} aria-label={`${ui.text("ui_composer_remove_input")} ${input.name}`} onclick={() => operation({ kind: "remove-input", name: input.name })}><Trash2Icon /></Button>
          </div>
        {/each}
        <Button variant="outline" class="justify-self-start" disabled={authoringBusy} onclick={() => operation({ kind: "add-input", name: nextIdentifier("value", authoring?.inputs.map(input => input.name) ?? []), function: "string" })}><CirclePlusIcon />{ui.text("ui_composer_add_input")}</Button>
        <h4 class="text-sm font-medium">{ui.text("ui_composer_selection_rules")}</h4>
        {#each authoring.selectors as name, selectorIndex (name)}
          <div class="flex items-center justify-between gap-2 rounded-md bg-muted p-2">
            <select class="h-9 min-w-0 rounded-md border bg-background px-3 font-mono text-sm" value={name} aria-label={`${ui.text("ui_composer_uses_input")} ${name}`} disabled={authoringBusy} onchange={(event) => operation({ kind: "set-selectors", selectors: authoring?.selectors.map((selector, index) => index === selectorIndex ? event.currentTarget.value : selector) })}>
              {#if !authoring.inputs.some(input => input.name === name)}<option value={name}>{`$${name}`}</option>{/if}
              {#each authoring.inputs as input (input.name)}<option value={input.name}>{`$${input.name}`}</option>{/each}
            </select>
            <Button variant="ghost" size="icon-sm" disabled={authoringBusy} aria-label={`${ui.text("ui_composer_remove_selector")} ${name}`} onclick={() => operation({ kind: "set-selectors", selectors: authoring?.selectors.filter(selector => selector !== name) })}><Trash2Icon /></Button>
          </div>
        {/each}
        <div class="flex flex-wrap items-end gap-2">
          <label class="grid gap-2 text-sm" for="mf2-add-selector">{ui.text("ui_composer_uses_input")}
            <select id="mf2-add-selector" class="h-9 rounded-md border bg-background px-3 text-sm" bind:value={selectorInput} disabled={authoringBusy}>
              <option value="">{ui.text("ui_composer_uses_input")}</option>
              {#each authoring.inputs.filter(input => !authoring?.selectors.includes(input.name)) as input (input.name)}<option value={input.name}>{input.name}</option>{/each}
            </select>
          </label>
          <Button variant="outline" disabled={authoringBusy || selectorInput === "" || authoring.selectors.includes(selectorInput)} onclick={() => { void operation({ kind: "set-selectors", selectors: [...(authoring?.selectors ?? []), selectorInput] }); selectorInput = ""; }}><CirclePlusIcon />{ui.text("ui_composer_add_selection_rule")}</Button>
        </div>
      </div>
    </details>
  {/if}
</div>
