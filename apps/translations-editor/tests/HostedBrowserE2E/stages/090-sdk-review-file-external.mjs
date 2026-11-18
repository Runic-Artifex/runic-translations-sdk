import { readFile, writeFile } from "node:fs/promises";

// Another writer (runic-translations review mark, the language server) changes the review file: the editor's
// external-change check reloads the review data without a workspace reload.
/** @param {import("../context.mjs").HostedContext} context */
export default async function sdkReviewFileExternal(context) {
  const { page, waitFor, saveState } = context;
  const { reviewPath } = context.paths;
  const external = JSON.parse(await readFile(reviewPath, "utf8"));
  external.entries = external.entries.map(item => item.key === "common_save" && item.locale === "en" ? { ...item, note: "Changed by the CLI" } : item);
  await writeFile(reviewPath, JSON.stringify(external, null, 2) + "\n");
  await page.getByText("The review file changed on disk; the review data was reloaded.").first().waitFor({ timeout: 15_000 });
  await waitFor(async () => (await page.locator("#review-note").inputValue()) === "Changed by the CLI", "external review note shown in the workflow");
  await waitFor(async () => await saveState() === "Saved", "saved review data after the external change");
}
