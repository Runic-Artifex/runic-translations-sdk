import { expect, test } from "bun:test";
import { readFileSync } from "node:fs";
import { availableResponse, needsLatest } from "./publish.mjs";

test("npm latest follows the preview after its GitHub release, never backwards", () => {
  const steps = Bun.YAML.parse(readFileSync(new URL("../../.github/workflows/publish-preview.yml", import.meta.url), "utf8")).jobs.publish.steps;
  expect(steps.findIndex(s => s.run?.includes("ci-artifact.mjs release "))).toBeLessThan(steps.findIndex(s => s.run?.includes("publish.mjs tag-latest")));
  expect(needsLatest(undefined, "0.6.0-preview.1")).toBe(true);
  expect(needsLatest("0.2.0-preview.1", "0.6.0-preview.1")).toBe(true);
  expect(needsLatest("0.6.0-preview.1", "0.6.0-preview.2")).toBe(true);
  expect(needsLatest("0.6.0-preview.1", "0.6.0-preview.1")).toBe(false);
  expect(needsLatest("0.7.0-preview.1", "0.6.0-preview.2")).toBe(false);
});

test("tag-latest waits through npm 401 and 404 for a just-published name, within a bound", async () => {
  const statuses = [401, 404, 200], delays = [];
  const value = await availableResponse("https://registry.npmjs.org/-/package/new/dist-tags", {
    sleep: async ms => { delays.push(ms); }, fetchImpl: async () => new Response("{}", { status: statuses.shift() }) });
  expect(value.status).toBe(200);
  expect(delays).toHaveLength(2);
  let attempts = 0;
  await expect(availableResponse("https://registry.npmjs.org/x", { attempts: 3, sleep: async () => {},
    fetchImpl: async () => { attempts++; return new Response("", { status: 401 }); } })).rejects.toThrow("401");
  expect(attempts).toBe(3);
  attempts = 0;
  await expect(availableResponse("https://registry.npmjs.org/x", { sleep: async () => {},
    fetchImpl: async () => { attempts++; return new Response("", { status: 500 }); } })).rejects.toThrow("500");
  expect(attempts).toBe(1);
});
