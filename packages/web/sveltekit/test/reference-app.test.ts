import { execFile } from "node:child_process";
import { readFile, rm } from "node:fs/promises";
import { promisify } from "node:util";
import { resolve } from "node:path";
import { afterEach, describe, expect, test } from "vitest";

const execute = promisify(execFile);
const fixture = resolve("test/fixtures/translations-app");
const output = resolve(fixture, "build");
const generated = resolve(fixture, ".svelte-kit");
const vite = resolve("../../node_modules/vite/bin/vite.js");

afterEach(async () => {
  await Promise.all([
    rm(output, { recursive: true, force: true }),
    rm(generated, { recursive: true, force: true }),
  ]);
});

describe("translation routing reference application", () => {
  test("production-builds unprefixed English and prefixed German routes", async () => {
    await execute(process.execPath, [vite, "build"], { cwd: fixture });
    const english = await readFile(resolve(output, "setup.html"), "utf8");
    const german = await readFile(resolve(output, "de/setup.html"), "utf8");

    expect(english).toContain('lang="en"');
    expect(english).toContain("<h1>Setup</h1>");
    expect(english).toContain('href="./de/setup"');
    expect(german).toContain('lang="de"');
    expect(german).toContain("<h1>Einrichtung</h1>");
    expect(german).toContain('href="../setup"');
  }, 30_000);
});
