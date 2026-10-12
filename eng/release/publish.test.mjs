import { expect, test } from "bun:test";
import { execFileSync } from "node:child_process";
import { createHash } from "node:crypto";
import { mkdtempSync, readFileSync, rmSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { availableResponse, needsLatest, zipContent } from "./publish.mjs";

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

test("a published NuGet package compares by exact entry names, including [Content_Types].xml", () => {
  const directory = mkdtempSync(join(tmpdir(), "runic-translations-zip-"));
  try {
    const nupkg = join(directory, "Example.nupkg"), signed = join(directory, "Signed.nupkg");
    const zip = (path, names) => execFileSync("python3", ["-I", "-c",
      "import sys, zipfile\nwith zipfile.ZipFile(sys.argv[1], 'w') as z:\n    [z.writestr(n, 'content of ' + n) for n in sys.argv[2:]]", path, ...names]);
    zip(nupkg, ["[Content_Types].xml", "_rels/.rels", "Example.nuspec"]);
    zip(signed, ["[Content_Types].xml", "_rels/.rels", "Example.nuspec", ".signature.p7s"]);
    const content = zipContent(nupkg);
    expect(Object.keys(content)).toEqual(["Example.nuspec", "[Content_Types].xml", "_rels/.rels"]);
    expect(content["[Content_Types].xml"]).toBe(createHash("sha256").update("content of [Content_Types].xml").digest("hex"));
    expect(zipContent(signed)).toEqual(content);
  } finally {
    rmSync(directory, { recursive: true, force: true });
  }
});
