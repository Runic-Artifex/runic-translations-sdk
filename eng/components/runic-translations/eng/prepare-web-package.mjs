import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const versionPattern = /^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-[0-9A-Za-z.-]+)?(?:\+[0-9A-Za-z.-]+)?$/;
const revisionPattern = /^[0-9a-f]{40}$/;
const registries = new Map([
  ["github", { url: "https://npm.pkg.github.com", access: "restricted" }],
  ["public", { url: "https://registry.npmjs.org", access: "public" }],
]);

export function prepareWebPackage(root, version, revision, registryName, commandLineVersion) {
  if (!versionPattern.test(version) || !versionPattern.test(commandLineVersion)) {
    throw new Error("package and dependency versions must be valid SemVer");
  }
  if (!revisionPattern.test(revision)) {
    throw new Error("revision must be a lowercase 40-character Git SHA");
  }
  const registry = registries.get(registryName);
  if (!registry) throw new Error("registry must be github or public");

  const manifestPath = path.join(root, "web", "package.json");
  const manifest = JSON.parse(fs.readFileSync(manifestPath, "utf8"));
  manifest.version = version;
  manifest.gitHead = revision;
  manifest.publishConfig = { registry: registry.url, access: registry.access };
  manifest.runicCandidate = {
    sourceRevision: revision,
    dependencies: [
      { ecosystem: "nuget", package: "Runic.CommandLine", version: commandLineVersion },
    ],
  };
  fs.writeFileSync(manifestPath, `${JSON.stringify(manifest, null, 2)}\n`);
}

if (import.meta.url === `file://${process.argv[1]}`) {
  const [, , version, revision, registryName, commandLineVersion] = process.argv;
  prepareWebPackage(repositoryRoot, version, revision, registryName, commandLineVersion);
}
