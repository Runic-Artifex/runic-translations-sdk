import { expect, test } from "bun:test";
import { execFileSync, spawnSync } from "node:child_process";
import { mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { ARTIFACT, CI_WORKFLOW, FULL_RUN_ARTIFACT, REPOSITORY, compareVersions, createRelease, githubPrerelease, previousTag, releaseNotes, findCiPackages, prepare, refuseTrainFeed, releaseCheck, selectArtifact, scan, selectCiRun, verify } from "./ci-artifact.mjs";

const sha = "a".repeat(40);
const run = (id, extra = {}) => ({ id, head_sha: sha, event: "push", head_branch: "main", path: CI_WORKFLOW,
  repository: { full_name: REPOSITORY }, head_repository: { full_name: REPOSITORY }, status: "completed", conclusion: "success",
  html_url: `https://github.com/${REPOSITORY}/actions/runs/${id}`, ...extra });
const workflow = name => Bun.YAML.parse(readFileSync(new URL(`../../.github/workflows/${name}`, import.meta.url), "utf8"));

const all = ids => new Set(ids);
test("selects the newest successful full run of ci.yml on main for the exact commit", () => {
  expect(selectCiRun([run(1), run(3), run(2, { conclusion: "failure" })], { repository: REPOSITORY, sha, full: all([1, 2, 3]) }).id).toBe(3);
  // A nightly or dispatched run of the commit is as good as its push run.
  for (const event of ["schedule", "workflow_dispatch"])
    expect(selectCiRun([run(1), run(4, { event })], { repository: REPOSITORY, sha, full: all([4]) }).id).toBe(4);
  for (const other of [{ head_sha: "b".repeat(40) }, { event: "pull_request" }, { head_branch: "feature" },
    { path: ".github/workflows/publish-preview.yml" }, { head_repository: { full_name: "fork/runic-translations-sdk" } }])
    expect(() => selectCiRun([run(9, other)], { repository: REPOSITORY, sha, full: all([9]) })).toThrow(`No ${CI_WORKFLOW} run on main`);
});

test("passes over an ordinary push run that skipped platform jobs, and says how to start a full run", () => {
  // Run 6, the commit's ordinary push run, has no marker; run 7, a later dispatched run, has.
  expect(selectCiRun([run(6), run(7, { event: "workflow_dispatch" })], { repository: REPOSITORY, sha, full: all([7]) }).id).toBe(7);
  const only = () => selectCiRun([run(6)], { repository: REPOSITORY, sha, full: all([]) });
  expect(only).toThrow(`No full CI run exists for ${sha}: ${run(6).html_url} skipped macOS or Windows jobs`);
  expect(only).toThrow(`gh workflow run ci.yml -R ${REPOSITORY} --ref main`);
  // A full run that is still running is waited for, not reported as missing.
  expect(() => selectCiRun([run(6), run(8, { event: "workflow_dispatch", status: "in_progress", conclusion: null })], { repository: REPOSITORY, sha, full: all([8]) }))
    .toThrow("still in_progress");
});

test("fails clearly when CI for the commit is missing, running or failed, or its artifact is unusable", () => {
  expect(() => selectCiRun([], { repository: REPOSITORY, sha })).toThrow(`No ${CI_WORKFLOW} run on main exists for ${sha}`);
  expect(() => selectCiRun([run(4, { status: "in_progress", conclusion: null })], { repository: REPOSITORY, sha })).toThrow("still in_progress");
  expect(() => selectCiRun([run(5, { conclusion: "failure" })], { repository: REPOSITORY, sha, full: all([5]) })).toThrow("concluded failure");
  const artifact = { id: 70, name: ARTIFACT, expired: false, workflow_run: { id: 7, head_sha: sha } };
  expect(selectArtifact([artifact, { ...artifact, name: "other" }], run(7))).toBe(artifact);
  expect(selectArtifact([artifact, { ...artifact, id: 71, name: "other" }], run(7), "other").id).toBe(71);
  expect(() => selectArtifact([artifact], run(7), "other")).toThrow("no single other artifact");
  expect(() => selectArtifact([], run(7))).toThrow("no single");
  expect(() => selectArtifact([{ ...artifact, expired: true }], run(7))).toThrow("expired");
  expect(() => selectArtifact([{ ...artifact, workflow_run: { id: 7, head_sha: "c".repeat(40) } }], run(7))).toThrow();
  expect(() => selectArtifact([{ ...artifact, id: undefined }], run(7))).toThrow("no id");
});

test("refuses a CI run that built against the train feed", () => {
  const artifact = { id: 70, name: ARTIFACT, expired: false, workflow_run: { id: 7, head_sha: sha } };
  expect(() => refuseTrainFeed([artifact], run(7))).not.toThrow();
  expect(() => refuseTrainFeed([artifact, { ...artifact, id: 71, name: "train-feed" }], run(7))).toThrow("cannot be published");
});

test("queries GitHub for the main runs of the commit and takes the full one", async () => {
  const urls = [];
  // Run 9 is the commit's ordinary push run (no marker); run 8, its nightly, is full.
  const names = { 8: [ARTIFACT, "editor-hosted-packed", FULL_RUN_ARTIFACT], 9: [ARTIFACT, "editor-hosted-packed"] };
  const fetchImpl = async url => {
    urls.push(new URL(url));
    const id = Number(/\/runs\/(\d+)\/artifacts/.exec(url)?.[1]);
    const artifacts = (names[id] ?? []).map((name, index) => ({ id: id * 10 + index, name, expired: false, workflow_run: { id, head_sha: sha } }));
    return Response.json(url.includes("/artifacts") ? { artifacts: artifacts.reverse() } : { workflow_runs: [run(9), run(8, { event: "schedule" })] });
  };
  expect(await findCiPackages({ repository: REPOSITORY, sha, token: "t", fetchImpl })).toEqual({ runId: "8", runUrl: run(8).html_url, artifact: ARTIFACT, artifactId: "80" });
  expect(urls[0].pathname).toBe(`/repos/${REPOSITORY}/actions/workflows/ci.yml/runs`);
  expect(Object.fromEntries(urls[0].searchParams)).toMatchObject({ head_sha: sha, branch: "main" });
  expect(urls[0].searchParams.has("event")).toBe(false);
  delete names[8];
  await expect(findCiPackages({ repository: REPOSITORY, sha, token: "t", fetchImpl })).rejects.toThrow("No full CI run exists");
  await expect(findCiPackages({ repository: REPOSITORY, sha, token: "t", fetchImpl: async () => new Response("", { status: 403 }) })).rejects.toThrow("failed: 403");
});

test("packages must be exactly the release set at the version, packed from the commit", () => {
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
    expect(Object.keys(manifest)).toEqual(["schema", "repository", "version", "source", "ciRunId", "packages"]);
    verify(directory, manifest, sha, version, "42", inventory);
    expect(() => verify(directory, manifest, "b".repeat(40), version, "42", inventory)).toThrow("Inventory is for");
    expect(() => verify(directory, manifest, sha, "1.2.3-preview.2", "42", inventory)).toThrow("version");
    expect(() => verify(directory, manifest, sha, version, "43", inventory)).toThrow("CI run");
    expect(() => prepare(directory, "1.2.3-preview.2", sha, "42", inventory)).toThrow("exactly");
    pack("1.2.3-preview.2");
    expect(() => prepare(directory, version, sha, "42", inventory)).toThrow("declares version");
    expect(() => verify(directory, manifest, sha, version, "42", inventory)).toThrow();
    pack(version, "b".repeat(40));
    expect(() => prepare(directory, version, sha, "42", inventory)).toThrow("packed from");
    // An npm archive names the commit that last changed its inputs; scan hands it to the npm source check.
    const checked = [];
    expect(scan(directory, version, sha, inventory, { npmSource: (...args) => checked.push(args) })).toHaveLength(2);
    expect(checked).toEqual([["@runic-artifex/translations-svelte", "b".repeat(40), sha, { file: `runic-artifex-translations-svelte-${version}.tgz` }]]);
    pack();
    writeFileSync(join(directory, "npm", "stale.tgz"), "stale");
    expect(() => prepare(directory, version, sha, "42", inventory)).toThrow("exactly");
  } finally { rmSync(directory, { recursive: true, force: true }); }
});

