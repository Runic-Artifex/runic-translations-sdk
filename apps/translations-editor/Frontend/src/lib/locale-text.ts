import type { UiDirection } from "./simulation";

const rtlLanguages = new Set(["ar", "arc", "ckb", "dv", "fa", "he", "ku", "nqo", "ps", "sd", "ug", "ur", "yi"]);

/** The BCP-47 language subtag determines text direction when no document-level override exists. */
export function localeDirection(locale: string): UiDirection {
  return rtlLanguages.has(locale.trim().toLocaleLowerCase().split("-")[0] ?? "") ? "rtl" : "ltr";
}
