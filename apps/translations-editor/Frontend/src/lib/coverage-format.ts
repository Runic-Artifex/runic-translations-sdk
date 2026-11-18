/** A locale's coverage as runic-translations status reports it: translated plus fallback messages, in percent. */
export interface CoverageCounts {
  readonly percent: number;
  /** Messages with neither a translation nor a fallback; absent when unknown. */
  readonly missing?: number;
  readonly total?: number;
}

/** The coverage as shown: rounded to whole percent in the interface locale's format, and exact for a tooltip. */
export interface CoverageLabel {
  readonly text: string;
  readonly exact: string;
}

/**
 * Formats coverage for the interface locale (`53 %` in German, `53%` in English), rounded to whole percent (#225 P16).
 * Rounding never claims completeness or emptiness that is not there: 100 % only when nothing is missing, 0 % only when
 * nothing is covered. The exact value keeps up to two decimals.
 */
export function formatCoverage(counts: CoverageCounts, locale: string): CoverageLabel {
  const percent = Number.isFinite(counts.percent) ? Math.min(100, Math.max(0, counts.percent)) : 0;
  const somethingMissing = counts.missing !== undefined ? counts.missing > 0 : percent < 100;
  const somethingCovered = counts.missing !== undefined && counts.total !== undefined ? counts.total - counts.missing > 0 : percent > 0;
  let rounded = Math.round(percent);
  if (somethingMissing) rounded = Math.min(rounded, 99);
  if (somethingCovered) rounded = Math.max(rounded, 1);
  return {
    text: percentFormat(locale, 0).format(rounded / 100),
    exact: percentFormat(locale, 2).format(percent / 100),
  };
}

function percentFormat(locale: string, fractionDigits: number): Intl.NumberFormat {
  try {
    return new Intl.NumberFormat(locale, { style: "percent", maximumFractionDigits: fractionDigits });
  } catch {
    return new Intl.NumberFormat("en", { style: "percent", maximumFractionDigits: fractionDigits });
  }
}
