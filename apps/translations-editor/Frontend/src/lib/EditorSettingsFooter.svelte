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

  const modeNames: Record<ThemeMode, string> = { system: ui.text("Ui.Settings.System"), light: ui.text("Ui.Settings.Light"), dark: ui.text("Ui.Settings.Dark") };
  const paletteNames: Record<ThemePalette, string> = { runic: ui.text("Ui.Settings.RunicGold"), moss: ui.text("Ui.Settings.Moss"), fjord: ui.text("Ui.Settings.Fjord"), ember: ui.text("Ui.Settings.Ember") };
  let localeName = $derived(locale === "de" ? ui.text("Ui.Settings.German") : ui.text("Ui.Settings.English"));
  let appearanceName = $derived(`${paletteNames[themePalette]} · ${modeNames[themeMode]}`);
</script>

<Sidebar.Footer class="border-t border-sidebar-border p-2">
  <Sidebar.Menu>
    <Sidebar.MenuItem>
      <DropdownMenu.Root>
        <DropdownMenu.Trigger>
          {#snippet child({ props })}
            <Sidebar.MenuButton {...props} size="lg" aria-label={`${ui.text("Ui.Settings.EditorSettings")}, ${appearanceName}, ${ui.text("Ui.Settings.InterfaceLanguage")} ${localeName}`} tooltipContent={ui.text("Ui.Settings.EditorSettings")}>
              <Badge variant="outline" class="size-8 shrink-0 justify-center p-0">
                <Settings2Icon aria-hidden="true" />
              </Badge>
              <span class="grid min-w-0 flex-1 text-left text-sm leading-tight">
                <span class="truncate font-medium">{ui.text("Ui.Settings.EditorSettings")}</span>
                <span class="truncate text-xs text-muted-foreground">{appearanceName} · {localeName}</span>
              </span>
              <ChevronsUpDownIcon class="ml-auto" aria-hidden="true" />
            </Sidebar.MenuButton>
          {/snippet}
        </DropdownMenu.Trigger>
        <DropdownMenu.Content class="w-(--bits-dropdown-menu-anchor-width) min-w-64" align="start" side="top">
          <DropdownMenu.Label>{ui.text("Ui.Settings.Appearance")}</DropdownMenu.Label>
          <DropdownMenu.RadioGroup value={themeMode} onValueChange={(value) => onthememodechange(value as ThemeMode)}>
            <DropdownMenu.RadioItem value="system"><MonitorIcon />{ui.text("Ui.Settings.System")}</DropdownMenu.RadioItem>
            <DropdownMenu.RadioItem value="light"><SunIcon />{ui.text("Ui.Settings.Light")}</DropdownMenu.RadioItem>
            <DropdownMenu.RadioItem value="dark"><MoonIcon />{ui.text("Ui.Settings.Dark")}</DropdownMenu.RadioItem>
          </DropdownMenu.RadioGroup>
          <DropdownMenu.Separator />
          <DropdownMenu.Label>{ui.text("Ui.Settings.ColorTheme")}</DropdownMenu.Label>
          <DropdownMenu.RadioGroup value={themePalette} onValueChange={(value) => onthemepalettechange(value as ThemePalette)}>
            <DropdownMenu.RadioItem value="runic"><PaletteIcon />{ui.text("Ui.Settings.RunicGold")}</DropdownMenu.RadioItem>
            <DropdownMenu.RadioItem value="moss"><PaletteIcon />{ui.text("Ui.Settings.Moss")}</DropdownMenu.RadioItem>
            <DropdownMenu.RadioItem value="fjord"><PaletteIcon />{ui.text("Ui.Settings.Fjord")}</DropdownMenu.RadioItem>
            <DropdownMenu.RadioItem value="ember"><PaletteIcon />{ui.text("Ui.Settings.Ember")}</DropdownMenu.RadioItem>
          </DropdownMenu.RadioGroup>
          <DropdownMenu.Separator />
          <DropdownMenu.Label>{ui.text("Ui.Settings.LocalizationSimulation")}</DropdownMenu.Label>
          <DropdownMenu.CheckboxItem checked={pseudoLocalization} onCheckedChange={() => ontogglepseudo()}>
            <LanguagesIcon />
            {ui.text("Ui.Settings.PseudoLocalization")}
          </DropdownMenu.CheckboxItem>
          <DropdownMenu.CheckboxItem
            checked={uiDirection === "rtl"}
            onCheckedChange={() => ontoggledirection()}
          >
            <LanguagesIcon />
            {ui.text("Ui.Settings.RightToLeft")}
          </DropdownMenu.CheckboxItem>
          <DropdownMenu.Separator />
          <DropdownMenu.Label>{ui.text("Ui.Settings.InterfaceLanguage")}</DropdownMenu.Label>
          <DropdownMenu.RadioGroup value={locale} onValueChange={onlocalechange}>
            <DropdownMenu.RadioItem value="en">
              <LanguagesIcon />
              {ui.text("Ui.Settings.English")}
            </DropdownMenu.RadioItem>
            <DropdownMenu.RadioItem value="de">
              <LanguagesIcon />
              {ui.text("Ui.Settings.German")}
            </DropdownMenu.RadioItem>
          </DropdownMenu.RadioGroup>
          <DropdownMenu.Separator />
          <DropdownMenu.Item onclick={onabout}>
            <InfoIcon />
            {ui.text("Ui.Settings.AboutDiagnostics")}
          </DropdownMenu.Item>
        </DropdownMenu.Content>
      </DropdownMenu.Root>
    </Sidebar.MenuItem>
  </Sidebar.Menu>
</Sidebar.Footer>
