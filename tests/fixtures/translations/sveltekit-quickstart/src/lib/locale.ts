import { createLocaleContext } from '@runic-artifex/translations-svelte';
import type { Locale } from '#lib/i18n.ts';

export const localeContext = createLocaleContext<Locale>();
