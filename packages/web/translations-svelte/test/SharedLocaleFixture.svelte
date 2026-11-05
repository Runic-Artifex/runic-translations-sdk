<script lang="ts">
  import { useMessageLocale, type SvelteLocale } from "../src/translations/index.js";
  import { library } from "./message-runtime.js";

  // One call, no runtime argument: every catalog without a resolver of its own follows the locale.
  let { state, locale }: { state: { locale: string }; locale?: SvelteLocale<string> } = $props();
  // svelte-ignore state_referenced_locally
  useMessageLocale(locale ?? (() => state.locale));
</script>

<p>{library.empty()}</p>
<span>{library.requested()}</span>
