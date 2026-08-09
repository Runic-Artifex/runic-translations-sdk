<script lang="ts">
  import {
    formatFunctions,
    relativeTimeUnits,
    type FormatFunction,
    type MessageInput,
    type MessagePatternNode,
  } from "./message-composer";

  interface Props {
    nodes: MessagePatternNode[];
    inputs: Record<string, MessageInput>;
    localNames: string[];
    onchange: (nodes: MessagePatternNode[]) => void;
  }

  let { nodes, inputs, localNames, onchange }: Props = $props();
  let inputNames = $derived(Object.keys(inputs));
  let formattableInputs = $derived(inputNames.filter((name) => inputs[name].type !== "bool"));

  function kind(node: MessagePatternNode): "text" | "input" | "local" | "format" | "markup" {
    if (typeof node === "string") return "text";
    if ("input" in node) return "input";
    if ("local" in node) return "local";
    if ("format" in node) return "format";
    return "markup";
  }

  function replacement(type: ReturnType<typeof kind>): MessagePatternNode {
    const input = inputNames[0] ?? "value";
    if (type === "text") return "";
    if (type === "input") return { input };
    if (type === "local") return { local: localNames[0] ?? "formattedValue" };
    if (type === "format") {
      const formattedInput = formattableInputs[0] ?? input;
      return { format: { input: formattedInput, function: functionFor(inputs[formattedInput]?.type) } };
    }
    return { markup: { name: "strong", attributes: {}, children: [""] } };
  }

  function functionFor(type: MessageInput["type"] | undefined): FormatFunction {
    return ({ int64: "integer", decimal: "number", date: "date", time: "time", instant: "datetime", uuid: "uuid" } as Partial<Record<MessageInput["type"], FormatFunction>>)[type ?? "string"] ?? "string";
  }

  function listAt(root: MessagePatternNode[], path: number[]): MessagePatternNode[] {
    let list = root;
    for (const index of path) {
      const node = list[index];
      if (typeof node === "string" || !("markup" in node)) throw new TypeError("Invalid markup path.");
      list = node.markup.children;
    }
    return list;
  }

  function mutate(path: number[], action: (list: MessagePatternNode[]) => void): void {
    const next = structuredClone(nodes);
    action(listAt(next, path));
    onchange(next);
  }

  function replace(path: number[], index: number, node: MessagePatternNode): void {
    mutate(path, (list) => list[index] = node);
  }

  function updateFormat(path: number[], index: number, property: string, value: string): void {
    mutate(path, (list) => {
      const node = list[index];
      if (typeof node === "string" || !("format" in node)) return;
      const format = node.format as unknown as Record<string, string>;
      if (property === "format" && value === "") delete format.format;
      else format[property] = value;
      if (property === "function" && value === "relativeTime") {
        format.unit = "day";
        format.numeric = "auto";
      } else if (property === "function") {
        delete format.unit;
        delete format.numeric;
      }
    });
  }

  function addAttribute(path: number[], index: number): void {
    mutate(path, (list) => {
      const node = list[index];
      if (typeof node !== "string" && "markup" in node) {
        node.markup.attributes ??= {};
        let name = "attribute";
        let suffix = 2;
        while (name in node.markup.attributes) name = `attribute${suffix++}`;
        node.markup.attributes[name] = "";
      }
    });
  }

  function updateAttribute(path: number[], index: number, previous: string, name: string, value: string): void {
    mutate(path, (list) => {
      const node = list[index];
      if (typeof node === "string" || !("markup" in node)) return;
      const next: Record<string, string> = {};
      for (const [key, oldValue] of Object.entries(node.markup.attributes ?? {})) {
        if (key !== previous) next[key] = oldValue;
      }
      next[name] = value;
      node.markup.attributes = next;
    });
  }

  function removeAttribute(path: number[], index: number, name: string): void {
    mutate(path, (list) => {
      const node = list[index];
      if (typeof node !== "string" && "markup" in node) delete node.markup.attributes?.[name];
    });
  }
