import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import { join } from "node:path";

// A missing translation (#225 P2) opens in the visual composer on an empty starter shaped for the target locale
// (French: one, many and the fallback), under the source text, with nothing pre-filled and nothing written; its
// length limit (P21) shows at the field.
/** @param {import("../context.mjs").HostedContext} context */
export default async function missingTranslationStarter(context) {
  const { page, reportDirectory, waitFor, select, saveState, variantBox, composerSource } = context;
  const { frPath } = context.paths;
  const frSaved = await readFile(frPath, "utf8");
  const characterCount = async () => (await page.getByTestId("character-count").textContent())?.trim();
  await page.getByRole("button", { name: /^fr / }).first().click();
  await select("files_deleted");
  await variantBox("Fallback").waitFor();
  await page.getByRole("region", { name: "Source text (German)", exact: true }).getByText("Eine Datei gelöscht", { exact: true }).waitFor();
  assert.equal(await page.getByText("Visual editing is unavailable for this message.", { exact: false }).count(), 0,
    "A missing translation opens in the visual composer.");
  for (const title of ["one", "many", "Fallback"]) assert.equal(await variantBox(title).inputValue(), "", `The ${title} variant is not empty.`);
  assert.equal(await page.getByRole("textbox", { name: /^Translation for count: (few|other|two)/ }).count(), 0, "The starter has categories French does not use.");
  assert.equal((await page.getByTestId("workflow-state").textContent())?.trim(), "Not translated");
  assert.equal(await characterCount(), "0 / 24");
  assert.deepEqual((await page.getByTestId("variant-character-count").allTextContents()).map(item => item.trim()), ["0 / 24", "0 / 24", "0 / 24"]);
  // The composer's MF2 source of the starter is the starter, not the base text.
  await page.getByRole("button", { name: "Message source", exact: true }).first().click();
  assert.equal(await composerSource.inputValue(), ".input {$count :integer}\n.match $count\none {{}}\nmany {{}}\n* {{}}");
  await page.getByRole("button", { name: "Message source", exact: true }).first().click();
  await variantBox("one").waitFor();
  assert.equal(await saveState(), "Saved", "An empty starter is no unsaved change.");
  assert.equal(await readFile(frPath, "utf8"), frSaved);

  // Typing makes it a normal unsaved draft; the counter follows the longest variant and warns over the limit.
  await variantBox("one").fill("Un fichier supprimé");
  await waitFor(async () => await saveState() === "Unsaved changes: text in one document", "a starter with text is unsaved");
  await waitFor(async () => await characterCount() === "19 / 24", "counter of the typed variant");
  await variantBox("many").fill("Beaucoup de fichiers supprimés");
  await waitFor(async () => await characterCount() === "30 / 24", "counter over the limit");
  assert.equal(await page.getByTestId("character-count").getAttribute("data-over-limit"), "true");
  await page.getByTestId("length-limit").waitFor();
  assert.match(await page.getByTestId("length-limit").textContent(), /24/);
  await variantBox("many").fill("De nombreux fichiers");
  await variantBox("Fallback").fill("Des fichiers supprimés");
  await waitFor(async () => await characterCount() === "22 / 24", "counter back under the limit");
  assert.equal(await page.getByTestId("length-limit").count(), 0);
  assert.deepEqual((await page.getByTestId("variant-character-count").allTextContents()).map(item => item.trim()), ["19 / 24", "20 / 24", "22 / 24"]);
  if (reportDirectory) await page.screenshot({ path: join(reportDirectory, "missing-translation-fr.png"), fullPage: true });
  // Ctrl+S writes the message into its existing group (P7).
  await page.keyboard.press("Control+s");
  await waitFor(async () => (await readFile(frPath, "utf8")).includes("deleted ="), "saved starter in fr.rmf2");
  await waitFor(async () => await saveState() === "Saved", "nothing unsaved after saving the starter");
  const frWritten = await readFile(frPath, "utf8");
  assert.equal((frWritten.match(/^files \{$/gm) ?? []).length, 1, "The new message opened a second files group.");
  const filesGroup = frWritten.slice(frWritten.indexOf("files {\n"));
  assert.ok(filesGroup.indexOf("selected =") < filesGroup.indexOf("deleted =") && filesGroup.indexOf("deleted =") < filesGroup.lastIndexOf("\n}\n"),
    "The message is written inside its group, after its sibling.");
  for (const line of [".match $count", "one {{Un fichier supprimé}}", "many {{De nombreux fichiers}}", "* {{Des fichiers supprimés}}"])
    assert.ok(filesGroup.includes(line), `fr.rmf2 lacks ${line}`);
  assert.ok(frWritten.startsWith(frSaved.slice(0, frSaved.indexOf("files {\n"))), "The rest of fr.rmf2 changed.");
  assert.notEqual((await page.getByTestId("workflow-state").textContent())?.trim(), "Not translated");
}
