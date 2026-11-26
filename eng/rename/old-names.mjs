#!/usr/bin/env node
// Old-name guards (D079.11): writes a family's <Family>.RenamedNames.targets from its old-names.json.
//
//   node <dir>/old-names.mjs            regenerates every output the input names
//   node <dir>/old-names.mjs --check    fails when an output differs from what the input generates
//   node <dir>/old-names.mjs --input <file> [--check]
//
// The guards report, as build errors, an old package ID in the package graph, an old public MSBuild property or item
// that the project still sets, and an old environment variable that is set. The rename minor fills the input's lists
// (from the rename plan's rax-map.json names) and regenerates; the guards ship through 1.0.x and are removed in 2.0.
// The same script is in runic-sdk, runic-translations-sdk and runic-cli-sdk; each repository's input holds its family
// settings, so the copies stay identical. README.md beside the input describes its format.
import { readFileSync, writeFileSync } from "node:fs";
import { dirname, isAbsolute, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const kinds = ["property", "item"];
const msbuildName = /^[A-Za-z_][A-Za-z0-9_-]*$/;
const packageId = /^[A-Za-z0-9_][A-Za-z0-9_.-]*$/;
const environmentName = /^[A-Za-z_][A-Za-z0-9_]*$/;
const diagnosticId = /^[A-Z]{3,}[0-9]{3,}$/;

const xml = text => String(text).replaceAll("&", "&amp;").replaceAll("<", "&lt;").replaceAll(">", "&gt;").replaceAll("\"", "&quot;");
const comment = text => String(text).replaceAll("--", "- -");

function fail(message) {
  throw new Error(`old-names: ${message}`);
}

function entries(input, list, pattern, what) {
  const values = input[list] ?? fail(`the input has no "${list}" list (use [] for none)`);
  if (!Array.isArray(values)) fail(`"${list}" is not a list`);
  const seen = new Set();
  for (const entry of values) {
    for (const field of ["old", "new"]) {
      if (typeof entry[field] !== "string" || !pattern.test(entry[field])) fail(`${list}: ${JSON.stringify(entry)} has no valid ${what} in "${field}"`);
    }
    if (entry.old === entry.new) fail(`${list}: ${entry.old} is renamed to itself`);
    const key = entry.old.toLowerCase();
    if (seen.has(key)) fail(`${list}: ${entry.old} is listed twice`);
    seen.add(key);
  }
  return values;
}

// Validates the input and returns it with every list present.
export function validate(input) {
  if (input.version !== 1) fail(`unsupported version ${input.version}; this script reads version 1`);
  const family = input.family ?? fail("the input has no family");
  if (!/^[A-Z][A-Za-z0-9]*$/.test(family.token ?? "")) fail("family.token must be a PascalCase word, such as RunicSdk");
  if (typeof family.buildName !== "string" || family.buildName === "") fail("family.buildName is missing");
  if (family.helpLinkBase !== undefined && (typeof family.helpLinkBase !== "string" || family.helpLinkBase === "")) fail("family.helpLinkBase is empty");
  for (const list of ["packages", "msbuild", "environment"]) {
    if (!diagnosticId.test(family.diagnostics?.[list] ?? "")) fail(`family.diagnostics.${list} is not a diagnostic ID`);
  }
  if (!Array.isArray(family.outputs) || family.outputs.length === 0) fail("family.outputs names no file");
  entries(input, "packages", packageId, "package ID");
  entries(input, "msbuild", msbuildName, "MSBuild name");
  for (const entry of input.msbuild) {
    if (!kinds.includes(entry.kind)) fail(`msbuild: ${entry.old} has kind ${JSON.stringify(entry.kind)}; use property or item`);
  }
  entries(input, "environment", environmentName, "environment variable name");
  return input;
}

const reference = (kind, name) => kind === "item" ? `@(${name})` : `$(${name})`;

// Returns the text of the guard file for a validated input.
export function render(source) {
  const input = validate(source);
  const { token, buildName, helpLinkBase, diagnostics } = input.family;
  const skip = input.family.skipCondition ? ` and ${input.family.skipCondition}` : "";
  // A help link to the catalog entry, or without a link base the ID as the help keyword.
  const help = id => helpLinkBase ? `HelpLink="${xml(`${helpLinkBase}${id.toLowerCase()}`)}"` : `HelpKeyword="${id}"`;
  const imported = `_${token}RenamedNamesImported`;
  const packageItem = `_${token}RenamedPackage`;
  const nameItem = `_${token}RenamedName`;
  const environmentItem = `_${token}RenamedEnvironmentVariable`;
  const lines = [];
  const add = (...text) => lines.push(...text);

  add(
    "<Project>",
    `  <!-- Generated from ${input.family.input ?? "old-names.json"}`,
    `       by ${input.family.generator ?? "old-names.mjs"}: do not edit; change the input and regenerate.`,
    "       Old-name guards (D079.11), shipped through 1.0.x and removed in 2.0:",
    `       ${diagnostics.packages}: an old package ID is in the package graph (a PackageReference, or a package that NuGet resolved);`,
    `       ${diagnostics.msbuild}: the project sets an old public MSBuild property or item;`,
    `       ${diagnostics.environment}: an old environment variable is set.`,
    "       Each name is reported once. A root package imports this file once, also when two of the family's packages ship it. -->",
    "  <PropertyGroup>",
    `    <${imported}>true</${imported}>`,
    "  </PropertyGroup>",
  );

  const group = (title, items) => {
    add(`  <!-- ${title}${items.length === 0 ? ": none." : "."} -->`);
    if (items.length === 0) return;
    add("  <ItemGroup>", ...items, "  </ItemGroup>");
  };
  group("Old package IDs", input.packages.map(entry =>
    `    <${packageItem} Include="${entry.old.toLowerCase()}" Name="${entry.old}" NewName="${entry.new}" />`));
  group("Old public MSBuild properties and items", input.msbuild.flatMap(entry => {
    const set = `'${reference(entry.kind, entry.old)}' != ''`;
    const condition = entry.whenNewUnset ? `${set} and '${reference(entry.kind, entry.new)}' == ''` : set;
    const note = entry.note ? [`    <!-- ${comment(entry.note)} -->`] : [];
    return [...note, `    <${nameItem} Include="${entry.old}" NewName="${entry.new}" Kind="${entry.kind}" Condition="${xml(condition)}" />`];
  }));
  group("Old environment variables", input.environment.map(entry =>
    `    <${environmentItem} Include="${entry.old}" NewName="${entry.new}" Condition="'$([System.Environment]::GetEnvironmentVariable(\`${entry.old}\`))' != ''" />`));

  add(
    "",
    "  <!-- ErrorAndContinue reports every old name, not only the first. -->",
    `  <Target Name="${token}ReportRenamedNames" BeforeTargets="PrepareForBuild"`,
    `          Condition="('@(${nameItem})' != '' or '@(${environmentItem})' != '') and '$(DesignTimeBuild)' != 'true'${xml(skip)}">`,
    `    <Error Code="${diagnostics.msbuild}" File="$(MSBuildProjectFullPath)" ${help(diagnostics.msbuild)} ContinueOnError="ErrorAndContinue"`,
    `           Condition="'@(${nameItem})' != ''"`,
    `           Text="The ${xml(buildName)} build %(${nameItem}.Kind) %(${nameItem}.Identity) is renamed to %(${nameItem}.NewName). The old name no longer applies; rename it in $(MSBuildProjectFile), its imports or the command line." />`,
    `    <Error Code="${diagnostics.environment}" File="$(MSBuildProjectFullPath)" ${help(diagnostics.environment)} ContinueOnError="ErrorAndContinue"`,
    `           Condition="'@(${environmentItem})' != ''"`,
    `           Text="The environment variable %(${environmentItem}.Identity) is renamed to %(${environmentItem}.NewName). The old name no longer applies; set %(${environmentItem}.NewName) instead and remove %(${environmentItem}.Identity) from the environment." />`,
    "  </Target>",
    "",
    "  <!-- After restore, PackageDependencies lists every package of the graph, the transitive ones too. Package IDs",
    "       compare without case. -->",
    `  <Target Name="${token}ReportRenamedPackages" AfterTargets="ResolvePackageAssets"`,
    `          Condition="'@(${packageItem})' != '' and '$(DesignTimeBuild)' != 'true'${xml(skip)}">`,
    "    <PropertyGroup>",
    `      <_${token}PackageGraph>;@(PackageReference);@(PackageDependencies);</_${token}PackageGraph>`,
    `      <_${token}PackageGraph>$(_${token}PackageGraph.ToLowerInvariant())</_${token}PackageGraph>`,
    "    </PropertyGroup>",
    "    <ItemGroup>",
    `      <_${token}RenamedPackageInGraph Include="@(${packageItem})" Condition="$(_${token}PackageGraph.Contains(';%(Identity);'))" />`,
    "    </ItemGroup>",
    `    <Error Code="${diagnostics.packages}" File="$(MSBuildProjectFullPath)" ${help(diagnostics.packages)} ContinueOnError="ErrorAndContinue"`,
    `           Condition="'@(_${token}RenamedPackageInGraph)' != ''"`,
    `           Text="$(MSBuildProjectName) references the package %(_${token}RenamedPackageInGraph.Name), directly or through another package. It is renamed to %(_${token}RenamedPackageInGraph.NewName), and packages of the old and the new names do not work together. Run rax migrate (dnx RunicArtifex.Tool migrate without a tool manifest) to move the project to the new names." />`,
    "  </Target>",
    "</Project>",
    "",
  );
  return lines.join("\n");
}

function argument(args, name) {
  const index = args.indexOf(name);
  return index < 0 ? undefined : args[index + 1] ?? fail(`${name} needs a value`);
}

// Reads an input file; output paths are relative to the repository root (family.root, relative to the input).
export function load(file) {
  const input = JSON.parse(readFileSync(file, "utf8"));
  const root = resolve(dirname(file), input.family?.root ?? ".");
  return { input, root, outputs: (input.family?.outputs ?? []).map(path => isAbsolute(path) ? path : join(root, path)) };
}

// Regenerates the outputs, or with check only compares them; returns the outputs that differ(ed).
export function generate(file, { check = false } = {}) {
  const { input, outputs } = load(file);
  const text = render(input);
  const stale = [];
  for (const output of outputs) {
    let current;
    try { current = readFileSync(output, "utf8"); } catch { current = undefined; }
    // A checkout may turn the LF line ends into CRLF (core.autocrlf); the text is the same.
    if (current?.replaceAll("\r\n", "\n") === text) continue;
    stale.push(output);
    if (!check) writeFileSync(output, text);
  }
  return stale;
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  const args = process.argv.slice(2);
  const file = resolve(argument(args, "--input") ?? join(dirname(fileURLToPath(import.meta.url)), "old-names.json"));
  const check = args.includes("--check");
  try {
    const stale = generate(file, { check });
    if (check && stale.length > 0) {
      console.error(`old-names: out of date; run node ${process.argv[1]} to regenerate:\n${stale.map(path => `  ${path}`).join("\n")}`);
      process.exit(1);
    }
    console.log(check ? "old-names: the guard files match their input." : stale.length ? `old-names: wrote ${stale.join(", ")}` : "old-names: up to date.");
  } catch (error) {
    console.error(error.message);
    process.exit(1);
  }
}
