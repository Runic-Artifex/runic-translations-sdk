import { runicAdapter } from "@runic-artifex/sveltekit";
import tailwindcss from "@tailwindcss/vite";
import { runicTranslations } from "@runic-artifex/vite-plugin-runic-translations";
import type { Adapter } from "@sveltejs/kit";
import { sveltekit } from "@sveltejs/kit/vite";
import { defineConfig } from "vite";
import { readFileSync } from "node:fs";
import { readFile, writeFile } from "node:fs/promises";

const manifest = process.env.RUNIC_TRANSLATIONS_MANIFEST
  ?? readFileSync(".runic-translations-manifest-path", "utf8").trim();
if (!manifest) throw new Error("Generate editor translations before building the frontend.");

// Runic Desktop serves each window below a path of its own (/surface-<id>/), so the page names its files relative to
// itself. SvelteKit writes the SPA fallback page with root-relative /_app/ paths (paths.relative does not apply to a
// fallback). The adapter's Desktop option rewrites them, but also inserts runic-desktop.js, the plain Desktop client,
// which a Views page does not load: it loads webui.js and runic-views.js (src/app.html). So the page's /_app/ paths are
// rewritten here, after the adapter; everything else in app.html is already relative.
function relocatable(adapter: Adapter, page: string): Adapter {
  return {
    ...adapter,
    async adapt(builder) {
      await adapter.adapt(builder);
      const html = await readFile(page, "utf8");
      await writeFile(page, html.replaceAll('"/_app/', '"./_app/'));
    },
  };
}

export default defineConfig({
  plugins: [
    tailwindcss(),
    runicTranslations({
      manifest,
      typeDeclarations: "src/generated/runic-translations.d.ts",
      sourceFiles: ["../EditorResources/runic.json", "../EditorResources/en.rmf2", "../EditorResources/de.rmf2"],
    }),
    sveltekit({
      adapter: relocatable(runicAdapter({ mode: "spa", desktop: false, fallback: "index.html" }), "build/index.html"),
      router: { type: "hash" },
    }),
  ],
  build: { target: "es2022" },
});
