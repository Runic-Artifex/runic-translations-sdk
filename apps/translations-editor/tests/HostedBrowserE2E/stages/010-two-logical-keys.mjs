import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";

/** @param {import("../context.mjs").HostedContext} context */
export default async function twoLogicalKeys(context) {
  const { page, waitFor, select, editor } = context;
  const { diskPath } = context.paths;
  const { preservedComment } = context.fixture;
  await select("common_cancel");
  await page.evaluate(() => {
    // Records again from here, so the checks below see only the queued edits and their save.
    window.__runicEditorDocumentCalls = [];
  });
  await editor().fill("Queued cancel from the real editor");
  await select("common_save");
  await editor().fill("Queued save from the real editor");
  await page.keyboard.press("Control+s");
  await waitFor(async () => (await readFile(diskPath, "utf8")).includes("Queued save from the real editor"), "save of both logical keys");
  const savedBytes = await readFile(diskPath, "utf8");
  assert.ok(savedBytes.includes("Queued cancel from the real editor"));
  assert.ok(savedBytes.startsWith(preservedComment), "Unchanged RMF2 comments must survive authoring.");
  // The document's operations run on its own routed View, not on the root View that opened it.
  const documentOperation = member => call => call.kind === "start" && call.member === member && typeof call.route === "string" && call.route.length > 0;
  await waitFor(async () => (await page.evaluate(() => window.__runicEditorDocumentCalls))
    .some(documentOperation("save")), "routed document save completion");
  const routedCalls = await page.evaluate(() => window.__runicEditorDocumentCalls);
  const saveRoute = routedCalls.find(documentOperation("save")).route;
  // `open` presents the routed View and replies with its state; `snapshot` reads it again.
  assert.ok(routedCalls.some(call => (call.kind === "open" || call.kind === "snapshot") && call.route === saveRoute), "Opening a document must read its routed ViewModel.");
  assert.ok(routedCalls.some(call => documentOperation("validate")(call) && call.route === saveRoute), "Validation must use the routed document ViewModel.");
  await page.reload({ waitUntil: "domcontentloaded" });
  await select("common_cancel");
  assert.equal(await editor().inputValue(), "Queued cancel from the real editor");
}
