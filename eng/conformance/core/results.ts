// Outcomes, receipts (observed, never committed) and status files (committed expectations), and the comparison
// between them that both the local command and the later CI gate use (eng/conformance/README.md, "Status files").
import { names, sameFormat } from "./names";

export type OutcomeKind = "pass" | "fail" | "unsupported" | "excluded";
export type Outcome = { outcome: OutcomeKind; detail?: string; step?: number; requires?: string[]; ms?: number };

export type Receipt = {
  format: string;
  corpus: { id: string; root: string; commit?: string };
  implementation: { id: string; name?: string; version?: string };
  adapter: { protocol: string; command: string[] };
  run: { started: string; durationMs: number; filter?: string };
  results: Record<string, Outcome>;
};

// A committed status (R7 T1-13):
// - pass: runs and passes;
// - unsupported: a feature or seam the implementation does not list (`requires`), or a gap with an issue;
// - excluded: a role or profile the implementation does not have (`reason`, such as `role:client`);
// - known-failure: a case it fails today; needs an issue, and only a development or preview implementation has any;
// - deviation: an intentional difference (D011), citing its row of the area's difference table (`deviation`).
export type StatusKind = "pass" | "unsupported" | "excluded" | "known-failure" | "deviation";
export type StatusEntry = { status: StatusKind; reason?: string; requires?: string[]; issue?: string; deviation?: string };
export type Maturity = "development" | "preview" | "stable";
export type StatusFile = {
  format: string;
  corpus: { id: string };
  implementation: { id: string; roles: string[]; maturity: Maturity };
  cases: Record<string, StatusEntry>;
};

export type FindingKind = "regression" | "fixed" | "new-case" | "stale-status" | "mismatch" | "not-run";
export type Finding = { id: string; kind: FindingKind; text: string };

// Compares a run with the committed statuses. `ids` is the corpus's case list: with it, a case without a status and a
// status without a case are findings too. A filtered run (`filtered`) checks only the cases it ran.
export function compare(receipt: Receipt, status: StatusFile, ids?: string[], filtered = false): Finding[] {
  const findings: Finding[] = [];
  if (!sameFormat(status.format, names.status)) findings.push({ id: "-", kind: "mismatch", text: `status format ${status.format}` });
  const all = ids ?? Object.keys(receipt.results);
  for (const id of all) {
    const entry = status.cases[id];
    const observed = receipt.results[id];
    if (!entry) {
      if (!filtered || observed) findings.push({ id, kind: "new-case", text: `no status for this case (observed ${observed?.outcome ?? "nothing"})` });
      continue;
    }
    if (!observed) {
      if (!filtered) findings.push({ id, kind: "not-run", text: `status ${entry.status}, but the run has no result` });
      continue;
    }
    const o = observed.outcome;
    switch (entry.status) {
      case "pass":
        if (o !== "pass") findings.push({ id, kind: "regression", text: `expected pass, observed ${o}: ${observed.detail ?? ""}` });
        break;
      case "deviation":
        // A deviation can depend on the machine, as .NET's local offset does at UTC: passing is not a finding.
        if (o !== "pass" && o !== "fail") findings.push({ id, kind: "mismatch", text: `status deviation, observed ${o}: ${observed.detail ?? ""}` });
        break;
      case "known-failure":
      case "unsupported":
        if (o === "pass") findings.push({ id, kind: "fixed", text: `status ${entry.status}, now passes: update the status to pass` });
        else if (entry.status === "unsupported" && o !== "unsupported")
          findings.push({ id, kind: "mismatch", text: `status unsupported, observed ${o}: ${observed.detail ?? ""}` });
        else if (entry.status === "known-failure" && o !== "fail")
          findings.push({ id, kind: "mismatch", text: `status known-failure, observed ${o}: ${observed.detail ?? ""}` });
        break;
      case "excluded":
        if (o !== "excluded") findings.push({ id, kind: "mismatch", text: `status excluded, observed ${o}` });
        break;
      default:
        findings.push({ id, kind: "mismatch", text: `unknown status ${(entry as StatusEntry).status}` });
    }
  }
  if (ids && !filtered) {
    const known = new Set(ids);
    for (const id of Object.keys(status.cases)) if (!known.has(id)) findings.push({ id, kind: "stale-status", text: "the corpus has no such case" });
  }
  return findings;
}

