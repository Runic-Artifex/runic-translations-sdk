<script lang="ts">
  import { resolve } from "$app/paths";
  import { routing } from "$lib/i18n.js";
  import { localeContext } from "$lib/locale-context.js";

  const locale = localeContext.use();
  const message = (options: Readonly<{ locale: "en" | "de" }>) =>
    options.locale === "de" ? "Einrichtung" : "Setup";
</script>

<svelte:head>
  <link rel="canonical" href={routing.canonicalUrl("/setup", locale.locale)} />
</svelte:head>

<h1>{message(locale.messageOptions)}</h1>
<nav>
  <a href={resolve(routing.localizeUrl("/setup", "en"))}>English</a>
  <a href={resolve(routing.localizeUrl("/setup", "de"))}>Deutsch</a>
</nav>
