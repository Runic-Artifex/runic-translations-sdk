import { runicTranslations } from '@runic-artifex/vite-plugin-runic-translations';
import { defineConfig } from 'vite';

// The plugin compiles translations/runic.json, the catalog that ../Tray formats in C# as well.
export default defineConfig({
  plugins: [runicTranslations({ project: 'translations' })],
});
