<script lang="ts" module>
  export type EditorMode = "simple" | "advanced" | "raw";
</script>

<script lang="ts">
  import * as ToggleGroup from "$lib/components/ui/toggle-group/index.js";

  let {
    mode,
    simpleLabel,
    advancedLabel,
    rawLabel,
    onchange,
  }: {
    mode: EditorMode;
    simpleLabel: string;
    advancedLabel: string;
    rawLabel: string;
    onchange: (mode: EditorMode) => void;
  } = $props();

  let options: { value: EditorMode; label: string }[] = $derived([
    { value: "simple", label: simpleLabel },
    { value: "advanced", label: advancedLabel },
    { value: "raw", label: rawLabel },
  ]);
</script>

<div class="mx-auto max-w-[1000px] border-b pb-2">
  <ToggleGroup.Root
    type="single"
    variant="outline"
    size="sm"
    spacing={1}
    value={mode}
    class="grid w-full grid-cols-3 sm:flex sm:w-auto"
    aria-label="Editing mode"
    onValueChange={(value) => {
      if (value !== "") onchange(value as EditorMode);
    }}
  >
    {#each options as option (option.value)}
      <ToggleGroup.Item
        value={option.value}
        class="min-w-0 overflow-hidden px-1 text-[0.6875rem] sm:w-auto sm:flex-none sm:px-3 sm:text-sm"
        title={option.label}
        onclick={(event) => {
          if (mode === option.value) event.preventDefault();
        }}
      >
        <span class="sm:hidden">{option.value === "advanced" ? "Structured" : option.value === "raw" ? "JSON" : option.label}</span>
        <span class="hidden sm:inline">{option.label}</span>
      </ToggleGroup.Item>
    {/each}
  </ToggleGroup.Root>
</div>
