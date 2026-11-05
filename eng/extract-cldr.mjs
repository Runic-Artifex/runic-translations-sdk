// Derives specs/translations/cldr/runic-subset-<version>.json and runic-formats-<version>.json from the unpacked
// cldr-json npm packages cldr-core, cldr-misc-full, cldr-dates-full, cldr-numbers-full and cldr-units-full of the pinned tag.
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

const packages = ["cldr-core", "cldr-misc-full", "cldr-dates-full", "cldr-numbers-full", "cldr-units-full"];
const formatsPath = resolve(root, "specs/translations/cldr/runic-formats-48.2.json");
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
const numberingSource = (await supplemental("numberingSystems.json")).numberingSystems;
const dayPeriodSource = (await supplemental("dayPeriods.json")).dayPeriodRuleSet;
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
  const numberData = numbers.main[id].numbers;
  const minimumGroupingDigits = Number(numberData.minimumGroupingDigits);
  // The default numbering system and, for it and for latn (-u-nu-latn), the symbols and the standard decimal and
  // percent patterns, verbatim.
  const system = numberData.defaultNumberingSystem;
  const numberFormats = { system };
  for (const name of [...new Set([system, "latn"])]) {
    const symbols = numberData[`symbols-numberSystem-${name}`];
    const decimal = numberData[`decimalFormats-numberSystem-${name}`]?.standard, percent = numberData[`percentFormats-numberSystem-${name}`]?.standard;
    if (!symbols || !decimal || !percent) throw new Error(`${id} has no ${name} number symbols or patterns.`);
    if (numberingSource[name]?._type !== "numeric") throw new Error(`${id} uses the non-numeric numbering system ${name}.`);
    numberFormats[name] = { decimal: symbols.decimal, group: symbols.group, minusSign: symbols.minusSign, percentSign: symbols.percentSign, decimalFormat: decimal, percentFormat: percent };
  }
  // Platform date data that .NET cannot reach: the format-context weekday names where they differ from the stand-alone
  // names (.NET's dddd), and the flexible day periods of a time pattern with B, as [from (minutes), name] pairs.
  const gregorian = (await json("cldr-dates-full", `main/${id}/ca-gregorian.json`)).main[id].dates.calendars.gregorian;
  const dayKeys = ["sun", "mon", "tue", "wed", "thu", "fri", "sat"];
  const formatDays = dayKeys.map((day) => gregorian.days.format.wide[day]), standAloneDays = dayKeys.map((day) => gregorian.days["stand-alone"].wide[day]);
  const unquoted = (pattern) => (typeof pattern === "string" ? pattern : pattern._value).replace(/'[^']*'/g, "");
  // Only date patterns with a format-context weekday (E) need them; Finnish writes the stand-alone cccc.
  const formatWeekday = ["short", "medium", "long", "full"].some((style) => /E/.test(unquoted(gregorian.dateFormats[style])));
  const weekdays = !formatWeekday || JSON.stringify(formatDays) === JSON.stringify(standAloneDays) ? [] : formatDays;
  let dayPeriods = [];
  const times = [gregorian.timeFormats.short, gregorian.timeFormats.medium].map(unquoted);
  if (times.some((pattern) => /B/.test(pattern))) {
    if (times.some((pattern) => /BB|b/.test(pattern))) throw new Error(`${id} uses an unsupported day period width.`);
    const ruleKey = chainOf(dataId(id, likely)).map((item) => item.replaceAll("-", "_")).concat(parseTag(id).language).find((item) => dayPeriodSource[item] !== undefined);
    if (ruleKey === undefined) throw new Error(`${id} has no day period rules.`);
    const minutes = (time) => Number(time.slice(0, 2)) * 60 + Number(time.slice(3, 5));
    // ICU writes B with the from/before periods only; the at periods (midnight, noon) are not used.
    dayPeriods = Object.entries(dayPeriodSource[ruleKey]).filter(([, rule]) => rule._from !== undefined)
      .map(([name, rule]) => [minutes(rule._from), gregorian.dayPeriods.format.abbreviated[name], minutes(rule._before)])
      .sort((a, b) => a[0] - b[0]);
    let next = 0;
    for (const [from, name, before] of dayPeriods) {
      if (from !== next || typeof name !== "string") throw new Error(`The day periods of ${id} do not cover the day.`);
      next = before;
    }
    if (next !== 1440) throw new Error(`The day periods of ${id} do not cover the day.`);
    dayPeriods = dayPeriods.map(([from, name]) => [from, name]);
  }
  const key = dataId(id, likely);
  const record = { minimumGroupingDigits, numbers: numberFormats, weekdays, dayPeriods, listPatterns, relativeTime };
  if (resolved.has(key) && JSON.stringify(resolved.get(key)) !== JSON.stringify(record))
    throw new Error(`CLDR locales ${id} and ${resolved.get(key).id} share data ID ${key} but differ.`);
  resolved.set(key, record);
}
const localeEntries = [];
for (const id of [...resolved.keys()].sort(ordinalSort)) {
  const data = resolved.get(id), entry = {};
  const ancestor = chainOf(id).slice(1).find((candidate) => resolved.has(candidate));
  const base = ancestor === undefined ? undefined : resolved.get(ancestor);
  for (const name of ["minimumGroupingDigits", "numbers", "weekdays", "dayPeriods", "listPatterns", "relativeTime"]) {
    // Weekdays and day periods are empty for most locales; an empty value is stored only to override an ancestor's.
    const empty = Array.isArray(data[name]) && data[name].length === 0;
    if (base === undefined ? !empty : JSON.stringify(base[name]) !== JSON.stringify(data[name])) entry[name] = data[name];
  }
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

// The digits of every numbering system a locale uses, from supplemental/numberingSystems.json.
const usedSystems = new Set([...resolved.values()].flatMap((data) => Object.keys(data.numbers).filter((name) => name !== "system")));
const numberingSystems = sortedObject([...usedSystems].map((name) => {
  const digits = [...numberingSource[name]._digits];
  if (digits.length !== 10) throw new Error(`Numbering system ${name} does not have ten digits.`);
  return [name, numberingSource[name]._digits];
}));

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
  numberingSystems,
  // Every CLDR locale as a data ID, for the cross-runtime comparisons of platform-data formatting.
  availableLocales: [...resolved.keys()].sort(ordinalSort),
  locales: Object.fromEntries(localeEntries),
};
if (!subset.source.packageIntegrity || packages.some((name) => typeof subset.source.packageIntegrity[name] !== "string"))
  throw new Error("Record source.packageIntegrity (npm dist.integrity per package) first.");

