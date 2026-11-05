import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import ts from "typescript";

const sourceUrl = new URL("../src/lib/ui-locale.ts", import.meta.url);
const transpiled = ts.transpileModule(await readFile(sourceUrl, "utf8"), {
  compilerOptions: { module: ts.ModuleKind.ESNext, target: ts.ScriptTarget.ES2022 },
  fileName: sourceUrl.pathname,
});
const uiLocale = await import(`data:text/javascript;base64,${Buffer.from(transpiled.outputText).toString("base64")}`);
const { createWindowLocaleSync, matchUiLocale, negotiateUiLocale, uiLocaleName, uiLocaleStateKey } = uiLocale;

const available = ["en", "de"];
// The first preference the catalog provides wins, after truncating each preference subtag by subtag.
assert.equal(negotiateUiLocale(["de-CH", "en-US"], available, "en"), "de");
assert.equal(negotiateUiLocale(["fr-FR", "de-AT", "en"], available, "en"), "de");
assert.equal(negotiateUiLocale(["en-GB", "de"], available, "en"), "en");
assert.equal(negotiateUiLocale(["DE_de"], available, "en"), "de", "POSIX separators and case must not hide a match.");
assert.equal(negotiateUiLocale(["de-u-co-phonebk"], available, "en"), "de", "Extensions must truncate to the language.");
assert.equal(negotiateUiLocale(["x-private", "de"], available, "en"), "de", "A private-use tag must not match.");
// Nothing the catalog provides: the base locale, as the catalog spells it.
assert.equal(negotiateUiLocale(["fr", "ja-JP"], available, "en"), "en");
assert.equal(negotiateUiLocale([], available, "EN"), "en");
assert.equal(negotiateUiLocale([], ["de"], "en"), "de", "A base locale outside the list must fall back to a listed locale.");
// Regional catalog locales match their exact tag, and a regional preference reaches the language.
assert.equal(negotiateUiLocale(["pt-BR"], ["en", "pt", "pt-BR"], "en"), "pt-BR");
assert.equal(negotiateUiLocale(["pt-PT"], ["en", "pt", "pt-BR"], "en"), "pt");

assert.equal(matchUiLocale("DE", available), "de");
assert.equal(matchUiLocale("fr", available), undefined);
assert.equal(matchUiLocale(null, available), undefined);

assert.equal(uiLocaleName("de"), "Deutsch", "Picker options name each language in itself.");
assert.equal(uiLocaleName("en"), "English");
assert.equal(uiLocaleName("not a tag"), "not a tag");

// The window's locale follows the page's choice across a failed request and a reconnect.
{
  const settle = () => new Promise(resolve => setTimeout(resolve, 0));
  let windowValue = "en";
  let online = false;
  const sent = [];
  const sync = createWindowLocaleSync({
    current: () => windowValue,
    send: async locale => {
      sent.push(locale);
      if (!online) throw new Error("disconnected");
      windowValue = locale;
    },
  });
  sync.request("en");
  assert.deepEqual(sent, [], "A locale the window already has was sent again.");
  // A switch while the host is unreachable is kept, and the page keeps its own locale over the host's old one.
  sync.request("de");
  await settle();
  assert.deepEqual(sent, ["de"]);
  assert.equal(sync.windowChanged("en"), undefined, "The window's old locale replaced a switch it has not received.");
  online = true;
  sync.connected();
  await settle();
  assert.deepEqual(sent, ["de", "de"], "The reconnect did not send the switch again.");
  assert.equal(windowValue, "de");
  sync.connected();
  await settle();
  assert.equal(sent.length, 2, "A reconnect sent a locale the window already has.");
  // Once the window has the page's locale, a change by the application or another page is shown.
  assert.equal(sync.windowChanged("de"), "de");
  assert.equal(sync.windowChanged("en"), "en");
  assert.equal(sync.windowChanged(null), undefined);
}

// The choice is stored in the native editor state, which accepts only listed keys.
const [localState, nativeStore] = await Promise.all([
  readFile(new URL("../src/lib/local-state.ts", import.meta.url), "utf8"),
  readFile(new URL("../../EditorLocalStateStore.cs", import.meta.url), "utf8"),
]);
assert.ok(localState.includes(`"${uiLocaleStateKey}"`), "The page's local state does not accept the interface locale key.");
assert.ok(nativeStore.includes(`"${uiLocaleStateKey}"`), "The native local state does not accept the interface locale key.");

// The page lists the catalog's locales and sets the window's locale instead of hard-coding EN and DE.
const page = await readFile(new URL("../src/routes/+page.svelte", import.meta.url), "utf8");
assert.doesNotMatch(page, /<option value="(?:en|de)">/, "The interface language picker hard-codes its locales.");
assert.match(page, /\{#each uiLocales as locale/, "The interface language picker does not list the catalog's locales.");
assert.match(page, /createWindowLocaleSync\(\{ current: windowLocale, send: \(locale\) => bridge\.setUiLocale\(locale\) \}\)/,
  "The interface language does not set the window's locale.");
assert.match(page, /onWindowLocale\(\(locale\) => \{\s*const match = matchUiLocale\(windowLocaleSync\.windowChanged\(locale\)/,
  "The page does not follow the window's locale.");
assert.match(page, /onBridgeConnection\(\(state\) => \{\s*if \(state !== "connected"\) return;\s*windowLocaleSync\.connected\(\);/,
  "The page does not send its locale again after a reconnect.");
assert.match(page, /onBridgeConnection\(\(state\) => \{\s*if \(state !== "connected"\) return;[^}]*resumeLocalEditorState\(\);/,
  "The page does not write an interrupted local-state change again after a reconnect.");
assert.match(page, /\} finally \{[^}]*applyUiLocale\(matchUiLocale\(getLocalEditorState\(uiLocaleStateKey\)/,
  "A failed state load skips setting the window's locale.");

console.log("PASS: the interface locale is negotiated from the OS preferences against the editor catalog, stored natively, and set as the window's locale, also after a failed load or a reconnect.");
