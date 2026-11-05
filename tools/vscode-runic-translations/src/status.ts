/** Language-server state of one workspace folder, as the status bar item shows it. */
export type ServerState = "starting" | "running" | "stopped" | "failed";

/**
 * The `runic/documentInfo` answer: the owning catalog and the file's locale (null when unknown). For an override source of a
 * library catalog, `catalog` is the library's and `library` names its package and version.
 */
export interface DocumentInfo { project: string | null; catalog: string | null; baseLocale: string | null; locale: string | null; library?: string | null }

export interface StatusPresentation { text: string; tooltip: string; error: boolean }

/** First line of an error, without the `Error: ` prefix that String(error) adds. */
export function errorSummary(error: unknown): string {
  const text = error instanceof Error ? error.message : String(error);
  return (text.split(/\r?\n/, 1)[0] ?? "").replace(/^Error: /, "").trim() || "Unknown error.";
}

/** One locale of `runic-translations status` (`runic.translations.status/1`). */
export interface StatusLocale {
  locale: string; fallbackLocale?: string; translated: number; fallback: number; missing: number; coverage: number;
  stale?: number; minimumCoverage?: number; belowThreshold: boolean;
  /** The locale's effective `validation.translationCompleteness`; absent for the base locale and from older tools. */
  completeness?: string;
}
export interface StatusProject { project: string; catalog: string; baseLocale: string; messages: number; locales: StatusLocale[]; reviewFile?: string }
export interface StatusResult { projects: StatusProject[]; belowThreshold: boolean }

/**
 * The payload of `runic-translations status --format json`: the same numbers, thresholds and per-locale completeness
 * policy as the command line and the Translations Editor. A failing envelope throws with its fault message.
 */
export function parseStatus(stdout: string): StatusResult {
  const envelope = JSON.parse(stdout) as { payloadType?: string; payload?: unknown; success?: boolean; fault?: { message?: string } | null };
  if (envelope.payloadType !== "runic.translations.status/1" || typeof envelope.payload !== "object" || envelope.payload === null)
    throw new Error(envelope.fault?.message ?? "runic-translations status returned no status report.");
  const result = envelope.payload as StatusResult;
  if (!Array.isArray(result.projects)) throw new Error("runic-translations status returned an invalid status report.");
  return result;
}

/** The status of `locale` in the report's first project; matched case-insensitively, as locale tags are. */
export function localeStatus(result: StatusResult | undefined, locale: string | null | undefined): StatusLocale | undefined {
  if (!result || !locale) return undefined;
  return result.projects[0]?.locales.find(item => item.locale.toLowerCase() === locale.toLowerCase());
}

const percent = (value: number) => `${Number.isInteger(value) ? value : value.toFixed(2).replace(/0+$/, "").replace(/\.$/, "")}%`;

/** `de 87.5%: 35 translated, 2 from a fallback locale, 3 missing, 1 stale; missing translations are warnings`. */
export function coverageLine(locale: StatusLocale): string {
  const parts = [`${locale.translated} translated`];
  if (locale.fallback) parts.push(`${locale.fallback} from ${locale.fallbackLocale ?? "a fallback locale"}`);
  parts.push(`${locale.missing} missing`);
  if (locale.stale !== undefined) parts.push(`${locale.stale} stale`);
  let line = `Coverage ${locale.locale}: ${percent(locale.coverage)} (${parts.join(", ")})`;
  if (locale.minimumCoverage !== undefined) line += locale.belowThreshold ? `, below its minimum ${percent(locale.minimumCoverage)}` : `, minimum ${percent(locale.minimumCoverage)}`;
  if (locale.completeness) line += `; missing translations are ${locale.completeness === "allow" ? "allowed" : `${locale.completeness}s`}`;
  return line;
}

/** The "Runic: Show Translation Status" view: one table per project, inert (no scripts). */
export function statusHtml(result: StatusResult): string {
  const escape = (value: unknown) => String(value).replaceAll("&", "&amp;").replaceAll("<", "&lt;").replaceAll(">", "&gt;").replaceAll('"', "&quot;");
  const tables = result.projects.map(project => {
    const stale = project.reviewFile !== undefined;
    const rows = project.locales.map(locale => `<tr${locale.belowThreshold ? ' class="below"' : ""}><th scope="row">${escape(locale.locale)}${locale.locale === project.baseLocale ? " (base)" : locale.fallbackLocale ? ` → ${escape(locale.fallbackLocale)}` : ""}</th><td>${locale.translated}</td><td>${locale.fallback}</td><td>${locale.missing}</td>${stale ? `<td>${locale.stale ?? "–"}</td>` : ""}<td>${escape(percent(locale.coverage))}${locale.minimumCoverage !== undefined ? ` (min ${escape(percent(locale.minimumCoverage))})` : ""}</td><td>${escape(locale.completeness ?? (locale.locale === project.baseLocale ? "–" : ""))}</td></tr>`).join("");
    return `<h2>${escape(project.catalog)}</h2><p>${escape(project.project)} · ${project.messages} message(s) · base locale ${escape(project.baseLocale)}${stale ? ` · review file ${escape(project.reviewFile)}` : ""}</p><table><thead><tr><th scope="col">Locale</th><th scope="col">Translated</th><th scope="col">Fallback</th><th scope="col">Missing</th>${stale ? '<th scope="col">Stale</th>' : ""}<th scope="col">Coverage</th><th scope="col">Missing translations</th></tr></thead><tbody>${rows}</tbody></table>`;
  }).join("");
  return `<!doctype html><html><head><meta http-equiv="Content-Security-Policy" content="default-src 'none'; style-src 'unsafe-inline'"><style>body{font:var(--vscode-font-size) var(--vscode-font-family);padding:1rem}table{border-collapse:collapse}th,td{padding:.25rem .75rem;text-align:right;border-bottom:1px solid var(--vscode-panel-border)}th[scope=row],thead th:first-child{text-align:left}.below{color:var(--vscode-errorForeground)}</style></head><body><h1>Translation status</h1>${tables}<p>From runic-translations status, the same report as the command line and the Translations Editor.</p></body></html>`;
}

/**
 * The status bar text and tooltip. Running shows the catalog, the file's locale and its coverage
 * (`Runic: app · de 87%`); the other states name the server state, and a failure keeps its reason
 * in the tooltip.
 */
export function statusPresentation(state: ServerState, info?: DocumentInfo, failure?: string, coverage?: StatusLocale): StatusPresentation {
  const lines = [`Runic language server: ${state}`];
  if (state === "failed" && failure) lines.push(failure);
  if (info?.catalog) lines.push(`Catalog: ${info.catalog}${info.library ? ` (library ${info.library}, overridden by the application)` : ""}`);
  if (info?.locale) lines.push(`Locale: ${info.locale}${info.baseLocale && info.baseLocale.toLowerCase() === info.locale.toLowerCase() ? " (base)" : ""}`);
  else if (info?.baseLocale) lines.push(`Base locale: ${info.baseLocale}`);
  if (coverage) lines.push(coverageLine(coverage));
  lines.push(state === "failed" ? "Select to show the output." : "Select to show the Runic Translations output.");
  const label = state === "running"
    ? `$(globe) Runic${info?.catalog ? `: ${info.catalog}` : ""}${info?.locale ? ` · ${info.locale}` : ""}${coverage ? ` ${percent(coverage.coverage)}` : ""}`
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
