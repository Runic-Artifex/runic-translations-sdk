import { localeFromLocals } from "@runic-artifex/sveltekit/translations";
import { routing } from "$lib/i18n.js";
import type { LayoutServerLoad } from "./$types.js";

export const prerender = true;

export const load: LayoutServerLoad = ({ locals }) => ({
  locale: localeFromLocals(locals, routing),
});
