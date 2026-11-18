// The context every stage of the hosted acceptance receives: the page, the fixture's paths, what the page sent and
// reported, the shared helpers, and the values one stage hands to a later one.
import { join } from "node:path";

export const fixtureMarker = "runic-hosted-rmf2-ui-v1";
export const preservedComment = "# Runic hosted browser fixture: preserve this comment exactly.\n";

/**
 * @typedef {object} Observations
 * @property {string[]} pageErrors Uncaught browser errors.
 * @property {string[]} consoleErrors Browser console errors.
 * @property {(string | null)[]} sentUiLocales The interface language each `saveLocalState` request carried.
 * @property {{ key: string, value: string }[][]} sentLocalState Every local-state record each `saveLocalState`
 *   request carried, newest last (key and value).
 * @property {(lang: string, label: string) => Promise<void>} uiLocaleSent Resolves as soon as the page sends a
 *   `saveLocalState` request carrying `lang`, so the caller can act before its reply.
 * @property {(page: import("playwright-core").Page) => void} observe Records the page's errors and requests.
 */

/** @returns {Observations} */
export function createObservations() {
  const pageErrors = [];
  const consoleErrors = [];
  const sentUiLocales = [];
  const sentLocalState = [];
  let onUiLocaleSent = () => {};
  const uiLocaleSent = (lang, label) => {
    const sent = new Promise((resolveSent, rejectSent) => {
      const timer = setTimeout(() => rejectSent(new Error(`Timed out: ${label}; sent: ${JSON.stringify(sentUiLocales)}`)), 20_000);
      onUiLocaleSent = value => { if (value === lang) { clearTimeout(timer); onUiLocaleSent = () => {}; resolveSent(); } };
    });
    sent.catch(() => {});
    return sent;
  };
  const observe = page => {
    page.on("pageerror", error => pageErrors.push(error.stack ?? error.message));
    page.on("console", message => { if (message.type() === "error") { consoleErrors.push(message.text()); console.error(message.text()); } });
    page.on("dialog", dialog => void dialog.accept());
    // The interface language each `saveLocalState` request carried when the page sent it on the Bridge's WebSocket
    // (CS-WebUI frames: a short binary header, then the Views request JSON).
    page.on("websocket", socket => socket.on("framesent", frame => {
      const text = String(frame.payload);
      if (!text.includes('"member":"saveLocalState"')) return;
      try {
        // The header's length bytes can include "{", so the JSON starts at the request's first member.
        const request = JSON.parse(text.slice(text.indexOf('{"kind":'), text.lastIndexOf("}") + 1));
        const entries = JSON.parse(request.input).argument;
        sentLocalState.push(entries);
        const value = entries.find(entry => entry.key === "runic-translations.ui-locale")?.value ?? null;
        sentUiLocales.push(value);
        onUiLocaleSent(value);
      } catch (error) {
        sentUiLocales.push(`unreadable: ${error}`);
      }
    }));
  };
  return { pageErrors, consoleErrors, sentUiLocales, sentLocalState, uiLocaleSent, observe };
}

export const waitFor = async (predicate, label) => {
  const deadline = Date.now() + 20_000;
  while (Date.now() < deadline) {
    if (await predicate()) return;
    await new Promise(resolveWait => setTimeout(resolveWait, 30));
  }
  throw new Error(`Timed out: ${label}`);
};

/** Values a stage hands to a later one, by name. Reading a value no earlier stage set fails the reading stage. */
function createHandoff() {
  const values = new Map();
  return {
    set(name, value) {
      if (values.has(name)) throw new Error(`Stage handoff ${name} was already set by an earlier stage.`);
      values.set(name, value);
    },
    get(name) {
      if (!values.has(name)) throw new Error(`Stage handoff ${name} was not set by an earlier stage.`);
      return values.get(name);
    },
  };
}

/**
 * @param {object} options
 * @param {import("playwright-core").Page} options.page
 * @param {string} options.workspace The prepared workspace.
 * @param {string | undefined} options.reportDirectory Where screenshots go; unset outside the runner.
 * @param {Observations} options.observations
 */
export function createContext({ page, workspace, reportDirectory, observations }) {
  // A message row of the namespace tree, by its flattened key (#225 M1), and a group row, by its dotted path.
  const messageRow = key => page.locator(`[role="treeitem"][data-message-key="${key}"]`);
  const groupRow = id => page.locator(`[role="treeitem"][data-group="${id}"]`);
  const focusedRow = () => page.evaluate(() => document.activeElement?.getAttribute("data-row-id") ?? null);
  const select = async key => {
    await page.evaluate(() => {
      const bridge = window.__runicBridge;
      if (bridge.__authoringObserved) return;
      bridge.__authoringObserved = true;
      // Views protocol 4 (Runic SDK 0.16) sends each request as one JSON message through `request`, such as
      // {"kind":"start","route":…,"member":"save"} for an operation and {"kind":"open","route":…} for a routed View.
      const request = bridge.request.bind(bridge);
      window.__runicEditorDocumentCalls = [];
      bridge.request = async (text, ...rest) => {
        let message;
        try { message = JSON.parse(text); } catch { message = {}; }
        const call = { kind: message.kind, route: message.route, member: message.member };
        try {
          const response = await request(text, ...rest);
          window.__runicEditorDocumentCalls.push(call);
          return response;
        } catch (error) {
          window.__runicEditorDocumentCalls.push({ ...call, error: String(error) });
          throw error;
        }
      };
    });
    await messageRow(key).click();
    await page.locator('textarea[spellcheck="true"]').first().waitFor();
  };
  const editor = () => page.locator('textarea[spellcheck="true"]').first();
  // The toolbar's one save state: "Saved", or "Unsaved changes: …" naming what is unsaved.
  const saveState = async () => (await page.getByTestId("save-state").textContent())?.trim();
  const reloadFiles = async () => {
    await page.getByRole("button", { name: "Project runic-hosted-e2e", exact: true }).click();
    await page.getByRole("menuitem", { name: "Reload", exact: true }).click();
  };
  return {
    page,
    workspace,
    reportDirectory,
    paths: {
      diskPath: join(workspace, "de.rmf2"),
      enPath: join(workspace, "en.rmf2"),
      frPath: join(workspace, "fr.rmf2"),
      reviewPath: join(workspace, "runic-hosted-e2e.review.json"),
    },
    fixture: { preservedComment, staleFingerprint: "sha256:" + "0".repeat(64) },
    ...observations,
    waitFor,
    select,
    editor,
    saveState,
    reloadFiles,
    messageRow,
    groupRow,
    focusedRow,
    // The editor of a plural message's `one` variant ("Text for …" in the base locale, whose text is the source text),
    // of a starter's variant by its title, and the composer's MF2 source.
    branchEditor: () => page.getByRole("textbox", { name: /(Translation|Text) for count: one/ }).first(),
    variantBox: title => page.getByRole("textbox", { name: new RegExp(`^Translation for count: ${title}`) }).first(),
    composerSource: page.getByRole("textbox", { name: "Message source", exact: true }),
    undoButton: page.getByRole("button", { name: /^Undo(:| saved change)/ }),
    handoff: createHandoff(),
  };
}

/** @typedef {ReturnType<typeof createContext>} HostedContext */
