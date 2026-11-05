import { createLocaleHandle } from '@runic-artifex/translations-sveltekit';
import { runWithLocale } from 'virtual:runic-translations/shop/server';
import { routing } from '#lib/i18n.ts';

// Every catalog's server.js shares one request storage, so the application's runWithLocale also sets the locale of
// the library's catalog: its text renders in the request's locale and does not change at hydration.
export const handle = createLocaleHandle(routing, { runWithLocale });