const fakeGh = ({ release = false, tag, draft = false, assets = [], target = sha, annotated = false, tagError = false, releaseError = false,
  tags = ["v1.2.2"], listError = false } = {}) => {
  const calls = [];
  const spawn = (command, args) => {
    calls.push([command, ...args]);
    if (args[0] === "release" && args[1] === "view") {
      if (releaseError) return { status: 1, stdout: "", stderr: "HTTP 502: Bad Gateway" };
      return release ? { status: 0, stdout: JSON.stringify({ url: "u", isDraft: draft, targetCommitish: target, assets: assets.map(name => ({ name })) }) }
        : { status: 1, stdout: "", stderr: "release not found" };
    }
    if (args[0] === "api" && args.includes(`repos/${REPOSITORY}/git/matching-refs/tags/v`)) {
      if (listError) return { status: 1, stdout: "", stderr: "gh: Server Error (HTTP 502)" };
      return { status: 0, stdout: tags.map(name => `refs/tags/${name}\n`).join("") };
    }
    if (args[0] === "api") {
      if (tagError) return { status: 1, stdout: "", stderr: "gh: Server Error (HTTP 502)" };
      if (!tag) return { status: 1, stdout: "", stderr: "gh: Not Found (HTTP 404)" };
      if (annotated && args[1].includes("/git/ref/tags/")) return { status: 0, stdout: `tag ${"e".repeat(40)}\n` };
      return { status: 0, stdout: `commit ${tag}\n` };
    }
    return { status: 0 };
  };
  return { spawn, calls };
};
const writes = calls => calls.some(c => c[1] === "release" && ["create", "upload", "edit"].includes(c[2]));

