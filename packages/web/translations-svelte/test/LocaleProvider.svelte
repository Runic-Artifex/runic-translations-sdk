<script lang="ts">
  import type { Snippet } from "svelte";
  import type { LocaleSource } from "../src/translations/index.js";
  import { testLocaleContext, type TestLocale } from "./locale-context.js";

  let {
    source,
    dispose = true,
    children,
  }: {
    source: LocaleSource<TestLocale>;
    dispose?: boolean;
    children: Snippet;
  } = $props();

  // The provider contract intentionally owns its initialization-time source.
  // svelte-ignore state_referenced_locally
  testLocaleContext.provide(source, { dispose });
</script>

{@render children()}
