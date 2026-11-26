// The driver core's export manifest: everything a repository's eng/conformance/cli.ts and its corpus modules use.
// runic-cli-sdk and runic-translations-sdk vendor this directory unchanged (core/README.md), so a change here is a
// change to their API too.
export { Adapter, AdapterError, type AdapterSpec } from "./adapter";
export { caseHash, formatCases, type Case } from "./cases";
export { createDriver, type CorpusModule, type DriverOptions, type Implementation, type RunContext } from "./driver";
export { PREFIX, names, sameFormat } from "./names";
export {
  compare,
  formatStatus,
  lintStatus,
  statusFromReceipt,
  type Finding,
  type FindingKind,
  type Maturity,
  type Outcome,
  type OutcomeKind,
  type Receipt,
  type StatusEntry,
  type StatusFile,
  type StatusKind,
} from "./results";
