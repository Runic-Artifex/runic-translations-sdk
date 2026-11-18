import assert from "node:assert/strict";
import { mkdir, readFile, writeFile } from "node:fs/promises";
import { dirname, join } from "node:path";

// D074.2: a project with one language is a content-editing project. A German-only workspace beside the fixture
// (runic.json declares de and the pseudo-locale en-XA, and a review file is kept from an earlier translation project)
// opens with the content wording and without the translation parts: no coverage, no Missing filter or Next missing,
// no source panel or Copy source, no review workflow. A text is edited and saved under its length limit, Save and next
// goes to the next text in tree order, and the review file stays untouched. Adding English switches the editor to
// translation mode without a restart. Then the fixture workspace is opened again: English, common.cancel selected,
// nothing unsaved, as 340 left it.
/** @param {import("../context.mjs").HostedContext} context */
export default async function oneLanguageProject(context) {
  const { page, workspace, reportDirectory, pageErrors, waitFor, editor, saveState, messageRow } = context;
  const content = join(dirname(workspace), "one-language");
  const dePath = join(content, "de.rmf2");
  const reviewPath = join(content, "runic-content-e2e.review.json");
  await mkdir(content);
  await writeFile(join(content, "runic.json"), JSON.stringify({
    schemaVersion: 1,
    catalog: "runic-content-e2e",
    code: { namespace: "Runic.HostedBrowserProof.Content", className: "Texts" },
    baseLocale: "de", locales: ["de"], pseudoLocales: ["en-XA"],
  }, null, 2) + "\n");
  await writeFile(dePath, [
    "notes {",
    "  empty = Dieser Ordner enthält noch keine Notizen.",
    "  deleted =",
    "    .input {$count :integer}",
    "    .match $count",
    "    one {{Eine Notiz gelöscht}}",
    "    * {{{$count} Notizen gelöscht}}",
    "}",
    "settings {",
    "  # Label next to the language picker. The picker leaves little room.",
    "  @maxLength 24",
    "  language = Sprache der Anzeige",
    "  title = Einstellungen",
    "}",
    "",
  ].join("\n"));
  // A review file the project kept from the time it was translated: hidden in a content-editing project, never changed.
  const review = JSON.stringify({
    $schema: "runic.translations.interchange-review/2", catalog: "runic-content-e2e",
    entries: [{ key: "settings_title", locale: "de", state: "needs-review" }],
  }, null, 2) + "\n";
  await writeFile(reviewPath, review);

  const shot = async name => {
    if (!reportDirectory) return;
    // The preview of the current text, not the compiling notice, and the text field focused as someone editing it.
    await waitFor(async () => await page.getByText("Compiling the current draft…").count() === 0, "the preview of the current text");
    await editor().focus();
    await page.waitForTimeout(300);
    await page.screenshot({ path: join(reportDirectory, `one-language-${name}.png`) });
  };
  const openWorkspace = async (directory, project) => {
    await page.getByRole("button", { name: /^Project / }).first().click();
    await page.getByRole("menuitem", { name: "Open workspace", exact: true }).click();
    await page.locator("#dialog-open-directory").fill(directory);
    await page.getByRole("dialog").getByRole("button", { name: "Open workspace", exact: true }).click();
    await page.getByRole("button", { name: `Project ${project}`, exact: true }).waitFor();
  };
  const selected = async () => page.locator('[role="treeitem"][data-message-key][aria-selected="true"]').getAttribute("data-message-key");
  const settingsMenu = async () => {
    await page.getByRole("button", { name: /^Editor settings, / }).click();
    return page.getByRole("menu");
  };
  const sidebar = page;

  assert.equal(await saveState(), "Saved");
  await openWorkspace(content, "runic-content-e2e");

  // The project header names the language and the number of texts; the language list is one label with the add action
  // and the pseudo-locale, without coverage or marks.
  await waitFor(async () => (await page.getByTestId("project-summary").textContent())?.trim() === "German · 4 texts", "the content project summary");
  const language = page.getByTestId("project-language");
  assert.equal((await language.getByTestId("project-language-name").textContent())?.trim(), "German");
  await language.getByRole("button", { name: "Add a language", exact: true }).waitFor();
  await language.getByRole("button", { name: /^en-XA, / }).waitFor();
  assert.equal(await page.getByTestId("locale-coverage").count(), 0, "A content-editing project shows coverage.");
  assert.equal(await page.getByRole("button", { name: "Manage languages" }).count(), 0);
  assert.equal(await page.getByRole("region", { name: "Locale coverage" }).count(), 0);

  // The list: "Texts", searched as texts, no state marks, no bulk review actions, no translation filters.
  await sidebar.getByRole("navigation", { name: "Texts", exact: true }).waitFor();
  assert.equal(await page.locator("#message-search").getAttribute("placeholder"), "Search texts…");
  assert.equal(await page.getByLabel("Search texts", { exact: true }).count(), 1);
  assert.equal(await page.getByRole("tree").locator("[data-state-mark]").count(), 0, "The text list shows translation states.");
  assert.equal(await page.getByRole("button", { name: "Message bulk actions" }).count(), 0);
  await page.getByRole("button", { name: /^Text filters: / }).click();
  const filters = (await page.getByRole("menuitemradio").allTextContents()).map(text => text.replace(/\d+/g, "").trim());
  assert.deepEqual(filters, ["All", "Variants & formatting", "Quality"], "A content-editing project offers translation filters.");
  await page.keyboard.press("Escape");

  // The toolbar has no Next missing; the editor no workflow, source panel or Copy source; the developers' note is
  // labelled as such; the composer edits "the text".
  await messageRow("settings_language").click();
  await page.locator('textarea[spellcheck="true"]').first().waitFor();
  assert.equal(await page.getByTestId("next-message").count(), 0, "A content-editing project shows Next missing.");
  assert.equal(await page.getByRole("button", { name: /^Workflow/ }).count(), 0, "A content-editing project shows the review workflow.");
  assert.equal(await page.getByTestId("workflow-stale").count(), 0);
  assert.equal(await page.getByTestId("source-text").count(), 0, "A content-editing project shows a source panel.");
  assert.equal(await page.getByRole("button", { name: "Copy source" }).count(), 0);
  await page.getByRole("heading", { name: "Edit the text", exact: true }).waitFor();
  assert.equal(await page.getByText("Translate the message").count(), 0);
  const note = page.getByRole("region", { name: "Note from the developers", exact: true });
  assert.match(await note.innerText(), /Label next to the language picker\. The picker leaves little room\./);
  assert.deepEqual((await page.locator('[data-slot="toggle-group-item"]').allTextContents()).map(text => text.trim()), ["Text", "File"]);

  // The length counter, over the limit and back under it; one save writes the text and leaves the review file alone.
  const counter = page.getByTestId("character-count");
  assert.equal((await counter.textContent())?.trim(), "19 / 24");
  await editor().fill("Sprache der Benutzeroberfläche");
  await waitFor(async () => await counter.getAttribute("data-over-limit") === "true", "the counter shows the text over its limit");
  await page.getByTestId("length-limit").waitFor();
  await shot("length-limit");
  await editor().fill("Anzeigesprache");
  await waitFor(async () => (await counter.textContent())?.trim() === "14 / 24" && await counter.getAttribute("data-over-limit") === "false", "the text fits again");
  await waitFor(async () => await saveState() === "Unsaved changes: text in one document", "the edited text is unsaved");
  await shot("workbench-dark");
  await page.keyboard.press("Control+s");
  await waitFor(async () => await saveState() === "Saved", "the text is saved");
  assert.match(await readFile(dePath, "utf8"), /^ {2}language = Anzeigesprache$/m, "de.rmf2 lacks the edited text.");
  assert.equal(await readFile(reviewPath, "utf8"), review, "Saving a text changed the review file.");

  // Save and next goes to the next text in tree order (settings.language → settings.title); Alt+↓ is not bound.
  const saveAndNext = page.getByTestId("save-and-next");
  assert.equal((await saveAndNext.textContent())?.replace(/\s+/g, " ").trim(), "Save and next text Ctrl+Enter");
  await editor().focus();
  await page.keyboard.press("Alt+ArrowDown");
  await page.waitForTimeout(200);
  assert.equal(await selected(), "settings_language", "Alt+↓ moved in a content-editing project.");
  await editor().fill("Sprache");
  await waitFor(async () => await saveState() === "Unsaved changes: text in one document", "the second edit is unsaved");
  await page.keyboard.press("Control+Enter");
  await waitFor(async () => await selected() === "settings_title", "Save and next selects the next text");
  await waitFor(async () => await saveState() === "Saved", "Save and next saved");
  assert.match(await readFile(dePath, "utf8"), /^ {2}language = Sprache$/m);
  assert.equal(await readFile(reviewPath, "utf8"), review, "Save and next changed the review file.");
  // The review file's needs-review entry of settings.title does not show.
  assert.equal(await messageRow("settings_title").getAttribute("aria-label"), "title: Einstellungen");

  // The palette has no Next missing and no review commands.
  await page.keyboard.press("Control+k");
  const search = page.getByRole("textbox", { name: "Search commands", exact: true });
  await search.fill("next");
  const options = page.getByRole("dialog").getByRole("option");
  await options.first().waitFor();
  const titles = (await options.allTextContents()).map(text => text.replace(/\s+/g, " ").trim());
  assert.ok(titles.some(title => title.startsWith("Save and next text")), `The palette lacks Save and next text: ${titles.join(" | ")}`);
  assert.ok(!titles.some(title => /missing|outdated|to review/i.test(title)), `The palette offers translation steps: ${titles.join(" | ")}`);
  await search.fill("approved");
  await page.waitForTimeout(200);
  assert.equal(await options.count(), 0, "The palette offers review commands.");
  await page.keyboard.press("Escape");

  // The German interface uses the content wording too.
  await (await settingsMenu()).getByRole("menuitemradio", { name: "Deutsch", exact: true }).click();
  await page.getByRole("heading", { name: "Text bearbeiten", exact: true }).waitFor();
  await sidebar.getByRole("navigation", { name: "Texte", exact: true }).waitFor();
  assert.equal(await page.locator("#message-search").getAttribute("placeholder"), "Texte suchen…");
  await page.getByTestId("project-language").getByRole("button", { name: "Sprache hinzufügen", exact: true }).waitFor();
  await messageRow("settings_language").click();
  await page.getByRole("region", { name: "Hinweis vom Entwicklungsteam", exact: true }).waitFor();
  await page.getByRole("button", { name: /^Editoreinstellungen, / }).click();
  await page.getByRole("menu").getByRole("menuitemradio", { name: "English", exact: true }).click();
  await page.getByRole("heading", { name: "Edit the text", exact: true }).waitFor();

  // The screenshots: the workbench in light (dark above), the language list with its add action, compact density.
  const theme = await page.evaluate(() => document.documentElement.dataset.scheme);
  await (await settingsMenu()).getByRole("menuitemradio", { name: "Light", exact: true }).click();
  await waitFor(async () => (await page.evaluate(() => document.documentElement.dataset.scheme)) === "light", "the light scheme");
  await page.keyboard.press("Escape");
  await shot("workbench-light");
  if (reportDirectory) {
    await page.waitForTimeout(200);
    await language.screenshot({ path: join(reportDirectory, "one-language-language-list.png") });
    await page.screenshot({ path: join(reportDirectory, "one-language-language-list-context.png"), clip: { x: 0, y: 0, width: 420, height: 360 } });
  }
  await (await settingsMenu()).getByRole("menuitemradio", { name: "Compact", exact: true }).click();
  await waitFor(async () => (await page.evaluate(() => document.documentElement.dataset.density)) === "compact", "compact density");
  await page.keyboard.press("Escape");
  await shot("compact");
  await (await settingsMenu()).getByRole("menuitemradio", { name: "Comfortable", exact: true }).click();
  await waitFor(async () => (await page.evaluate(() => document.documentElement.dataset.density)) === "comfortable", "comfortable density");
  if (theme === "dark") {
    await (await settingsMenu()).getByRole("menuitemradio", { name: "Dark", exact: true }).click();
    await waitFor(async () => (await page.evaluate(() => document.documentElement.dataset.scheme)) === "dark", "the dark scheme again");
  }
  await page.keyboard.press("Escape");

  // Adding English, in the language list's add action, switches to translation mode without a restart.
  await language.getByRole("button", { name: "Add a language", exact: true }).click();
  await page.locator("#mutation-locale").fill("en");
  await page.getByRole("button", { name: "Preview change", exact: true }).click();
  await page.getByRole("checkbox", { name: /^I understand this workspace change cannot be undone/ }).click();
  await page.getByRole("button", { name: "Commit change", exact: true }).click();
  await waitFor(async () => (await page.getByTestId("project-summary").textContent())?.trim() === "2 locales", "the translation project summary");
  assert.equal(await page.getByTestId("project-language").count(), 0);
  await page.getByRole("button", { name: "Manage languages", exact: true }).waitFor();
  assert.equal(await page.getByTestId("locale-coverage").count(), 2, "The translation project shows no coverage.");
  await page.getByTestId("next-message").waitFor();
  await sidebar.getByRole("navigation", { name: "Messages", exact: true }).waitFor();
  assert.equal(await page.locator("#message-search").getAttribute("placeholder"), "Search messages…");
  await page.getByRole("button", { name: /^en / }).first().click();
  await messageRow("settings_title").click();
  await page.getByRole("heading", { name: "Translate the message", exact: true }).waitFor();
  await page.getByTestId("source-text").waitFor();
  await page.getByRole("button", { name: /^Workflow/ }).first().waitFor();
  assert.ok(JSON.parse(await readFile(join(content, "runic.json"), "utf8")).locales.includes("en"), "runic.json lacks the added language.");
  assert.equal(await readFile(reviewPath, "utf8").then(text => JSON.parse(text).entries.some(entry => entry.key === "settings_title" && entry.locale === "de")), true,
    "Adding a language dropped the kept review entry.");

  // Back to the fixture workspace as 340 left it.
  await openWorkspace(workspace, "runic-hosted-e2e");
  await page.getByRole("button", { name: /^en / }).first().click();
  await messageRow("common_cancel").click();
  await page.locator('textarea[spellcheck="true"]').first().waitFor();
  assert.equal(await saveState(), "Saved");
  assert.deepEqual(pageErrors, [], "The production editor reported uncaught browser errors.");
}
