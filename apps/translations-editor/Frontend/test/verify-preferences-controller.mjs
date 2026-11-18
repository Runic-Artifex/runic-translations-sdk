// The preferences controller: the appearance, locale and simulation kept in the native state, under their storage keys.
import assert from "node:assert/strict";
import "./svelte-modules.mjs";

const { PreferencesController } = await import("../src/lib/state/preferences-controller.svelte.ts");

const keys = {
  mode: "runic-translations.theme-mode",
  density: "runic-translations.density",
  retiredPalette: "runic-translations.theme-palette",
  locale: "runic-translations.ui-locale",
  pseudo: "runic-translations.pseudo-localization",
  direction: "runic-translations.ui-direction",
};

// `storage` is a plain map; `windowRequests` records the locales sent to the window; `applied` records theme applies,
// `densities` density applies.
function createController(options = {}) {
  const values = new Map(Object.entries(options.stored ?? {}));
  const log = [];
  const windowRequests = [];
  const applied = [];
  const densities = [];
  const preview = { pseudoLocale: options.pseudoLocale ?? "en-XA" };
  const controller = new PreferencesController({
    initialLocale: options.initialLocale ?? "en",
    locales: ["en", "de-CH"],
    storage: {
      get: (key) => values.get(key) ?? null,
      set: (key, value) => { values.set(key, value); log.push(`set ${key}`); },
    },
    windowLocale: { request: (locale) => windowRequests.push(locale) },
    preview: () => preview,
    refreshPreview: () => log.push("refresh"),
    applyAppearance: (mode) => applied.push(mode),
    applyDensity: (density) => densities.push(density),
  });
  return { controller, values, log, windowRequests, preview, applied, densities };
}

// The interface locale starts as the one the host negotiated, and the defaults apply before anything is restored.
{
  const { controller, applied } = createController({ initialLocale: "de-CH" });
  assert.equal(controller.uiLocale, "de-CH");
  assert.equal(controller.themeMode, "dark");
  assert.equal(controller.density, "comfortable");
  assert.equal("themePalette" in controller, false, "The colour palettes are gone.");
  assert.equal(controller.pseudoLocalization, false);
  assert.equal(controller.uiDirection, "ltr");
  assert.deepEqual(applied, [], "Nothing is applied until the preferences are restored.");
}

// restore() reads the stored appearance and simulation, applies the appearance and reads nothing else into storage.
{
  const { controller, applied, densities, values } = createController({
    stored: { [keys.mode]: "light", [keys.density]: "compact", [keys.pseudo]: "true", [keys.direction]: "rtl" },
  });
  controller.restore();
  assert.equal(controller.themeMode, "light");
  assert.equal(controller.density, "compact");
  assert.deepEqual(applied, ["light"]);
  assert.deepEqual(densities, ["compact"]);
  assert.equal(controller.pseudoLocalization, true);
  assert.equal(controller.uiDirection, "rtl");
  assert.equal(values.size, 4, "Restoring does not write the preferences back.");
}

// restore() falls back to the defaults for values that are not recognised.
{
  const { controller, applied, densities } = createController({
    stored: { [keys.mode]: "neon", [keys.density]: "cosy", [keys.pseudo]: "yes", [keys.direction]: "up" },
  });
  controller.restore();
  assert.equal(controller.themeMode, "dark");
  assert.equal(controller.density, "comfortable");
  assert.deepEqual(applied, ["dark"]);
  assert.deepEqual(densities, ["comfortable"]);
  assert.equal(controller.pseudoLocalization, false);
  assert.equal(controller.uiDirection, "ltr");
}

// changeUiLocale(): a matching locale is stored under its key, shown, and requested from the window.
{
  const { controller, values, windowRequests } = createController();
  controller.changeUiLocale("DE-ch");
  assert.equal(values.get(keys.locale), "de-CH", "The catalog's own spelling is stored.");
  assert.equal(controller.uiLocale, "de-CH");
  assert.deepEqual(windowRequests, ["de-CH"]);
}

// changeUiLocale(): an unknown locale is ignored.
{
  const { controller, values, windowRequests, log } = createController();
  controller.changeUiLocale("fr");
  assert.equal(values.has(keys.locale), false);
  assert.equal(controller.uiLocale, "en");
  assert.deepEqual(windowRequests, []);
  assert.deepEqual(log, []);
}

// applyUiLocale() shows the locale and requests it from the window without storing it.
{
  const { controller, values, windowRequests } = createController();
  controller.applyUiLocale("de-CH");
  assert.equal(controller.uiLocale, "de-CH");
  assert.equal(values.has(keys.locale), false);
  assert.deepEqual(windowRequests, ["de-CH"]);
}

