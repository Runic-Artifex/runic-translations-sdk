<script lang="ts">
  import ArrowDownIcon from "@lucide/svelte/icons/arrow-down";
  import ArrowUpIcon from "@lucide/svelte/icons/arrow-up";
  import ChevronDownIcon from "@lucide/svelte/icons/chevron-down";
  import CirclePlusIcon from "@lucide/svelte/icons/circle-plus";
  import CodeXmlIcon from "@lucide/svelte/icons/code-xml";
  import Settings2Icon from "@lucide/svelte/icons/settings-2";
  import Trash2Icon from "@lucide/svelte/icons/trash-2";
  import AppDialog from "$lib/AppDialog.svelte";
  import { Badge } from "$lib/components/ui/badge/index.js";
  import { Button, buttonVariants } from "$lib/components/ui/button/index.js";
  import * as Card from "$lib/components/ui/card/index.js";
  import * as Collapsible from "$lib/components/ui/collapsible/index.js";
  import * as DropdownMenu from "$lib/components/ui/dropdown-menu/index.js";
  import * as Field from "$lib/components/ui/field/index.js";
  import { Input } from "$lib/components/ui/input/index.js";
  import * as Popover from "$lib/components/ui/popover/index.js";
  import * as Select from "$lib/components/ui/select/index.js";
  import { Separator } from "$lib/components/ui/separator/index.js";
  import { Textarea } from "$lib/components/ui/textarea/index.js";
  import PatternEditor from "./PatternEditor.svelte";
  import InlineMessageEditor from "./InlineMessageEditor.svelte";
  import {
    formatFunctions,
    inputTypes,
    nextIdentifier,
    patternNodes,
    patternText,
    relativeTimeUnits,
    renameDeclaration,
    renameInput,
    renameSelector,
    selectorFunctions,
    synchronizeMatches,
    toStructuredMessage,
    type FormatFunction,
    type InputType,
    type MessageFormat,
    type MessagePatternNode,
    type MessageSelector,
    type StructuredMessage,
  } from "./message-composer";
  import type { ResourceValue } from "./resource-model";

  interface Props {
    value: ResourceValue | undefined;
    locale: string;
    onchange: (value: ResourceValue) => void;
  }

  let { value, locale, onchange }: Props = $props();
  let rawMode = $state(false);
  let rawText = $state("");
  let rawError = $state<string>();
  let structureOpen = $state(false);
  let exactCaseOpen = $state(false);
  let exactCaseValue = $state("");
  let message = $derived(toStructuredMessage(value));
  let inputNames = $derived(Object.keys(message.inputs));
  let effectiveInputs = $derived.by(() => {
    const names = new Set(Object.keys(message.inputs));
    for (const variant of message.variants) collectInputNames(patternNodes(variant.value), names);
    return Object.fromEntries([...names].map((name) => [name, message.inputs[name] ?? { type: "string" as const }]));
  });
  let declarationNames = $derived((message.declarations ?? []).map((item) => item.name));
  let primarySelector = $derived(message.selectors[0]);
  let exactCaseMatch = $derived.by(() => {
    const normalized = exactCaseValue.trim().replace(/^=/, "");
    if (normalized === "") return "";
    return primarySelector?.function === "literal" ? normalized : `=${normalized}`;
  });
  let exactCaseDuplicate = $derived(
    exactCaseMatch !== "" && primarySelector !== undefined && message.variants.some((variant) => variant.match[primarySelector.name] === exactCaseMatch),
  );
  let availableCaseMatches = $derived.by(() => {
    const selector = message.selectors[0];
    if (selector === undefined || selector.function === "literal") return [];
    const used = new Set(message.variants.map((variant) => variant.match[selector.name]));
    return localePluralCategories(locale, selector.function === "ordinal")
      .filter((category) => category !== "other" && !used.has(category));
  });

  function commit(action: (next: StructuredMessage) => void): void {
    const next = structuredClone(message);
    action(next);
    onchange(synchronizeMatches(next));
  }

  function addInput(type: InputType = "string", preferredName = "value"): void {
    commit((next) => {
      const name = nextIdentifier(preferredName, Object.keys(next.inputs));
      next.inputs[name] = { type };
    });
  }

  function ensureInput(name: string, type: InputType): void {
    commit((next) => {
      next.inputs[name] ??= { type };
      next.inputs[name].type = type;
    });
  }

  function updateInputFormat(name: string, format: string): void {
    commit((next) => {
      next.inputs[name] ??= { type: "string" };
      if (format === "") delete next.inputs[name].format;
      else next.inputs[name].format = format;
    });
  }

  function removeInput(name: string): void {
    commit((next) => {
      delete next.inputs[name];
      next.declarations = next.declarations?.filter((item) => item.input !== name);
      next.selectors = next.selectors.filter((item) => item.input !== name);
      scrubNodes(next, (node) =>
        ("input" in node && node.input === name) ||
        ("format" in node && node.format.input === name),
      );
    });
  }

  function addDeclaration(): void {
    commit((next) => {
      const name = nextIdentifier("formattedValue", (next.declarations ?? []).map((item) => item.name));
      const input = Object.keys(next.inputs).find((candidate) => next.inputs[candidate].type !== "bool") ?? "value";
      next.declarations ??= [];
      next.declarations.push({
        name,
        input,
        function: functionFor(next.inputs[input]?.type),
      });
    });
  }

  function functionFor(type: InputType | undefined): FormatFunction {
    return ({ int64: "integer", decimal: "number", date: "date", time: "time", instant: "datetime", uuid: "uuid" } as Partial<Record<InputType, FormatFunction>>)[type ?? "string"] ?? "string";
  }

  function updateDeclaration(index: number, property: keyof MessageFormat, value: string): void {
    commit((next) => {
      const declaration = next.declarations?.[index];
      if (declaration === undefined) return;
      if (property === "format" && value === "") delete declaration.format;
      else (declaration as unknown as Record<string, string>)[property] = value;
      if (property === "function" && value === "relativeTime") {
        declaration.unit = "day";
        declaration.numeric = "auto";
        delete declaration.format;
      } else if (property === "function") {
        delete declaration.unit;
        delete declaration.numeric;
        declaration.format ??= "plain";
      }
    });
  }

  function addSelector(): void {
    commit((next) => {
      const name = nextIdentifier("choice", next.selectors.map((item) => item.name));
      next.selectors.push({
        name,
        input: Object.keys(next.inputs)[0] ?? "value",
        function: "literal",
      });
    });
  }

  function enablePluralForms(): void {
    commit((next) => {
      let input = Object.keys(next.inputs).find((name) => next.inputs[name].type === "int64" || next.inputs[name].type === "decimal");
      if (input === undefined) {
        input = nextIdentifier("count", Object.keys(next.inputs));
        next.inputs[input] = { type: "int64" };
      }
      const name = nextIdentifier("quantity", next.selectors.map((item) => item.name));
      next.selectors.push({ name, input, function: "plural" });
      const original = structuredClone(next.variants[0]?.value ?? "");
      next.variants = [
        { match: { [name]: "one" }, value: original },
        { match: { [name]: "*" }, value: structuredClone(original) },
      ];
    });
  }

  function addVariant(primaryMatch: string): void {
    commit((next) => {
      const matches = Object.fromEntries(next.selectors.map((selector) => [selector.name, "*"]));
      const primarySelector = next.selectors[0];
      if (primarySelector !== undefined) matches[primarySelector.name] = primaryMatch;
      next.variants.splice(Math.max(0, next.variants.length - 1), 0, { match: matches, value: "" });
    });
  }

  function addExactCase(): void {
    if (exactCaseMatch === "" || exactCaseDuplicate) return;
    addVariant(exactCaseMatch);
    exactCaseValue = "";
    exactCaseOpen = false;
  }

  function updateMatch(variantIndex: number, selectorName: string, match: string): void {
    commit((next) => next.variants[variantIndex].match[selectorName] = match || "*");
  }

  function editableText(value: string | MessagePatternNode[]): string | undefined {
    return typeof value === "string" ? value : patternText(value);
  }

  function openRaw(): void {
    rawText = JSON.stringify(message, null, 2);
    rawError = undefined;
    rawMode = true;
  }

  function applyRaw(): void {
    try {
      const next = toStructuredMessage(JSON.parse(rawText) as ResourceValue);
      onchange(synchronizeMatches(next));
      rawMode = false;
      rawError = undefined;
    } catch (error) {
      rawError = error instanceof Error ? error.message : String(error);
    }
  }

  function variantTitle(index: number): string {
    if (message.selectors.length === 0) return "Default translation";
    const labels = message.selectors.map((selector) => matchLabel(selector, message.variants[index].match[selector.name] ?? "*"));
    return labels.join(" + ");
  }

  function variantActionLabel(index: number): string {
    const title = variantTitle(index);
    return title.toLocaleLowerCase().endsWith("translation") ? title : `${title} translation`;
  }

  function matchLabel(selector: MessageSelector, match: string): string {
    if (match === "*") return selector.function === "literal" ? "Fallback" : "Other";
    if (match.startsWith("=")) return `Exactly ${match.slice(1)}`;
    return match.charAt(0).toLocaleUpperCase() + match.slice(1);
  }

  function conditionDescription(selector: MessageSelector, match: string): string {
    if (match === "*") return `Used for every ${selector.input} value without a more specific translation`;
    if (match.startsWith("=")) return `When ${selector.input} equals ${match.slice(1)}`;
    if (selector.function === "plural") return match === "one" ? `Used for the language’s singular form of ${selector.input}` : `Used for the language’s “${match}” number form of ${selector.input}`;
    if (selector.function === "ordinal") return `Used for the language’s “${match}” ordinal form of ${selector.input}`;
    return `When ${selector.input} is “${match}”`;
  }

  function variantDescription(index: number): string {
    if (message.selectors.length === 0) return "Shown whenever this message is used.";
    return message.selectors
      .map((selector) => conditionDescription(selector, message.variants[index].match[selector.name] ?? "*"))
      .join(" · ");
  }

  function updateVariantText(index: number, text: string): void {
    if (value === undefined || typeof value === "string") {
      onchange(text);
      return;
    }
    commit((next) => {
      next.variants[index].value = text;
      for (const node of patternNodes(text)) {
        if (typeof node !== "string" && "input" in node) next.inputs[node.input] ??= { type: "string" };
      }
    });
  }

  function collectInputNames(nodes: MessagePatternNode[], names: Set<string>): void {
    for (const node of nodes) {
      if (typeof node === "string") continue;
      if ("input" in node) names.add(node.input);
      else if ("format" in node) names.add(node.format.input);
      else if ("markup" in node) collectInputNames(node.markup.children, names);
    }
  }

  function isFallback(index: number): boolean {
    return message.selectors.length > 0 && message.selectors.every((selector) => message.variants[index].match[selector.name] === "*");
  }

  function localePluralCategories(targetLocale: string, ordinal: boolean): string[] {
    try {
      return new Intl.PluralRules(targetLocale, ordinal ? { type: "ordinal" } : undefined).resolvedOptions().pluralCategories;
    } catch {
      return ["one", "other"];
    }
  }

  function selectorMatches(selector: MessageSelector): string[] {
    if (selector.function === "literal") return ["*"];
    return ["*", ...localePluralCategories(locale, selector.function === "ordinal")];
  }

  function scrubNodes(
    next: StructuredMessage,
    predicate: (node: Exclude<MessagePatternNode, string>) => boolean,
  ): void {
    const scrub = (nodes: MessagePatternNode[]): MessagePatternNode[] => {
      const result: MessagePatternNode[] = [];
      for (const node of nodes) {
        if (typeof node === "string") result.push(node);
        else if (!predicate(node)) {
          if ("markup" in node) node.markup.children = scrub(node.markup.children);
          result.push(node);
        }
      }
      return result;
    };
    for (const variant of next.variants) {
      if (Array.isArray(variant.value)) variant.value = scrub(variant.value);
    }
  }
