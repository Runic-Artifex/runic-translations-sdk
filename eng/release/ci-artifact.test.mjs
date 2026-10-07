import { expect, test } from "bun:test";
import { execFileSync } from "node:child_process";
import { mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { ARTIFACT, CI_WORKFLOW, REPOSITORY, VSIX, createRelease, findCiPackages, prepare, releaseCheck, scanVsix, selectArtifact, selectCiRun, verify } from "./ci-artifact.mjs";
import { writeVsixFixtures } from "./vsix-fixtures.mjs";

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
  const artifact = { id: 70, name: ARTIFACT, expired: false, workflow_run: { id: 7, head_sha: sha } };
  expect(selectArtifact([artifact, { ...artifact, name: "rmf2-vscode-vsix" }], run(7))).toBe(artifact);
  expect(selectArtifact([artifact, { ...artifact, id: 71, name: "rmf2-vscode-vsix" }], run(7), "rmf2-vscode-vsix").id).toBe(71);
  expect(() => selectArtifact([artifact], run(7), "rmf2-visualstudio-vsix")).toThrow("no single rmf2-visualstudio-vsix artifact");
  expect(() => selectArtifact([], run(7))).toThrow("no single");
  expect(() => selectArtifact([{ ...artifact, expired: true }], run(7))).toThrow("expired");
  expect(() => selectArtifact([{ ...artifact, workflow_run: { id: 7, head_sha: "c".repeat(40) } }], run(7))).toThrow();
  expect(() => selectArtifact([{ ...artifact, id: undefined }], run(7))).toThrow("no id");
});

test("queries GitHub for push runs of the commit", async () => {
  const urls = [];
  const fetchImpl = async url => {
    urls.push(new URL(url));
    const artifacts = [ARTIFACT, ...VSIX.map(entry => entry.artifact)].map((name, index) => ({ id: 80 + index, name, expired: false, workflow_run: { id: 8, head_sha: sha } }));
    return Response.json(url.includes("/artifacts") ? { artifacts: artifacts.reverse() } : { workflow_runs: [run(8)] });
  };
  expect(await findCiPackages({ repository: REPOSITORY, sha, token: "t", fetchImpl })).toEqual({ runId: "8", runUrl: run(8).html_url, artifact: ARTIFACT, artifactId: "80",
    vsixArtifactIds: "81,82" });
  expect(urls[0].pathname).toBe(`/repos/${REPOSITORY}/actions/workflows/ci.yml/runs`);
  expect(Object.fromEntries(urls[0].searchParams)).toMatchObject({ head_sha: sha, event: "push", branch: "main" });
  await expect(findCiPackages({ repository: REPOSITORY, sha, token: "t", fetchImpl: async () => new Response("", { status: 403 }) })).rejects.toThrow("failed: 403");
});

test("packages and VSIX files must be exactly the release set at the version, packed from the commit", () => {
  const directory = mkdtempSync(join(tmpdir(), "runic-translations-ci-artifact-"));
  const vsix = mkdtempSync(join(tmpdir(), "runic-translations-vsix-"));
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
    writeVsixFixtures(vsix, version);
    const manifest = prepare(directory, version, sha, "42", inventory, vsix);
    expect(manifest.packages.map(p => p.name)).toEqual(["Runic.Translations", "@runic-artifex/translations-svelte"]);
    expect(manifest.vsix.map(p => [p.file, p.identity.id, p.identity.version])).toEqual([
      ["rmf2-vscode-vsix/runic-translations.vsix", "runic-translations", "1.2.3001"],
      ["rmf2-visualstudio-vsix/runic-translations-visualstudio.vsix", "Runic.Artifex.Translations.Rmf2", "1.2.3.1"]]);
    expect(manifest.vsix.every(p => /^[0-9a-f]{64}$/.test(p.sha256))).toBe(true);
    verify(directory, manifest, sha, version, "42", inventory, vsix);
    expect(() => verify(directory, manifest, "b".repeat(40), version, "42", inventory, vsix)).toThrow("Inventory is for");
    expect(() => verify(directory, manifest, sha, "1.2.3-preview.2", "42", inventory, vsix)).toThrow("version");
    expect(() => verify(directory, manifest, sha, version, "43", inventory, vsix)).toThrow("CI run");
    expect(() => verify(directory, manifest, sha, version, "42", inventory)).toThrow("VSIX directory");
    expect(() => prepare(directory, "1.2.3-preview.2", sha, "42", inventory, vsix)).toThrow("exactly");
    writeFileSync(join(vsix, "rmf2-vscode-vsix", "runic-translations.vsix"), "changed");
    expect(() => verify(directory, manifest, sha, version, "42", inventory, vsix)).toThrow();
    writeVsixFixtures(vsix, version);
    writeFileSync(join(vsix, "rmf2-vscode-vsix", "extra.vsix"), "extra");
    expect(() => prepare(directory, version, sha, "42", inventory, vsix)).toThrow("must contain only");
    rmSync(join(vsix, "rmf2-vscode-vsix"), { recursive: true });
    expect(() => prepare(directory, version, sha, "42", inventory, vsix)).toThrow("exactly the IDE extension artifacts");
    pack("1.2.3-preview.2");
    writeVsixFixtures(vsix, version);
    expect(() => prepare(directory, version, sha, "42", inventory, vsix)).toThrow("declares version");
    expect(() => verify(directory, manifest, sha, version, "42", inventory, vsix)).toThrow();
    pack(version, "b".repeat(40));
    expect(() => prepare(directory, version, sha, "42", inventory, vsix)).toThrow("packed from");
    pack();
    writeFileSync(join(directory, "npm", "stale.tgz"), "stale");
    expect(() => prepare(directory, version, sha, "42", inventory, vsix)).toThrow("exactly");
  } finally { rmSync(directory, { recursive: true, force: true }); rmSync(vsix, { recursive: true, force: true }); }
});

