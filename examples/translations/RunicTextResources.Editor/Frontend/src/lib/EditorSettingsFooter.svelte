<script lang="ts">
  import InfoIcon from "@lucide/svelte/icons/info";
  import LanguagesIcon from "@lucide/svelte/icons/languages";
  import MonitorIcon from "@lucide/svelte/icons/monitor";
  import MoonIcon from "@lucide/svelte/icons/moon";
  import PaletteIcon from "@lucide/svelte/icons/palette";
  import Settings2Icon from "@lucide/svelte/icons/settings-2";
  import SunIcon from "@lucide/svelte/icons/sun";
  import ChevronsUpDownIcon from "@lucide/svelte/icons/chevrons-up-down";
  import { Badge } from "$lib/components/ui/badge/index.js";
  import * as DropdownMenu from "$lib/components/ui/dropdown-menu/index.js";
  import * as Sidebar from "$lib/components/ui/sidebar/index.js";
  import type { ThemeMode, ThemePalette } from "$lib/appearance";

  let {
    locale,
    themeMode,
    themePalette,
    onlocalechange,
    onthememodechange,
    onthemepalettechange,
    onabout,
  }: {
    locale: string;
    themeMode: ThemeMode;
    themePalette: ThemePalette;
    onlocalechange: (locale: string) => void;
    onthememodechange: (mode: ThemeMode) => void;
    onthemepalettechange: (palette: ThemePalette) => void;
    onabout: () => void;
  } = $props();

  const modeNames: Record<ThemeMode, string> = { system: "System", light: "Light", dark: "Dark" };
  const paletteNames: Record<ThemePalette, string> = { runic: "Runic Gold", moss: "Moss", fjord: "Fjord", ember: "Ember" };
  let localeName = $derived(locale === "de" ? "Deutsch" : "English");
  let appearanceName = $derived(`${paletteNames[themePalette]} · ${modeNames[themeMode]}`);
</script>

<Sidebar.Footer class="border-t border-sidebar-border p-2">
  <Sidebar.Menu>
    <Sidebar.MenuItem>
      <DropdownMenu.Root>
        <DropdownMenu.Trigger>
          {#snippet child({ props })}
            <Sidebar.MenuButton {...props} size="lg" aria-label={`Editor settings, ${appearanceName}, interface language ${localeName}`} tooltipContent="Editor settings">
              <Badge variant="outline" class="size-8 shrink-0 justify-center p-0">
                <Settings2Icon aria-hidden="true" />
              </Badge>
              <span class="grid min-w-0 flex-1 text-left text-sm leading-tight">
                <span class="truncate font-medium">Editor settings</span>
                <span class="truncate text-xs text-muted-foreground">{appearanceName} · {localeName}</span>
              </span>
              <ChevronsUpDownIcon class="ml-auto" aria-hidden="true" />
            </Sidebar.MenuButton>
          {/snippet}
        </DropdownMenu.Trigger>
        <DropdownMenu.Content class="w-(--bits-dropdown-menu-anchor-width) min-w-64" align="start" side="top">
          <DropdownMenu.Label>Appearance</DropdownMenu.Label>
          <DropdownMenu.RadioGroup value={themeMode} onValueChange={(value) => onthememodechange(value as ThemeMode)}>
            <DropdownMenu.RadioItem value="system"><MonitorIcon />System</DropdownMenu.RadioItem>
            <DropdownMenu.RadioItem value="light"><SunIcon />Light</DropdownMenu.RadioItem>
            <DropdownMenu.RadioItem value="dark"><MoonIcon />Dark</DropdownMenu.RadioItem>
          </DropdownMenu.RadioGroup>
          <DropdownMenu.Separator />
          <DropdownMenu.Label>Color theme</DropdownMenu.Label>
          <DropdownMenu.RadioGroup value={themePalette} onValueChange={(value) => onthemepalettechange(value as ThemePalette)}>
            <DropdownMenu.RadioItem value="runic"><PaletteIcon />Runic Gold</DropdownMenu.RadioItem>
            <DropdownMenu.RadioItem value="moss"><PaletteIcon />Moss</DropdownMenu.RadioItem>
            <DropdownMenu.RadioItem value="fjord"><PaletteIcon />Fjord</DropdownMenu.RadioItem>
            <DropdownMenu.RadioItem value="ember"><PaletteIcon />Ember</DropdownMenu.RadioItem>
          </DropdownMenu.RadioGroup>
          <DropdownMenu.Separator />
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
