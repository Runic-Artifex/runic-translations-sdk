import { createLocaleHandle } from '@runic-artifex/translations-sveltekit';
import { runWithLocale } from 'virtual:runic-translations/app/server';
import { routing } from '#lib/i18n.ts';

export const handle = createLocaleHandle(routing, { runWithLocale });
