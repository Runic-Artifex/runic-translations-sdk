import { test, expect } from "bun:test";
import { mkdtempSync, mkdirSync, writeFileSync, rmSync, symlinkSync, realpathSync } from "node:fs";
import { join } from "node:path";
import { tmpdir } from "node:os";
import { serverLaunch, sourceWatchRoots } from "../src/server.js";
import { resolvePreviewHtml } from "../src/preview.js";
import type { Preview } from "../src/preview.js";
import { PreviewSession, sampleChoices, type PreviewHost, type PreviewState, type Samples } from "../src/preview-session.js";
import { ForwardedWatchers, type DisposableLike, type WatcherLike } from "../src/watchers.js";

class MockWatcher implements WatcherLike<string> {
  readonly created: ((uri: string) => void)[] = [];
  readonly changed: ((uri: string) => void)[] = [];
  readonly deleted: ((uri: string) => void)[] = [];
  disposeCount = 0;
  onDidCreate(listener: (uri: string) => void): DisposableLike { this.created.push(listener); return this.subscription(this.created, listener); }
  onDidChange(listener: (uri: string) => void): DisposableLike { this.changed.push(listener); return this.subscription(this.changed, listener); }
  onDidDelete(listener: (uri: string) => void): DisposableLike { this.deleted.push(listener); return this.subscription(this.deleted, listener); }
  dispose(): void { this.disposeCount++; }
  fireChange(uri: string): void { for (const listener of [...this.changed]) listener(uri); }
  private subscription(listeners: ((uri: string) => void)[], listener: (uri: string) => void): DisposableLike {
    return { dispose: () => { const index = listeners.indexOf(listener); if (index >= 0) listeners.splice(index, 1); } };
  }
}

test("local tool resolution respects a root manifest and preserves argument boundaries", () => {
  const root = mkdtempSync(join(tmpdir(), "runic-vscode-"));
  try {
    mkdirSync(join(root, ".config"));
    writeFileSync(join(root, ".config", "dotnet-tools.json"), JSON.stringify({ isRoot: true, tools: {} }));
    expect(() => serverLaunch(root, "dotnet", "")).toThrow("No project-local");
    writeFileSync(join(root, ".config", "dotnet-tools.json"), JSON.stringify({ isRoot: true, tools: { "dotnet-runic-translations": { commands: ["runic-translations"] } } }));
    expect(serverLaunch(root, "dotnet custom", "")).toEqual({ command: "dotnet custom", args: ["tool", "run", "runic-translations", "--", "lsp"], cwd: root });
    writeFileSync(join(root, "server with spaces.dll"), "fixture");
    expect(serverLaunch(root, "dotnet", "server with spaces.dll").args).toEqual([join(root, "server with spaces.dll"), "lsp"]);
  } finally { rmSync(root, { recursive: true }); }
});
test("translation source watcher plans dedupe nested mounts and reject workspace escapes", () => {
  const root = mkdtempSync(join(tmpdir(), "runic-vscode-mounts-"));
  const outside = mkdtempSync(join(tmpdir(), "runic-vscode-outside-"));
  try {
    const canonicalRoot = realpathSync.native(root);
    mkdirSync(join(root, "translations"));
    mkdirSync(join(root, "feature"));
    mkdirSync(join(root, "feature", "nested"));
    if (process.platform !== "win32") symlinkSync(outside, join(root, "escaped-link"), "dir");
    writeFileSync(join(root, "translations", "runic.json"), JSON.stringify({
      schemaVersion: 1, catalog: "app",
      sourceRoots: [
        { path: "../feature", namespace: ["shop"] },
        { path: "../feature/nested", namespace: ["nested"] },
        { path: "../../outside-workspace", namespace: ["escape"] },
        ...(process.platform === "win32" ? [] : [{ path: "../escaped-link", namespace: ["linked"] }]),
      ],
    }));
    expect(sourceWatchRoots(root)).toEqual([canonicalRoot]);
    mkdirSync(join(root, "new-feature"));
    writeFileSync(join(root, "translations", "runic.json"), JSON.stringify({
      schemaVersion: 1, catalog: "app",
      sourceRoots: [{ path: "../new-feature", namespace: ["new-shop"] }],
    }));
    writeFileSync(join(root, "new-feature", "de.rmf2"), "title = Neu\n");
    // The extension refreshes its manually forwarded subscriptions from this
    // stable plan after the manifest event. The one containing watcher observes
    // the replacement mount without adding a duplicate recursive subscription.
    expect(sourceWatchRoots(root)).toEqual([canonicalRoot]);
  } finally {
    rmSync(root, { recursive: true });
    rmSync(outside, { recursive: true });
  }
});
test("refreshed source watchers forward events and dispose replaced subscriptions", () => {
  let roots = ["old"];
  const manifest = new MockWatcher();
  const created: MockWatcher[] = [];
  const events: { uri: string; type: number }[] = [];
  const forwarded = new ForwardedWatchers(
    manifest,
    () => roots,
    () => { const watcher = new MockWatcher(); created.push(watcher); return watcher; },
    (uri, type) => events.push({ uri, type }),
  );
  expect(created).toHaveLength(1);
  const old = created[0];
  roots = ["new"];
  manifest.fireChange("runic.json");
  expect(old.disposeCount).toBe(1);
  expect(created).toHaveLength(2);
  created[1].fireChange("new/de.rmf2");
  created[1].fireChange("new/de/title.mf2");
  expect(events).toEqual([
    { uri: "runic.json", type: 2 },
    { uri: "new/de.rmf2", type: 2 },
    { uri: "new/de/title.mf2", type: 2 },
  ]);
  forwarded.dispose();
  expect(manifest.disposeCount).toBe(1);
  expect(manifest.changed).toHaveLength(0);
  expect(created[1].disposeCount).toBe(1);
  expect(created[1].changed).toHaveLength(0);
});
test("execution-v2 preview selects server rendering and remains inert", async () => {
  let rendered = false;
  const html = await resolvePreviewHtml({
    key: "<key>", locale: "en", examples: [], inputs: [],
    ast: { astVersion: 5, profile: "rmf2-execution-v2", inputs: [] },
  }, {}, async () => {
    rendered = true;
    return { key: "<key>", locale: "en", runs: [
    { text: "<script>alert(1)</script>" },
    { name: "runic:link", text: null, children: [{ text: "Help" }] },
    { name: "runic:action", text: null, children: [{ text: "Retry" }] },
    { name: "shop:badge", text: null, children: [{ text: "Ready" }] },
    ] };
  });
  expect(rendered).toBe(true);
  expect(html).toContain("&lt;script&gt;"); expect(html).not.toContain("<script>");
  expect(html).toContain("aria-disabled=\"true\""); expect(html).toContain("<button disabled>"); expect(html).not.toContain("href=");
  expect(html).toContain("title=\"shop:badge\"");
});

