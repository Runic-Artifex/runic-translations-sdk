// Maintains specs/translations/examples/rmf2-document: generates its ESM output with the repository CLI and
// checks the page in Chromium for semantics, keyboard access, selection and copy, locale changes and callback retirement.
import assert from "node:assert/strict";
import { execFileSync } from "node:child_process";
import { mkdtempSync, readFileSync, rmSync } from "node:fs";
import { tmpdir } from "node:os";
import { extname, join, normalize, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { chromium } from "playwright-core";

const repository = fileURLToPath(new URL("../../../../", import.meta.url));
const example = resolve(repository, "specs/translations/examples/rmf2-document");
const generated = mkdtempSync(join(tmpdir(), "runic-document-example-"));
const types = { ".html": "text/html", ".js": "text/javascript", ".json": "application/json" };
let browser;
try {
  execFileSync("dotnet", ["run", "--project", resolve(repository, "tools/dotnet-runic-translations"), "--",
    "generate", "--project", example, "--output", generated, "--emit-esm"], { cwd: repository, stdio: ["ignore", "ignore", "inherit"] });

  browser = await chromium.launch({ executablePath: process.env.PLAYWRIGHT_CHROMIUM_EXECUTABLE_PATH || process.env.WEBUI_BROWSER_PATH, headless: true });
  const page = await browser.newPage();
  const errors = [];
  page.on("pageerror", error => errors.push(error.message));
  await page.route("http://example.test/**", route => {
    const path = normalize(decodeURIComponent(new URL(route.request().url()).pathname)).replace(/^[/\\]+/, "");
    const file = path.startsWith("generated") ? join(generated, path.slice("generated".length)) : join(example, path);
    try { route.fulfill({ contentType: types[extname(file)] ?? "application/octet-stream", body: readFileSync(file) }); }
    catch { route.fulfill({ status: 404, body: "" }); }
  });
  await page.goto("http://example.test/browser.html");
  await page.waitForSelector("#help .runic-leaf");
  const help = page.locator("#help");
  const status = () => page.locator("#status").textContent();

  // Emphasis, a link and an action slot, and bullets, as native HTML semantics.
  assert.equal(await help.ariaSnapshot(), [
    "- region \"Backup help\":",
    "  - paragraph:",
    "    - strong: Before continuing",
    "    - text: \", save a copy of report.txt.\"",
    "  - list:",
    "    - listitem:",
    "      - text: Read the",
    "      - link \"guide\":",
    "        - /url: \"#guide\"",
    "      - text: .",
    "    - listitem:",
    "      - button \"Check\"",
    "      - text: the result.",
    "  - paragraph: You can continue when the check finishes.",
  ].join("\n"));
  assert.deepEqual(await help.locator(":scope > *").evaluateAll(nodes => nodes.map(node => node.lang)), ["en", "en", "en"]);
  assert.equal(await help.locator("p").first().evaluate(node => getComputedStyle(node).whiteSpace), "pre-wrap");

  // Keyboard: Tab reaches the link and then the action; Enter activates both.
  await page.focus("#rerender");
  await page.keyboard.press("Tab");
  assert.equal(await page.evaluate(() => document.activeElement.textContent), "guide");
  await page.keyboard.press("Tab");
  assert.equal(await page.evaluate(() => document.activeElement.textContent), "Check");
  await page.keyboard.press("Enter");
  assert.equal(await status(), "Check 1 from render 1");
  await page.keyboard.press("Shift+Tab");
  await page.keyboard.press("Enter");
  await page.waitForFunction(() => location.hash === "#guide");

  // Selection and copy: text is selectable; copying the whole message writes the plain-text projection,
  // copying part of it leaves the browser's own text.
  const copy = (select) => page.evaluate((select) => {
    const help = document.querySelector("#help"), range = document.createRange();
    if (select === "whole") range.selectNodeContents(help);
    else { const text = help.querySelector("p").childNodes[1]; range.setStart(text, 2); range.setEnd(text, 6); }
    document.getSelection().removeAllRanges(); document.getSelection().addRange(range);
    const event = new ClipboardEvent("copy", { clipboardData: new DataTransfer(), bubbles: true, cancelable: true });
    (range.startContainer.nodeType === Node.ELEMENT_NODE ? range.startContainer : range.startContainer.parentElement).dispatchEvent(event);
    return { selected: document.getSelection().toString(), copied: event.clipboardData.getData("text/plain"), replaced: event.defaultPrevented,
      selectable: getComputedStyle(help.querySelector("li")).userSelect };
  }, select);
  const whole = await copy("whole");
  assert.notEqual(whole.selectable, "none");
  assert.match(whole.selected, /Before continuing, save a copy of report\.txt\./);
  assert.equal(whole.copied, "Before continuing, save a copy of report.txt.\n\n- Read the guide.\n- Check the result.\n\nYou can continue when the check finishes.");
  const part = await copy("part");
  assert.deepEqual([part.selected, part.replaced], ["save", false]);

  // Callback retirement: a retained node from the previous render stays inert after a new render.
  await page.evaluate(() => { window.retained = { link: document.querySelector("#help a"), action: document.querySelector("#help button") }; });
  await page.click("#rerender");
  assert.equal(await status(), "Rendered again");
  assert.deepEqual(await page.evaluate(() => {
    const { link, action } = window.retained;
    const result = { connected: action.isConnected, disabled: action.disabled, href: link.hasAttribute("href") };
    action.disabled = false; action.click();
    return result;
  }), { connected: false, disabled: true, href: false });
  assert.equal(await status(), "Rendered again", "A retired action called back");
  await help.getByRole("button", { name: "Check" }).click();
  assert.equal(await status(), "Check 2 from render 2");

  // Changing the locale renders the translation with its effective content locale.
  await page.selectOption("#locale", "de");
  await page.waitForFunction(() => document.querySelector("#help p")?.lang === "de");
  assert.match(await help.ariaSnapshot(), /- strong: Bevor Sie fortfahren[\s\S]*- button "Prüfen"/);
  assert.equal(await page.evaluate(() => window.retained.action.isConnected), false);

  assert.deepEqual(errors, []);
  console.log("PASS RMF2 document example: semantics, keyboard, selection and copy, locale change and callback retirement.");
} finally {
  await browser?.close();
  rmSync(generated, { recursive: true, force: true });
}
