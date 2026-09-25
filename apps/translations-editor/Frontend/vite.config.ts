import tailwindcss from "@tailwindcss/vite";
import { runicTranslations } from "@runic-artifex/vite-plugin-runic-translations";
import { sveltekit } from "@sveltejs/kit/vite";
import { defineConfig } from "vite";
import { readFileSync } from "node:fs";

const manifest = process.env.RUNIC_TRANSLATIONS_MANIFEST
  ?? readFileSync(".runic-translations-manifest-path", "utf8").trim();
if (!manifest) throw new Error("Generate editor translations before building the frontend.");

export default defineConfig({
  plugins: [
    tailwindcss(),
    runicTranslations({
      manifest,
      sourceFiles: ["../EditorResources/runic.json", "../EditorResources/en.rmf2", "../EditorResources/de.rmf2"],
    }),
    sveltekit(),
  ],
  build: { target: "es2022" },
});
