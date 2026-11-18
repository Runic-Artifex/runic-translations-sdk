<script lang="ts">
  import InfoIcon from "@lucide/svelte/icons/info";
  import LanguagesIcon from "@lucide/svelte/icons/languages";
  import MonitorIcon from "@lucide/svelte/icons/monitor";
  import MoonIcon from "@lucide/svelte/icons/moon";
  import Rows3Icon from "@lucide/svelte/icons/rows-3";
  import Rows4Icon from "@lucide/svelte/icons/rows-4";
  import Settings2Icon from "@lucide/svelte/icons/settings-2";
  import SunIcon from "@lucide/svelte/icons/sun";
  import ChevronsUpDownIcon from "@lucide/svelte/icons/chevrons-up-down";
  import { Badge } from "#lib/components/ui/badge/index.js";
  import * as DropdownMenu from "#lib/components/ui/dropdown-menu/index.js";
  import * as Sidebar from "#lib/components/ui/sidebar/index.js";
  import type { Density, ThemeMode } from "#lib/appearance.js";
  import type { UiDirection } from "#lib/simulation.js";
  import { getUiText } from "#lib/ui-text.js";
  import { uiLocaleName } from "#lib/ui-locale.js";

  let {
    locale,
    locales,
    themeMode,
    density,
    pseudoLocalization,
    uiDirection,
    onlocalechange,
    onthememodechange,
    ondensitychange,
    ontogglepseudo,
    ontoggledirection,
    onabout,
  }: {
    locale: string;
    /** The editor catalog's locales, listed by their own names. */
    locales: readonly string[];
    themeMode: ThemeMode;
    density: Density;
    pseudoLocalization: boolean;
    uiDirection: UiDirection;
    onlocalechange: (locale: string) => void;
    onthememodechange: (mode: ThemeMode) => void;
    ondensitychange: (density: Density) => void;
    ontogglepseudo: () => void;
    ontoggledirection: () => void;
    onabout: () => void;
  } = $props();

  const ui = getUiText();

  const modeNames: Record<ThemeMode, string> = $derived({ system: ui.text("ui_settings_system"), light: ui.text("ui_settings_light"), dark: ui.text("ui_settings_dark") });
  const densityNames: Record<Density, string> = $derived({ comfortable: ui.text("ui_settings_comfortable"), compact: ui.text("ui_settings_compact") });
  let localeName = $derived(uiLocaleName(locale));
  let appearanceName = $derived(`${modeNames[themeMode]} · ${densityNames[density]}`);
</script>

<Sidebar.Footer class="border-t border-sidebar-border p-2">
  <Sidebar.Menu>
    <Sidebar.MenuItem>
      <DropdownMenu.Root>
        <DropdownMenu.Trigger>
          {#snippet child({ props })}
            <Sidebar.MenuButton {...props} size="lg" aria-label={ui.text("ui_settings_summary", { appearance: appearanceName, locale: localeName })} tooltipContent={ui.text("ui_settings_editor_settings")}>
              <Badge variant="outline" class="size-8 shrink-0 justify-center p-0">
                <Settings2Icon aria-hidden="true" />
              </Badge>
              <span class="grid min-w-0 flex-1 text-left text-sm leading-tight">
                <span class="truncate font-medium">{ui.text("ui_settings_editor_settings")}</span>
                <span class="truncate text-xs text-muted-foreground">{appearanceName} · {localeName}</span>
              </span>
              <ChevronsUpDownIcon class="ml-auto" aria-hidden="true" />
            </Sidebar.MenuButton>
          {/snippet}
        </DropdownMenu.Trigger>
        <DropdownMenu.Content class="w-(--bits-dropdown-menu-anchor-width) min-w-64" align="start" side="top">
          <DropdownMenu.Label>{ui.text("ui_settings_appearance")}</DropdownMenu.Label>
          <DropdownMenu.RadioGroup value={themeMode} onValueChange={(value) => onthememodechange(value as ThemeMode)}>
            <DropdownMenu.RadioItem value="system"><MonitorIcon />{ui.text("ui_settings_system")}</DropdownMenu.RadioItem>
            <DropdownMenu.RadioItem value="light"><SunIcon />{ui.text("ui_settings_light")}</DropdownMenu.RadioItem>
            <DropdownMenu.RadioItem value="dark"><MoonIcon />{ui.text("ui_settings_dark")}</DropdownMenu.RadioItem>
          </DropdownMenu.RadioGroup>
          <DropdownMenu.Separator />
          <DropdownMenu.Label>{ui.text("ui_settings_density")}</DropdownMenu.Label>
          <DropdownMenu.RadioGroup value={density} onValueChange={(value) => ondensitychange(value as Density)} data-testid="settings-density">
            <DropdownMenu.RadioItem value="comfortable"><Rows3Icon />{ui.text("ui_settings_comfortable")}</DropdownMenu.RadioItem>
            <DropdownMenu.RadioItem value="compact"><Rows4Icon />{ui.text("ui_settings_compact")}</DropdownMenu.RadioItem>
          </DropdownMenu.RadioGroup>
          <DropdownMenu.Separator />
          <DropdownMenu.Label>{ui.text("ui_settings_localization_simulation")}</DropdownMenu.Label>
          <DropdownMenu.CheckboxItem checked={pseudoLocalization} onCheckedChange={() => ontogglepseudo()}>
            <LanguagesIcon />
            {ui.text("ui_settings_pseudo_localization")}
          </DropdownMenu.CheckboxItem>
          <DropdownMenu.CheckboxItem
            checked={uiDirection === "rtl"}
            onCheckedChange={() => ontoggledirection()}
          >
            <LanguagesIcon />
            {ui.text("ui_settings_right_to_left")}
          </DropdownMenu.CheckboxItem>
          <DropdownMenu.Separator />
          <DropdownMenu.Label>{ui.text("ui_settings_interface_language")}</DropdownMenu.Label>
          <DropdownMenu.RadioGroup value={locale} onValueChange={onlocalechange}>
            {#each locales as option (option)}
              <DropdownMenu.RadioItem value={option} lang={option}>
                <LanguagesIcon />
                {uiLocaleName(option)}
              </DropdownMenu.RadioItem>
            {/each}
          </DropdownMenu.RadioGroup>
          <DropdownMenu.Separator />
          <DropdownMenu.Item onclick={onabout}>
            <InfoIcon />
            {ui.text("ui_settings_about_diagnostics")}
          </DropdownMenu.Item>
        </DropdownMenu.Content>
      </DropdownMenu.Root>
    </Sidebar.MenuItem>
  </Sidebar.Menu>
</Sidebar.Footer>
