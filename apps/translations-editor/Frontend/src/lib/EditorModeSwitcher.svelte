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
        class="min-w-0 px-1 text-[0.6875rem] sm:flex-none sm:px-3 sm:text-sm"
        onclick={(event) => {
          if (mode === option.value) event.preventDefault();
        }}
      >{option.label}</ToggleGroup.Item>
    {/each}
  </ToggleGroup.Root>
</div>