// A tracked issue: #123, owner/repo#123 or a GitHub issue URL. "TODO" is what --write-status leaves.
const issuePattern = /^(#\d+|[\w.-]+\/[\w.-]+#\d+|https:\/\/github\.com\/[\w.-]+\/[\w.-]+\/issues\/\d+)$/;

// Rules a status file follows on its own (R7 T1-13). `anchorExists` resolves a deviation's `<file>#<anchor>`, relative
// to the repository root, to whether that row of a difference table exists.
export function lintStatus(status: StatusFile, anchorExists?: (reference: string) => boolean): string[] {
  const problems: string[] = [];
  const maturity = status.implementation?.maturity;
  if (!["development", "preview", "stable"].includes(maturity)) problems.push(`implementation.maturity is ${maturity}, not development, preview or stable`);
  for (const [id, e] of Object.entries(status.cases)) {
    if (e.status === "excluded" && !e.reason) problems.push(`${id}: excluded needs a reason (role:…, profile:…)`);
    if (e.status === "unsupported" && !e.requires?.length && !e.issue) problems.push(`${id}: unsupported needs requires or an issue`);
    if (e.status === "known-failure" && !e.issue) problems.push(`${id}: known-failure needs an issue`);
    if (e.status === "known-failure" && maturity === "stable") problems.push(`${id}: a stable implementation has no known failures`);
    if (e.issue && !issuePattern.test(e.issue) && !(e.issue === "TODO" && maturity === "development"))
      problems.push(`${id}: the issue ${JSON.stringify(e.issue)} is not #<n>, <owner>/<repo>#<n> or an issue URL`);
    if (e.status === "deviation") {
      if (!e.deviation) problems.push(`${id}: deviation needs the anchor of its difference-table row (D011)`);
      else if (!/^[^#\s]+#[\w-]+$/.test(e.deviation)) problems.push(`${id}: the deviation ${JSON.stringify(e.deviation)} is not <file>#<anchor>`);
      else if (anchorExists && !anchorExists(e.deviation)) problems.push(`${id}: the deviation ${e.deviation} names no row of a difference table`);
    }
  }
  return problems;
}

// A status file that records a run as it is: the starting point for a new implementation (--write-status). Failures
// become known-failure with the issue TODO, which the lint accepts only for a development implementation.
export function statusFromReceipt(receipt: Receipt, roles: string[], maturity: Maturity, previous?: StatusFile): StatusFile {
  const cases: Record<string, StatusEntry> = {};
  for (const [id, r] of Object.entries(receipt.results)) {
    const kept = previous?.cases[id];
    if (r.outcome === "pass") cases[id] = { status: "pass" };
    else if (r.outcome === "excluded") cases[id] = { status: "excluded", reason: r.detail ?? "role" };
    else if (kept && (kept.status === "unsupported" ? r.outcome === "unsupported" : ["known-failure", "deviation"].includes(kept.status) && r.outcome === "fail"))
      cases[id] = kept;
    else if (r.outcome === "unsupported") cases[id] = r.requires?.length ? { status: "unsupported", requires: r.requires } : { status: "unsupported", issue: "TODO", reason: r.detail };
    else cases[id] = { status: "known-failure", issue: "TODO", reason: (r.detail ?? "").split("\n")[0].slice(0, 200) };
  }
  return { format: names.status, corpus: { id: receipt.corpus.id }, implementation: { id: receipt.implementation.id, roles, maturity }, cases };
}

// A status file as committed: one case per line, so a review shows each changed status on its own line.
export function formatStatus(status: StatusFile): string {
  const ids = Object.keys(status.cases);
  const lines = ids.map((id, i) => `    ${JSON.stringify(id)}: ${JSON.stringify(status.cases[id])}${i < ids.length - 1 ? "," : ""}`);
  return [
    "{",
    `  "format": ${JSON.stringify(status.format)},`,
    `  "corpus": ${JSON.stringify(status.corpus)},`,
    `  "implementation": ${JSON.stringify(status.implementation)},`,
    `  "cases": {`,
    ...lines,
    "  }",
    "}",
    "",
  ].join("\n");
}
