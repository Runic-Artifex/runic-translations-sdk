// Shared page instrumentation for the dev-server browser checks. It records what the page did so that a check that
// never sees its result fails quickly with the page's console, errors, failed requests and navigations instead of
// a bare Playwright timeout.

/**
 * The checks' dev-server options. HMR and the websocket server are off: the page's Vite client fails to connect
 * (it logs that to the console), so the server can never reload the page and every dependency must be pre-bundled
 * before the page loads (`optimizeDeps.entries` in vite.config.ts).
 */
export const devServerOptions = { host: "127.0.0.1", port: 0, hmr: false, ws: false };

/**
 * Starts recording a page. `errors` collects page errors and hydration warnings, which the checks assert are empty.
 * @param {import("playwright-core").Page} page
 */
export function watchPage(page) {
  const started = Date.now();
  const log = [];
  const errors = [];
  let fatal;
  let fail;
  const failed = new Promise((_, reject) => { fail = reject; });
  failed.catch(() => undefined);
  const record = (kind, text) => log.push(`${String(Date.now() - started).padStart(6)} ms ${kind}: ${text}`);
  const stop = reason => { fatal ??= reason; fail(new Error(reason)); };
  page.on("pageerror", error => {
    errors.push(error.message);
    record("pageerror", error.stack ?? error.message);
    stop(`page error: ${error.message}`);
  });
  page.on("console", message => {
    record(`console.${message.type()}`, message.text());
    if (message.type() === "warning" && message.text().includes("hydration")) errors.push(message.text());
  });
  page.on("requestfailed", request => record("requestfailed", `${request.url()} ${request.failure()?.errorText ?? ""}`));
  page.on("response", response => {
    if (response.status() < 400) return;
    record("response", `${response.status()} ${response.statusText()} ${response.url()}`);
    // A module the page imports failed (for example 504 Outdated Optimize Dep), so its script can never finish.
    if (["script", "document"].includes(response.request().resourceType())) stop(`${response.status()} for ${response.url()}`);
  });
  page.on("framenavigated", frame => { if (frame === page.mainFrame()) record("navigated", frame.url()); });
  page.on("crash", () => stop("the page crashed"));

  return {
    errors,
    /** Waits until `predicate` holds in the page, failing early on a page error or a failed module. */
    async waitForResult(predicate, description, timeout = 30_000) {
      try {
        await Promise.race([page.waitForFunction(predicate, undefined, { timeout }), failed]);
      } catch (error) {
        const reason = fatal ?? error.message;
        throw new Error(`${description} did not arrive: ${reason}\nPage activity:\n${log.join("\n") || "(none)"}`, { cause: error });
      }
    },
  };
}
