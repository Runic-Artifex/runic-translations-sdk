import tailwindcss from "@tailwindcss/vite";
import { runic } from "@runic-artifex/vite-plugin-runic";
import { runicTranslations } from "@runic-artifex/vite-plugin-runic-translations";
import { sveltekit } from "@sveltejs/kit/vite";
import { globSync } from "node:fs";
import { defineConfig } from "vite";

const manifest = process.env.RUNIC_TRANSLATIONS_MANIFEST;

if (manifest === undefined || manifest.length === 0) {
  throw new Error("RUNIC_TRANSLATIONS_MANIFEST must point to the generated editor web-module manifest.");
}

export default defineConfig({
  plugins: [
    tailwindcss(),
    runic({
      contract: { identity: "runic.translations.editor", version: "1" },
      applicationBridge: true,
    }),
    runicTranslations({
      manifest,
      sourceFiles: globSync("../EditorResources/{runic.json,en/*.mf2,de/*.mf2}")
    }),
    sveltekit()
  ],
  build: { target: "es2022" }
});
