import assert from "node:assert/strict";
import { resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { chromium } from "playwright-core";
import { createServer } from "vite";
import { devServerOptions, watchPage } from "./browser-page.mjs";

const root = fileURLToPath(new URL("../", import.meta.url));
const vite = await createServer({ root, configFile: resolve(root, "vite.config.ts"), server: devServerOptions });
let browser;
try {
  await vite.listen();
  const fixture = await vite.ssrLoadModule("/test/InlineHydrationFixture.svelte");
  const { paymentNodes } = await vite.ssrLoadModule("/test/inline-hydration-data.ts");
  const { render } = await vite.ssrLoadModule("svelte/server");
  let calls = 0;
  let accessibleNameCalls = 0;
  const { body } = await render(fixture.default, { props: { nodes: paymentNodes(() => calls++, locale => { accessibleNameCalls++; return locale === "en" ? "Star" : "Stern"; }) } });
  assert.equal(calls, 0, "SSR must not activate application actions");
  assert.equal(accessibleNameCalls, 1, "SSR must evaluate a meaningful icon accessibleName exactly once");
  browser = await chromium.launch({ executablePath: process.env.PLAYWRIGHT_CHROMIUM_EXECUTABLE_PATH || process.env.WEBUI_BROWSER_PATH, headless: true });
  const page = await browser.newPage();
  const { errors, waitForResult } = watchPage(page);
  await page.route("**/inline-hydration", route => route.fulfill({ contentType: "text/html", body: `<!doctype html><html><body><div id="fixture">${body}</div><script type="module" src="/test/inline-hydration-client.ts"></script></body></html>` }));
  await page.goto(`${vite.resolvedUrls.local[0]}inline-hydration`);
  await waitForResult(() => window.runicInlineResult !== undefined, "the inline hydration result");
  assert.deepEqual(errors, []);
  assert.deepEqual(await page.evaluate(() => window.runicInlineResult), { sameButton: true, sameLink: true, escaped: true, accessibleName: "Star", accessibleNameCalls: 1, badge: "Available", initialCalls: 0, activatedCalls: 1, disposedCalls: 1, linkRetired: true, empty: true, blankRejected: true });
  console.log("RMF2 Svelte SSR identity, escaping, accessibility, custom markup and action and link teardown passed.");
} finally {
  await browser?.close();
  await vite.close();
}