const metadata = (key = "count"): Preview => ({ key, locale: "en", inputs: [{ name: "count", type: "integer" }], examples: [{ count: 1 }, { count: 2 }], ast: { astVersion: 5, profile: "rmf2-execution-v2", inputs: [] } });
const target = (key = "count", locale = "en") => ({ uri: "file:///en.rmf2", key, locale });
function deferred<T>() {
  let resolve!: (value: T) => void;
  let reject!: (error: unknown) => void;
  const promise = new Promise<T>((done, fail) => { resolve = done; reject = fail; });
  return { promise, resolve, reject };
}
function fixture(overrides: Partial<PreviewHost> = {}, maximumSessions = 128) {
  const published: PreviewState[] = [];
  let choices = 0;
  const host: PreviewHost = { revision: () => 0, load: async () => metadata(), choose: async () => { choices++; return { count: "2" }; }, render: async (_, __, samples) => samples.count, publish: state => { published.push(state); }, ...overrides };
  return { session: new PreviewSession(host, maximumSessions), published, choices: () => choices };
}
test("all examples and custom samples are offered; session reuse is scoped by message and locale", async () => {
  expect(sampleChoices(metadata(), { count: "9" }).map(choice => [choice.label, choice.samples])).toEqual([
    ["Previous sample values", { count: "9" }], ["Example 1", { count: "1" }], ["Example 2", { count: "2" }], ["Custom sample values", undefined],
  ]);
  const { session, choices } = fixture();
  await session.open(target()); await session.open(target());
  expect(choices()).toBe(1);
  expect(session.state?.samples).toEqual({ count: "2" });
  expect(session.preferredLocale(target().uri, "count")).toBe("en");
  await session.open(target("another")); await session.open(target("count", "de"));
  expect(choices()).toBe(3);
  await session.open(target(), async () => ({ count: "7" }));
  session.close(); await session.open(target());
  expect(choices()).toBe(3);
  expect(session.state?.samples).toEqual({ count: "7" });
});
test("cancelled or late older selections cannot replace a newer preview or its remembered samples", async () => {
  const older = deferred<Samples | undefined>();
  const { session, published } = fixture();
  const pending = session.open(target(), async () => older.promise);
  await Promise.resolve(); await Promise.resolve();
  await session.open(target(), async () => ({ count: "8" }));
  older.resolve({ count: "1" }); await pending;
  expect(published.map(state => state.samples.count)).toEqual(["8"]);
  await session.open(target(), async () => undefined);
  await session.open(target());
  expect(session.state?.samples.count).toBe("8");
});
test("an older completed render cannot overwrite a new message selection", async () => {
  const started = deferred<void>(); const rendered = deferred<string>();
  const { session, published } = fixture({ render: async item => { if (item.key === "old") { started.resolve(); return rendered.promise; } return "new"; } });
  const old = session.open(target("old")); await started.promise;
  await session.open(target("new")); rendered.resolve("old"); await old;
  expect(published.map(state => state.target.key)).toEqual(["new"]);
});
test("refresh and edits during rendering publish only the latest unsaved revision with remembered samples", async () => {
  let revision = 1;
  const started = deferred<void>(); const rendered = deferred<string>();
  let renders = 0;
  const { session, published } = fixture({ revision: () => revision, render: async (_, __, samples) => { renders++; if (renders === 1) { started.resolve(); return rendered.promise; } return `revision ${revision}: ${samples.count}`; } });
  const initial = session.open(target()); await started.promise;
  revision = 2; rendered.resolve("stale revision 1"); await initial;
  expect(published.map(state => state.html)).toEqual(["revision 2: 2"]);
  revision = 3; await session.refresh();
  expect(session.state?.html).toBe("revision 3: 2");
});
test("content-modified responses retry and closing a panel invalidates in-flight results", async () => {
  let requests = 0;
  const { session } = fixture({ load: async () => { if (++requests === 1) throw { code: -32801 }; return metadata(); } });
  await session.open(target()); expect(session.state?.samples.count).toBe("2");
  const rendering = deferred<string>(); const started = deferred<void>();
  const closed = fixture({ render: async () => { started.resolve(); return rendering.promise; } });
  const pending = closed.session.open(target()); await started.promise;
  closed.session.close(); rendering.resolve("late"); await pending;
  expect(closed.published).toHaveLength(0);
  expect(closed.session.state).toBeUndefined();
});
test("cancelling sample selection still refreshes edits made while the picker was open", async () => {
  let revision = 1;
  const selection = deferred<Samples | undefined>();
  const started = deferred<void>();
  const { session, published } = fixture({ revision: () => revision, render: async () => `revision ${revision}` });
  await session.open(target());
  const choosing = session.open(target(), async () => { started.resolve(); return selection.promise; });
  await started.promise; revision = 2;
  await session.refresh();
  expect(published).toHaveLength(1);
  selection.resolve(undefined); await choosing;
  expect(session.state?.html).toBe("revision 2");
  expect(session.state?.samples.count).toBe("2");
});
test("session sample and preferred-locale cache evicts the least recently used identity", async () => {
  const { session, choices } = fixture({}, 2);
  await session.open(target("a")); await session.open(target("b"));
  await session.open(target("a")); await session.open(target("c"));
  expect(session.preferredLocale(target().uri, "b")).toBeUndefined();
  await session.open(target("a")); expect(choices()).toBe(3);
  await session.open(target("b")); expect(choices()).toBe(4);
  expect(session.state?.target.key).toBe("b");
});
test("a failed older render cannot publish an error over a newer preview", async () => {
  const started = deferred<void>(); const rendering = deferred<string>();
  const failures: unknown[] = [];
  const { session, published } = fixture({ failed: error => { failures.push(error); }, render: async item => { if (item.key === "old") { started.resolve(); return rendering.promise; } return "new"; } });
  const old = session.open(target("old")); await started.promise;
  await session.open(target("new")); rendering.reject(new Error("old failure")); await old;
  expect(failures).toHaveLength(0);
  expect(published.map(state => state.html)).toEqual(["new"]);
});
