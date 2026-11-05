import { createLocaleRouting } from '@runic-artifex/translations-sveltekit';

export const routing = createLocaleRouting({
	locales: ['en', 'de', 'fr'] as const,
	baseLocale: 'en',
	baseLocalePath: 'unprefixed'
});

export type Locale = (typeof routing.locales)[number];
