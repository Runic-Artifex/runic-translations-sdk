<script lang="ts">
  import type { LocaleSource } from "../src/translations/index.js";
  import LocaleConsumer from "./LocaleConsumer.svelte";
  import LocaleProvider from "./LocaleProvider.svelte";
  import type { TestLocale } from "./locale-context.js";

  let {
    outer,
    inner,
  }: {
    outer: LocaleSource<TestLocale>;
    inner: LocaleSource<TestLocale>;
  } = $props();
</script>

<LocaleProvider source={outer}>
  <LocaleConsumer id="outer" />
  <LocaleProvider source={inner}>
    <LocaleConsumer id="inner" />
  </LocaleProvider>
</LocaleProvider>
