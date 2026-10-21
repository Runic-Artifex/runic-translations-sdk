import { createLocaleReroute } from "@runic-artifex/translations-sveltekit";
import { routing } from "./lib/i18n.js";

export const reroute = createLocaleReroute(routing);
