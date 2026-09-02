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
  import type { UiDirection } from "$lib/simulation";
  import { getUiText } from "$lib/ui-text";

  let {
    locale,
    themeMode,
    themePalette,
    pseudoLocalization,
    uiDirection,
    onlocalechange,
    onthememodechange,
    onthemepalettechange,
    ontogglepseudo,
    ontoggledirection,
    onabout,
  }: {
    locale: string;
    themeMode: ThemeMode;
    themePalette: ThemePalette;
    pseudoLocalization: boolean;
    uiDirection: UiDirection;
    onlocalechange: (locale: string) => void;
    onthememodechange: (mode: ThemeMode) => void;
    onthemepalettechange: (palette: ThemePalette) => void;
    ontogglepseudo: () => void;
    ontoggledirection: () => void;
    onabout: () => void;
  } = $props();

  const ui = getUiText();

  const modeNames: Record<ThemeMode, string> = { system: ui.text("ui_settings_system"), light: ui.text("ui_settings_light"), dark: ui.text("ui_settings_dark") };
  const paletteNames: Record<ThemePalette, string> = { runic: ui.text("ui_settings_runic_gold"), moss: ui.text("ui_settings_moss"), fjord: ui.text("ui_settings_fjord"), ember: ui.text("ui_settings_ember") };
  let localeName = $derived(locale === "de" ? ui.text("ui_settings_german") : ui.text("ui_settings_english"));
  let appearanceName = $derived(`${paletteNames[themePalette]} · ${modeNames[themeMode]}`);
</script>

<Sidebar.Footer class="border-t border-sidebar-border p-2">
  <Sidebar.Menu>
    <Sidebar.MenuItem>
      <DropdownMenu.Root>
        <DropdownMenu.Trigger>
          {#snippet child({ props })}
            <Sidebar.MenuButton {...props} size="lg" aria-label={`${ui.text("ui_settings_editor_settings")}, ${appearanceName}, ${ui.text("ui_settings_interface_language")} ${localeName}`} tooltipContent={ui.text("ui_settings_editor_settings")}>
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
          <DropdownMenu.Label>{ui.text("ui_settings_color_theme")}</DropdownMenu.Label>
          <DropdownMenu.RadioGroup value={themePalette} onValueChange={(value) => onthemepalettechange(value as ThemePalette)}>
            <DropdownMenu.RadioItem value="runic"><PaletteIcon />{ui.text("ui_settings_runic_gold")}</DropdownMenu.RadioItem>
            <DropdownMenu.RadioItem value="moss"><PaletteIcon />{ui.text("ui_settings_moss")}</DropdownMenu.RadioItem>
            <DropdownMenu.RadioItem value="fjord"><PaletteIcon />{ui.text("ui_settings_fjord")}</DropdownMenu.RadioItem>
            <DropdownMenu.RadioItem value="ember"><PaletteIcon />{ui.text("ui_settings_ember")}</DropdownMenu.RadioItem>
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
            <DropdownMenu.RadioItem value="en">
              <LanguagesIcon />
              {ui.text("ui_settings_english")}
            </DropdownMenu.RadioItem>
            <DropdownMenu.RadioItem value="de">
              <LanguagesIcon />
              {ui.text("ui_settings_german")}
            </DropdownMenu.RadioItem>
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
