// The Translations corpus (specs/translations/corpus) as the driver's cases, keyed `<file stem>/<id>` as the corpus README
// ("Case ids") keys them. The module covers what a runtime with a pack reader runs against the committed compiled artifacts
// (README, "Compiled artifacts"): the message corpora, the pack recipes, persisted text references and locale negotiation.
import { readFileSync } from "node:fs";
import { join } from "node:path";
import { caseHash, type Case } from "../core/index";
import { expandMacros as expandReference } from "./recipes";

// Every case is for a runtime: the corpus has no compiler-only case in this module.
const roles = ["runtime"];

const read = (root: string, file: string) => JSON.parse(readFileSync(join(root, file), "utf8"));

// The message corpora, each formatted through the committed artifact of its case's group.
const messageCorpora = [
  "rmf2-list-conformance.json",
  "rmf2-grouping-conformance.json",
  "rmf2-date-percent-conformance.json",
  "rmf2-bidi-conformance.json",
  "rmf2-currency-unit-conformance.json",
];

// The group of compiled/index.json a pack recipe's packBase names ("../compiled/rmf2-v1/de.json" → "rmf2-v1/de").
const groupOf = (packBase: string) => packBase.replace(/^(\.\.\/)?compiled\//, "").replace(/\.json$/, "");

export function loadTranslationsCorpus(root: string): Case[] {
  const corpus = join(root, "../corpus");
  const index = read(corpus, "compiled/index.json");
  // A message corpus case's group and key: compiled/index.json lists each group's cases.
  const keys = new Map<string, { group: string; key: string }>();
  for (const group of index.groups) for (const [id, key] of Object.entries<string>(group.cases ?? {})) keys.set(`${group.corpus}#${id}`, { group: group.id, key });
  const cases: Case[] = [];
  const add = (file: string, id: string, set: string, kind: string, source: unknown, body: any, requires: string[] = []) =>
    cases.push({ id: `${file.replace(/\.json$/, "").replace(/\/index$/, "")}/${id}`, kind, set, file: `corpus/${file}`, roles, requires, sha256: caseHash(source), body });

  for (const file of messageCorpora) {
    const set = file.replace(/\.json$/, "");
    for (const c of read(corpus, file).cases) {
      const target = keys.get(`${file}#${c.id}`);
      if (!target) throw new Error(`compiled/index.json has no group for ${file} ${c.id}; run bun eng/generate-corpus.mjs`);
      add(file, c.id, set, "format", c, {
        ...target,
        locale: c.requested ?? c.requestedLocale ?? c.locale,
        arguments: c.arguments,
        expected: c.expected,
        alternates: c.alternates ?? [],
        note: c.note,
        // The date and percent corpus's one tolerance: platform ICU versions write some spaces and apostrophes differently.
        tolerant: file === "rmf2-date-percent-conformance.json",
      });
    }
  }

  // Pack recipes over a committed artifact: each must be rejected with its reason, or accepted (validPacks).
  const recipes = (file: string, set: string, defaultRejection: string | null) => {
    const data = read(corpus, file);
    const group = groupOf(data.packBase);
    for (const c of data.invalidPacks ?? data.cases) add(file, c.id, set, "pack", c, { group, recipe: c, rejection: c.rejection ?? defaultRejection });
    for (const c of data.validPacks ?? []) add(file, c.id, set, "pack", c, { group, recipe: c, rejection: null });
  };
  recipes("rmf2-format-data-invalid.json", "rmf2-format-data-invalid", "RTR0023/malformed");
  recipes("rmf2-v1/index.json", "rmf2-v1.packs", null);
  recipes("rmf2-document-v1/index.json", "rmf2-document-v1.packs", null);

  // Persisted text references against the reader catalog's artifacts: resolutions, rejected wire texts and written texts.
  const transport = "transport-v2-persisted-references.json";
  const references = read(corpus, transport);
  const fingerprints: Record<string, string> = references.fingerprints;
  const wire = (c: any) =>
    (c.json ?? JSON.stringify(expandReference(c.reference))).replace(/"\$(writer|reader)"/g, (_: string, side: string) => JSON.stringify(fingerprints[`$${side}`]));
  for (const c of references.cases)
    add(transport, c.id, "transport-v2", "reference", c, { group: `transport-v2/reader.${c.locale}`, locale: c.locale, json: wire(c), expected: c.expected });
  for (const c of references.rejected) add(transport, `rejected/${c.id}`, "transport-v2.rejected", "reference-rejected", c, { json: wire(c), reason: c.reason });
  for (const c of references.written) {
    const reference = JSON.parse(JSON.stringify(expandReference(c.reference)).replace(/"\$reader"/g, JSON.stringify(fingerprints.$reader)));
    add(transport, `written/${c.id}`, "transport-v2.written", "reference-written", c,
      { group: "transport-v2/reader.en", reference, expected: c.expected.replaceAll("$reader", fingerprints.$reader) },
      reference.version === 1 ? ["transport-v1-writer"] : []);
  }

  // Locale negotiation: one case per group, with every preference list of the group.
  for (const g of read(corpus, "locale-negotiation.json").groups)
    add("locale-negotiation.json", g.id, "locale-negotiation", "negotiate", g, { group: `negotiation/${g.id}`, locales: g.locales, base: g.base, cases: g.cases });

  const seen = new Set<string>();
  for (const c of cases) {
    if (seen.has(c.id)) throw new Error(`duplicate case id ${c.id}`);
    seen.add(c.id);
  }
  return cases;
}
