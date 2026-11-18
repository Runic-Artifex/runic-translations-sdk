import assert from "node:assert/strict";
import { mkdir, readFile, writeFile } from "node:fs/promises";
import { dirname, join } from "node:path";

// The real-window check (#225, R6): a one-language project's composer leaves the message's structure to developers. A
// German-only workspace beside the fixture opens without "Add plural forms", adding or removing cases, the advanced
// structure (inputs, formatters, selection rules) and the "Message source" buttons; its plural keeps its cases. A small
// "Show source" link in the composer's footer opens the message source for fixing a broken text, and the File view
// stays. An empty plural starter's counter shows 0 (R4) in the fixture's English. Then the fixture workspace is opened
// again: English, common.cancel selected, nothing unsaved.
/** @param {import("../context.mjs").HostedContext} context */
export default async function contentComposer(context) {
  const { page, workspace, reportDirectory, pageErrors, waitFor, select, saveState, messageRow, composerSource } = context;

  // R4: files.deleted is missing in English; its empty plural starter counts no characters, not its MF2 syntax.
  await page.getByRole("button", { name: /^en / }).first().click();
  await messageRow("files_deleted").click();
  await page.getByRole("textbox", { name: /^Translation for count: one/ }).first().waitFor();
  await waitFor(async () => /^0( \/ 24| characters)$/.test((await page.getByTestId("character-count").textContent())?.trim() ?? ""),
    "the empty plural starter counts 0 against its limit");
  await select("common_cancel");
  await waitFor(async () => (await page.getByTestId("character-count").textContent())?.trim() === "6 characters", "common.cancel counts its text");

  const content = join(dirname(workspace), "content-composer");
  const dePath = join(content, "de.rmf2");
  await mkdir(content);
  await writeFile(join(content, "runic.json"), JSON.stringify({
    schemaVersion: 1,
    catalog: "runic-content-composer-e2e",
    code: { namespace: "Runic.HostedBrowserProof.ContentComposer", className: "Texts" },
    baseLocale: "de", locales: ["de"],
  }, null, 2) + "\n");
  const source = [
    "notes {",
    "  deleted =",
    "    .input {$count :integer}",
    "    .match $count",
    "    one {{Eine Notiz gelöscht}}",
    "    * {{{$count} Notizen gelöscht}}",
    "  title = Notizen",
    "}",
    "",
  ].join("\n");
  await writeFile(dePath, source);
  const openWorkspace = async (directory, project) => {
    await page.getByRole("button", { name: /^Project / }).first().click();
    await page.getByRole("menuitem", { name: "Open workspace", exact: true }).click();
    await page.locator("#dialog-open-directory").fill(directory);
    await page.getByRole("dialog").getByRole("button", { name: "Open workspace", exact: true }).click();
    await page.getByRole("button", { name: `Project ${project}`, exact: true }).waitFor();
  };
  assert.equal(await saveState(), "Saved");
  await openWorkspace(content, "runic-content-composer-e2e");

  const column = page.getByTestId("editor-column");
  await messageRow("notes_deleted").click();
  await column.getByRole("heading", { name: "Edit the text", exact: true }).waitFor();
  await page.getByRole("textbox", { name: /^Text for count: one/ }).first().waitFor();
  await page.getByRole("textbox", { name: /^Text for count: Fallback/ }).first().waitFor();
  assert.equal(await column.getByRole("button", { name: "Add plural forms" }).count(), 0, "A content-editing project offers Add plural forms.");
  assert.equal(await column.getByText("Advanced structure", { exact: true }).count(), 0, "A content-editing project shows the advanced structure.");
  assert.equal(await column.getByRole("button", { name: "Message source", exact: true }).count(), 0, "A content-editing project shows Message source buttons.");
  assert.equal(await column.getByRole("combobox").count(), 0, "A content-editing project shows the structure's selectors.");
  assert.equal(await column.getByText("Exact or custom match", { exact: true }).count(), 0, "A content-editing project adds cases.");
  assert.equal(await column.getByRole("button", { name: /^Remove count:/ }).count(), 0, "A content-editing project removes cases.");
  if (reportDirectory) await page.screenshot({ path: join(reportDirectory, "content-composer-plural.png") });
  // A simple text: no Add plural forms either.
  await messageRow("notes_title").click();
  await page.getByRole("textbox", { name: /^Text for/ }).first().waitFor();
  assert.equal(await column.getByRole("button", { name: "Add plural forms" }).count(), 0);

  // "Show source" in the footer opens the message source; editing it and hiding it again works as before.
  const showSource = page.getByTestId("composer-show-source");
  assert.equal((await showSource.textContent())?.trim(), "Show source");
  await showSource.click();
  await composerSource.waitFor();
  assert.match(await composerSource.inputValue(), /Notizen/);
  await composerSource.fill("Alle Notizen");
  await waitFor(async () => await saveState() === "Unsaved changes: text in one document", "the source edit is unsaved");
  assert.equal((await showSource.textContent())?.trim(), "Hide source");
  await showSource.click();
  await composerSource.waitFor({ state: "detached" });
  await page.keyboard.press("Control+s");
  await waitFor(async () => await saveState() === "Saved", "the edit is saved");
  assert.match(await readFile(dePath, "utf8"), /^ {2}title = Alle Notizen$/m);
  // The File view stays.
  await page.locator('[data-slot="toggle-group-item"]').filter({ hasText: "File" }).click();
  await page.locator("#translation-value").waitFor();
  await page.locator('[data-slot="toggle-group-item"]').filter({ hasText: "Text" }).click();
  await page.getByRole("textbox", { name: /^Text for/ }).first().waitFor();

  await openWorkspace(workspace, "runic-hosted-e2e");
  await page.getByRole("button", { name: /^en / }).first().click();
  await select("common_cancel");
  assert.equal(await saveState(), "Saved");
  assert.deepEqual(pageErrors, [], "The production editor reported uncaught browser errors.");
}
