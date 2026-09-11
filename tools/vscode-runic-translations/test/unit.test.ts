import { test, expect } from "bun:test";
import { mkdtempSync, mkdirSync, writeFileSync, rmSync } from "node:fs";
import { join } from "node:path";
import { tmpdir } from "node:os";
import { serverLaunch } from "../src/server.js";
import { previewHtml } from "../src/preview.js";

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
test("preview reuses normalized execution and never creates application navigation or active markup", () => {
  const html = previewHtml({ key: "<script>", locale: "en", examples: [], ast: { astVersion: 4, inputs: {}, selectors: [], variants: [{ matches: {}, nodes: [{ kind: "text", value: "<script>alert(1)</script>" }, { kind: "markup", name: "runic:link", attributes: { ref: "help" }, children: [{ kind: "text", value: "Help" }] }, { kind: "markup", name: "runic:action", attributes: {}, children: [{ kind: "text", value: "Retry" }] }] }] } }, {});
  expect(html).toContain("&lt;script&gt;"); expect(html).not.toContain("<script>");
  expect(html).toContain("aria-disabled=\"true\""); expect(html).toContain("<button disabled>"); expect(html).not.toContain("href=");
});