</script>

{#snippet nodeList(list: MessagePatternNode[], path: number[], depth: number)}
  <div class="pattern-list" class:nested={depth > 0}>
    {#each list as node, index (`${path.join(".")}-${index}-${kind(node)}`)}
      <article class="pattern-node">
        <header>
          <label>Content type
            <select value={kind(node)} onchange={(event) => replace(path, index, replacement(event.currentTarget.value as ReturnType<typeof kind>))}>
              <option value="text">Text</option>
              <option value="input" disabled={inputNames.length === 0}>Input chip</option>
              <option value="local" disabled={localNames.length === 0}>Declaration chip</option>
              <option value="format" disabled={formattableInputs.length === 0}>Inline formatter</option>
              <option value="markup">Semantic markup</option>
            </select>
          </label>
          <div class="node-actions">
            <button aria-label="Move content up" disabled={index === 0} onclick={() => mutate(path, (items) => items.splice(index - 1, 0, items.splice(index, 1)[0]))}>↑</button>
            <button aria-label="Move content down" disabled={index === list.length - 1} onclick={() => mutate(path, (items) => items.splice(index + 1, 0, items.splice(index, 1)[0]))}>↓</button>
            <button class="remove" aria-label="Remove content" onclick={() => mutate(path, (items) => items.splice(index, 1))}>×</button>
          </div>
        </header>

        {#if typeof node === "string"}
          <textarea aria-label="Text content" value={node} oninput={(event) => replace(path, index, event.currentTarget.value)}></textarea>
        {:else if "input" in node}
          <label class="chip-field">Protected input
            <select value={node.input} onchange={(event) => replace(path, index, { input: event.currentTarget.value })}>
              {#each inputNames as name (name)}<option value={name}>{name}</option>{/each}
            </select>
          </label>
        {:else if "local" in node}
          <label class="chip-field local">Formatted declaration
            <select value={node.local} onchange={(event) => replace(path, index, { local: event.currentTarget.value })}>
              {#each localNames as name (name)}<option value={name}>{name}</option>{/each}
            </select>
          </label>
        {:else if "format" in node}
          <div class="format-grid">
            <label>Input<select value={node.format.input} onchange={(event) => updateFormat(path, index, "input", event.currentTarget.value)}>{#each formattableInputs as name (name)}<option value={name}>{name}</option>{/each}</select></label>
            <label>Formatter<select value={node.format.function} onchange={(event) => updateFormat(path, index, "function", event.currentTarget.value as FormatFunction)}>{#each formatFunctions as fn (fn)}<option value={fn}>{fn}</option>{/each}</select></label>
            {#if node.format.function === "relativeTime"}
              <label>Unit<select value={node.format.unit ?? "day"} onchange={(event) => updateFormat(path, index, "unit", event.currentTarget.value)}>{#each relativeTimeUnits as unit (unit)}<option value={unit}>{unit}</option>{/each}</select></label>
              <label>Numeric<select value={node.format.numeric ?? "auto"} onchange={(event) => updateFormat(path, index, "numeric", event.currentTarget.value)}><option value="auto">auto</option><option value="always">always</option></select></label>
            {:else}
              <label>Format<input value={node.format.format ?? ""} placeholder="compiler default" oninput={(event) => updateFormat(path, index, "format", event.currentTarget.value)} /></label>
            {/if}
          </div>
        {:else}
          <div class="markup-editor">
            <label>Semantic name<input value={node.markup.name} oninput={(event) => mutate(path, (items) => { const current = items[index]; if (typeof current !== "string" && "markup" in current) current.markup.name = event.currentTarget.value; })} /></label>
            <div class="attributes">
              <header><strong>Attributes</strong><button onclick={() => addAttribute(path, index)}>＋ Add</button></header>
              {#each Object.entries(node.markup.attributes ?? {}) as [name, value] (name)}
                <div><input aria-label="Attribute name" value={name} oninput={(event) => updateAttribute(path, index, name, event.currentTarget.value, value)} /><input aria-label={`Value for ${name}`} value={value} oninput={(event) => updateAttribute(path, index, name, name, event.currentTarget.value)} /><button aria-label={`Remove ${name}`} onclick={() => removeAttribute(path, index, name)}>×</button></div>
              {/each}
            </div>
            <div class="children-label">Children · rendered as safe semantic data</div>
            {@render nodeList(node.markup.children, [...path, index], depth + 1)}
          </div>
        {/if}
      </article>
    {/each}
    <div class="add-nodes" role="group" aria-label="Add content">
      <button onclick={() => mutate(path, (items) => items.push(""))}>＋ Text</button>
      <button disabled={inputNames.length === 0} onclick={() => mutate(path, (items) => items.push({ input: inputNames[0] }))}>＋ Input</button>
      <button disabled={localNames.length === 0} onclick={() => mutate(path, (items) => items.push({ local: localNames[0] }))}>＋ Declaration</button>
      <button disabled={formattableInputs.length === 0} onclick={() => mutate(path, (items) => items.push(replacement("format")))}>＋ Formatter</button>
      <button onclick={() => mutate(path, (items) => items.push(replacement("markup")))}>＋ Markup</button>
    </div>
  </div>
{/snippet}

{@render nodeList(nodes, [], 0)}

<style>
  .pattern-list { display: grid; gap: .55rem; }
  .pattern-list.nested { border-left: 2px solid color-mix(in oklch, var(--primary) 42%, var(--border)); padding-left: .7rem; }
  .pattern-node { border: 1px solid var(--border); border-radius: .5rem; padding: .65rem; color: var(--card-foreground); background: var(--card); }
  .pattern-node > header, .attributes > header { display: flex; align-items: end; justify-content: space-between; gap: .6rem; margin-bottom: .55rem; }
  label { display: grid; gap: .28rem; color: var(--muted-foreground); font-size: .57rem; font-weight: 650; }
  input, select, textarea { min-width: 0; border: 1px solid var(--input); border-radius: .35rem; padding: .45rem .5rem; color: var(--foreground); background: var(--background); font: .63rem ui-monospace, monospace; }
  textarea { width: 100%; min-height: 4.2rem; resize: vertical; line-height: 1.5; }
  button { border: 1px solid var(--border); border-radius: .3rem; padding: .35rem .45rem; color: var(--secondary-foreground); background: var(--secondary); font-size: .56rem; cursor: pointer; }
  button:hover:not(:disabled) { border-color: var(--ring); color: var(--accent-foreground); background: var(--accent); }
  button:disabled { opacity: .35; cursor: not-allowed; }
  .node-actions, .add-nodes { display: flex; flex-wrap: wrap; gap: .3rem; }
  .node-actions .remove { color: var(--destructive); }
  .chip-field select { border-color: color-mix(in oklch, var(--primary) 55%, var(--border)); color: var(--primary); background: color-mix(in oklch, var(--primary) 10%, var(--background)); }
  .chip-field.local select { border-color: color-mix(in oklch, var(--chart-2) 55%, var(--border)); color: var(--chart-2); background: color-mix(in oklch, var(--chart-2) 10%, var(--background)); }
  .format-grid { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: .55rem; }
  .markup-editor { display: grid; gap: .65rem; }
  .attributes { border: 1px solid var(--border); border-radius: .4rem; padding: .55rem; background: var(--muted); }
  .attributes header { margin-bottom: .4rem; }
  .attributes strong, .children-label { color: var(--muted-foreground); font-size: .55rem; text-transform: uppercase; letter-spacing: .08em; }
  .attributes > div { display: grid; grid-template-columns: 1fr 1.5fr auto; gap: .35rem; margin-top: .35rem; }
  .add-nodes { border: 1px dashed var(--border); border-radius: .4rem; padding: .45rem; }
</style>
