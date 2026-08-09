<script lang="ts">
  import InfoIcon from "@lucide/svelte/icons/info";
  import LanguagesIcon from "@lucide/svelte/icons/languages";
  import Settings2Icon from "@lucide/svelte/icons/settings-2";
  import ChevronsUpDownIcon from "@lucide/svelte/icons/chevrons-up-down";
  import { Badge } from "$lib/components/ui/badge/index.js";
  import * as DropdownMenu from "$lib/components/ui/dropdown-menu/index.js";
  import * as Sidebar from "$lib/components/ui/sidebar/index.js";

  let {
    locale,
    onlocalechange,
    onabout,
  }: {
    locale: string;
    onlocalechange: (locale: string) => void;
    onabout: () => void;
  } = $props();

  let localeName = $derived(locale === "de" ? "Deutsch" : "English");
</script>

<Sidebar.Footer class="border-t border-sidebar-border p-2">
  <Sidebar.Menu>
    <Sidebar.MenuItem>
      <DropdownMenu.Root>
        <DropdownMenu.Trigger>
          {#snippet child({ props })}
            <Sidebar.MenuButton {...props} size="lg" aria-label={`Editor settings, interface language ${localeName}`} tooltipContent="Editor settings">
              <Badge variant="outline" class="size-8 shrink-0 justify-center p-0">
                <Settings2Icon aria-hidden="true" />
              </Badge>
              <span class="grid min-w-0 flex-1 text-left text-sm leading-tight">
                <span class="truncate font-medium">Editor settings</span>
                <span class="truncate text-xs text-muted-foreground">Interface language · {localeName}</span>
              </span>
              <ChevronsUpDownIcon class="ml-auto" aria-hidden="true" />
            </Sidebar.MenuButton>
          {/snippet}
        </DropdownMenu.Trigger>
        <DropdownMenu.Content class="w-(--bits-dropdown-menu-anchor-width) min-w-64" align="start" side="top">
          <DropdownMenu.Label>Interface language</DropdownMenu.Label>
          <DropdownMenu.RadioGroup value={locale} onValueChange={onlocalechange}>
            <DropdownMenu.RadioItem value="en">
              <LanguagesIcon />
              English
            </DropdownMenu.RadioItem>
            <DropdownMenu.RadioItem value="de">
              <LanguagesIcon />
              Deutsch
            </DropdownMenu.RadioItem>
          </DropdownMenu.RadioGroup>
          <DropdownMenu.Separator />
          <DropdownMenu.Item onclick={onabout}>
            <InfoIcon />
            About &amp; diagnostics
          </DropdownMenu.Item>
        </DropdownMenu.Content>
      </DropdownMenu.Root>
    </Sidebar.MenuItem>
  </Sidebar.Menu>
</Sidebar.Footer>
