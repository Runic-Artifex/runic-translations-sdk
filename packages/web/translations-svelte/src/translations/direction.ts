// The writing direction of a locale: its explicit script subtag, else the likely script Intl.Locale's maximize() gives,
// is one of CLDR 48.2's right-to-left scripts (specs/translations/cldr/runic-subset-48.2.json; a test keeps the list in
// step). The generated ESM runtime's localeDirection gives the same for a catalog's own languages, from pinned CLDR data.
const rightToLeftScripts: ReadonlySet<string> = new Set(["Adlm", "Arab", "Armi", "Avst", "Chrs", "Cprt", "Elym", "Gara", "Hatr", "Hebr", "Hung", "Khar", "Lydi", "Mand", "Mani", "Mend", "Merc", "Mero", "Narb", "Nbat", "Nkoo", "Orkh", "Ougr", "Palm", "Phli", "Phlp", "Phnx", "Prti", "Rohg", "Samr", "Sarb", "Sidt", "Sogd", "Sogo", "Syrc", "Thaa", "Yezi"]);

/** A writing direction, as HTML's `dir` attribute takes it. */
export type TextDirection = "ltr" | "rtl";

/**
 * The writing direction of a BCP 47 locale: `rtl` for `ar`, `he`, `fa`, `pa-PK`, `ku-Arab` or `ar-u-nu-latn`, `ltr` for
 * `en`, `ku`, `he-Latn` or `en-u-nu-arab`. Only the script subtag counts (after the language and up to three extlangs),
 * never an extension or private-use subtag, as in the generated runtime's `localeDirection` and .NET's `LocaleDirection`.
 * An invalid tag, or a language whose script is unknown without `Intl.Locale`, is `ltr`.
 */
export function textDirection(locale: string): TextDirection {
  if (typeof locale !== "string" || locale.length === 0) return "ltr";
  const tag = locale.replaceAll("_", "-");
  let script = explicitScript(tag);
  if (script === undefined && typeof Intl === "object" && typeof Intl.Locale === "function") {
    try { script = new Intl.Locale(tag).maximize().script; } catch { script = undefined; }
  }
  if (script === undefined) return "ltr";
  return rightToLeftScripts.has(script.charAt(0).toUpperCase() + script.slice(1).toLowerCase()) ? "rtl" : "ltr";
}

// The script subtag in its BCP 47 position: language (2–8 letters), up to three 3-letter extlangs, then a 4-letter script.
// A singleton (u, t, x, …) starts an extension or private use, so nothing after it is a script.
function explicitScript(tag: string): string | undefined {
  const parts = tag.split("-");
  if (!/^[A-Za-z]{2,8}$/.test(parts[0] ?? "")) return undefined;
  let index = 1;
  if (parts[0]!.length <= 3) for (let count = 0; count < 3 && index < parts.length && /^[A-Za-z]{3}$/.test(parts[index]!); count++) index++;
  const candidate = parts[index];
  return candidate !== undefined && /^[A-Za-z]{4}$/.test(candidate) ? candidate : undefined;
}
