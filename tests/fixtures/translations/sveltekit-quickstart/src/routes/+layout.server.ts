import { localeFromLocals } from '@runic-artifex/translations-sveltekit';
import { routing } from '#lib/i18n.ts';
import type { LayoutServerLoad } from './$types';

export const load: LayoutServerLoad = ({ locals }) => ({
	locale: localeFromLocals(locals, routing)
});
