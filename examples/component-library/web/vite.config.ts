import adapter from '@sveltejs/adapter-node';
import { runicTranslations } from '@runic-artifex/vite-plugin-runic-translations';
import { sveltekit } from '@sveltejs/kit/vite';
import { defineConfig } from 'vite';

export default defineConfig({
	plugins: [
		// The catalog and its overrides are shared with the .NET application in ../app. dependencies: 'auto' finds
		// @acme/widgets through its package.json#runicTranslations and regenerates its catalog with
		// ../translations/overrides/acme.widgets/, filtered to this app's locales (en, de, fr).
		runicTranslations({ project: '../translations', dependencies: 'auto' }),
		sveltekit({
			compilerOptions: {
				runes: ({ filename }) =>
					filename.split(/[/\\]/).includes('node_modules') ? undefined : true
			},
			adapter: adapter()
		})
	]
});
