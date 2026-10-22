import { expect, test } from "bun:test";
import { execFileSync } from "node:child_process";
import { createHash } from "node:crypto";
import { mkdtempSync, readdirSync, readFileSync, rmSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { availableResponse, checkNugetPush, globMatches, needsLatest, nugetOwnership, nugetPolicy, publishCandidates, zipContent } from "./publish.mjs";
import { workspace } from "../run.mjs";

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

// Recorded shapes of the keyless nuget.org lookups: an ID published by runic-artifex,
// one owned by another account, one whose versions are all unlisted, and a new ID.
const search = "https://azuresearch-usnc.nuget.org/query";
const fixtures = {
  "runic.translations": { owners: ["runic-artifex"] },
  "runic.translations.foreign": { owners: ["someone-else"] },
  "runic.translations.unlisted": { unlisted: true },
};
async function nugetFixture(url) {
  const json = value => new Response(JSON.stringify(value), { status: 200 });
  const { pathname, searchParams } = new URL(url);
  if (url === "https://api.nuget.org/v3/index.json")
    return json({ resources: [{ "@id": search, "@type": "SearchQueryService/3.5.0" }] });
  if (url.startsWith(`${search}?`)) {
    const id = searchParams.get("q").replace("packageid:", "");
    const fixture = fixtures[id.toLowerCase()];
    return json({ data: fixture && !fixture.unlisted ? [{ id, owners: fixture.owners }] : [] });
  }
  const flat = pathname.match(/^\/v3-flatcontainer\/([^/]+)\/index\.json$/);
  if (flat) return fixtures[flat[1]] ? json({ versions: ["0.6.0-preview.7"] }) : new Response("", { status: 404 });
  throw new Error(`Unexpected request ${url}`);
}
const ownership = id => nugetOwnership(id, { fetchImpl: nugetFixture });

test("nuget.org ownership lookups need no key and tell new IDs from published ones", async () => {
  expect(await ownership("Runic.Translations")).toEqual({ exists: true, owners: ["runic-artifex"] });
  expect(await ownership("Runic.Translations.Unlisted")).toEqual({ exists: true, owners: undefined });
  expect(await ownership("Runic.Translations.New")).toEqual({ exists: false });
});

test("package globs follow nuget.org: * matches any run, case-insensitively", () => {
  expect(globMatches("*", "dotnet-runic-translations")).toBe(true);
  expect(globMatches("Runic.Translations*", "runic.translations.commandline")).toBe(true);
  expect(globMatches("Runic.Translations", "Runic.Translations.CommandLine")).toBe(false);
  expect(globMatches("Runic.*.Build", "RunicXTranslations.Build")).toBe(false);
});

test("a new ID outside the policy fails the check by name with the remedy; a published ID passes", async () => {
  const policy = { owner: "runic-artifex", packageGlobs: ["Runic.Translations", "Runic.Translations.Build"], pushNewPackages: true };
  await checkNugetPush(["Runic.Translations"], { policy, ownership, log: () => {} });
  const failure = checkNugetPush(["Runic.Translations", "Runic.Translations.CommandLine"], { policy, ownership, log: () => {} });
  await expect(failure).rejects.toThrow("Runic.Translations.CommandLine matches none of the policy's package globs (Runic.Translations, Runic.Translations.Build).");
  await expect(failure).rejects.toThrow("eng/release/nuget-policy.json");
  await expect(failure).rejects.not.toThrow("- Runic.Translations matches");
});

test("a covered new ID passes with a notice unless the policy may only push new versions", async () => {
  const messages = [];
  const policy = { owner: "runic-artifex", packageGlobs: ["*"], pushNewPackages: true };
  await checkNugetPush(["Runic.Translations", "Runic.Translations.New", "Runic.Translations.Unlisted"], { policy, ownership, log: message => messages.push(message) });
  expect(messages).toEqual([
    "::notice::Runic.Translations.New is a new nuget.org package ID; the mirrored policy of runic-artifex covers it (glob *).",
    "::warning::Runic.Translations.Unlisted has no listed version on nuget.org, so its owners could not be checked.",
  ]);
  await expect(checkNugetPush(["Runic.Translations.New"], { policy: { ...policy, pushNewPackages: false }, ownership, log: () => {} }))
    .rejects.toThrow("Runic.Translations.New is a new nuget.org package ID, but the policy may only push new versions");
});

test("an ID owned by another nuget.org account fails the check", async () => {
  await expect(checkNugetPush(["Runic.Translations.Foreign"], { ownership, log: () => {} }))
    .rejects.toThrow("Runic.Translations.Foreign is owned on nuget.org by someone-else, not by runic-artifex");
});

test("nothing is pushed when the NuGet check fails, and it runs in the dry run", async () => {
  const candidates = [
    { registry: "npm", name: "@runic-artifex/translations-svelte" },
    { registry: "nuget", name: "Runic.Translations" },
    { registry: "nuget", name: "Runic.Translations.CommandLine" },
  ];
  for (const dryRun of [false, true]) {
    const pushed = [], checked = [];
    await expect(publishCandidates(candidates, "1.0.0", {
      dryRun, log: () => {}, push: async candidate => { pushed.push(candidate.name); },
      published: async candidate => candidate.name === "Runic.Translations",
      checkPush: async ids => { checked.push(...ids); throw new Error("refused"); },
    })).rejects.toThrow("refused");
    expect(checked).toEqual(["Runic.Translations.CommandLine"]);
    expect(pushed).toEqual([]);
  }
  const pushed = [];
  await publishCandidates(candidates, "1.0.0", { dryRun: false, log: () => {}, published: async () => false,
    checkPush: async () => {}, push: async candidate => { pushed.push(candidate.name); } });
  expect(pushed).toEqual(candidates.map(candidate => candidate.name));
});

test("the mirrored policy covers every NuGet package of the workspace, and the dry run gates publication", () => {
  expect(nugetPolicy.owner).toBe("runic-artifex");
  expect(typeof nugetPolicy.pushNewPackages).toBe("boolean");
  for (const entry of workspace.nuget)
    expect(nugetPolicy.packageGlobs.some(pattern => globMatches(pattern, entry.name))).toBe(true);
  const jobs = Bun.YAML.parse(readFileSync(new URL("../../.github/workflows/publish-preview.yml", import.meta.url), "utf8")).jobs;
  expect(jobs.candidate.steps.some(s => s.run?.includes("publish.mjs publish \"$VERSION\" --dry-run"))).toBe(true);
  expect(jobs.publish.needs).toBe("candidate");
  const steps = jobs.publish.steps;
  expect(steps.findIndex(s => s.uses?.startsWith("NuGet/login@"))).toBeLessThan(steps.findIndex(s => s.run?.includes("publish.mjs publish \"$VERSION\"")));
});

test("every publishing workflow waits in one fixed concurrency group, so publications never overlap", () => {
  const dir = new URL("../../.github/workflows/", import.meta.url);
  const parse = name => Bun.YAML.parse(readFileSync(new URL(name, dir), "utf8"));
  const writes = permissions => ["contents", "id-token", "attestations", "packages"].some(name => permissions?.[name] === "write");
  const publishing = readdirSync(dir).filter(name => /\.ya?ml$/.test(name)).filter(name => {
    const file = parse(name);
    return writes(file.permissions) || Object.values(file.jobs).some(job => writes(job.permissions) || job.environment);
  });
  expect(publishing).toContain("publish-preview.yml");
  // One group for every version: a per-version group would let two versions publish and move tags at once.
  for (const name of publishing) expect(parse(name).concurrency).toEqual({ group: "publish-preview", "cancel-in-progress": false });
});