test("each VSIX must carry the version CI stamps for the release", () => {
  const vsix = mkdtempSync(join(tmpdir(), "runic-translations-vsix-"));
  const check = (version, overrides) => { rmSync(vsix, { recursive: true, force: true }); writeVsixFixtures(vsix, version, overrides); return () => scanVsix(vsix, version); };
  try {
    expect(check("0.6.0-preview.2")().map(p => p.identity)).toEqual([
      { id: "runic-translations", version: "0.6.2", publisher: "runic-artifex" },
      { id: "Runic.Artifex.Translations.Rmf2", version: "0.6.0.2", publisher: "Runic Artifex" }]);
    expect(check("0.6.0")().map(p => p.identity.version)).toEqual(["0.6.1000", "0.6.0.1000"]);
    // The unstamped placeholder of the committed manifests is never released.
    expect(check("0.6.0-preview.2", { vscode: { version: "0.0.1", packageVersion: "0.0.1" } })).toThrow("declares version 0.0.1, not 0.6.2");
    expect(check("0.6.0-preview.2", { visualStudio: { version: "0.0.1" } })).toThrow("declares version 0.0.1, not 0.6.0.2");
    expect(check("0.6.0-preview.2", { visualStudio: { version: "0.6.2" } })).toThrow("not 0.6.0.2");
    expect(check("0.6.0-preview.2", { vscode: { packageVersion: "0.0.1" } })).toThrow("bundles package.json version 0.0.1");
    expect(check("0.6.0-preview.2", { vscode: { preRelease: false } })).toThrow("is not marked pre-release");
    expect(check("0.6.0", { vscode: { preRelease: true } })).toThrow("is marked pre-release");
    expect(check("0.6.0-preview.2", { visualStudio: { id: "Other" } })).toThrow("declares extension Other");
    expect(check("0.6.0-preview.2", { vscode: { publisher: "someone" } })).toThrow("declares publisher someone");
    check("0.6.0-preview.2");
    expect(() => scanVsix(vsix, "0.6.0-rc.1")).toThrow("cannot express");
  } finally { rmSync(vsix, { recursive: true, force: true }); }
});

