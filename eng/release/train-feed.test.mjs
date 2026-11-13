import { expect, test } from "bun:test";
import { mkdtempSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { COMMAND_LINE_PACKAGES, commandLineVersion, feedPackages, trainFeedNugetConfig } from "../train-feed.mjs";

test("the train feed reads the Command Line version of eng/Versions.props", () => {
  expect(commandLineVersion("<Project><PropertyGroup><RunicCommandLineVersion>0.12.0</RunicCommandLineVersion></PropertyGroup></Project>")).toBe("0.12.0");
  expect(commandLineVersion()).toMatch(/^\d+\.\d+\.\d+/);
  expect(() => commandLineVersion("<Project />")).toThrow("no RunicCommandLineVersion");
});

test("consumer NuGet.config files map Runic.CommandLine* to the train feed only when one is in use", () => {
  expect(trainFeedNugetConfig({})).toEqual({ source: "", mapping: "" });
  const { source, mapping } = trainFeedNugetConfig({ RUNIC_TRAIN_FEED: "/feeds/a&b" });
  expect(source).toBe('<add key="train-command-line" value="/feeds/a&amp;b"/>');
  expect(mapping).toBe('<packageSource key="train-command-line"><package pattern="Runic.CommandLine*"/></packageSource>');
});

test("a train feed holds every Command Line package of the version, with its SHA-256", () => {
  const directory = mkdtempSync(join(tmpdir(), "runic-train-feed-test-"));
  try {
    for (const id of COMMAND_LINE_PACKAGES.slice(1)) writeFileSync(join(directory, `${id}.0.12.0.nupkg`), id);
    expect(() => feedPackages(directory, "0.12.0")).toThrow("lacks Runic.CommandLine.0.12.0.nupkg");
    writeFileSync(join(directory, "Runic.CommandLine.0.12.0.nupkg"), "x");
    const packages = feedPackages(directory, "0.12.0");
    expect(packages.map(entry => entry.file)).toEqual(COMMAND_LINE_PACKAGES.map(id => `${id}.0.12.0.nupkg`));
    expect(packages[0].sha256).toBe("2d711642b726b04401627ca9fbac32f5c8530fb1903cc4db02258717921a4881");
  } finally {
    rmSync(directory, { recursive: true, force: true });
  }
});
