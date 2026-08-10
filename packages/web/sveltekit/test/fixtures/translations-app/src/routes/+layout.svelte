<script lang="ts">
  import {
    createLocaleNavigation,
    synchronizeLocaleWithNavigation,
  } from "@runic-artifex/sveltekit/translations/navigation";
  import { routing } from "$lib/i18n.js";
  import { localeContext } from "$lib/locale-context.js";
  import { createFixtureLocaleSource } from "$lib/locale-source.js";
  import type { LayoutProps } from "./$types.js";

  let { data, children }: LayoutProps = $props();
  // A root provider intentionally owns its hydration-time source.
  // svelte-ignore state_referenced_locally
  const source = createFixtureLocaleSource(data.locale);
  synchronizeLocaleWithNavigation(source, routing);
  localeContext.provide(source, { requestLocale: createLocaleNavigation(routing) });
</script>

{@render children()}