const fakeGh = ({ release = false, tag, draft = false, assets = [], target = sha, annotated = false, tagError = false, releaseError = false } = {}) => {
  const calls = [];
  const spawn = (command, args) => {
    calls.push([command, ...args]);
    if (args[0] === "release" && args[1] === "view") {
      if (releaseError) return { status: 1, stdout: "", stderr: "HTTP 502: Bad Gateway" };
      return release ? { status: 0, stdout: JSON.stringify({ url: "u", isDraft: draft, targetCommitish: target, assets: assets.map(name => ({ name })) }) }
        : { status: 1, stdout: "", stderr: "release not found" };
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
  expect(calls.map(c => c.slice(0, 3))).toEqual([["gh", "release", "view"], ["gh", "api", `repos/${REPOSITORY}/git/ref/tags/v1.2.3`]]);
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
  expect(calls.filter(c => c[1] === "api").map(c => c[2])).toEqual([`repos/${REPOSITORY}/git/ref/tags/v1.2.3`, `repos/${REPOSITORY}/git/tags/${"e".repeat(40)}`]);
});

test("a rerun keeps a release of this commit, never modifies a published one and resumes a matching draft", () => {
  const files = ["p/nuget/A.nupkg", "p/npm/b.tgz"];
  let { spawn, calls } = fakeGh();
  expect(createRelease("1.2.3", sha, files, spawn)).toContain("Created");
  const create = calls.at(-1);
  expect(create.slice(0, 3)).toEqual(["gh", "release", "create"]);
  expect(create[create.indexOf("--target") + 1]).toBe(sha);
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

test("publication reuses the CI artifacts and never reruns tests or packing", () => {
  const ci = workflow("ci.yml"), release = workflow("publish-preview.yml");
  expect(ci.jobs["build-and-test"].steps.find(s => s.uses?.startsWith("actions/upload-artifact@")).with.name).toBe(ARTIFACT);
  // Kept as long as the package artifact, so a release can reuse them.
  for (const { artifact } of VSIX)
    expect(Object.values(ci.jobs).flatMap(job => job.steps).find(s => s.with?.name === artifact).with["retention-days"]).toBe(30);
  expect(release.jobs.candidate.outputs["vsix-artifact-ids"]).toBe("${{ steps.ci.outputs.vsix-artifact-ids }}");
  expect(release.jobs.candidate.steps.find(s => s.id === "ci").run).toBe("bun eng/release/ci-artifact.mjs find-ci");
  expect(release.jobs.candidate.if).toContain("github.ref == 'refs/heads/main'");
  for (const [name, source] of [["candidate", "steps.ci"], ["publish", "needs.candidate"]]) {
    const job = release.jobs[name];
    expect(job.steps.filter(s => s.run).map(s => s.run).join("\n")).not.toMatch(/run\.mjs (test|pack|verify)|verify-packages|verify:candidate/);
    // By id, so a re-upload under the same name cannot change what is published.
    const downloads = job.steps.filter(s => s.uses?.startsWith("actions/download-artifact@") && s.with["artifact-ids"]).map(s => s.with);
    const runId = `\${{ ${source}.outputs.${source === "steps.ci" ? "run-id" : "ci-run-id"} }}`;
    expect(downloads).toEqual([
      { "artifact-ids": `\${{ ${source}.outputs.artifact-id }}`, path: "artifacts/packages", "run-id": runId, "github-token": "${{ github.token }}" },
      { "artifact-ids": `\${{ ${source}.outputs.vsix-artifact-ids }}`, path: "artifacts/vsix", "run-id": runId, "github-token": "${{ github.token }}" }]);
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
  expect(runs[index("ci-artifact.mjs verify")]).toBe('bun eng/release/ci-artifact.mjs verify artifacts/packages artifacts/release/packages.json "$VERSION" "$CI_RUN_ID" artifacts/vsix');
  expect(index("ci-artifact.mjs verify")).toBeLessThan(index("publish.mjs publish"));
  expect(runs[index("ci-artifact.mjs release ")]).toBe('bun eng/release/ci-artifact.mjs release "$VERSION" artifacts/packages artifacts/vsix "artifacts/release/$SBOM"');
  expect(index("publish.mjs publish")).toBeLessThan(index("ci-artifact.mjs release "));
  expect(index("ci-artifact.mjs release ")).toBeLessThan(index("publish.mjs tag-latest"));
});

test("only the publish job attests, after verifying and before publishing, every package, VSIX and release asset", () => {
  const release = workflow("publish-preview.yml");
  expect(Object.entries(release.jobs).filter(([, job]) => job.steps.some(s => s.uses?.startsWith("actions/attest"))).map(([name]) => name)).toEqual(["publish"]);
  const steps = release.jobs.publish.steps;
  const attest = steps.filter(s => s.uses?.startsWith("actions/attest"));
  expect(attest.map(s => s.uses.split("@")[0])).toEqual(["actions/attest-build-provenance", "actions/attest"]);
  for (const step of attest) expect(step.uses).toMatch(/@[0-9a-f]{40}$/);
  const lines = step => step.with["subject-path"].trim().split("\n");
  const files = ["artifacts/packages/nuget/*.nupkg", "artifacts/packages/npm/*.tgz", "artifacts/vsix/*/*.vsix"];
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
  expect(describe.run).toContain('bun eng/release/ci-artifact.mjs describe artifacts/packages artifacts/vsix "$VERSION" artifacts/release');
  expect(candidate.steps.find(s => s.id === "prepare").run).toContain('prepare artifacts/packages "$VERSION" "$CI_RUN_ID" artifacts/release/packages.json artifacts/vsix');
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

test("CI stamps and checks both VSIX files with the mapped release version", () => {
  const { jobs } = workflow("ci.yml");
  expect(jobs["ide-packaging"].steps.find(s => s["working-directory"] === "tools/vscode-runic-translations").run).toContain("bun run package");
  const vsix = jobs.vsix.steps.map(s => s.run ?? "");
  expect(vsix.findIndex(run => run.startsWith("msbuild tools/visualstudio-runic-translations/")))
    .toBeLessThan(vsix.indexOf("python tools/visualstudio-runic-translations/package.py --configuration Release"));
  expect(vsix.join("\n")).not.toContain("RunicIdeReleaseVersion");
});
