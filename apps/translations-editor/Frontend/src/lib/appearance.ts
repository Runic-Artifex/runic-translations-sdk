export const themeModes = ["system", "light", "dark"] as const;
export const themePalettes = ["runic", "moss", "fjord", "ember"] as const;

export type ThemeMode = typeof themeModes[number];
export type ThemePalette = typeof themePalettes[number];

const modeKey = "runic-text-resources.theme-mode";
const paletteKey = "runic-text-resources.theme-palette";

export function readAppearance(): { mode: ThemeMode; palette: ThemePalette } {
  if (typeof localStorage === "undefined") return { mode: "dark", palette: "runic" };
  const storedMode = localStorage.getItem(modeKey);
  const storedPalette = localStorage.getItem(paletteKey);
  return {
    mode: isThemeMode(storedMode) ? storedMode : "dark",
    palette: isThemePalette(storedPalette) ? storedPalette : "runic",
  };
}

export function applyAppearance(mode: ThemeMode, palette: ThemePalette): void {
  if (typeof document === "undefined") return;
  const dark = mode === "dark" || (mode === "system" && matchMedia("(prefers-color-scheme: dark)").matches);
  document.documentElement.classList.toggle("dark", dark);
  document.documentElement.dataset.theme = palette;
  document.documentElement.style.colorScheme = dark ? "dark" : "light";
}

export function saveAppearance(mode: ThemeMode, palette: ThemePalette): void {
  if (typeof localStorage !== "undefined") {
    localStorage.setItem(modeKey, mode);
    localStorage.setItem(paletteKey, palette);
  }
  applyAppearance(mode, palette);
}

function isThemeMode(value: string | null): value is ThemeMode {
  return themeModes.some((mode) => mode === value);
}

function isThemePalette(value: string | null): value is ThemePalette {
  return themePalettes.some((palette) => palette === value);
}
