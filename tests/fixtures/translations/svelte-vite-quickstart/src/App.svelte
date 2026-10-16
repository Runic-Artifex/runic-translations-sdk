<script lang="ts">
  import { LocalizedInline, inlineFactory } from "@runic-artifex/translations-svelte/inline";
  import { useMessageLocale } from "@runic-artifex/translations-svelte/translations";
  import { m } from "virtual:runic-translations/app";
  import { configureLocaleResolver, createInlineRenderer, linkBinding } from "virtual:runic-translations/app/runtime";

  // The application owns the locale, as a ViewModel's language setting would.
  let locale = $state(new URLSearchParams(location.search).get("lang") ?? "en");
  // m.*() follows it: the page updates in place when it changes.
  useMessageLocale(configureLocaleResolver, () => locale);
  $effect(() => { document.documentElement.lang = locale; });

  const inline = createInlineRenderer(inlineFactory);
  const terms = $derived(inline.render(m.app_terms(), { slots: { terms: linkBinding({ href: "/terms" }) } }));
</script>

<h1>{m.app_title()}</h1>
<p id="hours">{m.app_hours({ count: 1.5 })}</p>
<p id="terms"><LocalizedInline nodes={terms} /></p>
<button onclick={() => (locale = locale === "en" ? "de" : "en")}>{m.app_switch()}</button>
