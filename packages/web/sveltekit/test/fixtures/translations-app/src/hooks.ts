import { createRunicLocaleReroute } from "@runic-artifex/sveltekit/translations";
import { routing } from "$lib/i18n.js";

export const reroute = createRunicLocaleReroute(routing);
