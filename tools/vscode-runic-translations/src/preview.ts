import type { MessageArtifact } from "../../../apps/translations-editor/Frontend/src/lib/message-model.js";
export interface Preview { key: string; locale: string; ast: MessageArtifact; inputs: Array<{ name: string; type: string }>; examples: Record<string, unknown>[] }
/**
 * A `runic.renderPreview` result. The server also reports how it formatted: the requested locale (the catalog locale
 * unless a regional one was chosen), how that request relates to the shown locale, the locale numbers and dates
 * formatted in, the text direction and the preview time zone.
 */
export interface RenderedPreview {
  key: string; locale: string; runs: RenderedRun[];
  requestedLocale?: string; resolution?: "exact" | "parent" | "related"; formattingLocale?: string; direction?: "ltr" | "rtl"; timeZone?: string;
}
export interface RenderedRun { name?: string; text?: string | null; options?: Record<string, string>; children?: RenderedRun[] }
export const escapeHtml = (value: unknown): string => String(value).replaceAll("&", "&amp;").replaceAll("<", "&lt;").replaceAll(">", "&gt;").replaceAll('"', "&quot;").replaceAll("'", "&#39;");
export function renderedPreviewHtml(preview: RenderedPreview): string {
  const render = (run: RenderedRun): string => {
    if (typeof run.text === "string") return escapeHtml(run.text);
    const children = (run.children ?? []).map(render).join("");
    switch (run.name) {
      case "runic:strong": case "runic:bold": return `<strong>${children}</strong>`;
      case "runic:em": case "runic:italic": return `<em>${children}</em>`;
      case "runic:code": return `<code>${children}</code>`;
      case "runic:br": return "<br>";
      case "runic:link": return `<span role="link" aria-disabled="true">${children}</span>`;
      case "runic:action": return `<button disabled>${children}</button>`;
      case "runic:icon": return `<span role="img" aria-label="${escapeHtml(run.options?.ref ?? "icon")}">◇</span>`;
      default: return `<span title="${escapeHtml(run.name)}">${children || escapeHtml(run.name)}</span>`;
    }
  };
  const body = preview.runs.map(render).join("");
  const direction = preview.direction === "rtl" ? "rtl" : "ltr";
  return `<!doctype html><html><head><meta http-equiv="Content-Security-Policy" content="default-src 'none'; style-src 'unsafe-inline'"><style>body{font:var(--vscode-font-size) var(--vscode-font-family);padding:1rem}button{font:inherit}span[role=link]{text-decoration:underline}.preview{white-space:pre-wrap}</style></head><body><h2>${escapeHtml(preview.key)}</h2><p>${escapeHtml(previewDescription(preview))}</p><div class="preview" lang="${escapeHtml(preview.locale)}" dir="${direction}">${body}</div></body></html>`;
}

/** `de · requested de-CH (parent) · formats in de-CH · time zone Europe/Zurich · inert preview`. */
export function previewDescription(preview: RenderedPreview): string {
  const parts = [preview.locale];
  if (preview.requestedLocale && preview.requestedLocale !== preview.locale) parts.push(`requested ${preview.requestedLocale}${preview.resolution ? ` (${preview.resolution})` : ""}`);
  if (preview.formattingLocale && preview.formattingLocale !== preview.locale) parts.push(`formats in ${preview.formattingLocale}`);
  if (preview.timeZone) parts.push(`time zone ${preview.timeZone}`);
  parts.push("inert preview");
  return parts.join(" · ");
}

export async function resolvePreviewHtml(
  preview: Preview,
  samples: Record<string, string>,
  renderExecutionV2: () => Promise<RenderedPreview>,
): Promise<string> {
  void samples;
  if (preview.ast.astVersion !== 5 || preview.ast.profile !== "rmf2-execution-v2") {
    throw new TypeError("The language server returned an unsupported preview contract.");
  }
  return renderedPreviewHtml(await renderExecutionV2());
}
