// Locale identity over the pinned CLDR data: likely subtags, the data ID of a tag and its
// parent chains. eng/extract-cldr.mjs and eng/generate-cldr.mjs use it; the ESM runtime
// (Rmf2EsmRuntimeV5.js) and the .NET runtime (LocaleData.cs) implement the same algorithm.

/** Splits a canonical tag into language, script, region and variants; extensions are dropped. */
export function parseTag(tag) {
  const parts = tag.split("-");
  const result = { language: parts[0].toLowerCase(), script: null, region: null, variants: [] };
  let index = 1;
  // Extended language subtags are skipped.
  if (parts[0].length <= 3)
    for (let count = 0; count < 3 && index < parts.length && /^[A-Za-z]{3}$/.test(parts[index]); count++) index++;
  if (index < parts.length && /^[A-Za-z]{4}$/.test(parts[index])) {
    const script = parts[index++];
    result.script = script[0].toUpperCase() + script.slice(1).toLowerCase();
  }
  if (index < parts.length && (/^[A-Za-z]{2}$/.test(parts[index]) || /^\d{3}$/.test(parts[index])))
    result.region = parts[index++].toUpperCase();
  while (index < parts.length && (/^[A-Za-z0-9]{5,8}$/.test(parts[index]) || /^\d[A-Za-z0-9]{3}$/.test(parts[index])))
    result.variants.push(parts[index++].toLowerCase());
  return result;
}

export function formatTag({ language, script, region, variants = [] }) {
  return [language, script, region, ...variants].filter(Boolean).join("-");
}

/**
 * Adds likely subtags as UTS #35 does: the first of language-script-region, language-region,
 * language-script and language found in the table fills the missing script and region.
 * Returns the parsed tag unchanged when the language has no likely subtags.
 */
export function maximize(tag, likely) {
  const parsed = typeof tag === "string" ? parseTag(tag) : tag;
  const { language, script, region } = parsed;
  const keys = [];
  if (script && region) keys.push(`${language}-${script}-${region}`);
  if (region) keys.push(`${language}-${region}`);
  if (script) keys.push(`${language}-${script}`);
  keys.push(language);
  for (const key of keys) {
    const value = likely[key];
    if (value === undefined) continue;
    const found = parseTag(value);
    return { ...parsed, script: script ?? found.script, region: region ?? found.region };
  }
  return parsed;
}

/** The language's default script, the script of its likely subtags. */
export function defaultScript(language, likely) {
  const value = likely[language];
  return value === undefined ? null : parseTag(value).script;
}

/**
 * The CLDR data ID of a tag: its likely script is added, then omitted again when it is the
 * language's default script, so zh-TW is zh-Hant-TW, zh-Hans-SG is zh-SG and sr-Latn stays.
 */
export function dataId(tag, likely) {
  const parsed = typeof tag === "string" ? parseTag(tag) : tag;
  const script = parsed.script ?? maximize({ ...parsed, script: null }, likely).script;
  return formatTag({ ...parsed, script: script !== null && script === defaultScript(parsed.language, likely) ? null : script });
}

/**
 * The parent of a data ID: an explicit CLDR parentLocale, else the ID without its last variant
 * or its region; a language-script ID whose script is not the default (sr-Latn) has the root as
 * parent (CLDR's nonlikelyScript rule), as does a bare language. Null is the root.
 * With `truncate` (CLDR's plurals component) explicit parents and the script rule do not apply.
 */
export function parentId(id, parents, truncate = false) {
  if (!truncate && Object.hasOwn(parents, id)) return parents[id] === "root" ? null : parents[id];
  const parsed = parseTag(id);
  if (parsed.variants.length > 0) return formatTag({ ...parsed, variants: parsed.variants.slice(0, -1) });
  if (parsed.region) return formatTag({ ...parsed, region: null });
  if (parsed.script && truncate) return parsed.language;
  return null;
}

/** The data ID followed by its ancestors, root excluded. */
export function chain(id, parents, truncate = false) {
  const result = [];
  for (let current = id; current !== null && result.length < 16; current = parentId(current, parents, truncate))
    result.push(current);
  return result;
}
