import { readFile, writeFile } from "node:fs/promises";
import { join } from "node:path";

/** @param {import("../context.mjs").HostedContext} context */
export default async function malformedRmf2Repair(context) {
  const { page, reportDirectory, waitFor, reloadFiles, handoff } = context;
  const { diskPath } = context.paths;
  const externalBytes = handoff.get("externalBytes");
  await writeFile(diskPath, "common {\n  cancel = \"unterminated\n");
  await reloadFiles();
  await page.getByRole("button", { name: "Repair de.rmf2", exact: true }).click();
  const repair = page.getByRole("dialog");
  const repairInput = repair.getByRole("textbox", { name: "Malformed source document", exact: true });
  await repairInput.fill(externalBytes);
  const repairedText = await repairInput.inputValue();
  handoff.set("repairedText", repairedText);
  await repair.getByRole("button", { name: "Validate and save", exact: true }).click();
  await waitFor(async () => (await readFile(diskPath, "utf8")) === repairedText, "repaired source save");
  await repair.waitFor({ state: "hidden" });
  if (reportDirectory) await page.screenshot({ path: join(reportDirectory, "repaired-en.png"), fullPage: true });
}