test("the release check only reads and accepts only a release of this exact commit", () => {
  const { spawn, calls } = fakeGh();
  expect(releaseCheck("1.2.3", sha, spawn)).toContain("Would create");
  expect(calls.map(c => c.slice(0, 3))).toEqual([["gh", "release", "view"], ["gh", "api", `repos/${REPOSITORY}/git/ref/tags/v1.2.3`],
    ["gh", "api", "--paginate"]]);
  expect(releaseCheck("1.2.3", sha, fakeGh().spawn)).toContain("its generated notes starting at v1.2.2");
  expect(releaseCheck("1.2.3", sha, fakeGh({ tags: [] }).spawn)).toContain("from the start of the history");
  expect(releaseCheck("1.2.3", sha, fakeGh({ tag: sha }).spawn)).toContain("Would create");
  expect(releaseCheck("1.2.3", sha, fakeGh({ release: true, tag: sha }).spawn)).toContain("already exists for");
  expect(releaseCheck("1.2.3", sha, fakeGh({ release: true, draft: true }).spawn)).toContain("resume the draft");
  expect(() => releaseCheck("1.2.3", sha, fakeGh({ release: true, tag: "b".repeat(40) }).spawn)).toThrow("belongs to");
  expect(() => releaseCheck("1.2.3", sha, fakeGh({ tag: "b".repeat(40) }).spawn)).toThrow("belongs to");
  expect(() => releaseCheck("1.2.3", sha, fakeGh({ release: true, draft: true, target: "main" }).spawn)).toThrow("targets main");
  expect(() => releaseCheck("1.2.3", sha, fakeGh({ release: true }).spawn)).toThrow("no tag");
});

