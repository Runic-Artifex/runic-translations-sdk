import { readFile, writeFile } from "node:fs/promises";
import { dirname, relative, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { brotliCompressSync, brotliDecompressSync, constants as zlibConstants, deflateRawSync, inflateRawSync } from "node:zlib";
import {
  csharpRuleBody,
  javascriptRuleFunction,
  parseRuleSet,
  pluralCategories,
} from "./cldr-plural-rules.mjs";
import { chain, dataId, parseTag } from "./cldr-locales.mjs";

const root = resolve(dirname(fileURLToPath(import.meta.url)), "..");
const sourcePath = resolve(root, "specs/translations/cldr/runic-subset-48.2.json");
const registryPath = resolve(root, "tools/Runic.Translations.Compiler/TranslationCapabilityRegistry.cs");
const runtimePath = resolve(root, "packages/dotnet/Runic.Translations/Formatting/GeneratedLocaleData.cs");
const tablesPath = resolve(root, "packages/dotnet/Runic.Translations/Formatting/GeneratedLocaleTables.cs");
const resourcePath = resolve(root, "packages/dotnet/Runic.Translations/Formatting/GeneratedLocaleData.bin");
const matrixPath = resolve(root, "specs/translations/capabilities-v1.json");
const formatsSourcePath = resolve(root, "specs/translations/cldr/runic-formats-48.2.json");
// Currency, unit and compact-notation data for the compiler only: a catalog embeds the entries it uses (D055.4).
const formatResourcePath = resolve(root, "tools/Runic.Translations.Compiler/GeneratedFormatData.bin");
// The matrix describes the SDK release built from this checkout.
const releaseTarget = (
  await readFile(resolve(root, "eng/Versions.props"), "utf8")
).match(/<RunicTranslationsVersion>([^<]+)<\/RunicTranslationsVersion>/)?.[1];
if (!releaseTarget) throw new Error("eng/Versions.props has no RunicTranslationsVersion.");
const check = process.argv.slice(2).includes("--check");

if (process.argv.length > (check ? 3 : 2))
  throw new Error("Usage: bun eng/generate-cldr.mjs [--check]");

const source = JSON.parse(await readFile(sourcePath, "utf8"));
if (!source.source || typeof source.source !== "object")
  throw new Error("CLDR subset has no source metadata.");
for (const name of ["cldrVersion", "cldrJsonTag", "archive", "archiveSha512", "license"])
  if (typeof source.source[name] !== "string" || source.source[name].length === 0)
    throw new Error(`CLDR subset source metadata '${name}' must be a non-empty string.`);

const ordinalSort = (a, b) => (a < b ? -1 : a > b ? 1 : 0);
const sortedKeys = (object, label) => {
  const keys = Object.keys(object);
  keys.forEach((key, index) => {
    if (index > 0 && keys[index - 1] >= key) throw new Error(`${label} must be unique and ordinal-sorted.`);
  });
  return keys;
};

// CLDR's right-to-left scripts (scriptMetadata rtl: YES), for the ESM runtime's localeDirection.
const rightToLeftScripts = source.rightToLeftScripts;
if (
  !Array.isArray(rightToLeftScripts) || rightToLeftScripts.length === 0 ||
  rightToLeftScripts.some((script, index) => typeof script !== "string" || !/^[A-Z][a-z]{3}$/.test(script) ||
    (index > 0 && rightToLeftScripts[index - 1] >= script))
)
  throw new Error("CLDR subset rightToLeftScripts must be sorted, unique ISO 15924 codes.");

const tagPattern = /^[a-z]{2,3}(?:-[A-Z][a-z]{3})?(?:-(?:[A-Z]{2}|\d{3}))?(?:-[a-z0-9]{4,8})*$/;
const checkTable = (name, pattern) => {
  const table = source[name];
  if (!table || typeof table !== "object") throw new Error(`CLDR subset has no ${name}.`);
  for (const key of sortedKeys(table, name))
    if (!tagPattern.test(key) || typeof table[key] !== "string" || !pattern.test(table[key]))
      throw new Error(`Invalid ${name} entry '${key}'.`);
  return table;
};
const likely = checkTable("likelySubtags", /^[a-z]{2,3}-[A-Z][a-z]{3}-(?:[A-Z]{2}|\d{3})$/);
const parents = checkTable("parentLocales", /^(?:root|[a-z]{2,3}(?:-[A-Z][a-z]{3})?(?:-(?:[A-Z]{2}|\d{3}))?)$/);
const aliases = checkTable("languageAliases", /^[a-z]{2,3}(?:-[A-Z][a-z]{3})?(?:-(?:[A-Z]{2}|\d{3}))?$/);
for (const key of Object.keys(likely)) if (likely[parseTag(key).language] === undefined)
  throw new Error(`Likely subtags for '${key}' have no language entry.`);

if (!Array.isArray(source.availableLocales) || source.availableLocales.some((id, index) => typeof id !== "string" || !tagPattern.test(id) ||
  (index > 0 && source.availableLocales[index - 1] >= id)))
  throw new Error("CLDR subset availableLocales must be sorted, unique data IDs.");

// Plural rules by CLDR data ID. Identical rule sets share one generated selector; rule 0 is
// CLDR's root rule (`other` only), which ordinals of a language without ordinal rules use.
const rules = [];
const ruleIndex = new Map();
const addRule = (ruleSet) => {
  const key = csharpRuleBody(ruleSet, "");
  if (!ruleIndex.has(key)) {
    ruleIndex.set(key, rules.length);
    rules.push({ csharp: csharpRuleBody(ruleSet, "        "), javascript: javascriptRuleFunction(ruleSet), categories: ruleSet.map((rule) => rule.category) });
  }
  return ruleIndex.get(key);
};
addRule(parseRuleSet({ other: "" }, "root"));
const pluralTable = (type) => {
  const table = source.plurals?.[type];
  if (!table || typeof table !== "object") throw new Error(`CLDR subset has no ${type} plural rules.`);
  return Object.fromEntries(sortedKeys(table, `plurals.${type}`).map((id) => {
    if (!tagPattern.test(id)) throw new Error(`Invalid plural locale '${id}'.`);
    return [id, addRule(parseRuleSet(table[id], `${id} ${type}`))];
  }));
};
const cardinal = pluralTable("cardinal");
const ordinal = pluralTable("ordinal");

// Per-locale data by CLDR data ID, stored where it differs from the parent's.
const units = ["second", "minute", "hour", "day", "week", "month", "year"];
const offsets = ["-2", "-1", "0", "1", "2"];
const listTypes = ["conjunction", "disjunction", "unit"];
const listStyles = ["long", "short", "narrow"];
const locales = source.locales;
if (!locales || typeof locales !== "object") throw new Error("CLDR subset has no locales.");
const numberingSystems = source.numberingSystems;
if (!numberingSystems || typeof numberingSystems !== "object") throw new Error("CLDR subset has no numberingSystems.");
for (const name of sortedKeys(numberingSystems, "numberingSystems")) {
  const digits = [...numberingSystems[name]];
  // Ten digits, all in the Basic Multilingual Plane or all outside it, so a runtime can split them by length.
  if (!/^[a-z]{3,8}$/.test(name) || digits.length !== 10 || new Set(digits.map((digit) => digit.length)).size !== 1)
    throw new Error(`Numbering system '${name}' needs ten digits of one width.`);
}
const grouping = {}, list = {}, relativeTime = {}, numbers = {}, weekdays = {}, dayPeriods = {};
for (const id of sortedKeys(locales, "locales")) {
  if (!tagPattern.test(id)) throw new Error(`Invalid locale '${id}'.`);
  const data = locales[id];
  if (data.minimumGroupingDigits !== undefined) {
    // CLDR numbers.minimumGroupingDigits: `useGrouping=auto` groups only when the
    // integer part has at least this many digits above the primary group.
    if (!Number.isInteger(data.minimumGroupingDigits) || data.minimumGroupingDigits < 1 || data.minimumGroupingDigits > 4)
      throw new Error(`Locale '${id}' needs minimumGroupingDigits 1..4.`);
    // Kept only where it differs from the value the parent chain gives (1 at the root).
    const inherited = chain(id, parents).slice(1).find((candidate) => locales[candidate]?.minimumGroupingDigits !== undefined);
    if (data.minimumGroupingDigits !== (inherited === undefined ? 1 : locales[inherited].minimumGroupingDigits))
      grouping[id] = data.minimumGroupingDigits;
  }
  // Number formats as one record: the default numbering system, then its fields and, when it is not latn, latn's
  // (for -u-nu-latn). See numberFields.
  if (data.numbers !== undefined) {
    const system = data.numbers.system;
    if (Object.keys(data.numbers).join() !== ["system", ...new Set([system, "latn"])].join())
      throw new Error(`Numbers for '${id}' must list the system, then its formats and latn's.`);
    numbers[id] = [system, ...numberFields(id, system, data.numbers[system]), ...(system === "latn" ? [] : numberFields(id, "latn", data.numbers.latn))];
  }
  // .NET only: the format-context weekday names (Sunday first) where .NET's stand-alone dddd names differ, and the
  // flexible day periods of a B time pattern as [from minute, name] pairs. An empty array overrides an ancestor's.
  if (data.weekdays !== undefined) {
    if (!Array.isArray(data.weekdays) || ![0, 7].includes(data.weekdays.length) || data.weekdays.some((name) => typeof name !== "string" || name.length === 0))
      throw new Error(`Weekdays for '${id}' must be seven names or empty.`);
    weekdays[id] = data.weekdays;
  }
  if (data.dayPeriods !== undefined) {
    if (!Array.isArray(data.dayPeriods) || data.dayPeriods.some(([from, name], index) => !Number.isInteger(from) || from < 0 || from >= 1440 ||
      (index === 0 ? from !== 0 : from <= data.dayPeriods[index - 1][0]) || typeof name !== "string" || name.length === 0))
      throw new Error(`Day periods for '${id}' must start at 0 and ascend.`);
    dayPeriods[id] = data.dayPeriods.flatMap(([from, name]) => [String(from), name]);
  }
  // CLDR list patterns as [start, middle, end, 2] for each Intl.ListFormat type and style, 36
  // patterns in all. Each has {0} before {1}; some add text before, between or after (mi, ti).
  if (data.listPatterns !== undefined) {
    if (Object.keys(data.listPatterns).join() !== listTypes.join())
      throw new Error(`List patterns for '${id}' must list ${listTypes.join(", ")}.`);
    list[id] = listTypes.flatMap((type) => {
      if (Object.keys(data.listPatterns[type]).join() !== listStyles.join())
        throw new Error(`List patterns '${id}/${type}' must list ${listStyles.join(", ")}.`);
      return listStyles.flatMap((style) => {
        const patterns = data.listPatterns[type][style];
        if (!Array.isArray(patterns) || patterns.length !== 4)
          throw new Error(`List patterns '${id}/${type}/${style}' must be [start, middle, end, 2].`);
        for (const pattern of patterns) {
          const first = typeof pattern === "string" ? pattern.indexOf("{0}") : -1, second = first < 0 ? -1 : pattern.indexOf("{1}");
          if (first < 0 || second < first || /[{}]/.test(pattern.replace("{0}", "").replace("{1}", "")))
            throw new Error(`List pattern '${id}/${type}/${style}' must contain {0} and then {1}.`);
        }
        return patterns;
      });
    });
  }
  // Relative time as 17 slots per unit: the past patterns by plural category (zero … other), the
  // future patterns likewise, then the texts of relative-type -2 … 2; absent entries are null.
  if (data.relativeTime !== undefined) {
    if (Object.keys(data.relativeTime).join() !== units.join())
      throw new Error(`Relative time for '${id}' must list ${units.join(", ")}.`);
    relativeTime[id] = units.flatMap((unit) => {
      const field = data.relativeTime[unit];
      const patterns = (direction) => pluralCategories.map((category) => {
        const pattern = field[direction]?.[category];
        if (pattern !== undefined && (typeof pattern !== "string" || pattern.split("{0}").length > 2 || /[{}]/.test(pattern.replace("{0}", ""))))
          throw new Error(`Relative-time pattern '${id}/${unit}/${direction}/${category}' may contain {0} at most once.`);
        return pattern ?? null;
      });
      const past = patterns("past"), future = patterns("future");
      if (past[5] === null || future[5] === null) throw new Error(`Relative time '${id}/${unit}' needs 'other' patterns.`);
      for (const key of Object.keys(field.relative ?? {})) if (!offsets.includes(key)) throw new Error(`Unknown relative type '${id}/${unit}/${key}'.`);
      return [...past, ...future, ...offsets.map((offset) => field.relative?.[offset] ?? null)];
    });
  }
}

// The 12 fields of a numbering system's formats in a locale: its digits, the decimal, group and minus symbols, the
// primary and secondary group sizes of the standard decimal pattern and of the percent pattern, and the prefix and
// suffix of the positive and negative percent patterns with the percent and minus signs in place. A pattern without
// a negative subpattern puts the minus sign before the positive prefix, as ICU does.
function numberFields(id, system, formats) {
  const label = `${id}/${system}`;
  if (!Object.hasOwn(numberingSystems, system)) throw new Error(`Numbers '${label}' use an unknown numbering system.`);
  for (const name of ["decimal", "group", "minusSign", "percentSign", "decimalFormat", "percentFormat"])
    if (typeof formats?.[name] !== "string" || formats[name].length === 0 || formats[name].length > 16) throw new Error(`Numbers '${label}' need ${name}.`);
  const affix = (text) => {
    if (/[#0-9,.¤‰E@*;]/.test(text.replace(/'[^']*'/g, ""))) throw new Error(`Unsupported pattern affix in '${label}': ${text}`);
    return text.replace(/'([^']*)'|%|-/g, (match, quoted) => quoted !== undefined ? quoted : match === "%" ? formats.percentSign : formats.minusSign);
  };
  const subpattern = (pattern) => {
    const first = pattern.search(/[#0,.]/), last = Math.max(...["#", "0", ",", "."].map((item) => pattern.lastIndexOf(item)));
    const body = pattern.slice(first, last + 1);
    if (first < 0 || !/^[#,]*0+(?:\.[0#]*)?$/.test(body)) throw new Error(`Unsupported number pattern in '${label}': ${pattern}`);
    const groups = body.split(".")[0].split(",");
    const primary = groups.length > 1 ? groups.at(-1).length : 0, secondary = groups.length > 2 ? groups.at(-2).length : primary;
    return { prefix: pattern.slice(0, first), suffix: pattern.slice(last + 1), primary, secondary };
  };
  const decimal = subpattern(formats.decimalFormat);
  if (formats.decimalFormat.includes(";") || decimal.prefix !== "" || decimal.suffix !== "") throw new Error(`Unsupported decimal pattern in '${label}'.`);
  const [positiveText, negativeText] = formats.percentFormat.split(";"), positive = subpattern(positiveText);
  if (!positiveText.includes("%")) throw new Error(`The percent pattern of '${label}' has no percent sign.`);
  const negative = negativeText === undefined ? { prefix: "-" + positive.prefix, suffix: positive.suffix } : subpattern(negativeText);
  return [numberingSystems[system], formats.decimal, formats.group, formats.minusSign, String(decimal.primary), String(decimal.secondary),
    String(positive.primary), String(positive.secondary), affix(positive.prefix), affix(positive.suffix), affix(negative.prefix), affix(negative.suffix)];
}

// Currencies, units and compact notation (runic-formats-48.2.json): checked here, then compressed whole into the
// compiler's resource, which selects the entries a catalog uses (Rmf2FormatDataV1.Select).
const formatsSource = JSON.parse(await readFile(formatsSourcePath, "utf8"));
if (formatsSource.source?.cldrVersion !== source.source.cldrVersion) throw new Error("The formats pin and the CLDR subset differ in version.");
for (const [code, digits] of Object.entries(formatsSource.currencies ?? {}))
  if (!/^[A-Z]{3}$/.test(code) || !Number.isInteger(digits) || digits < 0 || digits > 4) throw new Error(`Invalid currency '${code}'.`);
sortedKeys(formatsSource.currencies, "currencies");
if (!Array.isArray(formatsSource.units) || formatsSource.units.some((unit, index) => !/^[a-z]+(?:-[a-z0-9]+)*$/.test(unit) || (index > 0 && formatsSource.units[index - 1] >= unit)))
  throw new Error("Formats units must be sorted, unique unit identifiers.");
const patternSlot = (value, label) => { if (value !== null && (typeof value !== "string" || value.length === 0 || value.length > 128)) throw new Error(`Invalid pattern ${label}.`); };
const slotArray = (value, maximum, label) => {
  if (!Array.isArray(value) || value.length === 0 || value.length > maximum || value.at(-1) === null) throw new Error(`Invalid record ${label}.`);
  value.forEach((item, index) => patternSlot(item, `${label}[${index}]`));
  if (value[0] === null) throw new Error(`Record ${label} needs its first value.`);
};
for (const id of sortedKeys(formatsSource.locales, "formats locales")) {
  if (!tagPattern.test(id)) throw new Error(`Invalid formats locale '${id}'.`);
  const data = formatsSource.locales[id];
  for (const section of Object.keys(data)) if (!["currencyFormats", "currencies", "units", "per", "compact"].includes(section)) throw new Error(`Unknown formats section ${id}/${section}.`);
  for (const [system, record] of Object.entries(data.currencyFormats ?? {})) {
    if (!Object.hasOwn(numberingSystems, system)) throw new Error(`Currency formats ${id}/${system} use an unknown numbering system.`);
    slotArray(record, 10, `${id}/currencyFormats/${system}`);
    for (const pattern of record.slice(0, 2)) if (!/¤/.test(pattern)) throw new Error(`Currency pattern ${id}/${system} has no currency sign.`);
  }
  for (const [code, record] of Object.entries(data.currencies ?? {})) {
    if (!Object.hasOwn(formatsSource.currencies, code)) throw new Error(`Currency ${id}/${code} is not pinned.`);
    slotArray(record, 11, `${id}/currencies/${code}`);
  }
  for (const [unit, record] of Object.entries(data.units ?? {})) {
    if (!formatsSource.units.includes(unit) || !Array.isArray(record) || record.length === 0 || record.length > 3 || record[0] === null) throw new Error(`Invalid unit ${id}/${unit}.`);
    record.forEach((width, index) => { if (width !== null) slotArray(width, 7, `${id}/units/${unit}/${index}`); });
  }
  for (const [width, pattern] of Object.entries(data.per ?? {})) if (!["long", "short", "narrow"].includes(width) || !/\{0\}/.test(pattern) || !/\{1\}/.test(pattern)) throw new Error(`Invalid per pattern ${id}/${width}.`);
  for (const [system, styles] of Object.entries(data.compact ?? {})) for (const [style, magnitudes] of Object.entries(styles)) {
    if (!["short", "long"].includes(style) || !Array.isArray(magnitudes) || magnitudes.length < 12) throw new Error(`Invalid compact table ${id}/${system}/${style}.`);
    magnitudes.forEach((record, index) => slotArray(record, 7, `${id}/compact/${system}/${style}/${index + 3}`));
  }
}
const formatResourceJson = JSON.stringify(formatsSource);
// Brotli (BrotliStream in the compiler) is about a third smaller than deflate for this data.
const formatResource = brotliCompressSync(Buffer.from(formatResourceJson, "utf8"), {
  params: { [zlibConstants.BROTLI_PARAM_QUALITY]: 11, [zlibConstants.BROTLI_PARAM_SIZE_HINT]: formatResourceJson.length },
});

// The compressed resource holds the number formats, lists and relative times; .NET reads it lazily.
const resourceJson = JSON.stringify({ numbers, weekdays, dayPeriods, list, relativeTime });
const resource = deflateRawSync(Buffer.from(resourceJson, "utf8"), { level: 9 });

// No-break spaces and bidi marks stay visible as escapes.
const csString = (value) =>
  JSON.stringify(value)
    .replaceAll("\u00a0", "\\u00a0").replaceAll("\u202f", "\\u202f")
    .replaceAll("\u200e", "\\u200e").replaceAll("\u200f", "\\u200f")
    .replaceAll("\u2028", "\\u2028").replaceAll("\u2029", "\\u2029");
const wrap = (items, indent) => {
  const lines = []; let line = "";
  for (const item of items) {
    if (line.length + item.length + 2 > 116 - indent.length) { lines.push(line.trimEnd()); line = ""; }
    line += item + ", ";
  }
  if (line) lines.push(line.replace(/, $/, ""));
  return lines.map((text) => indent + text).join("\n");
};
const csArray = (type, values, indent = "        ") =>
  values.length === 0 ? "[]" : `\n    [\n${wrap(values.map((value) => (type === "string" ? csString(value) : String(value))), indent)}\n    ]`;
const keysAndValues = (name, table, valueType = "string") =>
  `    internal static readonly string[] ${name}Keys =${csArray("string", Object.keys(table))};\n` +
  `    internal static readonly ${valueType}[] ${name}Values =${csArray(valueType, Object.values(table))};`;

const tables = `// <auto-generated by eng/generate-cldr.mjs from CLDR ${source.source.cldrVersion} />
// Linked into Runic.Translations and Runic.Translations.Compiler; each assembly gets its own internal copy.
#if RUNIC_TRANSLATIONS_COMPILER
namespace Runic.Translations.Compiler;
#else
namespace Runic.Translations;
#endif

/// <summary>Pinned CLDR locale tables, each sorted by key in ordinal order.</summary>
internal static class GeneratedLocaleTables
{
    internal const string CldrVersion = ${csString(source.source.cldrVersion)};
    /// <summary>The number of plural rule sets; rule 0 is the root rule (<c>other</c> only).</summary>
    internal const int PluralRuleCount = ${rules.length};
    /// <summary>CLDR likelySubtags of the languages with CLDR data, without entries that shorter keys imply.</summary>
${keysAndValues("Likely", likely)}
    /// <summary>CLDR parentLocales as data IDs, without the parents the default rules give; <c>root</c> ends a chain.</summary>
${keysAndValues("Parent", parents)}
    /// <summary>Deprecated and legacy language codes and their replacements: <c>iw</c> → <c>he</c>, <c>sh</c> → <c>sr-Latn</c>.</summary>
${keysAndValues("Alias", aliases)}
    /// <summary>The cardinal plural rule set of each CLDR plural locale.</summary>
${keysAndValues("Cardinal", cardinal, "byte")}
    /// <summary>The ordinal plural rule set of each CLDR ordinal locale.</summary>
${keysAndValues("Ordinal", ordinal, "byte")}
    /// <summary>CLDR <c>minimumGroupingDigits</c> where it differs from the parent locale's (1 at the root).</summary>
${keysAndValues("Grouping", grouping, "byte")}
    /// <summary>The data IDs with number formats in <c>GeneratedLocaleData.bin</c>.</summary>
    internal static readonly string[] NumberIds =${csArray("string", Object.keys(numbers))};
    /// <summary>The data IDs with format-context weekday names (or an empty override) in <c>GeneratedLocaleData.bin</c>.</summary>
    internal static readonly string[] WeekdayIds =${csArray("string", Object.keys(weekdays))};
    /// <summary>The data IDs with flexible day periods (or an empty override) in <c>GeneratedLocaleData.bin</c>.</summary>
    internal static readonly string[] DayPeriodIds =${csArray("string", Object.keys(dayPeriods))};
    /// <summary>The data IDs with list patterns in <c>GeneratedLocaleData.bin</c>.</summary>
    internal static readonly string[] ListIds =${csArray("string", Object.keys(list))};
    /// <summary>The data IDs with relative-time data in <c>GeneratedLocaleData.bin</c>.</summary>
    internal static readonly string[] RelativeTimeIds =${csArray("string", Object.keys(relativeTime))};
    /// <summary>CLDR's right-to-left scripts (ISO 15924), sorted: a locale is right-to-left when its script, explicit or likely, is one of them.</summary>
    internal static readonly string[] RightToLeftScripts =${csArray("string", rightToLeftScripts)};
}
`;

const ruleCases = rules.map((_, index) => `        ${index} => Rule${index}(in operands),`).join("\n");
const ruleMethods = rules.map((rule, index) => `
    private static string Rule${index}(in PluralOperands o)
    {
${rule.csharp}
    }`).join("\n");
const runtime = `// <auto-generated by eng/generate-cldr.mjs from CLDR ${source.source.cldrVersion} />
#nullable enable
namespace Runic.Translations;

internal static partial class GeneratedLocaleData
{
    internal const string CldrVersion = ${csString(source.source.cldrVersion)};

    /// <summary>Applies a pinned CLDR plural rule set (an index from <see cref="CldrLocales.PluralRule"/>) to the operands of a visible decimal.</summary>
    internal static string ApplyPluralRule(int rule, in PluralOperands operands) => rule switch
    {
${ruleCases}
        _ => throw new System.ArgumentOutOfRangeException(nameof(rule)),
    };
${ruleMethods}
}
`;

const esmPluralRuleSources = rules.map((rule) => rule.javascript);
const registry = `// <auto-generated by eng/generate-cldr.mjs from CLDR ${source.source.cldrVersion} />
namespace Runic.Translations.Compiler;

internal static partial class TranslationCapabilityRegistry
{
    internal const string CldrVersion = ${csString(source.source.cldrVersion)};
    /// <summary>CLDR's right-to-left scripts, for the ESM runtime's <c>localeDirection</c>.</summary>
    internal const string RightToLeftScriptsJson = ${csString(JSON.stringify(rightToLeftScripts))};
    /// <summary>The JavaScript selector of each plural rule set over BigInt operands <c>{ i, v, w, f, t, e }</c>.</summary>
    internal static readonly string[] EsmPluralRuleSources =${csArray("string", esmPluralRuleSources)};
    /// <summary>The categories each plural rule set can select, in CLDR order.</summary>
    internal static readonly string[][] PluralRuleCategories =${csArray("raw", rules.map((rule) => `[${rule.categories.map(csString).join(", ")}]`))};
}
`;

// The capability matrix lists every CLDR language and script locale (regional variants inherit).
const pluralLanguage = (table, id) => chain(dataId(id, likely), {}, true).some((candidate) => Object.hasOwn(table, candidate));
const dataLocale = (table, id) => chain(dataId(id, likely), parents).some((candidate) => Object.hasOwn(table, candidate));
const matrixTags = [...new Set([...Object.keys(cardinal), ...source.availableLocales])]
  .filter((id) => parseTag(id).region === null && parseTag(id).variants.length === 0)
  .sort(ordinalSort);
const matrix =
  JSON.stringify(
    {
      capabilityMatrixVersion: 1,
      releaseTarget,
      cldr: {
        version: source.source.cldrVersion,
        jsonTag: source.source.cldrJsonTag,
        source: source.source.archive,
        sha512: source.source.archiveSha512,
        license: source.source.license,
      },
      backends: ["dotnet", "esm"],
      equivalence: {
        exact: "Backends produce the same canonical value for every accepted portable input.",
        semantic: "Backends preserve meaning but platform globalization can change punctuation, spacing, names, or representable decimal precision.",
      },
      formatterProfile: {
        string: { default: "exact" },
        boolean: { default: "exact" },
        integer: { plain: "exact", grouped: "exact" },
        number: { plain: "exact", grouped: "exact", fixed: "exact", percent: "exact" },
        date: { iso: "exact", localized: "semantic" },
        time: { iso: "exact", localized: "semantic" },
        datetime: { iso: "semantic", localized: "semantic" },
        uuid: { d: "exact", n: "exact" },
        currency: { symbol: "exact", narrowSymbol: "exact", code: "exact", name: "exact", accounting: "exact" },
        unit: { short: "exact", long: "exact", narrow: "exact", compound: "exact" },
        compact: { short: "exact", long: "exact" },
      },
      messageProfile: { cardinal: "exact", ordinal: "exact", relativeTime: "exact", list: "exact" },
      locales: matrixTags.map((tag) => {
        // A locale with CLDR data but no plural rules of its own (agq, rw) uses the root rule, `other` only.
        const plural = pluralLanguage(cardinal, tag) || dataLocale(list, tag) || dataLocale(relativeTime, tag);
        return {
          tag,
          cardinal: plural,
          ordinal: plural,
          relativeTime: plural && dataLocale(relativeTime, tag),
          list: dataLocale(list, tag),
        };
      }),
    },
    null,
    2,
  ) + "\n";

await Promise.all([
  emit(registryPath, registry),
  emit(runtimePath, runtime),
  emit(tablesPath, tables),
  emitResource(resourcePath, resource, resourceJson, inflateRawSync),
  emitResource(formatResourcePath, formatResource, formatResourceJson, brotliDecompressSync),
  emit(matrixPath, matrix),
]);

async function emit(file, content) {
  if (check) {
    const current = await readFile(file, "utf8").catch(() => "");
    if (current !== content) throw new Error(`${relative(root, file)} is stale; run bun eng/generate-cldr.mjs`);
    return;
  }
  await writeFile(file, content, "utf8");
}

// The check compares the decompressed content, so another zlib or brotli build cannot make it fail.
async function emitResource(file, content, json, decompress) {
  if (check) {
    const current = await readFile(file).catch(() => null);
    let text = null;
    try { text = current === null ? null : decompress(current).toString("utf8"); } catch { text = null; }
    if (text !== json)
      throw new Error(`${relative(root, file)} is stale; run bun eng/generate-cldr.mjs`);
    return;
  }
  await writeFile(file, content);
}
