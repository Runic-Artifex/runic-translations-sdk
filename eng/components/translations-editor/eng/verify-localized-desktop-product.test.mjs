import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";
import { verifyReceipt } from "./verify-localized-desktop-product.mjs";

const journey = () => ({ schema: "runic.localized-desktop-product/1", isolation: { nuget: ".nuget/packages", bun: ".bun-cache" }, compatibility: { id: "runic-1.0-preview.1", releaseTrainVersion: "1.0.0-preview.1", sha256: "a".repeat(64) }, generated: { catalog: "editor", esmAbiVersion: 2, contractFingerprint: "sha256:" + "a".repeat(64) }, embedded: { sha256: "a".repeat(64) }, negativeGates: ["missing-manifest", "stale-manifest", "forged-manifest-schema", "unsupported-locale", "fingerprint-skew"], localeEvidence: ["en", "de", "structured-interchange"], nugetCandidates: Array.from({ length: 4 }, () => ({ source: "exact-local", archiveSha256: "a".repeat(64) })), npmCandidate: { identity: "@runic-artifex/vite-plugin-runic-translations", source: "exact-local", archiveSha256: "a".repeat(64) }, phases: ["tool-restore", "editor-build", "frontend-check", "manifest-contract", "editor-interchange-smoke", "editor-package", "package-smoke"].map(name => ({ name, status: "passed", exitCode: 0 })) });
test("accepts a complete desktop receipt", () => assert.equal(verifyReceipt({ schema: "runic.localized-desktop-product-repeat/1", journeys: [journey(), journey()] }).ok, true));
test("fails closed for a missing negative gate", () => { const receipt = { schema: "runic.localized-desktop-product-repeat/1", journeys: [journey(), journey()] }; receipt.journeys[1].negativeGates.pop(); assert.equal(verifyReceipt(receipt).ok, false); });
test("fails closed for a non-canonical preview train", () => { const receipt = { schema: "runic.localized-desktop-product-repeat/1", journeys: [journey(), journey()] }; receipt.journeys[1].compatibility.releaseTrainVersion = "1.0.0-preview.2"; assert.equal(verifyReceipt(receipt).ok, false); });
test("repository NuGet configuration has no mutable candidate feed", async () => {
  const configuration = await readFile(new URL("../NuGet.config", import.meta.url), "utf8");
  assert.doesNotMatch(configuration, /candidate-feed|artifacts\/(?:candidate-feed)/);
});