test("only a 404 means absent; other lookup errors stop before writing", () => {
  for (const [options, message] of [[{ tagError: true }, "Tag lookup for v1.2.3 failed: gh: Server Error"], [{ releaseError: true }, "Release lookup for v1.2.3 failed"]]) {
    const { spawn, calls } = fakeGh(options);
    expect(() => releaseCheck("1.2.3", sha, spawn)).toThrow(message);
    expect(() => createRelease("1.2.3", sha, ["p/nuget/A.nupkg"], spawn)).toThrow(message);
    expect(writes(calls)).toBe(false);
  }
});

test("an annotated tag resolves to its commit", () => {
  const { spawn, calls } = fakeGh({ tag: sha, annotated: true });
  expect(releaseCheck("1.2.3", sha, spawn)).toContain("Would create");
  expect(calls.filter(c => c[1] === "api").map(c => c.at(-3))).toEqual([`repos/${REPOSITORY}/git/ref/tags/v1.2.3`, `repos/${REPOSITORY}/git/tags/${"e".repeat(40)}`,
    `repos/${REPOSITORY}/git/matching-refs/tags/v`]);
});

test("a rerun keeps a release of this commit, never modifies a published one and resumes a matching draft", () => {
  const files = ["p/nuget/A.nupkg", "p/npm/b.tgz"];
  let { spawn, calls } = fakeGh();
  expect(createRelease("1.2.3", sha, files, spawn)).toContain("Created");
  const create = calls.at(-1);
  expect(create.slice(0, 3)).toEqual(["gh", "release", "create"]);
  expect(create[create.indexOf("--target") + 1]).toBe(sha);
  expect(create).toContain("--generate-notes");
  expect(create).not.toContain("--notes-file");
  ({ spawn, calls } = fakeGh());
  createRelease("1.2.3", sha, files, spawn, "eng/release/notes/1.2.3.md");
  const withNotes = calls.at(-1);
  expect(withNotes).toContain("--generate-notes");
  expect(withNotes[withNotes.indexOf("--notes-file") + 1]).toBe("eng/release/notes/1.2.3.md");
  // Immutable releases reject new assets: a published release missing one is kept with a warning.
  ({ spawn, calls } = fakeGh({ release: true, tag: sha, assets: ["A.nupkg"] }));
  expect(createRelease("1.2.3", sha, files, spawn)).toContain("kept unchanged without b.tgz");
  expect(writes(calls)).toBe(false);
  ({ spawn, calls } = fakeGh({ release: true, tag: sha, assets: ["A.nupkg", "b.tgz"] }));
  expect(createRelease("1.2.3", sha, files, spawn)).toContain("every asset");
  expect(writes(calls)).toBe(false);
  ({ spawn, calls } = fakeGh({ release: true, draft: true, assets: ["A.nupkg"] }));
  expect(createRelease("1.2.3", sha, files, spawn)).toContain("Completed");
  expect(calls.slice(-2).map(c => c.slice(0, 3))).toEqual([["gh", "release", "upload"], ["gh", "release", "edit"]]);
  expect(calls.at(-1)).toContain("--draft=false");
  ({ spawn, calls } = fakeGh({ release: true, tag: "b".repeat(40) }));
  expect(() => createRelease("1.2.3", sha, files, spawn)).toThrow("belongs to");
  expect(writes(calls)).toBe(false);
});

