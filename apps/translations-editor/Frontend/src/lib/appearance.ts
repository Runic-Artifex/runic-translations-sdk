export const themeModes = ["system", "light", "dark"] as const;
export const themePalettes = ["runic", "moss", "fjord", "ember"] as const;

export type ThemeMode = typeof themeModes[number];
export type ThemePalette = typeof themePalettes[number];

export interface DesktopAppearance {
  colorScheme: 0 | 1 | 2;
  accentColor: { red: number; green: number; blue: number } | null;
  highContrast: boolean | null;
  reducedMotion: boolean | null;
}
let nativeAppearance: DesktopAppearance | null = null;
let applied: { mode: ThemeMode; palette: ThemePalette } | null = null;

export function setDesktopAppearance(value: DesktopAppearance | null): void {
  nativeAppearance = value;
  if (applied) applyAppearance(applied.mode, applied.palette);
}

// One request at a time. Remount/reload obtains a fresh value; failures restore browser preferences.
export function observeDesktopAppearance(): () => void {
  const stop = new AbortController();
  let timer: ReturnType<typeof setTimeout> | undefined;
  async function read(): Promise<void> {
    try {
      const response = await fetch("/runic-desktop-appearance.json", { signal: stop.signal, cache: "no-store" });
      const value: unknown = response.ok ? await response.json() : null;
      if (!stop.signal.aborted) setDesktopAppearance(isDesktopAppearance(value) ? value : null);
    } catch { if (!stop.signal.aborted) setDesktopAppearance(null); }
    finally { if (!stop.signal.aborted) timer = setTimeout(() => void read(), 1000); }
  }
  void read();
  return () => { stop.abort(); clearTimeout(timer); };
}
function isDesktopAppearance(value: unknown): value is DesktopAppearance {
  if (!value || typeof value !== "object") return false;
  const v = value as DesktopAppearance;
  return [0, 1, 2].includes(v.colorScheme)
    && (v.highContrast === null || typeof v.highContrast === "boolean")
    && (v.reducedMotion === null || typeof v.reducedMotion === "boolean")
    && (v.accentColor === null || !!v.accentColor && [v.accentColor.red, v.accentColor.green, v.accentColor.blue].every(c => typeof c === "number" && c >= 0 && c <= 1));
}

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
  applied = { mode, palette };
  const systemDark = nativeAppearance?.colorScheme === 2 || (nativeAppearance?.colorScheme !== 1 && matchMedia("(prefers-color-scheme: dark)").matches);
  const dark = mode === "dark" || (mode === "system" && systemDark);
  document.documentElement.dataset.highContrast = String(nativeAppearance?.highContrast ?? matchMedia("(prefers-contrast: more)").matches);
  document.documentElement.dataset.reducedMotion = String(nativeAppearance?.reducedMotion ?? matchMedia("(prefers-reduced-motion: reduce)").matches);
  // Palette selection remains explicit. Accent is exposed separately for native-aware controls.
  const accent = nativeAppearance?.accentColor;
  if (accent) document.documentElement.style.setProperty("--desktop-accent", `rgb(${[accent.red, accent.green, accent.blue].map(c => Math.round(c * 255)).join(" ")})`);
  else document.documentElement.style.removeProperty("--desktop-accent");
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
