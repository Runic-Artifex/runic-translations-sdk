import assert from "node:assert/strict";
import { resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { chromium } from "playwright-core";
import { createServer } from "vite";

const root = fileURLToPath(new URL("../", import.meta.url));
const vite = await createServer({ root, configFile: resolve(root, "vite.config.ts"), server: { host: "127.0.0.1", port: 0, hmr: false } });
let browser;
try {
  await vite.listen();
  const fixture = await vite.ssrLoadModule("/test/DocumentFixture.svelte");
  const { corpusNodes, executions } = await vite.ssrLoadModule("/test/document-corpus.ts");
  const { render } = await vite.ssrLoadModule("svelte/server");
  let calls = 0;
  const body = executions.map((execution, index) =>
    `<section id="fixture-${index}" data-execution="${execution.id}">${render(fixture.default, { props: { nodes: corpusNodes(execution.id, () => calls++) } }).body}</section>`).join("");
  assert.equal(calls, 0, "SSR must not activate application actions");
  browser = await chromium.launch({ executablePath: process.env.PLAYWRIGHT_CHROMIUM_EXECUTABLE_PATH || process.env.WEBUI_BROWSER_PATH, headless: true });
  const page = await browser.newPage();
  const errors = [];
  page.on("pageerror", error => errors.push(error.message));
  page.on("console", message => { if (message.type() === "warning" && message.text().includes("hydration")) errors.push(message.text()); });
  await page.route("**/document-hydration", route => route.fulfill({ contentType: "text/html", body: `<!doctype html><html lang="fr"><body>${body}<script type="module" src="/test/document-hydration-client.ts"></script></body></html>` }));
  await page.goto(`${vite.resolvedUrls.local[0]}document-hydration`);
  await page.waitForFunction(() => window.runicDocumentResult !== undefined);
  assert.deepEqual(errors, []);
  assert.deepEqual(await page.evaluate(() => window.runicDocumentResult), { hydrated: executions.length, mismatches: [], initialCalls: 0 });

  // Accessibility: semantic roles, heading levels and reading order come from native HTML.
  const example = page.locator('[data-execution="backup-en"]');
  assert.equal(await example.ariaSnapshot(), [
    "- paragraph:",
    "  - strong: Before continuing",
    "  - text: \", save a copy of report.txt.\"",
    "- list:",
    "  - listitem:",
    "    - text: Read the",
    "    - link \"guide\":",
    "      - /url: https://example.test/guide",
    "    - text: .",
    "  - listitem:",
    "    - button \"Check\"",
    "    - text: the result.",
    "- paragraph: You can continue when the check finishes.",
  ].join("\n"));
  assert.equal(await page.locator('[data-execution="headings-integer-literals"]').ariaSnapshot(), [
    "- heading \"Title\" [level=2]",
    "- heading \"Section\" [level=3]",
    "- paragraph: Body",
  ].join("\n"));
  assert.match(await page.locator('[data-execution="alpha-past-z"]').ariaSnapshot(), /- list:\n  - listitem: Twenty-five\n  - listitem: Twenty-six\n  - listitem: Twenty-seven/);
  // A fallback renders the effective content locale on its top-level blocks, not the page's requested locale.
  assert.deepEqual(await page.locator('[data-execution="backup-fr-falls-back-to-de"] > *').evaluateAll(nodes => nodes.map(node => node.getAttribute("lang"))), ["de", "de", "de"]);
  // Whitespace inside leaves is preserved by the shipped runic-leaf class rather than inline styles.
  assert.equal(await page.locator('[data-execution="whitespace-collapse"] p').first().evaluate(node => getComputedStyle(node).whiteSpace), "pre-wrap");
  assert.equal(await page.locator("[style]").count(), 0, "document adapters must not emit inline styles");
  // Ordered list markers come from native start and type attributes.
  assert.deepEqual(await page.locator('[data-execution="alpha-past-z"] ol').evaluate(node => [node.start, node.type]), [25, "a"]);

  // Keyboard: the link and then the action are reachable with Tab in document order, and Enter activates the action.
  await page.keyboard.press("Tab");
  assert.equal(await page.evaluate(() => document.activeElement?.closest("[data-execution]")?.getAttribute("data-execution") + ":" + document.activeElement?.tagName), "backup-en:A");
  await page.keyboard.press("Tab");
  assert.equal(await page.evaluate(() => document.activeElement?.textContent), "Check");
  await page.keyboard.press("Enter");
  assert.equal(await page.evaluate(() => window.runicDocumentCalls), 1);

  // Callback retirement: replacing the content detaches the list, and the retained controls are inert.
  const button = await example.locator("button").elementHandle();
  const link = await example.locator("a").elementHandle();
  await page.evaluate(() => window.runicDocument.replace("files-one-de"));
  assert.equal(await example.locator("button").count(), 0);
  // Svelte delegates clicks to the mount root, so re-attach the retained button there and re-enable it.
  const reattach = node => { document.querySelector('[data-execution="backup-en"]').append(node); node.disabled = false; node.click(); node.remove(); };
  await button.evaluate(reattach);
  assert.equal(await page.evaluate(() => window.runicDocumentCalls), 1, "a retired action remained active");
  assert.deepEqual(await link.evaluate(node => [node.isConnected, node.hasAttribute("href")]), [false, false], "a detached link kept its destination");
  await page.evaluate(() => window.runicDocument.replace("backup-en"));
  const replacement = await example.locator("button").elementHandle();
  await page.evaluate(() => window.runicDocument.clear());
  await replacement.evaluate(reattach);
  assert.equal(await page.evaluate(() => window.runicDocumentCalls), 1, "a cleared action remained active");
  assert.equal(await example.textContent(), "");
  await page.evaluate(() => window.runicDocument.unmount());
  assert.equal((await page.locator("section").allTextContents()).join(""), "");
  assert.deepEqual(errors, []);
  console.log("RMF2 Svelte document SSR, hydration of every corpus execution, accessibility, keyboard access and callback retirement passed.");
} finally {
  await browser?.close();
  await vite.close();
}
