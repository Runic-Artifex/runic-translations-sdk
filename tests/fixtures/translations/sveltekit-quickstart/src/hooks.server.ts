import { createRunicLocaleHandle } from '@runic-artifex/translations-sveltekit/translations';
import { runWithLocale } from 'virtual:runic-translations/app/server';
import { routing } from '#lib/i18n.ts';

export const handle = createRunicLocaleHandle(routing, { runWithLocale });
