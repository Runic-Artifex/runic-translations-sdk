import { createLocaleRouting } from "@runic-artifex/translations-sveltekit";

export const routing = createLocaleRouting({
  locales: ["en", "de"] as const,
  baseLocale: "en",
  baseLocalePath: "unprefixed",
});
