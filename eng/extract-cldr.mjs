// Derives specs/translations/cldr/runic-subset-<version>.json from the unpacked cldr-json npm
// packages cldr-core, cldr-misc-full, cldr-dates-full and cldr-numbers-full of the pinned tag.
// Offline and deterministic: the caller downloads and verifies the packages (README.md).
//
//   bun eng/extract-cldr.mjs <directory containing cldr-core/, cldr-misc-full/, ...> [--check]
import { readFile, writeFile } from "node:fs/promises";
import { dirname, join, relative, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { dataId, formatTag, maximize, parentId, parseTag } from "./cldr-locales.mjs";

const root = resolve(dirname(fileURLToPath(import.meta.url)), "..");
const subsetPath = resolve(root, "specs/translations/cldr/runic-subset-48.2.json");
const args = process.argv.slice(2);
const check = args.includes("--check");
const input = args.find((arg) => arg !== "--check");
if (!input || args.length > (check ? 2 : 1))
  throw new Error("Usage: bun eng/extract-cldr.mjs <cldr-json packages directory> [--check]");

const packages = ["cldr-core", "cldr-misc-full", "cldr-dates-full", "cldr-numbers-full"];
const packageRoot = (name) => join(input, name, "package");
const json = async (name, path) => JSON.parse(await readFile(join(packageRoot(name), path), "utf8"));

const previous = JSON.parse(await readFile(subsetPath, "utf8"));
const version = (await json("cldr-core", "package.json")).version;
if (version !== previous.source.cldrJsonTag)
  throw new Error(`cldr-core ${version} does not match the pinned tag ${previous.source.cldrJsonTag}.`);
for (const name of packages)
  if ((await json(name, "package.json")).version !== version)
    throw new Error(`${name} is not version ${version}.`);

const supplemental = (file) => json("cldr-core", `supplemental/${file}`).then((data) => data.supplemental);
const cardinalSource = (await supplemental("plurals.json"))["plurals-type-cardinal"];
const ordinalSource = (await supplemental("ordinals.json"))["plurals-type-ordinal"];
const likelySource = (await supplemental("likelySubtags.json")).likelySubtags;
const parentSource = (await supplemental("parentLocales.json")).parentLocales;
const aliasSource = (await supplemental("aliases.json")).metadata.alias.languageAlias;
const available = (await json("cldr-core", "availableLocales.json")).availableLocales.full.filter((id) => id !== "und" && id !== "root");
if (JSON.stringify(parentSource._localeRules) !== JSON.stringify({ parentLocale: { nonlikelyScript: "root" } }))
  throw new Error("Unexpected CLDR parentLocales rules; review eng/cldr-locales.mjs.");
if (Object.keys(parentSource.plurals ?? {}).length !== 0)
  throw new Error("CLDR now has plural-specific parent locales; review eng/cldr-locales.mjs.");

const ordinalSort = (a, b) => (a < b ? -1 : a > b ? 1 : 0);
const sortedObject = (entries) => Object.fromEntries([...entries].sort(([a], [b]) => ordinalSort(a, b)));

// Languages with CLDR locale data or plural rules. Likely subtags and aliases are kept for these only.
const languages = new Set([
  ...available.map((id) => parseTag(id).language),
  ...Object.keys(cardinalSource).filter((id) => id !== "root").map((id) => parseTag(id).language),
]);

// Likely subtags: every entry for a data language, minus those the shorter keys already imply.
const allLikely = Object.fromEntries(
  Object.entries(likelySource).filter(([key]) => languages.has(parseTag(key).language) && !key.startsWith("und")),
);
const likelySubtags = {};
for (const [key, value] of Object.entries(allLikely)) {
  const parsed = parseTag(key);
  if (key === parsed.language) { likelySubtags[key] = value; continue; }
  const without = { ...allLikely }; delete without[key];
  if (formatTag(maximize(parsed, without)) !== formatTag(parseTag(value))) likelySubtags[key] = value;
}
const likely = sortedObject(Object.entries(likelySubtags));

// Deprecated and legacy language codes and their replacements, which may add a script or region (iw → he,
// sh → sr-Latn, cnr → sr-ME). Deprecated and legacy codes are kept even when CLDR still lists plural rules for
// them (tl, mo, sh, jw), because ICU and Java still produce them; their rules equal the replacement's (checked
// below). Macrolanguage codes are kept only for languages without data of their own (cmn → zh).
const replacementPattern = /^[a-z]{2,3}(?:-[A-Z][a-z]{3})?(?:-(?:[A-Z]{2}|\d{3}))?$/;
const languageAliases = sortedObject(
  Object.entries(aliasSource)
    .filter(([key, value]) => /^[a-z]{2,3}$/.test(key) &&
      (["deprecated", "legacy"].includes(value._reason) || (value._reason === "macrolanguage" && !languages.has(key))) &&
      replacementPattern.test(value._replacement) && languages.has(parseTag(value._replacement).language))
    .map(([key, value]) => [key, value._replacement]),
);
// Explicit parents of the main component, as data IDs; parents that the default rules give are omitted.
const parentEntries = [];
for (const [child, parent] of Object.entries(parentSource.parentLocale)) {
  if (!languages.has(parseTag(child).language)) continue;
  const id = dataId(child, likely), target = parent === "root" || parent === "und" ? null : dataId(parent, likely);
  if (parentId(id, {}) !== target) parentEntries.push([id, target ?? "root"]);
}
const parents = sortedObject(parentEntries);
const parentOf = (id) => { const value = parentId(id, parents); return value === "root" ? null : value; };
const chainOf = (id) => {
  const result = [];
  for (let current = id; current !== null; current = parentOf(current)) result.push(current);
  return result;
};

// Plural rules keyed by data ID; `pluralRule-count-one` becomes `one`. Rules keep CLDR's category order.
const categoryOrder = ["zero", "one", "two", "few", "many", "other"];
const pluralRules = (source) => sortedObject(
  Object.entries(source)
    .filter(([id]) => id !== "root")
    .map(([id, rules]) => [
      dataId(id, likely),
      Object.fromEntries(
        Object.entries(rules)
          .map(([name, rule]) => [name.replace("pluralRule-count-", ""), rule])
          .sort(([a], [b]) => categoryOrder.indexOf(a) - categoryOrder.indexOf(b)),
      ),
    ]),
);

// Per-locale data, each value stored only where it differs from the nearest ancestor's.
const listTypes = { conjunction: "standard", disjunction: "or", unit: "unit" };
const listStyles = { long: "", short: "-short", narrow: "-narrow" };
const units = ["second", "minute", "hour", "day", "week", "month", "year"];
const offsets = ["-2", "-1", "0", "1", "2"];
const resolved = new Map();
for (const id of available) {
  const misc = await json("cldr-misc-full", `main/${id}/listPatterns.json`);
  const dates = await json("cldr-dates-full", `main/${id}/dateFields.json`);
  const numbers = await json("cldr-numbers-full", `main/${id}/numbers.json`);
  const listData = misc.main[id].listPatterns, fields = dates.main[id].dates.fields;
  const listPatterns = Object.fromEntries(Object.entries(listTypes).map(([type, cldrType]) => [type, Object.fromEntries(
    Object.entries(listStyles).map(([style, suffix]) => {
      const pattern = listData[`listPattern-type-${cldrType}${suffix}`];
      if (!pattern) throw new Error(`${id} has no ${cldrType}${suffix} list pattern.`);
      return [style, [pattern.start, pattern.middle, pattern.end, pattern["2"]]];
    }))]));
  const relativeTime = Object.fromEntries(units.map((unit) => {
    const field = fields[unit];
    const counts = (direction) => {
      const patterns = field[`relativeTime-type-${direction}`];
      if (!patterns) throw new Error(`${id}/${unit} has no ${direction} pattern.`);
      return Object.fromEntries(
        Object.entries(patterns)
          .map(([name, pattern]) => [name.replace("relativeTimePattern-count-", ""), pattern])
          .sort(([a], [b]) => categoryOrder.indexOf(a) - categoryOrder.indexOf(b)),
      );
    };
    const relative = Object.fromEntries(offsets.filter((offset) => field[`relative-type-${offset}`] !== undefined)
      .map((offset) => [offset, field[`relative-type-${offset}`]]));
    return [unit, { past: counts("past"), future: counts("future"), relative }];
  }));
  const minimumGroupingDigits = Number(numbers.main[id].numbers.minimumGroupingDigits);
  const key = dataId(id, likely);
  if (resolved.has(key) && JSON.stringify(resolved.get(key)) !== JSON.stringify({ minimumGroupingDigits, listPatterns, relativeTime }))
    throw new Error(`CLDR locales ${id} and ${resolved.get(key).id} share data ID ${key} but differ.`);
  resolved.set(key, { minimumGroupingDigits, listPatterns, relativeTime });
}
const localeEntries = [];
for (const id of [...resolved.keys()].sort(ordinalSort)) {
  const data = resolved.get(id), entry = {};
  const ancestor = chainOf(id).slice(1).find((candidate) => resolved.has(candidate));
  const base = ancestor === undefined ? undefined : resolved.get(ancestor);
  for (const name of ["minimumGroupingDigits", "listPatterns", "relativeTime"])
    if (base === undefined || JSON.stringify(base[name]) !== JSON.stringify(data[name])) entry[name] = data[name];
  if (Object.keys(entry).length > 0) localeEntries.push([id, entry]);
}
// Every data ID must reach stored data through its chain.
for (const id of resolved.keys()) if (!chainOf(id).some((candidate) => localeEntries.some(([key]) => key === candidate)))
  throw new Error(`${id} reaches no stored data.`);

// An alias with plural rules of its own must select as its replacement does, so aliasing never changes plurals.
for (const [alias, replacement] of Object.entries(languageAliases))
  for (const source of [cardinalSource, ordinalSource]) {
    if (source[alias] === undefined) continue;
    const target = chainOf(dataId(replacement, likely)).concat(parseTag(replacement).language).find((id) => source[id] !== undefined);
    if (JSON.stringify(source[alias]) !== JSON.stringify(source[target]))
      throw new Error(`The plural rules of alias ${alias} differ from those of ${replacement}.`);
  }

const subset = {
  source: {
    ...previous.source,
    packages: Object.fromEntries(packages.map((name) => [name, version])),
    packageIntegrity: previous.source.packageIntegrity,
  },
  rightToLeftScripts: previous.rightToLeftScripts,
  languageAliases,
  likelySubtags: likely,
  parentLocales: parents,
  plurals: { cardinal: pluralRules(cardinalSource), ordinal: pluralRules(ordinalSource) },
  locales: Object.fromEntries(localeEntries),
};
if (!subset.source.packageIntegrity) throw new Error("Record source.packageIntegrity (npm dist.integrity per package) first.");

// One locale or table entry per line keeps the pin reviewable.
const lines = ["{"];
const top = Object.entries(subset);
top.forEach(([name, value], index) => {
  const comma = index < top.length - 1 ? "," : "";
  if (name === "rightToLeftScripts") { lines.push(`  ${JSON.stringify(name)}: ${JSON.stringify(value)}${comma}`); return; }
  if (name === "plurals") {
    lines.push(`  "plurals": {`);
    ["cardinal", "ordinal"].forEach((type, typeIndex) => {
      lines.push(`    ${JSON.stringify(type)}: {`);
      const rows = Object.entries(value[type]);
      rows.forEach(([id, rules], rowIndex) => lines.push(`      ${JSON.stringify(id)}: ${JSON.stringify(rules)}${rowIndex < rows.length - 1 ? "," : ""}`));
      lines.push(`    }${typeIndex === 0 ? "," : ""}`);
    });
    lines.push(`  }${comma}`);
    return;
  }
  const rows = Object.entries(value);
  lines.push(`  ${JSON.stringify(name)}: {`);
  rows.forEach(([key, row], rowIndex) => lines.push(`    ${JSON.stringify(key)}: ${JSON.stringify(row)}${rowIndex < rows.length - 1 ? "," : ""}`));
  lines.push(`  }${comma}`);
});
lines.push("}");
const text = lines.join("\n").replaceAll("\u00a0", "\\u00a0").replaceAll("\u202f", "\\u202f").replaceAll("\u200f", "\\u200f").replaceAll("\u200e", "\\u200e") + "\n";
JSON.parse(text);
if (check) {
  if ((await readFile(subsetPath, "utf8")) !== text) throw new Error(`${relative(root, subsetPath)} differs from the packages.`);
} else {
  await writeFile(subsetPath, text, "utf8");
}