test("generated notes start at the previous SDK tag, never at an editor tag", () => {
  // matching-refs/tags/v lists only v* tags; an editor-v* release is never a candidate.
  const tags = ["v0.9.0", "v0.14.0", "v0.13.0", "v0.6.0-preview.7", "v0.15.0", "v0.16.0", "v1.0.0-rc.1", "vnext", "v0.14"];
  const { spawn, calls } = fakeGh({ tags });
  expect(previousTag("0.15.0", spawn)).toBe("v0.14.0");
  expect(calls.at(-1)).toEqual(["gh", "api", "--paginate", `repos/${REPOSITORY}/git/matching-refs/tags/v`, "--jq", ".[].ref"]);
  expect(previousTag("1.0.0-rc.2", fakeGh({ tags }).spawn)).toBe("v1.0.0-rc.1");
  expect(previousTag("1.0.0", fakeGh({ tags }).spawn)).toBe("v1.0.0-rc.1");
  expect(previousTag("0.7.0-preview.1", fakeGh({ tags }).spawn)).toBe("v0.6.0-preview.7");
  expect(previousTag("0.6.0-preview.7", fakeGh({ tags }).spawn)).toBeUndefined();
  for (const [a, b] of [["0.9.0", "0.10.0"], ["1.0.0-rc.1", "1.0.0"], ["1.0.0-rc.2", "1.0.0-rc.10"], ["1.0.0-alpha", "1.0.0-alpha.1"],
    ["1.0.0-1", "1.0.0-alpha"], ["0.7.0-preview.4", "0.8.0"]]) expect([a, b, Math.sign(compareVersions(a, b))]).toEqual([a, b, -1]);
  expect(compareVersions("0.15.0", "0.15.0+build")).toBe(0);

  let created = fakeGh({ tags: ["v1.3.0", "v1.4.0"] });
  createRelease("1.5.0", sha, ["p/nuget/A.nupkg"], created.spawn, "eng/release/notes/1.5.0.md");
  const create = created.calls.at(-1);
  expect(create[create.indexOf("--notes-start-tag") + 1]).toBe("v1.4.0");
  expect(create.indexOf("--notes-start-tag")).toBeGreaterThan(create.indexOf("--generate-notes"));
  created = fakeGh({ tags: [] });
  createRelease("1.5.0", sha, ["p/nuget/A.nupkg"], created.spawn, undefined);
  expect(created.calls.at(-1)).not.toContain("--notes-start-tag");
  // A failed listing stops before the release is created, as does any other lookup error.
  created = fakeGh({ listError: true });
  expect(() => createRelease("1.5.0", sha, ["p/nuget/A.nupkg"], created.spawn, undefined)).toThrow("Tag listing failed");
  expect(writes(created.calls)).toBe(false);
  // A rerun that finds a release of this commit does not list tags.
  created = fakeGh({ release: true, draft: true, assets: ["A.nupkg"] });
  createRelease("1.5.0", sha, ["p/nuget/A.nupkg"], created.spawn, undefined);
  expect(created.calls.some(c => c.includes("--paginate"))).toBe(false);
});

test("GitHub marks 0.y.z and prereleases as prereleases and a stable version from 1.0.0 as a release", () => {
  for (const version of ["0.9.0", "0.7.0-preview.4", "1.0.0-rc.1", "1.0.1-rc.1", "1.1.0-preview.1"]) expect([version, githubPrerelease(version)]).toEqual([version, true]);
  for (const version of ["1.0.0", "1.0.1", "1.1.0", "2.0.0"]) expect([version, githubPrerelease(version)]).toEqual([version, false]);
  expect(releaseCheck("1.0.0-rc.1", sha, fakeGh().spawn)).toContain("Would create the prerelease v1.0.0-rc.1");
  expect(releaseCheck("1.0.0", sha, fakeGh().spawn)).toContain("Would create the release v1.0.0");
  for (const [version, flagged] of [["0.9.0", true], ["1.0.0-rc.1", true], ["1.0.0", false]]) {
    let { spawn, calls } = fakeGh();
    createRelease(version, sha, ["p/nuget/A.nupkg"], spawn, undefined);
    expect([version, calls.at(-1).includes("--prerelease")]).toEqual([version, flagged]);
    ({ spawn, calls } = fakeGh({ release: true, draft: true, assets: ["A.nupkg"] }));
    createRelease(version, sha, ["p/nuget/A.nupkg"], spawn, undefined);
    expect(calls.at(-1)).toContain(`--prerelease=${flagged}`);
  }
});

