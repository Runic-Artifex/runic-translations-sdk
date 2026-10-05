import { createLocaleContext } from "../src/translations/index.js";

export type TestLocale = "en" | "de";
export const testLocaleContext = createLocaleContext<TestLocale>();
