<script lang="ts">
  import { testLocaleContext } from "./locale-context.js";

  let { id }: { id: string } = $props();
  const locale = testLocaleContext.use();

  function generatedGreeting(options: Readonly<{ locale: "en" | "de" }>): string {
    return options.locale === "de" ? "Hallo" : "Hello";
  }
</script>

<output data-locale-consumer={id}>{generatedGreeting(locale.messageOptions)}</output>
