import { createLocaleContext } from '@runic-artifex/translations-svelte/translations';
import type { Locale } from '#lib/i18n.ts';

export const localeContext = createLocaleContext<Locale>();
