<script lang="ts">
  import { createLocaleContext, type MutableLocaleSource } from "../src/translations/index.js";

  let { source }: { source: MutableLocaleSource<"en" | "de"> } = $props();
  const localeContext = createLocaleContext<"en" | "de">();
  // The provider intentionally owns the source captured at component creation.
  // svelte-ignore state_referenced_locally
  const locale = localeContext.provide(source);
</script>

<output data-locale-hydration>{locale.locale}</output>
