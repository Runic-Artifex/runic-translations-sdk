import { test, expect } from "bun:test";
import { mkdtempSync, mkdirSync, writeFileSync, rmSync, symlinkSync, realpathSync } from "node:fs";
import { join } from "node:path";
import { tmpdir } from "node:os";
import { serverLaunch, sourceWatchRoots } from "../src/server.js";
import { previewHtml, resolvePreviewHtml } from "../src/preview.js";
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
test("RMF2 source watcher plans dedupe nested mounts and reject workspace escapes", () => {
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
  expect(events).toEqual([{ uri: "runic.json", type: 2 }, { uri: "new/de.rmf2", type: 2 }]);
  forwarded.dispose();
  expect(manifest.disposeCount).toBe(1);
  expect(manifest.changed).toHaveLength(0);
  expect(created[1].disposeCount).toBe(1);
  expect(created[1].changed).toHaveLength(0);
});
test("preview reuses normalized execution and never creates application navigation or active markup", () => {
  const html = previewHtml({ key: "<script>", locale: "en", examples: [], inputs: [], ast: { astVersion: 4, inputs: {}, selectors: [], variants: [{ matches: {}, nodes: [{ kind: "text", value: "<script>alert(1)</script>" }, { kind: "markup", name: "runic:link", attributes: { ref: "help" }, children: [{ kind: "text", value: "Help" }] }, { kind: "markup", name: "runic:action", attributes: {}, children: [{ kind: "text", value: "Retry" }] }] }] } }, {});
  expect(html).toContain("&lt;script&gt;"); expect(html).not.toContain("<script>");
  expect(html).toContain("aria-disabled=\"true\""); expect(html).toContain("<button disabled>"); expect(html).not.toContain("href=");
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
