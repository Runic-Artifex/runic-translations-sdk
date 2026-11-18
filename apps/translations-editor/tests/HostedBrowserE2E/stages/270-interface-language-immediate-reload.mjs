import assert from "node:assert/strict";

// A reload straight after a switch, as a user pressing F5 would. The reload ends the Bridge connection while the
// native save is in flight: that must not surface as an uncaught `BridgeError: The Bridge is disconnected.`, and the
// switch must survive the reload. The editor guarantees the latter only for a save whose request has left the page
// (#147): one still waiting in the page's Bridge queue, or for the routed localState View to open, is lost with the
// page. So each reload waits until the page has sent the save carrying the new language, but not for its reply.
/** @param {import("../context.mjs").HostedContext} context */
export default async function interfaceLanguageImmediateReload(context) {
  const { page, uiLocaleSent, pageErrors } = context;
  const settingsButton = /^(Editor settings|Editoreinstellungen), /;
  for (const [name, lang, summary] of [
    ["English", "en", /^Editor settings, .*interface language English$/],
    ["Deutsch", "de", /^Editoreinstellungen, .*Oberflächensprache Deutsch$/],
    ["English", "en", /^Editor settings, .*interface language English$/],
  ]) {
    const sent = uiLocaleSent(lang, `the native save of ${name} sent to the host`);
    await page.getByRole("button", { name: settingsButton }).click();
    await page.getByRole("menuitemradio", { name, exact: true }).click();
    await sent;
    await page.reload({ waitUntil: "domcontentloaded" });
    await page.getByRole("button", { name: summary }).waitFor();
    assert.equal(await page.evaluate(() => document.documentElement.lang), lang, `The switch to ${name} did not survive an immediate reload.`);
  }
  assert.deepEqual(pageErrors, [], "The production editor reported uncaught browser errors after an immediate reload.");
}
