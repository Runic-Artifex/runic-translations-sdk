import { runicTranslations } from "@runic-artifex/vite-plugin-runic-translations";

const manifest = process.env.RUNIC_TRANSLATIONS_MANIFEST;
if (manifest === undefined || manifest.length === 0) {
  throw new Error("RUNIC_TRANSLATIONS_MANIFEST must point to the generated editor web-module manifest.");
}

const plugin = runicTranslations({
  manifest,
  typeDeclarations: "src/generated/runic-translations.d.ts",
});
if (typeof plugin.buildStart !== "function") {
  throw new Error("The Runic Translations plugin does not expose its build preparation hook.");
}
await plugin.buildStart.call({ addWatchFile() {} });