// A palette stored by an earlier version is ignored: the theme and density restore as without it.
{
  const { controller, applied, densities } = createController({ stored: { [keys.mode]: "light", [keys.retiredPalette]: "moss" } });
  controller.restore();
  assert.equal(controller.themeMode, "light");
  assert.deepEqual(applied, ["light"]);
  assert.deepEqual(densities, ["comfortable"]);
}

// changeThemeMode() writes the theme mode only; no palette is written any more.
{
  const { controller, values } = createController();
  controller.changeThemeMode("light");
  assert.equal(controller.themeMode, "light");
  assert.equal(values.get(keys.mode), "light");
  assert.equal(values.has(keys.retiredPalette), false);
}

// changeDensity() stores the density under its key and applies it.
{
  const { controller, values, densities } = createController();
  controller.changeDensity("compact");
  assert.equal(controller.density, "compact");
  assert.equal(values.get(keys.density), "compact");
  assert.deepEqual(densities, ["compact"]);
  controller.changeDensity("comfortable");
  assert.equal(values.get(keys.density), "comfortable");
  assert.deepEqual(densities, ["compact", "comfortable"]);
}

// togglePseudoLocalization() flips the simulation, saves it and previews the message again.
{
  const { controller, values, log } = createController();
  controller.togglePseudoLocalization();
  assert.equal(controller.pseudoLocalization, true);
  assert.equal(values.get(keys.pseudo), "true");
  assert.deepEqual(log, [`set ${keys.pseudo}`, `set ${keys.direction}`, "refresh"]);

  controller.togglePseudoLocalization();
  assert.equal(controller.pseudoLocalization, false);
  assert.equal(values.get(keys.pseudo), "false");
}

// selectPseudoLocale(): choosing a pseudo-locale previews in it and turns the simulation on.
{
  const { controller, values, preview } = createController({ pseudoLocale: "ar-XB" });
  controller.selectPseudoLocale("en-XA");
  assert.equal(preview.pseudoLocale, "en-XA");
  assert.equal(controller.pseudoLocalization, true);
  assert.equal(values.get(keys.pseudo), "true");

  // Choosing a different pseudo-locale while it is on switches to it and keeps the simulation on.
  controller.selectPseudoLocale("ar-XB");
  assert.equal(preview.pseudoLocale, "ar-XB");
  assert.equal(controller.pseudoLocalization, true);
}

// selectPseudoLocale(): choosing the active pseudo-locale again turns the simulation off.
{
  const { controller, values, preview, log } = createController();
  controller.selectPseudoLocale("en-XA");
  log.length = 0;
  controller.selectPseudoLocale("en-XA");
  assert.equal(controller.pseudoLocalization, false);
  assert.equal(preview.pseudoLocale, "en-XA");
  assert.equal(values.get(keys.pseudo), "false");
  assert.deepEqual(log, [`set ${keys.pseudo}`, `set ${keys.direction}`, "refresh"]);
}

// selectPseudoLocale(): a locale that is not a pseudo-locale changes nothing.
{
  const { controller, values, preview, log } = createController();
  controller.selectPseudoLocale("de-CH");
  assert.equal(controller.pseudoLocalization, false);
  assert.equal(preview.pseudoLocale, "en-XA");
  assert.equal(values.has(keys.pseudo), false);
  assert.deepEqual(log, []);
}

// toggleUiDirection() flips between left-to-right and right-to-left, and saves the simulation.
{
  const { controller, values } = createController();
  controller.toggleUiDirection();
  assert.equal(controller.uiDirection, "rtl");
  assert.equal(values.get(keys.direction), "rtl");
  assert.equal(values.get(keys.pseudo), "false", "The pseudo-localization state is saved alongside.");

  controller.toggleUiDirection();
  assert.equal(controller.uiDirection, "ltr");
  assert.equal(values.get(keys.direction), "ltr");
}

// systemThemeChanged(): the appearance is applied again only when the theme follows the system.
{
  const { controller, applied } = createController({ stored: { [keys.mode]: "dark" } });
  controller.restore();
  controller.systemThemeChanged();
  assert.deepEqual(applied, ["dark"], "A fixed theme ignores a system change.");
}
{
  const { controller, applied } = createController({ stored: { [keys.mode]: "system" } });
  controller.restore();
  controller.systemThemeChanged();
  assert.deepEqual(applied, ["system", "system"]);
}

console.log("PASS: preferences restore and save the theme, density, locale and simulation under their storage keys, ignoring a stored palette");
