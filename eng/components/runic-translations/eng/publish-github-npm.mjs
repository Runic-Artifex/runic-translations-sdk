import crypto from "node:crypto";
import fs from "node:fs";
import { spawnSync } from "node:child_process";

const registry = "https://npm.pkg.github.com";

function sha256(value) {
  return crypto.createHash("sha256").update(value).digest("hex");
}

function manifest(tarball) {
  const result = spawnSync("tar", ["-xOf", tarball, "package/package.json"], { encoding: "utf8" });
  if (result.status !== 0) throw new Error(`cannot read ${tarball}: ${result.stderr}`);
  return JSON.parse(result.stdout);
}

async function publishedBytes(name, version, token) {
  const metadata = await fetch(`${registry}/${encodeURIComponent(name)}`, {
    headers: { Accept: "application/json", Authorization: `Bearer ${token}` },
  });
  if (metadata.status === 404) return undefined;
  if (!metadata.ok) throw new Error(`registry metadata request failed: ${metadata.status}`);
  const tarballUrl = (await metadata.json()).versions?.[version]?.dist?.tarball;
  if (!tarballUrl) return undefined;
  const response = await fetch(tarballUrl, { headers: { Authorization: `Bearer ${token}` } });
  if (!response.ok) throw new Error(`registry tarball request failed: ${response.status}`);
  return Buffer.from(await response.arrayBuffer());
}

const [, , tarball, tag = "ci"] = process.argv;
const token = process.env.NODE_AUTH_TOKEN;
if (!tarball || !token || !["ci", "release-staging"].includes(tag)) {
  throw new Error("usage: NODE_AUTH_TOKEN=... node eng/publish-github-npm.mjs <tarball> [ci|release-staging]");
}

const packageManifest = manifest(tarball);
const localBytes = fs.readFileSync(tarball);
const existing = await publishedBytes(packageManifest.name, packageManifest.version, token);
if (existing) {
  if (sha256(existing) !== sha256(localBytes)) {
    throw new Error(`immutable coordinate collision for ${packageManifest.name}@${packageManifest.version}`);
  }
  console.log(`reused: ${packageManifest.name}@${packageManifest.version}`);
} else {
  const result = spawnSync("bun", ["publish", tarball, "--registry", registry, "--tag", tag, "--access", "restricted"], {
    encoding: "utf8",
    env: { ...process.env, NODE_AUTH_TOKEN: token },
  });
  if (result.status !== 0) throw new Error(`publish failed: ${result.stdout}${result.stderr}`);
  console.log(`published: ${packageManifest.name}@${packageManifest.version}`);
}
