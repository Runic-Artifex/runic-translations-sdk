// A corpus as the driver sees it: a list of cases, and the committed case list (cases.json) every corpus module writes
// the same way.
import { createHash } from "node:crypto";

// One case of a corpus. `kind` and `body` belong to the corpus module; the driver reads the rest.
export type Case = { id: string; kind: string; set: string; file: string; roles: string[]; requires: string[]; sha256: string; body: any };

// The hash of a case's content, independent of formatting, so a semantic edit shows in review (R2 section 3).
export function caseHash(value: unknown): string {
  const canonical = (v: any): string =>
    Array.isArray(v) ? `[${v.map(canonical).join(",")}]` : v && typeof v === "object" ? `{${Object.keys(v).sort().map((k) => JSON.stringify(k) + ":" + canonical(v[k])).join(",")}}` : JSON.stringify(v);
  return createHash("sha256").update(canonical(value)).digest("hex");
}

// The committed case list (cases.json): one case per line, without its body, so a semantic edit to a case shows as a
// changed sha256 in review (R6 section 4, rule 3).
export function formatCases(format: string, corpus: string, cases: Case[]): string {
  const lines = cases.map(({ body, ...c }, i) => `    ${JSON.stringify(c)}${i < cases.length - 1 ? "," : ""}`);
  return ["{", `  "format": ${JSON.stringify(format)},`, `  "corpus": ${JSON.stringify(corpus)},`, `  "cases": [`, ...lines, "  ]", "}", ""].join("\n");
}