</script>

<div class="grid gap-4">
  <header class="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
    <div class="grid gap-1">
      <div class="flex flex-wrap items-center gap-2">
        <h3 class="text-sm font-semibold">Translate the message</h3>
        {#if message.selectors.some((selector) => selector.function === "plural")}
          <Badge variant="secondary">Plural message</Badge>
        {:else if message.selectors.length > 0}
          <Badge variant="secondary">{message.variants.length} cases</Badge>
        {/if}
      </div>
      <p class="text-xs leading-relaxed text-muted-foreground">
        Write naturally. Variables such as <code>{"{count}"}</code> become protected, inspectable chips in the sentence.
      </p>
    </div>
    <Button variant="outline" size="sm" onclick={openRaw}>
      <CodeXmlIcon data-icon="inline-start" />
      Message source
    </Button>
  </header>

  {#if message.selectors.length === 0}
    <Card.Root size="sm">
      <Card.Header>
        <Card.Title>One translation is used in every situation</Card.Title>
        <Card.Description>If wording changes with a number, add plural forms and translate each case separately.</Card.Description>
        <Card.Action>
          <Button variant="outline" size="sm" onclick={enablePluralForms}>
            <CirclePlusIcon data-icon="inline-start" />
            Add plural forms
          </Button>
        </Card.Action>
      </Card.Header>
    </Card.Root>
  {/if}

  <div class="grid gap-3">
    {#each message.variants as variant, variantIndex (variantIndex)}
      <Card.Root size="sm">
        <Card.Header class="gap-2">
          <div class="flex min-w-0 flex-wrap items-center gap-2">
            <Card.Title class="font-serif text-lg">{variantTitle(variantIndex)}</Card.Title>
            {#if isFallback(variantIndex)}<Badge variant="secondary">Required fallback</Badge>{/if}
            {#each message.selectors as selector (selector.name)}
              <Popover.Root>
                <Popover.Trigger class={buttonVariants({ variant: "outline", size: "sm", class: "h-7 rounded-full px-2 text-xs" })}>
                  {selector.input}: {message.variants[variantIndex].match[selector.name] ?? "*"}
                </Popover.Trigger>
                <Popover.Content align="start" class="w-[calc(100vw-2rem)] max-w-80">
                  <Popover.Header>
                    <Popover.Title>When is this translation used?</Popover.Title>
                    <Popover.Description>{conditionDescription(selector, message.variants[variantIndex].match[selector.name] ?? "*")}</Popover.Description>
                  </Popover.Header>
                  {#if isFallback(variantIndex)}
                    <p class="text-sm text-muted-foreground">Every structured message needs this final fallback, so its matching rule cannot be changed or removed.</p>
                  {:else}
                    {#if selector.function === "plural" || selector.function === "ordinal"}
                      <Field.Field>
                        <Field.Label for={`match-${variantIndex}-${selector.name}`}>Number form</Field.Label>
                        <Select.Root
                          type="single"
                          value={message.variants[variantIndex].match[selector.name] ?? "*"}
                          onValueChange={(match) => updateMatch(variantIndex, selector.name, match)}
                        >
                          <Select.Trigger id={`match-${variantIndex}-${selector.name}`} class="w-full">
                            {matchLabel(selector, message.variants[variantIndex].match[selector.name] ?? "*")}
                          </Select.Trigger>
                          <Select.Content>
                            <Select.Group>
                              {#each selectorMatches(selector).filter((match) => match !== "*") as match (match)}
                                <Select.Item value={match} label={match}>{match}</Select.Item>
                              {/each}
                            </Select.Group>
                          </Select.Content>
                        </Select.Root>
                      </Field.Field>
                    {/if}
                    <Field.Field>
                      <Field.Label for={`custom-match-${variantIndex}-${selector.name}`}>Exact or custom match</Field.Label>
                      <Input
                        id={`custom-match-${variantIndex}-${selector.name}`}
                        value={message.variants[variantIndex].match[selector.name] ?? "*"}
                        placeholder={selector.function === "literal" ? "premium" : "=0"}
                        onblur={(event) => updateMatch(variantIndex, selector.name, event.currentTarget.value)}
                      />
                      <Field.Description>Use <code>=0</code> for an exact number.</Field.Description>
                    </Field.Field>
                  {/if}
                </Popover.Content>
              </Popover.Root>
            {/each}
          </div>
          <Card.Description>{variantDescription(variantIndex)}</Card.Description>
          <Card.Action class="flex gap-1">
            <Button variant="ghost" size="icon-sm" aria-label={`Move ${variantActionLabel(variantIndex)} up`} title={`Move ${variantTitle(variantIndex)} up`} disabled={variantIndex === 0 || isFallback(variantIndex)} onclick={() => commit((next) => next.variants.splice(variantIndex - 1, 0, next.variants.splice(variantIndex, 1)[0]))}>
              <ArrowUpIcon />
            </Button>
            <Button variant="ghost" size="icon-sm" aria-label={`Move ${variantActionLabel(variantIndex)} down`} title={`Move ${variantTitle(variantIndex)} down`} disabled={variantIndex === message.variants.length - 1 || isFallback(variantIndex) || isFallback(variantIndex + 1)} onclick={() => commit((next) => next.variants.splice(variantIndex + 1, 0, next.variants.splice(variantIndex, 1)[0]))}>
              <ArrowDownIcon />
            </Button>
            <Button variant="ghost" size="icon-sm" aria-label={`Remove ${variantActionLabel(variantIndex)}`} title={isFallback(variantIndex) ? "The fallback translation is required" : `Remove ${variantTitle(variantIndex)}`} disabled={message.variants.length === 1 || isFallback(variantIndex)} onclick={() => commit((next) => next.variants.splice(variantIndex, 1))}>
              <Trash2Icon />
            </Button>
          </Card.Action>
        </Card.Header>
        <Card.Content class="grid gap-3">
          {#if editableText(variant.value) !== undefined}
            <InlineMessageEditor
              value={editableText(variant.value) ?? ""}
              inputs={effectiveInputs}
              label={`Translation for ${variantTitle(variantIndex)}`}
              onchange={(text) => updateVariantText(variantIndex, text)}
              onensureinput={ensureInput}
              onupdateformat={updateInputFormat}
            />
          {:else}
            <p class="text-xs text-muted-foreground">This case contains formatting or semantic markup. Edit its content blocks below.</p>
            <PatternEditor nodes={variant.value as MessagePatternNode[]} inputs={message.inputs} localNames={declarationNames} onchange={(nodes) => commit((next) => next.variants[variantIndex].value = nodes)} />
          {/if}
        </Card.Content>
      </Card.Root>
    {/each}
  </div>

  {#if message.selectors.length > 0}
    <DropdownMenu.Root>
      <DropdownMenu.Trigger>
        {#snippet child({ props })}
          <Button {...props} variant="outline" class="justify-self-start">
            <CirclePlusIcon data-icon="inline-start" />
            Add translation case
          </Button>
        {/snippet}
      </DropdownMenu.Trigger>
      <DropdownMenu.Content align="start" class="w-64">
        <DropdownMenu.Label>Choose when this translation is used</DropdownMenu.Label>
        {#each availableCaseMatches as match (match)}
          <DropdownMenu.Item onclick={() => addVariant(match)}>
            {match.charAt(0).toLocaleUpperCase() + match.slice(1)} {primarySelector?.function === "ordinal" ? "ordinal" : "plural"} form
          </DropdownMenu.Item>
        {/each}
        <DropdownMenu.Item onclick={() => exactCaseOpen = true}>
          {primarySelector?.function === "literal" ? "Custom value…" : "Exact number…"}
        </DropdownMenu.Item>
      </DropdownMenu.Content>
    </DropdownMenu.Root>
  {/if}

  <Separator />

  <Collapsible.Root bind:open={structureOpen} class="group/structure rounded-3xl bg-card shadow-sm ring-1 ring-foreground/5 dark:ring-foreground/10">
    <div class="flex items-center justify-between gap-3 px-4 py-3">
      <div class="grid gap-0.5">
        <strong class="text-sm">Advanced structure</strong>
        <span class="text-xs text-muted-foreground">Inputs, formatters, and selection rules used by the translations above.</span>
      </div>
      <Collapsible.Trigger class={buttonVariants({ variant: "ghost", size: "icon-sm" })} aria-label="Toggle advanced structure">
        <ChevronDownIcon class="transition-transform group-data-[state=open]/structure:rotate-180" />
      </Collapsible.Trigger>
    </div>
    <Collapsible.Content>
      <Separator />
      <div class="grid gap-6 px-4 py-5">
        <Field.Set>
          <Field.Legend variant="label">Inputs</Field.Legend>
          <Field.Description>Values supplied by application code. Translators insert them by typing <code>{"{name}"}</code>.</Field.Description>
          <Field.Group class="gap-3">
            {#each Object.entries(message.inputs) as [name, descriptor] (name)}
              <div class="grid gap-3 rounded-2xl bg-muted/50 p-3 sm:grid-cols-[minmax(0,1fr)_minmax(0,1fr)_minmax(0,1.25fr)_auto] sm:items-end">
                <Field.Field>
                  <Field.Label for={`input-name-${name}`}>Name</Field.Label>
                  <Input id={`input-name-${name}`} pattern="[A-Za-z_][A-Za-z0-9_]*" value={name} onblur={(event) => onchange(renameInput(message, name, event.currentTarget.value))} />
                </Field.Field>
                <Field.Field>
                  <Field.Label for={`input-type-${name}`}>Type</Field.Label>
                  <Select.Root type="single" value={descriptor.type} onValueChange={(type) => ensureInput(name, type as InputType)}>
                    <Select.Trigger id={`input-type-${name}`} class="w-full">{descriptor.type}</Select.Trigger>
                    <Select.Content><Select.Group>{#each inputTypes as type (type)}<Select.Item value={type} label={type}>{type}</Select.Item>{/each}</Select.Group></Select.Content>
                  </Select.Root>
                </Field.Field>
                <Field.Field>
                  <Field.Label for={`input-format-${name}`}>Default format</Field.Label>
                  <Input id={`input-format-${name}`} value={descriptor.format ?? ""} placeholder="Compiler default" oninput={(event) => updateInputFormat(name, event.currentTarget.value)} />
                </Field.Field>
                <Button variant="ghost" size="icon" aria-label={`Remove input ${name}`} onclick={() => removeInput(name)}><Trash2Icon /></Button>
              </div>
            {/each}
          </Field.Group>
          <Button variant="outline" class="justify-self-start" onclick={() => addInput()}><CirclePlusIcon data-icon="inline-start" />Add input</Button>
        </Field.Set>

        <Field.Set>
          <Field.Legend variant="label">Selection rules</Field.Legend>
          <Field.Description>Rules decide which translation case is used for a given input.</Field.Description>
          <Field.Group class="gap-3">
            {#each message.selectors as selector, index (selector.name)}
              <div class="grid gap-3 rounded-2xl bg-muted/50 p-3 sm:grid-cols-3 sm:items-end">
                <Field.Field><Field.Label for={`selector-name-${selector.name}`}>Rule name</Field.Label><Input id={`selector-name-${selector.name}`} value={selector.name} onblur={(event) => onchange(renameSelector(message, selector.name, event.currentTarget.value))} /></Field.Field>
                <Field.Field>
                  <Field.Label for={`selector-input-${selector.name}`}>Uses input</Field.Label>
                  <Select.Root type="single" value={selector.input} onValueChange={(input) => commit((next) => next.selectors[index].input = input)}>
                    <Select.Trigger id={`selector-input-${selector.name}`} class="w-full">{selector.input}</Select.Trigger>
                    <Select.Content><Select.Group>{#each inputNames as name (name)}<Select.Item value={name} label={name}>{name}</Select.Item>{/each}</Select.Group></Select.Content>
                  </Select.Root>
                </Field.Field>
                <div class="flex items-end gap-2">
                  <Field.Field>
                    <Field.Label for={`selector-function-${selector.name}`}>Chooses by</Field.Label>
                    <Select.Root type="single" value={selector.function} onValueChange={(fn) => commit((next) => next.selectors[index].function = fn as MessageSelector["function"])}>
                      <Select.Trigger id={`selector-function-${selector.name}`} class="w-full">{selector.function}</Select.Trigger>
                      <Select.Content><Select.Group>{#each selectorFunctions as fn (fn)}<Select.Item value={fn} label={fn}>{fn}</Select.Item>{/each}</Select.Group></Select.Content>
                    </Select.Root>
                  </Field.Field>
                  <Button variant="ghost" size="icon" aria-label={`Remove selector ${selector.name}`} onclick={() => commit((next) => next.selectors.splice(index, 1))}><Trash2Icon /></Button>
                </div>
              </div>
            {/each}
          </Field.Group>
          <Button variant="outline" class="justify-self-start" disabled={inputNames.length === 0} onclick={addSelector}><CirclePlusIcon data-icon="inline-start" />Add selection rule</Button>
        </Field.Set>

        <Field.Set>
          <Field.Legend variant="label">Reusable formatters</Field.Legend>
          <Field.Description>Optional named formats for dates, numbers, relative time, and other typed values.</Field.Description>
          <Field.Group class="gap-3">
            {#each message.declarations ?? [] as declaration, index (declaration.name)}
              <div class="grid gap-3 rounded-2xl bg-muted/50 p-3 sm:grid-cols-2 lg:grid-cols-4">
                <Field.Field><Field.Label for={`declaration-name-${declaration.name}`}>Name</Field.Label><Input id={`declaration-name-${declaration.name}`} value={declaration.name} onblur={(event) => onchange(renameDeclaration(message, declaration.name, event.currentTarget.value))} /></Field.Field>
                <Field.Field>
                  <Field.Label for={`declaration-input-${declaration.name}`}>Input</Field.Label>
                  <Select.Root type="single" value={declaration.input} onValueChange={(input) => updateDeclaration(index, "input", input)}><Select.Trigger id={`declaration-input-${declaration.name}`} class="w-full">{declaration.input}</Select.Trigger><Select.Content><Select.Group>{#each inputNames as name (name)}<Select.Item value={name} label={name}>{name}</Select.Item>{/each}</Select.Group></Select.Content></Select.Root>
                </Field.Field>
                <Field.Field>
                  <Field.Label for={`declaration-function-${declaration.name}`}>Formatter</Field.Label>
                  <Select.Root type="single" value={declaration.function} onValueChange={(fn) => updateDeclaration(index, "function", fn)}><Select.Trigger id={`declaration-function-${declaration.name}`} class="w-full">{declaration.function}</Select.Trigger><Select.Content><Select.Group>{#each formatFunctions as fn (fn)}<Select.Item value={fn} label={fn}>{fn}</Select.Item>{/each}</Select.Group></Select.Content></Select.Root>
                </Field.Field>
                <div class="flex items-end gap-2">
                  {#if declaration.function === "relativeTime"}
                    <Field.Field>
                      <Field.Label for={`declaration-unit-${declaration.name}`}>Unit</Field.Label>
                      <Select.Root type="single" value={declaration.unit ?? "day"} onValueChange={(unit) => updateDeclaration(index, "unit", unit)}><Select.Trigger id={`declaration-unit-${declaration.name}`} class="w-full">{declaration.unit ?? "day"}</Select.Trigger><Select.Content><Select.Group>{#each relativeTimeUnits as unit (unit)}<Select.Item value={unit} label={unit}>{unit}</Select.Item>{/each}</Select.Group></Select.Content></Select.Root>
                    </Field.Field>
                    <Field.Field>
                      <Field.Label for={`declaration-numeric-${declaration.name}`}>Numeric</Field.Label>
                      <Select.Root type="single" value={declaration.numeric ?? "auto"} onValueChange={(numeric) => updateDeclaration(index, "numeric", numeric)}><Select.Trigger id={`declaration-numeric-${declaration.name}`} class="w-full">{declaration.numeric ?? "auto"}</Select.Trigger><Select.Content><Select.Group><Select.Item value="auto" label="auto">auto</Select.Item><Select.Item value="always" label="always">always</Select.Item></Select.Group></Select.Content></Select.Root>
                    </Field.Field>
                  {:else}
                    <Field.Field><Field.Label for={`declaration-format-${declaration.name}`}>Format</Field.Label><Input id={`declaration-format-${declaration.name}`} value={declaration.format ?? ""} placeholder="Compiler default" oninput={(event) => updateDeclaration(index, "format", event.currentTarget.value)} /></Field.Field>
                  {/if}
                  <Button variant="ghost" size="icon" aria-label={`Remove formatter ${declaration.name}`} onclick={() => commit((next) => { next.declarations?.splice(index, 1); scrubNodes(next, (node) => "local" in node && node.local === declaration.name); })}><Trash2Icon /></Button>
                </div>
              </div>
            {/each}
          </Field.Group>
          <Button variant="outline" class="justify-self-start" disabled={!inputNames.some((name) => message.inputs[name].type !== "bool")} onclick={addDeclaration}><CirclePlusIcon data-icon="inline-start" />Add formatter</Button>
        </Field.Set>
      </div>
    </Collapsible.Content>
  </Collapsible.Root>
</div>

<AppDialog
  open={exactCaseOpen}
  title={primarySelector?.function === "literal" ? "Add a custom case" : "Add an exact-number case"}
  description={primarySelector?.function === "literal"
    ? "Enter the exact value that should select this translation."
    : "Enter the number that should select this translation instead of the locale’s normal plural form."}
  class="sm:max-w-md"
  bodyClass="grid gap-3"
  onopenchange={(open) => exactCaseOpen = open}
>
  <Field.Field>
    <Field.Label for="exact-case-value">{primarySelector?.function === "literal" ? "Value" : "Exact number"}</Field.Label>
    <Input
      id="exact-case-value"
      type={primarySelector?.function === "literal" ? "text" : "number"}
      bind:value={exactCaseValue}
      placeholder={primarySelector?.function === "literal" ? "premium" : "0"}
      onkeydown={(event) => { if (event.key === "Enter") addExactCase(); }}
    />
    {#if exactCaseDuplicate}<Field.Error>This case already exists.</Field.Error>{/if}
  </Field.Field>
  {#snippet footer()}
    <Button variant="outline" onclick={() => exactCaseOpen = false}>Cancel</Button>
    <Button disabled={exactCaseMatch === "" || exactCaseDuplicate} onclick={addExactCase}>Add case</Button>
  {/snippet}
</AppDialog>

<AppDialog
  open={rawMode}
  title="Structured message source"
  description="An escape hatch for exact schema-v2 source editing."
  class="sm:max-w-3xl"
  bodyClass="grid gap-3"
  onopenchange={(open) => rawMode = open}
>
  <Textarea class="field-sizing-fixed min-h-[55svh] resize-none font-mono text-xs leading-relaxed" bind:value={rawText} spellcheck={false} aria-label="Structured message source" />
  {#if rawError}<p class="text-sm text-destructive" aria-live="polite">{rawError}</p>{/if}
  {#snippet footer()}
    <Button variant="outline" onclick={() => rawMode = false}>Cancel</Button>
    <Button onclick={applyRaw}><Settings2Icon data-icon="inline-start" />Apply source</Button>
  {/snippet}
</AppDialog>

<style>
  code {
    border-radius: var(--radius-sm);
    padding: 0.08rem 0.25rem;
    color: color-mix(in oklab, var(--primary) 72%, var(--foreground));
    background: var(--muted);
    font: 0.67rem ui-monospace, monospace;
  }
</style>
