<script lang="ts">
  import { LocalizedInline, inlineFactory, type InlineElement } from "@runic-artifex/translations-svelte/inline";
  import { useMessageLocale } from "@runic-artifex/translations-svelte";
  import { m } from "virtual:runic-translations/app";
  import { configureLocaleResolver, createInlineRenderer, linkBinding } from "virtual:runic-translations/app/runtime";

  // The application owns the locale, as a ViewModel's language setting would.
  let locale = $state(new URLSearchParams(location.search).get("lang") ?? "en");
  // m.*() follows it: the page updates in place when it changes.
  useMessageLocale(configureLocaleResolver, () => locale);
  $effect(() => { document.documentElement.lang = locale; });

  const inline = createInlineRenderer(inlineFactory);
  const terms = $derived(inline.render(m.app_terms(), { slots: { terms: linkBinding({ href: "/terms" }) } }));
  // app:highlight is declared in translations/runic.json; LocalizedInline renders it with the snippet of that name.
  const pending = $derived(inline.render(m.app_pending({ count: 1234 }), { slots: {} }));
</script>

{#snippet highlight(node: InlineElement)}<mark><LocalizedInline nodes={node.children} /></mark>{/snippet}

<h1>{m.app_title()}</h1>
<p id="hours">{m.app_hours({ count: 1.5 })}</p>
<p id="terms"><LocalizedInline nodes={terms} /></p>
<p id="pending"><LocalizedInline nodes={pending} custom={{ "app:highlight": highlight }} /></p>
<button onclick={() => (locale = locale === "en" ? "de" : "en")}>{m.app_switch()}</button>
