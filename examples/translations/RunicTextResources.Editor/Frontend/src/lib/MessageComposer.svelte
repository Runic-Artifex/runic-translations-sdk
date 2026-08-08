<script lang="ts">
  import PatternEditor from "./PatternEditor.svelte";
  import {
    formatFunctions, inputTypes, nextIdentifier, patternNodes, patternText, relativeTimeUnits,
    renameDeclaration, renameInput, renameSelector, selectorFunctions, synchronizeMatches,
    toStructuredMessage, type FormatFunction, type InputType, type MessageFormat,
    type MessagePatternNode, type StructuredMessage,
  } from "./message-composer";
  import type { ResourceValue } from "./resource-model";

  interface Props {
    value: ResourceValue | undefined;
    onchange: (value: StructuredMessage) => void;
  }

  let { value, onchange }: Props = $props();
  let rawMode = $state(false);
  let rawText = $state("");
  let rawError = $state<string>();
  let message = $derived(toStructuredMessage(value));
  let inputNames = $derived(Object.keys(message.inputs));
  let declarationNames = $derived((message.declarations ?? []).map((item) => item.name));

  function commit(action: (next: StructuredMessage) => void): void {
    const next = structuredClone(message);
    action(next);
    onchange(synchronizeMatches(next));
  }

  function addInput(): void {
    commit((next) => {
      const name = nextIdentifier("value", Object.keys(next.inputs));
      next.inputs[name] = { type: "string" };
    });
  }

  function removeInput(name: string): void {
    commit((next) => {
      delete next.inputs[name];
      next.declarations = next.declarations?.filter((item) => item.input !== name);
      next.selectors = next.selectors.filter((item) => item.input !== name);
      scrubNodes(next, (node) =>
        ("input" in node && node.input === name) ||
        ("format" in node && node.format.input === name));
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

  function addVariant(): void {
    commit((next) => next.variants.splice(Math.max(0, next.variants.length - 1), 0, {
      match: Object.fromEntries(next.selectors.map((selector) => [selector.name, "*"])),
      value: "",
    }));
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

<div class="composer">
  <header class="composer-header">
    <div>
      <strong>Structured message composer</strong>
      <span>Inputs and declarations stay protected while translators arrange content.</span>
    </div>
    <button class="raw-button" onclick={openRaw}>AST source</button>
  </header>

  <details open>
    <summary><span>1</span><strong>Inputs</strong><small>{inputNames.length} declared</small></summary>
    <div class="section-body">
      <p>Typed values supplied by application code. References become protected chips.</p>
      <div class="table-list">
        {#each Object.entries(message.inputs) as [name, descriptor] (name)}
          <div class="input-row">
            <label>Name<input pattern="[A-Za-z_][A-Za-z0-9_]*" value={name} onblur={(event) => onchange(renameInput(message, name, event.currentTarget.value))} /></label>
            <label>Type<select value={descriptor.type} onchange={(event) => commit((next) => next.inputs[name].type = event.currentTarget.value as InputType)}>{#each inputTypes as type (type)}<option value={type}>{type}</option>{/each}</select></label>
            <label>Default format<input value={descriptor.format ?? ""} placeholder="compiler default" oninput={(event) => commit((next) => { const input = next.inputs[name]; if (event.currentTarget.value === "") delete input.format; else input.format = event.currentTarget.value; })} /></label>
            <button class="remove" aria-label={"Remove input " + name} onclick={() => removeInput(name)}>×</button>
          </div>
        {/each}
      </div>
      <button class="add" onclick={addInput}>＋ Add typed input</button>
    </div>
  </details>

  <details>
    <summary><span>2</span><strong>Formatted declarations</strong><small>{declarationNames.length} reusable</small></summary>
    <div class="section-body">
      <p>Define a formatter once, then insert it as a declaration chip in any variant.</p>
      {#each message.declarations ?? [] as declaration, index (declaration.name)}
        <div class="declaration-card">
          <div class="format-grid">
            <label>Name<input value={declaration.name} onblur={(event) => onchange(renameDeclaration(message, declaration.name, event.currentTarget.value))} /></label>
            <label>Input<select value={declaration.input} onchange={(event) => updateDeclaration(index, "input", event.currentTarget.value)}>{#each inputNames as name (name)}<option value={name}>{name}</option>{/each}</select></label>
            <label>Formatter<select value={declaration.function} onchange={(event) => updateDeclaration(index, "function", event.currentTarget.value as FormatFunction)}>{#each formatFunctions as fn (fn)}<option value={fn}>{fn}</option>{/each}</select></label>
            {#if declaration.function === "relativeTime"}
              <label>Unit<select value={declaration.unit ?? "day"} onchange={(event) => updateDeclaration(index, "unit", event.currentTarget.value)}>{#each relativeTimeUnits as unit (unit)}<option value={unit}>{unit}</option>{/each}</select></label>
              <label>Numeric<select value={declaration.numeric ?? "auto"} onchange={(event) => updateDeclaration(index, "numeric", event.currentTarget.value)}><option value="auto">auto</option><option value="always">always</option></select></label>
            {:else}
              <label>Format<input value={declaration.format ?? ""} placeholder="compiler default" oninput={(event) => updateDeclaration(index, "format", event.currentTarget.value)} /></label>
            {/if}
          </div>
          <button class="remove" aria-label={"Remove declaration " + declaration.name} onclick={() => commit((next) => { next.declarations?.splice(index, 1); scrubNodes(next, (node) => "local" in node && node.local === declaration.name); })}>×</button>
        </div>
      {/each}
      <button class="add" disabled={!inputNames.some((name) => message.inputs[name].type !== "bool")} onclick={addDeclaration}>＋ Add formatter declaration</button>
    </div>
  </details>

  <details open>
    <summary><span>3</span><strong>Selectors</strong><small>{message.selectors.length} dimensions</small></summary>
    <div class="section-body">
      <p>Choose variants using cardinal plural, ordinal plural, or literal values.</p>
      {#each message.selectors as selector, index (selector.name)}
        <div class="selector-row">
          <label>Name<input value={selector.name} onblur={(event) => onchange(renameSelector(message, selector.name, event.currentTarget.value))} /></label>
          <label>Input<select value={selector.input} onchange={(event) => commit((next) => next.selectors[index].input = event.currentTarget.value)}>{#each inputNames as name (name)}<option value={name}>{name}</option>{/each}</select></label>
          <label>Function<select value={selector.function} onchange={(event) => commit((next) => next.selectors[index].function = event.currentTarget.value as typeof selector.function)}>{#each selectorFunctions as fn (fn)}<option value={fn}>{fn}</option>{/each}</select></label>
          <button class="remove" aria-label={"Remove selector " + selector.name} onclick={() => commit((next) => next.selectors.splice(index, 1))}>×</button>
        </div>
      {/each}
      <button class="add" disabled={inputNames.length === 0} onclick={addSelector}>＋ Add selector</button>
    </div>
  </details>

  <details open>
    <summary><span>4</span><strong>Variants and content</strong><small>{message.variants.length} rows</small></summary>
    <div class="section-body variants">
      <p>The first matching row wins. Keep an all-wildcard row as the final fallback.</p>
      {#each message.variants as variant, variantIndex (variantIndex)}
        <article class="variant-card">
          <header>
            <div class="matches">
              {#if message.selectors.length === 0}<span class="always">Always</span>{/if}
              {#each message.selectors as selector (selector.name)}
                <label>{selector.name}<input aria-label={"Match " + selector.name} value={variant.match[selector.name] ?? "*"} oninput={(event) => commit((next) => next.variants[variantIndex].match[selector.name] = event.currentTarget.value)} /></label>
              {/each}
            </div>
            <div class="variant-actions">
              <button aria-label="Move variant up" disabled={variantIndex === 0} onclick={() => commit((next) => next.variants.splice(variantIndex - 1, 0, next.variants.splice(variantIndex, 1)[0]))}>↑</button>
              <button aria-label="Move variant down" disabled={variantIndex === message.variants.length - 1} onclick={() => commit((next) => next.variants.splice(variantIndex + 1, 0, next.variants.splice(variantIndex, 1)[0]))}>↓</button>
              <button class="remove" aria-label="Remove variant" disabled={message.variants.length === 1} onclick={() => commit((next) => next.variants.splice(variantIndex, 1))}>×</button>
            </div>
          </header>
          <div class="pattern-mode">
            <button class:active={typeof variant.value === "string"} disabled={Array.isArray(variant.value) && patternText(variant.value) === undefined} title={Array.isArray(variant.value) && patternText(variant.value) === undefined ? "Formatted declarations and markup require rich pattern mode." : undefined} onclick={() => commit((next) => { const current = next.variants[variantIndex].value; if (Array.isArray(current)) next.variants[variantIndex].value = patternText(current) ?? current; })}>Plain text</button>
            <button class:active={Array.isArray(variant.value)} onclick={() => commit((next) => next.variants[variantIndex].value = patternNodes(next.variants[variantIndex].value))}>Rich pattern</button>
          </div>
          {#if typeof variant.value === "string"}
            <textarea aria-label={"Text for variant " + (variantIndex + 1)} value={variant.value} oninput={(event) => commit((next) => next.variants[variantIndex].value = event.currentTarget.value)}></textarea>
          {:else}
            <PatternEditor nodes={variant.value} inputs={message.inputs} localNames={declarationNames} onchange={(nodes) => commit((next) => next.variants[variantIndex].value = nodes)} />
          {/if}
        </article>
      {/each}
      <button class="add" onclick={addVariant}>＋ Add variant before fallback</button>
    </div>
  </details>
</div>

{#if rawMode}
  <div class="raw-backdrop">
    <div class="raw-dialog" role="dialog" aria-modal="true" aria-labelledby="raw-ast-title">
      <header><div><strong id="raw-ast-title">Structured message source AST</strong><span>Escape hatch for exact schema-v2 source editing.</span></div><button aria-label="Close source AST" onclick={() => rawMode = false}>×</button></header>
      <textarea bind:value={rawText} spellcheck={false}></textarea>
      {#if rawError}<p aria-live="polite">{rawError}</p>{/if}
      <footer><button onclick={() => rawMode = false}>Cancel</button><button class="apply" onclick={applyRaw}>Apply source</button></footer>
    </div>
  </div>
{/if}

<style>
  .composer { display: grid; gap: .7rem; }
  .composer-header { display: flex; align-items: center; justify-content: space-between; gap: 1rem; border: 1px solid #3c453f; border-radius: .6rem; padding: .8rem .9rem; background: #151b17; }
  .composer-header div { display: grid; gap: .2rem; }
  .composer-header strong { color: #e0e4e1; font-size: .72rem; }
  .composer-header span { color: #707b73; font-size: .59rem; }
  button { border: 1px solid #3d463f; border-radius: .35rem; padding: .42rem .55rem; color: #aab3ac; background: #171d19; font-size: .59rem; cursor: pointer; }
  button:hover:not(:disabled) { border-color: #716441; color: #e5d6aa; }
  button:disabled { cursor: not-allowed; opacity: .35; }
  .raw-button, .add { color: #c1aa69; }
  details { border: 1px solid #303833; border-radius: .55rem; background: #0d110f; overflow: hidden; }
  summary { display: grid; grid-template-columns: auto auto 1fr; align-items: center; gap: .55rem; padding: .7rem .8rem; color: #bcc5be; background: #151a17; cursor: pointer; }
  summary > span { display: grid; place-items: center; width: 1.3rem; height: 1.3rem; border: 1px solid #62583a; border-radius: 50%; color: #d1b96f; font: .54rem ui-monospace, monospace; }
  summary strong { font-size: .66rem; }
  summary small { justify-self: end; color: #657068; font-size: .55rem; font-weight: 400; }
  .section-body { display: grid; gap: .6rem; padding: .75rem; }
  .section-body > p { margin: 0; color: #6c776f; font-size: .59rem; line-height: 1.5; }
  .table-list { display: grid; gap: .45rem; }
  .input-row, .selector-row { display: grid; grid-template-columns: 1fr 1fr 1.25fr auto; align-items: end; gap: .45rem; }
  .selector-row { grid-template-columns: 1fr 1fr 1fr auto; }
  label { display: grid; gap: .28rem; color: #89948c; font-size: .56rem; font-weight: 650; }
  input, select, textarea { min-width: 0; border: 1px solid #3a433d; border-radius: .35rem; outline: 0; padding: .48rem .52rem; color: #e5e9e6; background: #090d0b; font: .63rem ui-monospace, monospace; }
  input:focus, select:focus, textarea:focus { border-color: #8c7848; }
  .remove { color: #d28c82; }
  .add { justify-self: start; border-style: dashed; }
  .declaration-card { display: grid; grid-template-columns: 1fr auto; align-items: end; gap: .5rem; border: 1px solid #313a34; border-radius: .45rem; padding: .6rem; background: #101512; }
  .format-grid { display: grid; grid-template-columns: repeat(4, minmax(0, 1fr)); gap: .45rem; }
  .variants { gap: .75rem; }
  .variant-card { display: grid; gap: .55rem; border: 1px solid #363f39; border-radius: .55rem; padding: .7rem; background: #111613; }
  .variant-card > header { display: flex; align-items: end; justify-content: space-between; gap: .5rem; }
  .matches { display: flex; flex-wrap: wrap; gap: .45rem; }
  .matches label { width: 9rem; }
  .always { align-self: center; border-radius: 1rem; padding: .25rem .5rem; color: #9fc1a8; background: #1c3022; font-size: .56rem; }
  .variant-actions, .pattern-mode { display: flex; gap: .25rem; }
  .pattern-mode button { border: 0; color: #68736b; background: transparent; }
  .pattern-mode button.active { color: #d3be7d; background: #29271b; }
  .variant-card > textarea { min-height: 6rem; resize: vertical; line-height: 1.55; }
  .raw-backdrop { position: fixed; z-index: 45; inset: 0; display: grid; place-items: center; padding: 2rem; background: #050706e8; }
  .raw-dialog { display: grid; grid-template-rows: auto minmax(0, 1fr) auto auto; width: min(780px, 100%); height: min(720px, calc(100vh - 4rem)); border: 1px solid #474f48; border-radius: .7rem; background: #101512; overflow: hidden; }
  .raw-dialog > header, .raw-dialog > footer { display: flex; align-items: center; justify-content: space-between; gap: .6rem; padding: .8rem 1rem; background: #161c18; }
  .raw-dialog header div { display: grid; gap: .15rem; }
  .raw-dialog header strong { color: #e7e2d4; font-size: .75rem; }
  .raw-dialog header span { color: #707b73; font-size: .58rem; }
  .raw-dialog > textarea { border: 0; border-block: 1px solid #303833; border-radius: 0; padding: 1rem; resize: none; line-height: 1.55; }
  .raw-dialog > p { margin: 0; padding: .6rem 1rem; color: #e39b90; background: #291b18; font-size: .62rem; }
  .raw-dialog footer { justify-content: flex-end; }
  .raw-dialog .apply { border-color: #b59a53; color: #1b170d; background: #c8ad63; font-weight: 700; }
</style>
