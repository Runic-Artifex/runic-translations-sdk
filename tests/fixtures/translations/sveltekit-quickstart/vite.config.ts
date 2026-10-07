import adapter from '@sveltejs/adapter-node';
import { runicTranslations } from '@runic-artifex/vite-plugin-runic-translations';
import { sveltekit } from '@sveltejs/kit/vite';
import { defineConfig } from 'vite';

export default defineConfig({
	plugins: [
		runicTranslations(),
		sveltekit({
			compilerOptions: {
				runes: ({ filename }) =>
					filename.split(/[/\\]/).includes('node_modules') ? undefined : true
			},
			adapter: adapter(),
			prerender: { entries: ['*', '/de'] }
		})
	]
});
