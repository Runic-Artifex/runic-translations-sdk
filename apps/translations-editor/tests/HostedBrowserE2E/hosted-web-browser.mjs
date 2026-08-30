// Hosted-web browser e2e driver, ported from the toolkit's
// tests/Runic.Application.Hosting.Tests/hosted-web-browser.mjs pattern:
// playwright-core plus the pinned Chromium named by WEBUI_BROWSER_PATH.
import { createRequire } from "module";

const require = createRequire(import.meta.url);
const { chromium } = require("playwright-core");

const [url] = process.argv.slice(2);
const executablePath = process.env.WEBUI_BROWSER_PATH;
if (!url || !executablePath) throw new Error("Expected hosted-web URL and WEBUI_BROWSER_PATH.");

const browser = await chromium.launch({
  executablePath,
  headless: true,
  args: ["--no-sandbox", "--disable-gpu", "--disable-dev-shm-usage"],
});
try {
  const page = await browser.newPage();
  page.on("pageerror", (error) => console.error(`[pageerror] ${error.message}`));
  page.on("console", (message) => {
    if (message.type() === "error") console.error(`[console] ${message.text()}`);
  });
  await page.goto(url, { waitUntil: "domcontentloaded" });
  await page.waitForFunction(() => document.body.dataset.result !== "pending", undefined, { timeout: 30_000 });
  const result = await page.evaluate(() => ({
    result: document.body.dataset.result,
    error: document.body.dataset.error,
    stage: document.body.dataset.stage,
    catalog: document.querySelector("#catalog")?.textContent,
    stages: [...document.querySelectorAll("#stages li")].map((item) => `${item.dataset.state} ${item.textContent}`),
  }));
  if (result.result !== "pass") {
    throw new Error(`Hosted-web bridge workflow failed at stage '${result.stage}': ${result.error}\n${result.stages.join("\n")}`);
  }
  console.log(`hosted-web-browser-ok (${result.catalog})`);
} finally {
  await browser.close();
}
