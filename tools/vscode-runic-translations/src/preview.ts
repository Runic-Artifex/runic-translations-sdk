import { executeMessagePreview } from "../../../apps/translations-editor/Frontend/src/lib/message-preview.js";
import type { MessageArtifact } from "../../../apps/translations-editor/Frontend/src/lib/message-model.js";
export interface Preview { key: string; locale: string; ast: MessageArtifact; examples: Record<string, unknown>[] }
export const escapeHtml = (value: unknown): string => String(value).replaceAll("&", "&amp;").replaceAll("<", "&lt;").replaceAll(">", "&gt;").replaceAll('"', "&quot;").replaceAll("'", "&#39;");
export function previewHtml(preview: Preview, samples: Record<string, string>): string {
  const result = executeMessagePreview(preview.ast, preview.locale, samples);
  const render = (node: { kind: string; value?: string; name?: string; attributes?: Record<string, string>; children?: unknown[] }): string => {
    if (node.kind === "text") return escapeHtml(node.value ?? "");
    const children = (node.children ?? []).map(child => render(child as Parameters<typeof render>[0])).join("");
    switch (node.name) {
      case "runic:strong": case "runic:bold": return `<strong>${children}</strong>`;
      case "runic:em": case "runic:italic": return `<em>${children}</em>`;
      case "runic:code": return `<code>${children}</code>`;
      case "runic:br": return "<br>";
      case "runic:link": return `<span role="link" aria-disabled="true">${children}</span>`;
      case "runic:action": return `<button disabled>${children}</button>`;
      case "runic:icon": return `<span role="img" aria-label="${escapeHtml(node.attributes?.ref ?? "icon")}">◇</span>`;
      default: return `<span title="${escapeHtml(node.name)}">${children || escapeHtml(node.name)}</span>`;
    }
  };
  const body = result.kind === "text" ? escapeHtml(result.value) : result.nodes.map(render).join("");
  return `<!doctype html><html><head><meta http-equiv="Content-Security-Policy" content="default-src 'none'; style-src 'unsafe-inline'"><style>body{font:var(--vscode-font-size) var(--vscode-font-family);padding:1rem}button{font:inherit}span[role=link]{text-decoration:underline}.preview{white-space:pre-wrap}</style></head><body><h2>${escapeHtml(preview.key)}</h2><p>${escapeHtml(preview.locale)} · inert preview</p><div class="preview">${body}</div></body></html>`;
}
