import { createRunicLocaleReroute } from "@runic-artifex/translations-sveltekit/translations";
import { routing } from "./lib/i18n.js";

export const reroute = createRunicLocaleReroute(routing);
