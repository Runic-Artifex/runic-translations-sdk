import { createLocaleHandle } from "@runic-artifex/translations-sveltekit";
import { routing } from "./lib/i18n.js";

export const handle = createLocaleHandle(routing);
