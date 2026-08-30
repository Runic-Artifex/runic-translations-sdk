import { execFileSync } from "node:child_process";
import { createHash } from "node:crypto";
import { createReadStream, readFileSync, writeFileSync } from "node:fs";
import { createServer } from "node:http";
import { basename, resolve } from "node:path";

const [readyFile, ...archives] = process.argv.slice(2);
if (!readyFile || archives.length === 0) {
  throw new Error("Usage: local-npm-registry.mjs <ready-file> <npm-archive>...");
}

const packages = new Map();
for (const archive of archives) {
  const absoluteArchive = resolve(archive);
  const manifest = JSON.parse(execFileSync("tar", ["-xOf", absoluteArchive, "package/package.json"], { encoding: "utf8" }));
  if (typeof manifest.name !== "string" || typeof manifest.version !== "string" || !manifest.name.startsWith("@runic-artifex/")) {
    throw new Error(`Expected a Runic Artifex archive, found ${basename(absoluteArchive)}.`);
  }
  const bytes = readFileSync(absoluteArchive);
  packages.set(manifest.name, {
    archive: absoluteArchive,
    integrity: `sha512-${createHash("sha512").update(bytes).digest("base64")}`,
    manifest,
    shasum: createHash("sha1").update(bytes).digest("hex"),
  });
}

const server = createServer((request, response) => {
  const path = new URL(request.url ?? "/", "http://127.0.0.1").pathname;
  for (const [name, entry] of packages) {
    const archivePath = `/archives/${basename(entry.archive)}`;
    if (decodeURIComponent(path.slice(1)) === name) {
      const tarball = `http://127.0.0.1:${server.address().port}${archivePath}`;
      response.writeHead(200, { "content-type": "application/json" });
      response.end(JSON.stringify({
        name,
        "dist-tags": { latest: entry.manifest.version },
        versions: {
          [entry.manifest.version]: {
            ...entry.manifest,
            dist: { integrity: entry.integrity, shasum: entry.shasum, tarball },
          },
        },
      }));
      return;
    }
    if (path === archivePath) {
      response.writeHead(200, { "content-type": "application/octet-stream" });
      createReadStream(entry.archive).pipe(response);
      return;
    }
  }
  response.writeHead(404);
  response.end();
});

server.listen(0, "127.0.0.1", () => {
  const address = server.address();
  if (!address || typeof address === "string") throw new Error("The local npm registry did not bind a loopback port.");
  writeFileSync(readyFile, `http://127.0.0.1:${address.port}\n`, "utf8");
});

process.on("SIGTERM", () => server.close(() => process.exit(0)));
process.on("SIGINT", () => server.close(() => process.exit(0)));
