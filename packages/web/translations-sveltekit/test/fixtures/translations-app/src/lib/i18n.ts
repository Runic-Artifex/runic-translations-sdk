import { createRunicLocaleRouting } from "@runic-artifex/translations-sveltekit/translations";

export const routing = createRunicLocaleRouting({
  locales: ["en", "de"] as const,
  baseLocale: "en",
  baseLocalePath: "unprefixed",
});
