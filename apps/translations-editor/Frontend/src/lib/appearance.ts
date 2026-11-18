export const themeModes = ["system", "light", "dark"] as const;
/** How much room the interface takes: spacing, the message tree's rows and the interface text. */
export const densities = ["comfortable", "compact"] as const;

export type ThemeMode = typeof themeModes[number];
export type Density = typeof densities[number];

export interface DesktopAppearance {
  colorScheme: 0 | 1 | 2;
  accentColor: { red: number; green: number; blue: number } | null;
  highContrast: boolean | null;
  reducedMotion: boolean | null;
}
let nativeAppearance: DesktopAppearance | null = null;
let applied: ThemeMode | null = null;

export function setDesktopAppearance(value: DesktopAppearance | null): void {
  nativeAppearance = value;
  if (applied) applyAppearance(applied);
}

// One request at a time. Remount/reload obtains a fresh value; failures restore browser preferences.
export function observeDesktopAppearance(): () => void {
  if (!("runicDesktop" in globalThis)) return () => undefined;
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
export const densityKey = "runic-translations.density";

/**
 * The stored appearance. Earlier versions also stored a colour palette; the palettes are gone (D074), so that value is
 * not read, and the native store drops it when it loads.
 */
export function readAppearance(read: (key: string) => string | null = () => null): { mode: ThemeMode; density: Density } {
  const storedMode = read(modeKey);
  const storedDensity = read(densityKey);
  return {
    mode: isThemeMode(storedMode) ? storedMode : "dark",
    density: isDensity(storedDensity) ? storedDensity : "comfortable",
  };
}

/**
 * Applies a theme mode: the `dark` class for the components' dark variants, and `data-scheme`, which makes the brand
 * tokens follow the editor's choice instead of the system's preference.
 */
export function applyAppearance(mode: ThemeMode): void {
  if (typeof document === "undefined") return;
  applied = mode;
  const systemDark = nativeAppearance?.colorScheme === 2 || (nativeAppearance?.colorScheme !== 1 && matchMedia("(prefers-color-scheme: dark)").matches);
  const dark = mode === "dark" || (mode === "system" && systemDark);
  document.documentElement.dataset.highContrast = String(nativeAppearance?.highContrast ?? matchMedia("(prefers-contrast: more)").matches);
  document.documentElement.dataset.reducedMotion = String(nativeAppearance?.reducedMotion ?? matchMedia("(prefers-reduced-motion: reduce)").matches);
  // The brand's colours stay; the desktop's accent colour is exposed separately for native-aware controls.
  const accent = nativeAppearance?.accentColor;
  if (accent) document.documentElement.style.setProperty("--desktop-accent", `rgb(${[accent.red, accent.green, accent.blue].map(c => Math.round(c * 255)).join(" ")})`);
  else document.documentElement.style.removeProperty("--desktop-accent");
  document.documentElement.classList.toggle("dark", dark);
  document.documentElement.dataset.scheme = dark ? "dark" : "light";
  document.documentElement.style.colorScheme = dark ? "dark" : "light";
}

export function saveAppearance(mode: ThemeMode, write: (key: string, value: string) => void = () => undefined): void {
  write(modeKey, mode);
  applyAppearance(mode);
}

/** Applies a density as `data-density` on the root; the stylesheet derives spacing, row height and text size from it. */
export function applyDensity(density: Density): void {
  if (typeof document === "undefined") return;
  document.documentElement.dataset.density = density;
}

/** The height of one row of the message tree in pixels (`--tree-row` in the stylesheet), for the virtualized tree. */
export function messageRowHeight(density: Density): number {
  return density === "compact" ? 28 : 32;
}

function isThemeMode(value: string | null): value is ThemeMode {
  return themeModes.some((mode) => mode === value);
}

function isDensity(value: string | null): value is Density {
  return densities.some((density) => density === value);
}
