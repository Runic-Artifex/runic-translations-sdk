// A separate process, like a tray icon: no server, only the generated catalogue and the history files.
// Usage: bun tray.ts <locale> <history.jsonl>...
import { readHistory, show } from "./history.ts";

const [locale = "en", ...paths] = process.argv.slice(2);
for (const entry of await readHistory(paths)) console.log(show(entry, locale).text);
