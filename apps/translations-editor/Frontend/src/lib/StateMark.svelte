<script lang="ts">
  import type { StateMarkKind } from "./state-mark";

  // A state's shape (state-mark.ts). Decorative by default: the text next to it, or the row's accessible name, names
  // the state. With `label` it is an image named by that label.
  let { state, label, class: className }: { state: StateMarkKind; label?: string; class?: string } = $props();
</script>

<span
  class={["state-mark", className]}
  data-state-mark={state}
  role={label === undefined ? undefined : "img"}
  aria-label={label}
  aria-hidden={label === undefined ? "true" : undefined}
  title={label}
></span>
