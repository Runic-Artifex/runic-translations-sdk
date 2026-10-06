import { expect, test } from "bun:test";
import { readFileSync } from "node:fs";
import { needsLatest } from "./publish.mjs";

test("npm latest follows the preview after its GitHub release, never backwards", () => {
  const steps = Bun.YAML.parse(readFileSync(new URL("../../.github/workflows/publish-preview.yml", import.meta.url), "utf8")).jobs.publish.steps;
  expect(steps.findIndex(s => s.run?.startsWith("gh release create"))).toBeLessThan(steps.findIndex(s => s.run?.includes("publish.mjs tag-latest")));
  expect(needsLatest(undefined, "0.6.0-preview.1")).toBe(true);
  expect(needsLatest("0.2.0-preview.1", "0.6.0-preview.1")).toBe(true);
  expect(needsLatest("0.6.0-preview.1", "0.6.0-preview.2")).toBe(true);
  expect(needsLatest("0.6.0-preview.1", "0.6.0-preview.1")).toBe(false);
  expect(needsLatest("0.7.0-preview.1", "0.6.0-preview.2")).toBe(false);
});
