import assert from "node:assert/strict";
import { readFile, writeFile } from "node:fs/promises";
import { join } from "node:path";

/** @param {import("../context.mjs").HostedContext} context */
export default async function diagnosticSpanAndSharedFix(context) {
  const { page, reportDirectory, waitFor, reloadFiles, handoff } = context;
  const { diskPath } = context.paths;
  const repairedText = handoff.get("repairedText");
  const invalidBytes = "# 🦊 Unicode before the diagnostic\n" + repairedText.replace("cancel = Queued cancel from the real editor", "cancel = ");
  await writeFile(diskPath, invalidBytes);
  await reloadFiles();
  await page.getByRole("button", { name: "Repair de.rmf2", exact: true }).click();
  const diagnosticDialog = page.getByRole("dialog");
  const diagnosticInput = diagnosticDialog.getByRole("textbox", { name: "Malformed source document", exact: true });
  await diagnosticDialog.getByRole("button", { name: /RTR0050.*assignment without a body/i }).click();
  const nameOffset = invalidBytes.indexOf("cancel = ");
  // The compiler points at the complete incomplete assignment, including its
  // equals sign and trailing space. UTF-16 selection must retain that span.
  await waitFor(async () => (await diagnosticInput.evaluate(input => [input.selectionStart, input.selectionEnd])).join(":") === `${nameOffset}:${nameOffset + "cancel = ".length}`, "physical Unicode diagnostic span selection");
  await diagnosticDialog.getByRole("button", { name: "Make message explicitly empty", exact: true }).click();
  await waitFor(async () => (await diagnosticInput.inputValue()).includes("cancel = {{}}"), "compiler-owned explicit-empty quick fix");
  await diagnosticDialog.getByRole("button", { name: "Validate and save", exact: true }).click();
  await diagnosticDialog.waitFor({ state: "hidden" });
  assert.ok((await readFile(diskPath, "utf8")).includes("cancel = {{}}"));
  if (reportDirectory) await page.screenshot({ path: join(reportDirectory, "diagnostic-fixed-en.png"), fullPage: true });
}
