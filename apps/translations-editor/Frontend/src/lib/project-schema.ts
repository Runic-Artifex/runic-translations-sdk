/**
 * The `runic.json` project schema version this editor's compiler writes and reads (`schemaVersion`; the compiler
 * reports any other value as RTR0003). Project schema 1 is the only version so far.
 */
export const currentProjectSchemaVersion = 1;

/**
 * Whether a project's `schemaVersion` is an older file format: a valid earlier version, not a newer or malformed one
 * (the compiler's diagnostics explain those). The validation panel names only an older format (D086.1).
 */
export function isOlderProjectFormat(schemaVersion: number, current: number = currentProjectSchemaVersion): boolean {
  return Number.isInteger(schemaVersion) && schemaVersion >= 1 && schemaVersion < current;
}
