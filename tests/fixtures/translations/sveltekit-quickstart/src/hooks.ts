import { createLocaleReroute } from '@runic-artifex/translations-sveltekit';
import { routing } from '#lib/i18n.ts';

export const reroute = createLocaleReroute(routing);
