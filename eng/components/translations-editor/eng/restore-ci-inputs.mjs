#!/usr/bin/env node

import { cp, readFile } from "node:fs/promises";
import { join } from "node:path";

const root = process.cwd();
const backup = process.env.RUNNER_TEMP
  ? join(process.env.RUNNER_TEMP, "runic-editor-candidate-inputs")
  : join(root, ".ci", "candidate-inputs");
let manifest;
try {
  manifest = await readFile(join(backup, "manifest.json"), "utf8");
} catch (error) {
  if (error.code === "ENOENT") process.exit(0);
  throw error;
}
const { inputs } = JSON.parse(manifest);
for (const input of inputs) await cp(join(backup, input), join(root, input));
