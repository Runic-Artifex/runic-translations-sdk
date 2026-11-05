// The writing direction of a locale: its explicit script subtag, else the likely script Intl.Locale's maximize() gives,
// is one of CLDR 48.2's right-to-left scripts (specs/translations/cldr/runic-subset-48.2.json; a test keeps the list in
// step). The generated ESM runtime's localeDirection gives the same for a catalog's own languages, from pinned CLDR data.
const rightToLeftScripts: ReadonlySet<string> = new Set(["Adlm", "Arab", "Armi", "Avst", "Chrs", "Cprt", "Elym", "Gara", "Hatr", "Hebr", "Hung", "Khar", "Lydi", "Mand", "Mani", "Mend", "Merc", "Mero", "Narb", "Nbat", "Nkoo", "Orkh", "Ougr", "Palm", "Phli", "Phlp", "Phnx", "Prti", "Rohg", "Samr", "Sarb", "Sidt", "Sogd", "Sogo", "Syrc", "Thaa", "Yezi"]);

/** A writing direction, as HTML's `dir` attribute takes it. */
export type TextDirection = "ltr" | "rtl";

/**
 * The writing direction of a BCP 47 locale: `rtl` for `ar`, `he`, `fa`, `pa-PK` or `ku-Arab`, `ltr` for `en`, `ku` or
 * `he-Latn`. An invalid tag, or a language whose script is unknown without `Intl.Locale`, is `ltr`.
 */
export function textDirection(locale: string): TextDirection {
  if (typeof locale !== "string" || locale.length === 0) return "ltr";
  const tag = locale.replaceAll("_", "-");
  let script = tag.split("-").slice(1).find(part => /^[A-Za-z]{4}$/.test(part));
  if (script === undefined && typeof Intl === "object" && typeof Intl.Locale === "function") {
    try { script = new Intl.Locale(tag).maximize().script; } catch { script = undefined; }
  }
  if (script === undefined) return "ltr";
  return rightToLeftScripts.has(script.charAt(0).toUpperCase() + script.slice(1).toLowerCase()) ? "rtl" : "ltr";
}
