/** Language-server state of one workspace folder, as the status bar item shows it. */
export type ServerState = "starting" | "running" | "stopped" | "failed";

/** The `runic/documentInfo` answer: the owning catalog and the file's locale (null when unknown). */
export interface DocumentInfo { project: string | null; catalog: string | null; baseLocale: string | null; locale: string | null }

export interface StatusPresentation { text: string; tooltip: string; error: boolean }

/** First line of an error, without the `Error: ` prefix that String(error) adds. */
export function errorSummary(error: unknown): string {
  const text = error instanceof Error ? error.message : String(error);
  return (text.split(/\r?\n/, 1)[0] ?? "").replace(/^Error: /, "").trim() || "Unknown error.";
}

/**
 * The status bar text and tooltip. Running shows the catalog and the file's locale
 * (`Runic: app · de`); the other states name the server state, and a failure keeps its reason
 * in the tooltip. Coverage for the file's locale joins the running text in a later release.
 */
export function statusPresentation(state: ServerState, info?: DocumentInfo, failure?: string): StatusPresentation {
  const lines = [`Runic language server: ${state}`];
  if (state === "failed" && failure) lines.push(failure);
  if (info?.catalog) lines.push(`Catalog: ${info.catalog}`);
  if (info?.locale) lines.push(`Locale: ${info.locale}${info.baseLocale && info.baseLocale.toLowerCase() === info.locale.toLowerCase() ? " (base)" : ""}`);
  else if (info?.baseLocale) lines.push(`Base locale: ${info.baseLocale}`);
  lines.push(state === "failed" ? "Select to show the output." : "Select to show the Runic Translations output.");
  const label = state === "running"
    ? `$(globe) Runic${info?.catalog ? `: ${info.catalog}` : ""}${info?.locale ? ` · ${info.locale}` : ""}`
    : state === "starting" ? "$(sync~spin) Runic: starting"
    : state === "failed" ? "$(error) Runic: not running"
    : "$(circle-slash) Runic: stopped";
  return { text: label, tooltip: lines.join("\n"), error: state === "failed" };
}

/** Shows a notice once per key until the key is reset (by a restart or a successful start). */
export class OneTimeNotice {
  private readonly shown = new Set<string>();
  public claim(key: string): boolean {
    if (this.shown.has(key)) return false;
    this.shown.add(key);
    return true;
  }
  public reset(key?: string): void {
    if (key === undefined) this.shown.clear(); else this.shown.delete(key);
  }
}
