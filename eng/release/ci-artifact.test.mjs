import { expect, test } from "bun:test";
import { execFileSync } from "node:child_process";
import { mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { ARTIFACT, CI_WORKFLOW, REPOSITORY, findCiPackages, prepare, releaseCheck, selectArtifact, selectCiRun, verify } from "./ci-artifact.mjs";

const sha = "a".repeat(40);
const run = (id, extra = {}) => ({ id, head_sha: sha, event: "push", head_branch: "main", path: CI_WORKFLOW,
  repository: { full_name: REPOSITORY }, head_repository: { full_name: REPOSITORY }, status: "completed", conclusion: "success",
  html_url: `https://github.com/${REPOSITORY}/actions/runs/${id}`, ...extra });
const workflow = name => Bun.YAML.parse(readFileSync(new URL(`../../.github/workflows/${name}`, import.meta.url), "utf8"));

test("selects the newest successful push run of ci.yml on main for the exact commit", () => {
  expect(selectCiRun([run(1), run(3), run(2, { conclusion: "failure" })], { repository: REPOSITORY, sha }).id).toBe(3);
  for (const other of [{ head_sha: "b".repeat(40) }, { event: "workflow_dispatch" }, { event: "pull_request" }, { head_branch: "feature" },
    { path: ".github/workflows/publish-preview.yml" }, { head_repository: { full_name: "fork/runic-translations-sdk" } }])
    expect(() => selectCiRun([run(9, other)], { repository: REPOSITORY, sha })).toThrow("No push run");
});

test("fails clearly when CI for the commit is missing, running or failed, or its artifact is unusable", () => {
  expect(() => selectCiRun([], { repository: REPOSITORY, sha })).toThrow(`No push run of ${CI_WORKFLOW} on main exists for ${sha}`);
  expect(() => selectCiRun([run(4, { status: "in_progress", conclusion: null })], { repository: REPOSITORY, sha })).toThrow("still in_progress");
  expect(() => selectCiRun([run(5, { conclusion: "failure" })], { repository: REPOSITORY, sha })).toThrow("concluded failure");
  const artifact = { name: ARTIFACT, expired: false, workflow_run: { id: 7, head_sha: sha } };
  expect(selectArtifact([artifact, { ...artifact, name: "rmf2-vscode-vsix" }], run(7))).toBe(artifact);
  expect(() => selectArtifact([], run(7))).toThrow("no single");
  expect(() => selectArtifact([{ ...artifact, expired: true }], run(7))).toThrow("expired");
  expect(() => selectArtifact([{ ...artifact, workflow_run: { id: 7, head_sha: "c".repeat(40) } }], run(7))).toThrow();
});

test("queries GitHub for push runs of the commit", async () => {
  const urls = [];
  const fetchImpl = async url => {
    urls.push(new URL(url));
    return Response.json(url.includes("/artifacts") ? { artifacts: [{ name: ARTIFACT, expired: false, workflow_run: { id: 8, head_sha: sha } }] }
      : { workflow_runs: [run(8)] });
  };
  expect(await findCiPackages({ repository: REPOSITORY, sha, token: "t", fetchImpl })).toEqual({ runId: "8", runUrl: run(8).html_url, artifact: ARTIFACT });
  expect(urls[0].pathname).toBe(`/repos/${REPOSITORY}/actions/workflows/ci.yml/runs`);
  expect(Object.fromEntries(urls[0].searchParams)).toMatchObject({ head_sha: sha, event: "push", branch: "main" });
  await expect(findCiPackages({ repository: REPOSITORY, sha, token: "t", fetchImpl: async () => new Response("", { status: 403 }) })).rejects.toThrow("failed: 403");
});

test("packages must be exactly the workspace set at the version, packed from the commit", () => {
  const directory = mkdtempSync(join(tmpdir(), "runic-translations-ci-artifact-"));
  const inventory = { nuget: [{ name: "Runic.Translations" }], npm: [{ name: "@runic-artifex/translations-svelte" }] };
  const version = "1.2.3-preview.1";
  const script = `import io,json,sys,tarfile,zipfile
d,v,s,r,nuget_version,npm_head=sys.argv[1:]
with zipfile.ZipFile(d+'/nuget/Runic.Translations.'+v+'.nupkg','w') as z:
 z.writestr('Runic.Translations.nuspec','<package><metadata><id>Runic.Translations</id><version>'+nuget_version+'</version><repository type="git" url="https://github.com/'+r+'" commit="'+s+'" /></metadata></package>')
b=json.dumps(dict(name='@runic-artifex/translations-svelte',version=v,gitHead=npm_head,repository=dict(type='git',url='git+https://github.com/'+r+'.git'))).encode()
with tarfile.open(d+'/npm/runic-artifex-translations-svelte-'+v+'.tgz','w:gz') as t:
 i=tarfile.TarInfo('package/package.json');i.size=len(b);t.addfile(i,io.BytesIO(b))
`;
  const pack = (nugetVersion = version, npmHead = sha) => execFileSync("python3", ["-c", script, directory, version, sha, REPOSITORY, nugetVersion, npmHead]);
  try {
    mkdirSync(join(directory, "nuget")); mkdirSync(join(directory, "npm"));
    pack();
    const manifest = prepare(directory, version, sha, "42", inventory);
    expect(manifest.packages.map(p => p.name)).toEqual(["Runic.Translations", "@runic-artifex/translations-svelte"]);
    verify(directory, manifest, sha, inventory);
    expect(() => verify(directory, manifest, "b".repeat(40), inventory)).toThrow("Inventory is for");
    expect(() => prepare(directory, "1.2.3-preview.2", sha, "42", inventory)).toThrow("exactly");
    pack("1.2.3-preview.2");
    expect(() => prepare(directory, version, sha, "42", inventory)).toThrow("declares version");
    expect(() => verify(directory, manifest, sha, inventory)).toThrow();
    pack(version, "b".repeat(40));
    expect(() => prepare(directory, version, sha, "42", inventory)).toThrow("packed from");
    pack();
    writeFileSync(join(directory, "npm", "stale.tgz"), "stale");
    expect(() => prepare(directory, version, sha, "42", inventory)).toThrow("exactly");
  } finally { rmSync(directory, { recursive: true, force: true }); }
});

test("the release check only reads and refuses an existing release or a foreign tag", () => {
  const fake = ({ release = false, tag } = {}) => {
    const calls = [];
    const spawn = (command, args) => {
      calls.push([command, ...args]);
      if (args[0] === "release") return { status: release ? 0 : 1, stdout: JSON.stringify({ url: "u" }) };
      return { status: tag ? 0 : 1, stdout: `${tag}\n` };
    };
    return { spawn, calls };
  };
  const { spawn, calls } = fake();
  expect(releaseCheck("1.2.3", sha, spawn)).toContain("Would create");
  expect(calls.map(c => c.slice(0, 3))).toEqual([["gh", "release", "view"], ["gh", "api", `repos/${REPOSITORY}/commits/v1.2.3`]]);
  expect(releaseCheck("1.2.3", sha, fake({ tag: sha }).spawn)).toContain("Would create");
  expect(() => releaseCheck("1.2.3", sha, fake({ release: true }).spawn)).toThrow("already exists");
  expect(() => releaseCheck("1.2.3", sha, fake({ tag: "b".repeat(40) }).spawn)).toThrow("belongs to");
});

test("publication reuses the CI artifact and never reruns tests or packing", () => {
  const ci = workflow("ci.yml"), release = workflow("publish-preview.yml");
  expect(ci.jobs["build-and-test"].steps.find(s => s.uses?.startsWith("actions/upload-artifact@")).with.name).toBe(ARTIFACT);
  expect(release.jobs.candidate.steps.find(s => s.id === "ci").run).toBe("bun eng/release/ci-artifact.mjs find-ci");
  expect(release.jobs.candidate.if).toContain("github.ref == 'refs/heads/main'");
  for (const [name, source] of [["candidate", "steps.ci"], ["publish", "needs.candidate"]]) {
    const job = release.jobs[name];
    expect(job.steps.filter(s => s.run).map(s => s.run).join("\n")).not.toMatch(/run\.mjs (test|pack|verify)|verify-packages|verify:candidate/);
    expect(job.steps.find(s => s.uses?.startsWith("actions/download-artifact@")).with).toEqual({
      name: `\${{ ${source}.outputs.artifact }}`, path: "artifacts/packages",
      "run-id": `\${{ ${source}.outputs.${source === "steps.ci" ? "run-id" : "ci-run-id"} }}`, "github-token": "${{ github.token }}" });
    expect(job.permissions.actions).toBe("read");
  }
});

test("a dry run performs every read-only check and never reaches OIDC, publication, release or tags", () => {
  const release = workflow("publish-preview.yml");
  expect(release.on.workflow_dispatch.inputs["dry-run"]).toMatchObject({ type: "boolean", default: false });
  expect(release.permissions).toEqual({ contents: "read" });
  const { candidate, publish } = release.jobs;
  expect(candidate.environment).toBeUndefined();
  expect(candidate.permissions).toEqual({ contents: "read", actions: "read" });
  expect(candidate.steps.some(s => s.uses?.startsWith("NuGet/login") || s.run?.includes("gh release create"))).toBe(false);
  const checks = candidate.steps.filter(s => /publish\.mjs|release-check/.test(s.run ?? "")).map(s => s.run);
  expect(checks).toEqual(['bun eng/release/publish.mjs publish "$VERSION" --dry-run', 'bun eng/release/ci-artifact.mjs release-check "$VERSION"',
    'bun eng/release/publish.mjs tag-latest "$VERSION" --dry-run']);
  expect(publish.if).toBe("${{ !inputs.dry-run }}");
  expect(publish.environment).toBe("preview");
  expect(publish.permissions).toEqual({ contents: "write", "id-token": "write", actions: "read" });
  const runs = publish.steps.filter(s => s.run).map(s => s.run);
  expect(runs.join("\n")).not.toContain("--dry-run");
  const index = text => runs.findIndex(r => r.includes(text));
  expect(index("ci-artifact.mjs verify")).toBeLessThan(index("publish.mjs publish"));
  expect(index("publish.mjs publish")).toBeLessThan(index("gh release create"));
  expect(runs[index("gh release create")]).toContain('--target "$GITHUB_SHA"');
});
