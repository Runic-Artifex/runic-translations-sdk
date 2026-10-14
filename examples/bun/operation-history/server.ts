// The backend: records operations as references and serves the history in the reader's language.
import { fileURLToPath } from "node:url";
import { createTextReference } from "./generated/operations.esm-v5/transport.js";
import { appendEntry, readHistory, show } from "./history.ts";

const stored = fileURLToPath(new URL("./history/2026-04.jsonl", import.meta.url));
const current = process.env.HISTORY_FILE ?? fileURLToPath(new URL("./history/current.jsonl", import.meta.url));

const server = Bun.serve({
  port: Number(process.env.PORT ?? 0),
  async fetch(request) {
    const url = new URL(request.url);
    if (url.pathname !== "/operations") return new Response("Not found", { status: 404 });
    if (request.method === "POST") {
      const { package: name } = (await request.json()) as { package: string };
      // The step label is a message of its own, so "Building gimp" is translated inside "… failed" too.
      const step = createTextReference("steps_build", { name }, { fallbackText: `Building ${name}` });
      const text = createTextReference("operation_failed", { step }, { fallbackText: `Building ${name} failed` });
      await appendEntry(current, text);
      return Response.json(text, { status: 201 });
    }
    const locale = url.searchParams.get("locale") ?? "en";
    return Response.json((await readHistory([stored, current])).map(entry => show(entry, locale)));
  },
});
console.log(`listening ${server.port}`);
