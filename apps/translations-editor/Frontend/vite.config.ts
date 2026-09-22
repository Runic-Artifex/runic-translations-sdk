import tailwindcss from "@tailwindcss/vite";
import { runic } from "@runic-artifex/vite-plugin-runic";
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
    runic({
      contract: { identity: "runic.translations.editor", version: "1" },
      applicationBridge: { authority: "effect", source: "src/application.bridge.ts" },
    }),
    runicTranslations({
      manifest,
      typeDeclarations: "src/generated/runic-translations.d.ts",
      sourceFiles: ["../EditorResources/runic.json", "../EditorResources/en.rmf2", "../EditorResources/de.rmf2"]
    }),
    sveltekit()
  ],
  build: { target: "es2022" }
});
