export const themeModes = ["system", "light", "dark"] as const;
export const themePalettes = ["runic", "moss", "fjord", "ember"] as const;

export type ThemeMode = typeof themeModes[number];
export type ThemePalette = typeof themePalettes[number];

const modeKey = "runic-translations.theme-mode";
const paletteKey = "runic-translations.theme-palette";

export function readAppearance(read: (key: string) => string | null = () => null): { mode: ThemeMode; palette: ThemePalette } {
  const storedMode = read(modeKey);
  const storedPalette = read(paletteKey);
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

export function saveAppearance(
  mode: ThemeMode,
  palette: ThemePalette,
  write: (key: string, value: string) => void = () => undefined,
): void {
  write(modeKey, mode);
  write(paletteKey, palette);
  applyAppearance(mode, palette);
}

function isThemeMode(value: string | null): value is ThemeMode {
  return themeModes.some((mode) => mode === value);
}

function isThemePalette(value: string | null): value is ThemePalette {
  return themePalettes.some((palette) => palette === value);
}
