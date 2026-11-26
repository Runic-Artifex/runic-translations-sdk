#!/usr/bin/env bun
// The language-neutral conformance driver (eng/conformance/README.md). From the repository root:
//
//   bun eng/conformance/cli.ts <implementation> [--only <text>] [--set <set>] [--verbose] ...
//   bun eng/conformance/cli.ts list | cases [--write] | lint | check <implementation>
//
// The command is runic-sdk's driver core, vendored unchanged in core/ (core.vendor.json). This file registers this
// repository's corpus modules; it has none yet (R7 G16 adds the Translations module).
import { resolve } from "node:path";
import { createDriver, type CorpusModule } from "./core/index";

export const ROOT = resolve(import.meta.dir, "../..");

// The corpus modules: one per corpus format the driver runs. A corpus's directory holds its cases.json and status/.
export const corpora: Record<string, CorpusModule> = {};

const driver = createDriver({ root: ROOT, dir: import.meta.dir, corpora, command: "bun eng/conformance/cli.ts" });
export const { implementations, anchorExists } = driver;

if (import.meta.main) process.exit(await driver.main());
