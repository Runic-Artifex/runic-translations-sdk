import { svelte } from "@sveltejs/vite-plugin-svelte";
import { runicTranslations } from "@runic-artifex/vite-plugin-runic-translations";
import { defineConfig } from "vite";

export default defineConfig({
  plugins: [runicTranslations(), svelte()],
});