// One locale or table entry per line keeps the pin reviewable.
const lines = ["{"];
const top = Object.entries(subset);
top.forEach(([name, value], index) => {
  const comma = index < top.length - 1 ? "," : "";
  if (name === "rightToLeftScripts" || name === "availableLocales") { lines.push(`  ${JSON.stringify(name)}: ${JSON.stringify(value)}${comma}`); return; }
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
const text = lines.join("\n").replaceAll("\u00a0", "\\u00a0").replaceAll("\u202f", "\\u202f").replaceAll("\u200f", "\\u200f").replaceAll("\u200e", "\\u200e").replaceAll("\u061c", "\\u061c") + "\n";
JSON.parse(text);
if (check) {
  if ((await readFile(subsetPath, "utf8")) !== text) throw new Error(`${relative(root, subsetPath)} differs from the packages.`);
} else {
  await writeFile(subsetPath, text, "utf8");
}

// ---- Currencies, units and compact notation (runic-formats-<version>.json) ----
// The compiler embeds only the entries a catalog uses (D055.4), so this data is not in the runtime resource. Every
// value is copied verbatim from the resolved cldr-json files and stored only where it differs from the nearest
// ancestor's, per currency, unit, numbering system and compact style.
const currencyData = (await supplemental("currencyData.json")).currencyData;
// Currencies: the ISO 4217 codes that CLDR lists as current legal tender of some region (no _to date, not _tender
// false), with their CLDR fraction digits (DEFAULT where none are listed).
const currentCurrencies = new Set();
for (const entries of Object.values(currencyData.region))
  for (const entry of entries) for (const [code, value] of Object.entries(entry))
    if (value._to === undefined && value._tender !== "false" && /^[A-Z]{3}$/.test(code)) currentCurrencies.add(code);
const fractionDigits = (code) => {
  const fraction = currencyData.fractions[code] ?? currencyData.fractions.DEFAULT;
  // Only cash rounding uses increments in CLDR 48; formatting uses the digits.
  if (fraction._rounding !== "0") throw new Error(`Currency ${code} has a non-cash rounding increment; review the runtimes.`);
  return Number(fraction._digits);
};
const currencyCodes = [...currentCurrencies].sort(ordinalSort);
// Units: the simple units ECMA-402 sanctions (Intl.NumberFormat style "unit"), so both runtimes can be compared with
// Intl, plus CLDR's own compound units of two of them. Other X-per-Y compounds of sanctioned units are composed with
// CLDR's per patterns at run time.
const sanctionedUnits = ["acre", "bit", "byte", "celsius", "centimeter", "day", "degree", "fahrenheit", "fluid-ounce", "foot", "gallon",
  "gigabit", "gigabyte", "gram", "hectare", "hour", "inch", "kilobit", "kilobyte", "kilogram", "kilometer", "liter", "megabit", "megabyte",
  "meter", "microsecond", "mile", "mile-scandinavian", "milliliter", "millimeter", "millisecond", "minute", "month", "nanosecond", "ounce",
  "percent", "petabyte", "pound", "second", "stone", "terabit", "terabyte", "week", "yard", "year"];
const rootUnits = (await json("cldr-units-full", "main/en/units.json")).main.en.units.long;
const unitKey = {};
for (const key of Object.keys(rootUnits)) {
  const name = key.slice(key.indexOf("-") + 1);
  if (unitKey[name] === undefined && key.indexOf("-") > 0) unitKey[name] = key;
}
const compoundUnits = Object.keys(unitKey).filter((name) => {
  const parts = name.split("-per-");
  return parts.length === 2 && sanctionedUnits.includes(parts[0]) && sanctionedUnits.includes(parts[1]);
}).sort(ordinalSort);
const formatUnits = [...sanctionedUnits, ...compoundUnits].sort(ordinalSort);
for (const name of formatUnits) if (unitKey[name] === undefined) throw new Error(`CLDR has no unit ${name}.`);
const widths = ["long", "short", "narrow"];
const counts = (entry, prefix) => {
  const result = {};
  for (const category of categoryOrder) if (entry[`${prefix}${category}`] !== undefined) result[category] = entry[`${prefix}${category}`];
  return result;
};
// Plural slots of the array records: other first, so a record with only other is one element.
const pluralSlots = ["other", "zero", "one", "two", "few", "many"];
// The pattern of each plural slot, null where CLDR has none or it equals other (lookups fall back to other, as ICU's do).
const slots = (lookup) => { const other = lookup("other"); return pluralSlots.map((category, index) => index === 0 ? other : lookup(category) === other ? null : lookup(category) ?? null); };
const trimmed = (values) => { const copy = [...values]; while (copy.length > 0 && copy.at(-1) === null) copy.pop(); return copy; };
// The currency spacing of every locale is CLDR root's: a no-break space between a digit and a currency symbol whose
// adjacent character is neither a symbol (S) nor a separator (Z). The runtimes apply exactly that rule.
const rootSpacing = JSON.stringify({ beforeCurrency: { currencyMatch: "[[:^S:]&[:^Z:]]", surroundingMatch: "[:digit:]", insertBetween: "\u00a0" },
  afterCurrency: { currencyMatch: "[[:^S:]&[:^Z:]]", surroundingMatch: "[:digit:]", insertBetween: "\u00a0" } });
const formatSections = ["currencyFormats", "currencies", "units", "per", "compact"];
const formatsResolved = new Map();
for (const id of available) {
  const numberData = (await json("cldr-numbers-full", `main/${id}/numbers.json`)).main[id].numbers;
  const currencies = (await json("cldr-numbers-full", `main/${id}/currencies.json`)).main[id].numbers.currencies;
  const units = (await json("cldr-units-full", `main/${id}/units.json`)).main[id].units;
  const system = numberData.defaultNumberingSystem;
  const currencyFormats = {}, compact = {};
  for (const name of [...new Set([system, "latn"])]) {
    // A numbering system's own sets fall back to latn's, as ICU reads them.
    const formats = { ...numberData["currencyFormats-numberSystem-latn"], ...numberData[`currencyFormats-numberSystem-${name}`] };
    const symbols = numberData[`symbols-numberSystem-${name}`];
    if (JSON.stringify(formats.currencySpacing) !== rootSpacing) throw new Error(`${id}/${name} has its own currency spacing; review the runtimes.`);
    if (!formats.standard || !formats.accounting) throw new Error(`${id}/${name} has no currency patterns.`);
    // [standard, accounting, the unit patterns other, zero, one, two, few, many, currency decimal, currency group].
    if (formats["unitPattern-count-other"] === undefined) throw new Error(`${id}/${name} has no currency unit pattern.`);
    currencyFormats[name] = trimmed([formats.standard, formats.accounting, ...slots((category) => formats[`unitPattern-count-${category}`]),
      symbols.currencyDecimal ?? null, symbols.currencyGroup ?? null]);
    const decimal = { ...numberData["decimalFormats-numberSystem-latn"] }, own = numberData[`decimalFormats-numberSystem-${name}`] ?? {};
    // The compact tables of the system, else latn's; long falls back to short.
    const table = (style) => own[style]?.decimalFormat ?? decimal[style]?.decimalFormat ?? own.short?.decimalFormat ?? decimal.short?.decimalFormat;
    compact[name] = {};
    for (const style of ["short", "long"]) {
      const source = table(style);
      if (!source) throw new Error(`${id}/${name} has no ${style} compact patterns.`);
      const magnitudes = {};
      for (const [key, pattern] of Object.entries(source)) {
        const match = /^1(0+)-count-(zero|one|two|few|many|other|1)$/.exec(key);
        if (!match) throw new Error(`Unexpected compact pattern key ${id}/${name}/${style}/${key}.`);
        // A pattern has one run of zeros (and maybe a negative subpattern), or none for a count whose number it spells out
        // (it "1000-count-one": "mille", fr "1000-count-1": "mille").
        const bare = pattern.replace(/'[^']*'/g, "");
        if (!/^[^0;]*0+[^0;]*(?:;[^0;]*0+[^0;]*)?$/.test(bare) && /[0;]/.test(bare)) throw new Error(`Unsupported compact pattern ${id}: ${pattern}`);
        (magnitudes[match[1].length] ??= {})[match[2]] = pattern;
      }
      // By magnitude from 3 (thousands) up to the largest the locale has (14, or 19 in ja and zh): [other, zero, one,
      // two, few, many, the explicit "1" pattern]. Larger numbers use the largest magnitude's pattern, as ICU does.
      const keys = Object.keys(magnitudes).map(Number).sort((a, b) => a - b);
      if (keys.some((magnitude, index) => magnitude !== index + 3)) throw new Error(`${id}/${name}/${style} has gaps in its compact patterns.`);
      compact[name][style] = keys.map((magnitude) => trimmed([...slots((category) => magnitudes[magnitude][category]), magnitudes[magnitude]["1"] ?? null]));
    }
  }
  const currencyRecords = {};
  for (const code of currencyCodes) {
    const entry = currencies[code] ?? {};
    // [symbol, narrow symbol (null: the symbol), then the display names other, zero, one, two, few, many, then the
    // currency's own pattern, decimal and group symbols]; trailing nulls are dropped.
    const symbol = entry.symbol ?? code, narrow = entry["symbol-alt-narrow"] ?? symbol, names = counts(entry, "displayName-count-");
    currencyRecords[code] = trimmed([symbol, narrow === symbol ? null : narrow, ...slots((category) => category === "other" ? names.other ?? entry.displayName ?? code : names[category]), entry.pattern ?? null, entry.decimal ?? null, entry.group ?? null]);
  }
  const unitRecords = {}, per = {};
  for (const width of widths) {
    per[width] = units[width].per?.compoundUnitPattern;
    if (typeof per[width] !== "string") throw new Error(`${id} has no ${width} per pattern.`);
  }
  // Each unit as [long, short, narrow]; each width as [other, zero, one, two, few, many, perUnitPattern] without trailing
  // nulls, and null where it equals the previous width's.
  for (const name of formatUnits) {
    const records = widths.map((width) => {
      const entry = units[width][unitKey[name]];
      if (!entry) throw new Error(`${id} has no ${width} ${name}.`);
      if (entry["unitPattern-count-other"] === undefined) throw new Error(`${id}/${width}/${name} has no other pattern.`);
      return trimmed([...slots((category) => entry[`unitPattern-count-${category}`]), entry.perUnitPattern ?? null]);
    });
    unitRecords[name] = trimmed(records.map((record, index) => index > 0 && JSON.stringify(record) === JSON.stringify(records[index - 1]) ? null : record));
  }
  const key = dataId(id, likely);
  const record = { currencyFormats, currencies: currencyRecords, units: unitRecords, per, compact };
  if (formatsResolved.has(key) && JSON.stringify(formatsResolved.get(key)) !== JSON.stringify(record))
    throw new Error(`CLDR locales ${id} and another share data ID ${key} but their formats differ.`);
  formatsResolved.set(key, record);
}
// Each locale stores the members of each section that differ from the nearest ancestor's (a currency code, a unit, a
// numbering system or "long", "short" and "narrow" of per). A locale without an ancestor stores everything.
const formatEntries = [];
for (const id of [...formatsResolved.keys()].sort(ordinalSort)) {
  const data = formatsResolved.get(id), entry = {};
  const ancestor = chainOf(id).slice(1).find((candidate) => formatsResolved.has(candidate));
  const base = ancestor === undefined ? undefined : formatsResolved.get(ancestor);
  for (const section of formatSections) {
    const members = Object.entries(data[section]).filter(([name, value]) => base === undefined || JSON.stringify(base[section][name]) !== JSON.stringify(value));
    if (members.length > 0) entry[section] = Object.fromEntries(members);
  }
  if (Object.keys(entry).length > 0) formatEntries.push([id, entry]);
}
const formats = {
  source: { cldrVersion: subset.source.cldrVersion, cldrJsonTag: subset.source.cldrJsonTag, packages: ["cldr-core", "cldr-numbers-full", "cldr-units-full"] },
  currencies: Object.fromEntries(currencyCodes.map((code) => [code, fractionDigits(code)])),
  units: formatUnits,
  locales: Object.fromEntries(formatEntries),
};
const formatLines = ["{"];
const formatTop = Object.entries(formats);
formatTop.forEach(([name, value], index) => {
  const comma = index < formatTop.length - 1 ? "," : "";
  if (name !== "locales") { formatLines.push(`  ${JSON.stringify(name)}: ${JSON.stringify(value)}${comma}`); return; }
  const rows = Object.entries(value);
  formatLines.push(`  "locales": {`);
  rows.forEach(([key, row], rowIndex) => formatLines.push(`    ${JSON.stringify(key)}: ${JSON.stringify(row)}${rowIndex < rows.length - 1 ? "," : ""}`));
  formatLines.push(`  }${comma}`);
});
formatLines.push("}");
const formatText = formatLines.join("\n").replaceAll("\u00a0", "\\u00a0").replaceAll("\u202f", "\\u202f").replaceAll("\u200f", "\\u200f").replaceAll("\u200e", "\\u200e").replaceAll("\u061c", "\\u061c") + "\n";
JSON.parse(formatText);
if (check) {
  if ((await readFile(formatsPath, "utf8").catch(() => "")) !== formatText) throw new Error(`${relative(root, formatsPath)} differs from the packages.`);
} else {
  await writeFile(formatsPath, formatText, "utf8");
}