test("from 1.0 the package validation baseline is a stable release, never a prerelease", () => {
  const props = readFileSync(new URL("../Versions.props", import.meta.url), "utf8");
  const version = /<RunicTranslationsVersion>([^<]+)</.exec(props)[1];
  const baseline = /<RunicTranslationsPackageValidationBaselineVersion>([^<]+)</.exec(props)[1];
  // Through 1.0.0-rc.N the baseline stays at the last 0.y.z release; after 1.0.0 it is 1.0.0 (eng/release/README.md).
  if (!version.startsWith("0.")) expect(baseline).not.toContain("-");
});

test("publication reuses the CI artifacts and never reruns tests or packing", () => {
  const ci = workflow("ci.yml"), release = workflow("publish-preview.yml");
  expect(ci.jobs["build-and-test"].steps.find(s => s.uses?.startsWith("actions/upload-artifact@")).with.name).toBe(ARTIFACT);
  // The editor extensions release from runic-editors: neither workflow builds or downloads a VSIX.
  expect(JSON.stringify([ci, release])).not.toMatch(/vsix/i);
  expect(release.jobs.candidate.steps.find(s => s.id === "ci").run).toBe("bun eng/release/ci-artifact.mjs find-ci");
  expect(release.jobs.candidate.if).toContain("github.ref == 'refs/heads/main'");
  for (const [name, source] of [["candidate", "steps.ci"], ["publish", "needs.candidate"]]) {
    const job = release.jobs[name];
    expect(job.steps.filter(s => s.run).map(s => s.run).join("\n")).not.toMatch(/run\.mjs (test|pack|verify)|verify-packages|verify:candidate/);
    // By id, so a re-upload under the same name cannot change what is published.
    const downloads = job.steps.filter(s => s.uses?.startsWith("actions/download-artifact@") && s.with["artifact-ids"]).map(s => s.with);
    const runId = `\${{ ${source}.outputs.${source === "steps.ci" ? "run-id" : "ci-run-id"} }}`;
    expect(downloads).toEqual([
      { "artifact-ids": `\${{ ${source}.outputs.artifact-id }}`, path: "artifacts/packages", "run-id": runId, "github-token": "${{ github.token }}" }]);
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
  expect(candidate.steps.some(s => s.uses?.startsWith("NuGet/login") || /gh release create|ci-artifact\.mjs release /.test(s.run ?? ""))).toBe(false);
  const checks = candidate.steps.filter(s => /publish\.mjs|release-check/.test(s.run ?? "")).map(s => s.run);
  expect(checks).toEqual(['bun eng/release/publish.mjs publish "$VERSION" --dry-run', 'bun eng/release/ci-artifact.mjs release-check "$VERSION"',
    'bun eng/release/publish.mjs tag-latest "$VERSION" --dry-run']);
  expect(publish.if).toBe("${{ !inputs.dry-run }}");
  expect(publish.environment).toBe("preview");
  expect(publish.permissions).toEqual({ contents: "write", "id-token": "write", attestations: "write", actions: "read" });
  for (const permission of ["id-token", "attestations", "contents"])
    expect(Object.entries(release.jobs).filter(([, job]) => job.permissions?.[permission] === "write").map(([name]) => name)).toEqual(["publish"]);
  expect(publish.steps.find(s => s.run?.startsWith("npm install --global")).run.trim()).toBe("npm install --global npm@12.2.0 --ignore-scripts");
  const runs = publish.steps.filter(s => s.run).map(s => s.run);
  expect(runs.join("\n")).not.toContain("--dry-run");
  const index = text => runs.findIndex(r => r.includes(text));
  expect(runs[index("ci-artifact.mjs verify")]).toBe('bun eng/release/ci-artifact.mjs verify artifacts/packages artifacts/release/packages.json "$VERSION" "$CI_RUN_ID"');
  expect(index("ci-artifact.mjs verify")).toBeLessThan(index("publish.mjs publish"));
  expect(runs[index("ci-artifact.mjs release ")]).toBe('bun eng/release/ci-artifact.mjs release "$VERSION" artifacts/packages "artifacts/release/$SBOM"');
  expect(index("publish.mjs publish")).toBeLessThan(index("ci-artifact.mjs release "));
  expect(index("ci-artifact.mjs release ")).toBeLessThan(index("publish.mjs tag-latest"));
});

test("only the publish job attests, after verifying and before publishing, every package and release asset", () => {
  const release = workflow("publish-preview.yml");
  expect(Object.entries(release.jobs).filter(([, job]) => (job.steps ?? []).some(s => s.uses?.startsWith("actions/attest"))).map(([name]) => name)).toEqual(["publish"]);
  const steps = release.jobs.publish.steps;
  const attest = steps.filter(s => s.uses?.startsWith("actions/attest"));
  expect(attest.map(s => s.uses.split("@")[0])).toEqual(["actions/attest-build-provenance", "actions/attest"]);
  for (const step of attest) expect(step.uses).toMatch(/@[0-9a-f]{40}$/);
  const lines = step => step.with["subject-path"].trim().split("\n");
  const files = ["artifacts/packages/nuget/*.nupkg", "artifacts/packages/npm/*.tgz"];
  const sbom = "artifacts/release/${{ needs.candidate.outputs.sbom }}";
  expect(lines(attest[0])).toEqual([...files, sbom]);
  expect(lines(attest[1])).toEqual(files);
  expect(attest[1].with["sbom-path"]).toBe(sbom);
  const index = predicate => steps.findIndex(predicate);
  const first = index(s => s.uses?.startsWith("actions/attest"));
  expect(index(s => s.run?.includes("sha256sum --check --strict"))).toBeLessThan(index(s => s.run?.includes("ci-artifact.mjs verify")));
  expect(index(s => s.run?.includes("ci-artifact.mjs verify"))).toBeLessThan(first);
  for (const write of ["publish.mjs publish", "ci-artifact.mjs release ", "publish.mjs tag-latest"]) expect(index(s => s.run?.includes(write))).toBeGreaterThan(first);
  expect(index(s => s.uses?.startsWith("NuGet/login"))).toBeGreaterThan(first);
});

test("the read-only candidate describes the release and hands its files to publish by hash", () => {
  const { candidate, publish } = workflow("publish-preview.yml").jobs;
  const describe = candidate.steps.find(s => s.id === "describe");
  expect(describe.run).toContain('bun eng/release/ci-artifact.mjs describe artifacts/packages "$VERSION" artifacts/release');
  expect(candidate.steps.find(s => s.id === "prepare").run).toContain('prepare artifacts/packages "$VERSION" "$CI_RUN_ID" artifacts/release/packages.json');
  expect(describe.run).toContain("sha256sum -- *");
  expect(candidate.outputs["release-sha256"]).toBe("${{ steps.describe.outputs.sha256 }}");
  expect(candidate.outputs.sbom).toBe("${{ steps.describe.outputs.sbom }}");
  const upload = candidate.steps.find(s => s.uses?.startsWith("actions/upload-artifact@"));
  expect(candidate.steps.indexOf(upload)).toBeGreaterThan(candidate.steps.indexOf(describe));
  expect(upload.with.path).toBe("artifacts/release");
  const check = publish.steps.find(s => s.run?.includes("sha256sum --check --strict"));
  expect(check["working-directory"]).toBe("artifacts/release");
  expect(check.env.RELEASE_SHA256).toBe("${{ needs.candidate.outputs.release-sha256 }}");
  expect(publish.steps.indexOf(check)).toBeGreaterThan(publish.steps.findIndex(s => s.with?.name === "release-candidate-${{ github.run_id }}"));
});

test("Publish preview rejects an unsupported version before looking for CI artifacts", () => {
  const steps = workflow("publish-preview.yml").jobs.candidate.steps;
  const index = steps.findIndex(step => step.name === "Validate version");
  expect(index).toBeGreaterThanOrEqual(0);
  expect(index).toBeLessThan(steps.findIndex(step => step.name === "Find the successful CI run for this commit"));
  // The format check, without the comparisons with eng/workspace.json and eng/Versions.props that follow it.
  const script = steps[index].run.split("\n");
  const formatCheck = script.slice(0, script.indexOf("fi") + 1).join("\n");
  expect(script.slice(script.indexOf("fi") + 1).every(line => !line || line.startsWith("test "))).toBe(true);
  const validate = version => spawnSync("bash", ["-c", formatCheck], { encoding: "utf8", env: { ...process.env, VERSION: version } });
  for (const version of ["0.6.0-rc.500", "0.6.0-preview.0", "0.6.0-preview.500", "0.6.0-beta", "0.6.0+build", "06.0.0", "0.6"]) {
    const refused = validate(version);
    expect(refused.status).not.toBe(0);
    expect(refused.stdout).toContain(`::error::Version '${version}' is not a plain x.y.z`);
  }
  for (const version of ["0.12.0", "1.0.0-preview.1", "1.0.0-rc.1", "1.0.0-rc.499", "1.0.0", "1.0.1"])
    expect([version, validate(version).status]).toEqual([version, 0]);
});

test("hand-written release notes are used only when the version has a notes file", () => {
  const directory = mkdtempSync(join(tmpdir(), "runic-translations-notes-"));
  try {
    writeFileSync(join(directory, "1.2.3.md"), "# Notes\n");
    expect(releaseNotes("1.2.3", directory)).toBe(join(directory, "1.2.3.md"));
    expect(releaseNotes("1.2.4", directory)).toBeUndefined();
    // Fragments not assembled by the release pull request stop the release.
    mkdirSync(join(directory, "1.2.5", "fixes"), { recursive: true });
    expect(() => releaseNotes("1.2.5", directory)).toThrow("run bun eng/release/notes.mjs assemble 1.2.5");
  } finally { rmSync(directory, { recursive: true, force: true }); }
});

test("publishing needs the train gate (row P-tr), and every managed CI job can build against the train feed", () => {
  const { candidate, train, publish } = workflow("publish-preview.yml").jobs;
  expect(publish.needs).toEqual(["candidate", "train"]);
  expect(train.needs).toBe("candidate");
  expect(train.if).toBeUndefined();
  const [reusable, sha] = train.uses.split("@");
  expect(reusable).toBe("Runic-Artifex/runic-sdk/.github/workflows/train-compat.yml");
  expect(sha).toMatch(/^[0-9a-f]{40}$/);
  expect(train.with).toEqual({ row: "P-tr", cli: "published:${{ needs.candidate.outputs.command-line-version }}",
    translations: "run:${{ needs.candidate.outputs.ci-run-id }}", sdk: train.with.sdk, "gate-ref": sha });
  // runic-sdk's run is selected by commit, never "the newest" (runic-sdk eng/train/README.md, "Run selection by commit").
  expect(train.with.sdk).toMatch(/^run@[0-9a-f]{40}$/);
  expect(train.permissions).toEqual({ contents: "read", actions: "read" });
  expect(candidate.outputs["command-line-version"]).toBe("${{ steps.command-line.outputs.version }}");

  const { jobs } = workflow("ci.yml");
  expect(jobs["train-feed"].steps.find(s => s.id === "feed").run).toBe("bun eng/train-feed.mjs prepare");
  expect(jobs["train-feed"].steps.find(s => s.uses?.startsWith("actions/upload-artifact@")).with.name).toBe("train-feed");
  for (const [name, job] of Object.entries(jobs)) {
    if (!job.steps.some(s => s.uses?.startsWith("actions/setup-dotnet@")) || name === "train-feed") continue;
    expect([job.needs].flat()).toContain("train-feed");
    const feed = job.steps.findIndex(s => s.uses === "./.github/actions/train-feed");
    expect(feed, `${name} uses the train feed`).toBeGreaterThan(0);
    expect(job.steps[feed].with.active).toBe("${{ needs.train-feed.outputs.active }}");
  }
  expect(jobs.verify.needs).toContain("train-feed");
});
