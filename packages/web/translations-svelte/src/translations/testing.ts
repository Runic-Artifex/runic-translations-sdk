/**
 * Stable, framework-neutral presentation cases for exercising a localized
 * Runic application. They intentionally contain no renderer, catalog, or
 * application state: a consumer mounts these values in its own UI tests.
 */
export type LocalizationStressDirection = "ltr" | "rtl";

export interface LocalizationStressCase {
  readonly id: string;
  readonly locale: string;
  readonly direction: LocalizationStressDirection;
  readonly text: string;
  readonly checks: readonly string[];
}

export interface VisualAccessibilityStressScenario {
  readonly id: string;
  readonly mediaQuery: string;
  readonly checks: readonly string[];
}

export const pluralStressCounts = [0, 1, 2, 5, 11, 21, 101, 1000] as const;

const pseudoCharacters: Readonly<Record<string, string>> = {
  A: "ÅÅ", B: "ß", C: "Ç", D: "Ð", E: "ÉÉ", F: "Ƒ", G: "Ğ", H: "Ħ", I: "ÏÏ", J: "Ĵ", K: "Ķ", L: "Ŀ", M: "Ḿ", N: "Ń", O: "ØØ", P: "Þ", Q: "Ǫ", R: "Ŕ", S: "Š", T: "Ŧ", U: "ÜÜ", V: "Ṽ", W: "Ŵ", X: "Ẋ", Y: "Ý", Z: "Ž",
  a: "áá", b: "ƀ", c: "ç", d: "đ", e: "éé", f: "ƒ", g: "ğ", h: "ħ", i: "ïï", j: "ĵ", k: "ķ", l: "ŀ", m: "ḿ", n: "ń", o: "øø", p: "þ", q: "ǫ", r: "ŕ", s: "š", t: "ŧ", u: "üü", v: "ṽ", w: "ŵ", x: "ẋ", y: "ý", z: "ž",
};

/** Wraps a value in a first-strong isolate so mixed-direction identifiers stay ordered. */
export function isolateBidi(text: string): string {
  return `\u2068${text}\u2069`;
}

/**
 * Produces an intentionally expanded pseudo-locale while leaving `{placeholders}`
 * byte-for-byte intact. Use it to expose clipped labels and hard-coded strings.
 */
export function pseudoLocalize(text: string): string {
  return `[${text.split(/(\{[^{}]*\})/u).map((part) =>
    part.startsWith("{") && part.endsWith("}") ? part : pseudoLocalizeLiteral(part),
  ).join("")}~]`;
}

/** Attributes to apply when mounting a case in DOM-oriented test harnesses. */
export function localizationStressAttributes(
  stressCase: LocalizationStressCase,
): Readonly<{ lang: string; dir: LocalizationStressDirection }> {
  return { lang: stressCase.locale, dir: stressCase.direction };
}

export const localizationStressCases: readonly LocalizationStressCase[] = [
  {
    id: "pseudo-locale-long-action",
    locale: "en-XA",
    direction: "ltr",
    text: pseudoLocalize("Synchronize the selected documents before you close this window."),
    checks: ["text wraps without clipping", "actions remain identifiable", "no literal bypasses localization"],
  },
  {
    id: "plural-boundaries",
    locale: "en-XA",
    direction: "ltr",
    text: pseudoLocalize("{count} selected documents still need your review."),
    checks: ["render every pluralStressCounts value", "plural output stays readable at extreme counts"],
  },
  {
    id: "rtl-mixed-identifier",
    locale: "ar-XB",
    direction: "rtl",
    text: `تم حفظ ${isolateBidi("Setup-2026-08-30.json")} بنجاح.`,
    checks: ["mixed-direction identifiers retain order", "layout mirrors without changing semantic order"],
  },
] as const;

/**
 * These browser media queries are the canonical minimum visual regression
 * matrix. Consumers assert the named properties in their own design system;
 * this package cannot author their colors or animation policy for them.
 */
export const visualAccessibilityStressScenarios: readonly VisualAccessibilityStressScenario[] = [
  {
    id: "forced-colors",
    mediaQuery: "(forced-colors: active)",
    checks: ["controls remain visible", "focus indicator remains visible", "meaning is not color-only"],
  },
  {
    id: "high-contrast",
    mediaQuery: "(prefers-contrast: more)",
    checks: ["text and control boundaries remain distinguishable", "status is not color-only"],
  },
  {
    id: "reduced-motion",
    mediaQuery: "(prefers-reduced-motion: reduce)",
    checks: ["non-essential animation is removed or shortened", "state changes remain understandable"],
  },
] as const;

function pseudoLocalizeLiteral(text: string): string {
  return text.replace(/[A-Za-z]/gu, (character) => pseudoCharacters[character] ?? character);
}
