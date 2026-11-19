import { applyAppearance, applyDensity, densityKey, readAppearance, saveAppearance, type Density, type ThemeMode } from "../appearance";
import { readWorkspaceLayout, workspaceLayoutKey, type WorkspaceLayout } from "../list-view";
import { isPseudoLocale, readUiSimulation, saveUiSimulation, type UiDirection } from "../simulation";
import { matchUiLocale, uiLocaleStateKey } from "../ui-locale";
import type { PreviewController } from "./preview-controller.svelte";

export interface PreferencesControllerOptions {
  /** The interface locale shown until the native state loads: the OS preference negotiated against the catalog. */
  initialLocale: string;
  /** The editor catalog's interface locales. */
  locales: readonly string[];
  storage: { get(key: string): string | null; set(key: string, value: string): void };
  /** Makes a locale the window's locale, so native surfaces and the window's other pages follow. */
  windowLocale: { request(locale: string): void };
  /** The preview, whose pseudo-locale the sidebar chooses (created after the preferences). */
  preview: () => Pick<PreviewController, "pseudoLocale">;
  /** Previews the selected message again, after the pseudo-locale simulation changed. */
  refreshPreview: () => void;
  applyAppearance?: (mode: ThemeMode) => void;
  applyDensity?: (density: Density) => void;
}

/**
 * The user's preferences of the editor itself, kept in the native editor state: the interface locale, the theme, the
 * density, the view (workbench or list), and the simulations of pseudo-localization and a right-to-left interface.
 */
export class PreferencesController {
  uiLocale = $state("");
  themeMode = $state<ThemeMode>("dark");
  density = $state<Density>("comfortable");
  /** The workbench or the side-by-side list (D074.1). */
  layout = $state<WorkspaceLayout>("workbench");
  pseudoLocalization = $state(false);
  uiDirection = $state<UiDirection>("ltr");

  readonly #options: PreferencesControllerOptions;

  constructor(options: PreferencesControllerOptions) {
    this.#options = options;
    this.uiLocale = options.initialLocale;
  }

  /** The editor catalog's interface locales. */
  get locales(): readonly string[] {
    return this.#options.locales;
  }

  #apply(): void {
    (this.#options.applyAppearance ?? applyAppearance)(this.themeMode);
  }

  #applyDensity(): void {
    (this.#options.applyDensity ?? applyDensity)(this.density);
  }

  /** Reads the stored appearance (theme and density) and simulation, and applies the appearance. */
  restore(): void {
    const appearance = readAppearance((key) => this.#options.storage.get(key));
    this.themeMode = appearance.mode;
    this.density = appearance.density;
    this.#apply();
    this.#applyDensity();
    this.layout = readWorkspaceLayout(this.#options.storage.get(workspaceLayoutKey));
    const simulation = readUiSimulation((key) => this.#options.storage.get(key));
    this.pseudoLocalization = simulation.pseudoLocalization;
    this.uiDirection = simulation.direction;
  }

  /** The system's color scheme changed: a theme that follows it is applied again. */
  systemThemeChanged(): void {
    if (this.themeMode === "system") this.#apply();
  }

  /**
   * Shows `locale` and makes it the window's locale, so native surfaces and the window's other pages follow. While the
   * host is unreachable the page keeps showing it and sends it again when the connection returns.
   */
  applyUiLocale(locale: string): void {
    this.uiLocale = locale;
    this.#options.windowLocale.request(locale);
  }

  /** The user chose an interface locale: it is stored and becomes the window's. */
  changeUiLocale(locale: string): void {
    const match = matchUiLocale(locale, this.#options.locales);
    if (match === undefined) return;
    this.#options.storage.set(uiLocaleStateKey, match);
    this.applyUiLocale(match);
  }

  changeThemeMode(mode: ThemeMode): void {
    this.themeMode = mode;
    saveAppearance(this.themeMode, (key, value) => this.#options.storage.set(key, value));
  }

  /** The user chose a density: it is stored and set on the root. */
  changeDensity(density: Density): void {
    this.density = density;
    this.#options.storage.set(densityKey, density);
    this.#applyDensity();
  }

  /** The user chose the workbench or the list: it is stored. The selection is the same in both. */
  changeLayout(layout: WorkspaceLayout): void {
    if (this.layout === layout) return;
    this.layout = layout;
    try {
      this.#options.storage.set(workspaceLayoutKey, layout);
    } catch {
      // Without the native state (a test page), the view lasts for the session only.
    }
  }

  togglePseudoLocalization(): void {
    this.pseudoLocalization = !this.pseudoLocalization;
    this.#saveSimulation();
    this.#options.refreshPreview();
  }

  /** A configured pseudo-locale in the sidebar previews in it; choosing it again turns the simulation off. */
  selectPseudoLocale(locale: string): void {
    if (!isPseudoLocale(locale)) return;
    const preview = this.#options.preview();
    if (this.pseudoLocalization && preview.pseudoLocale === locale) this.pseudoLocalization = false;
    else { preview.pseudoLocale = locale; this.pseudoLocalization = true; }
    this.#saveSimulation();
    this.#options.refreshPreview();
  }

  toggleUiDirection(): void {
    this.uiDirection = this.uiDirection === "rtl" ? "ltr" : "rtl";
    this.#saveSimulation();
  }

  #saveSimulation(): void {
    saveUiSimulation(this.pseudoLocalization, this.uiDirection, (key, value) => this.#options.storage.set(key, value));
  }
}
