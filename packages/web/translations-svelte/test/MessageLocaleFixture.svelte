<script lang="ts">
  import { useMessageLocale, type SvelteLocale } from "../src/translations/index.js";
  import { configureLocaleResolver, m } from "./message-runtime.js";

  // A ViewModel's reactive state, as useView returns it, or a SvelteLocale over a locale source.
  let { state, locale }: { state: { locale: string; count: number }; locale?: SvelteLocale<string> } = $props();
  // svelte-ignore state_referenced_locally
  useMessageLocale(configureLocaleResolver, locale ?? (() => state.locale));
  const summary = $derived(m.count({ count: state.count }));
</script>

<h1>{m.title()}</h1>
<p>{summary}</p>
