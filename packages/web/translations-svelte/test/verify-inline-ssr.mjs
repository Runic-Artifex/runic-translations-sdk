import assert from "node:assert/strict";
import { resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { createServer } from "vite";

const root = resolve(fileURLToPath(new URL("../", import.meta.url)));
const vite = await createServer({
  root,
  configFile: resolve(root, "vite.config.ts"),
  appType: "custom",
  server: { middlewareMode: true },
});
try {
  const fixture = await vite.ssrLoadModule("/test/InlineHydrationFixture.svelte");
  const { paymentNodes } = await vite.ssrLoadModule("/test/inline-hydration-data.ts");
  const { render } = await vite.ssrLoadModule("svelte/server");
  let rejection = "";
  try {
    await render(fixture.default, { props: { nodes: paymentNodes(() => undefined, () => " \t ") } });
  } catch (error) {
    rejection = String(error);
  }
  assert.match(rejection, /Meaningful icon alternate text is empty\./, "SSR accepted a whitespace-only meaningful icon label");
  console.log("RMF2 Svelte SSR rejects blank meaningful icon accessibility labels.");
} finally {
  await vite.close();
}
