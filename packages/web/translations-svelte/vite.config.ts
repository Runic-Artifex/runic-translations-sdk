import { svelte } from "@sveltejs/vite-plugin-svelte";
import { defineConfig } from "vitest/config";

export default defineConfig({
  plugins: [svelte()],
  resolve: {
    conditions: ["browser"],
  },
  optimizeDeps: {
    // The browser checks serve these entries from the dev server without an index.html. Scanning them up front
    // bundles every dependency (including esm-env behind src/) before the first page request, so the optimizer
    // never re-bundles mid-load and answers already-loaded imports with 504 Outdated Optimize Dep.
    entries: ["test/*-client.ts"],
  },
  test: {
    environment: "node",
  },
});
