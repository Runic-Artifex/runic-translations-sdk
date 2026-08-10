import { createRunicLocaleHandle } from "@runic-artifex/sveltekit/translations";
import { routing } from "$lib/i18n.js";

export const handle = createRunicLocaleHandle(routing);
