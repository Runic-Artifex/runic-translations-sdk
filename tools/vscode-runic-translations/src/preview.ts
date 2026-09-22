import type { MessageArtifact } from "../../../apps/translations-editor/Frontend/src/lib/message-model.js";
export interface Preview { key: string; locale: string; ast: MessageArtifact; inputs: Array<{ name: string; type: string }>; examples: Record<string, unknown>[] }
export interface RenderedPreview { key: string; locale: string; runs: RenderedRun[] }
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
  return `<!doctype html><html><head><meta http-equiv="Content-Security-Policy" content="default-src 'none'; style-src 'unsafe-inline'"><style>body{font:var(--vscode-font-size) var(--vscode-font-family);padding:1rem}button{font:inherit}span[role=link]{text-decoration:underline}.preview{white-space:pre-wrap}</style></head><body><h2>${escapeHtml(preview.key)}</h2><p>${escapeHtml(preview.locale)} · inert preview</p><div class="preview">${body}</div></body></html>`;
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
