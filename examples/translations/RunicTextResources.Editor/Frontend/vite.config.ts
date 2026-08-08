import { runicTextResources } from "@runic-artifex/vite-plugin-text-resources";
import { sveltekit } from "@sveltejs/kit/vite";
import { defineConfig } from "vite";

const manifest = process.env.RUNIC_TEXT_MANIFEST;
if (manifest === undefined || manifest.length === 0) {
  throw new Error("RUNIC_TEXT_MANIFEST must point to the generated editor web-module manifest.");
}

export default defineConfig({
  plugins: [
    runicTextResources({
      manifest,
      sourceFiles: ["../EditorResources/editor.en.json", "../EditorResources/editor.de.json"],
    }),
    sveltekit(),
  ],
  build: { target: "es2022" },
});
