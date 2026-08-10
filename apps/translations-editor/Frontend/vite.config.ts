import tailwindcss from "@tailwindcss/vite";
import { runicTranslations } from "@runic-artifex/vite-plugin-runic-translations";
import { sveltekit } from "@sveltejs/kit/vite";
import { defineConfig } from "vite";

const manifest = process.env.RUNIC_TRANSLATIONS_MANIFEST;

if (manifest === undefined || manifest.length === 0) {
  throw new Error("RUNIC_TRANSLATIONS_MANIFEST must point to the generated editor web-module manifest.");
}

export default defineConfig({
  plugins: [
    tailwindcss(),
    runicTranslations({
      manifest,
      sourceFiles: [
        "../EditorResources/editor.en.json",
        "../EditorResources/editor.de.json"
      ]
    }),
    sveltekit()
  ],
  build: { target: "es2022" }
});
