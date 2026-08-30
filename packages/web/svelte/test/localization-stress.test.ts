import { describe, expect, test } from "vitest";
import {
  isolateBidi,
  localizationStressAttributes,
  localizationStressCases,
  pluralStressCounts,
  pseudoLocalize,
  visualAccessibilityStressScenarios,
} from "../src/translations/testing.js";

describe("localization stress fixtures", () => {
  test("preserves message placeholders while making ordinary text visibly longer", () => {
    const pseudo = pseudoLocalize("Review {count} documents");
    expect(pseudo).toContain("{count}");
    expect(pseudo.length).toBeGreaterThan("Review {count} documents".length);
    expect(pseudo).toMatch(/^\[.*~\]$/u);
  });

  test("covers pseudo-localization, plural extremes, and isolated RTL identifiers", () => {
    expect(pluralStressCounts).toEqual([0, 1, 2, 5, 11, 21, 101, 1000]);
    expect(localizationStressCases.map((stressCase) => stressCase.id)).toEqual([
      "pseudo-locale-long-action",
      "plural-boundaries",
      "rtl-mixed-identifier",
    ]);
    const rtl = localizationStressCases[2];
    expect(rtl).toBeDefined();
    expect(rtl?.text).toContain(isolateBidi("Setup-2026-08-30.json"));
    expect(localizationStressAttributes(rtl!)).toEqual({ lang: "ar-XB", dir: "rtl" });
  });

  test("keeps visual accessibility scenarios explicit and independently testable", () => {
    expect(visualAccessibilityStressScenarios.map((scenario) => scenario.mediaQuery)).toEqual([
      "(forced-colors: active)",
      "(prefers-contrast: more)",
      "(prefers-reduced-motion: reduce)",
    ]);
    expect(visualAccessibilityStressScenarios.every((scenario) => scenario.checks.length >= 2)).toBe(true);
  });
});
