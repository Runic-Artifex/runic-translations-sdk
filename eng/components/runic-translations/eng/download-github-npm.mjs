import crypto from "node:crypto";
import fs from "node:fs";
import path from "node:path";

const registry = "https://npm.pkg.github.com";
const packageName = "@runic-artifex/vite-plugin-runic-translations";
const [, , outputDirectory, version] = process.argv;
const token = process.env.NODE_AUTH_TOKEN;
if (!outputDirectory || !version || !token) {
  throw new Error("usage: NODE_AUTH_TOKEN=... node eng/download-github-npm.mjs <directory> <version>");
}

const metadataResponse = await fetch(`${registry}/${encodeURIComponent(packageName)}`, {
  headers: { Accept: "application/json", Authorization: `Bearer ${token}` },
});
if (!metadataResponse.ok) throw new Error(`registry metadata request failed: ${metadataResponse.status}`);
const distribution = (await metadataResponse.json()).versions?.[version]?.dist;
if (!distribution?.tarball || !distribution.integrity) {
  throw new Error(`GitHub Packages does not contain ${packageName}@${version}`);
}
const response = await fetch(distribution.tarball, { headers: { Authorization: `Bearer ${token}` } });
if (!response.ok) throw new Error(`registry tarball request failed: ${response.status}`);
const tarball = Buffer.from(await response.arrayBuffer());
const integrity = `sha512-${crypto.createHash("sha512").update(tarball).digest("base64")}`;
if (integrity !== distribution.integrity) throw new Error(`registry integrity mismatch for ${packageName}@${version}`);

fs.mkdirSync(outputDirectory, { recursive: true });
fs.writeFileSync(path.join(outputDirectory, `runic-artifex-vite-plugin-runic-translations-${version}.tgz`), tarball);
console.log(`downloaded: ${packageName}@${version}`);
