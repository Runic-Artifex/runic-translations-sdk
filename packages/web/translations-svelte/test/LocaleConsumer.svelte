<script lang="ts">
  import { testLocaleContext } from "./locale-context.js";

  let { id }: { id: string } = $props();
  const locale = testLocaleContext.use();

  // As a generated message function, it takes any requested tag (MessageOptions is { locale?: string }).
  function generatedGreeting(options: Readonly<{ locale: string }>): string {
    return options.locale === "de" ? "Hallo" : "Hello";
  }
</script>

<output data-locale-consumer={id}>{generatedGreeting(locale.messageOptions)}</output>
