import type { EditorBridge } from "./editor-bridge";
import type { EditorDocument, WorkspaceSnapshot } from "./contracts";

const manifest = document("product.catalog.json", undefined, undefined, {
  schemaVersion: 2,
  catalog: "customer-product",
  code: { namespace: "Customer.Product", className: "ProductText", visibility: "public" },
  defaultLocale: "de",
  locales: [{ tag: "de" }, { tag: "en", fallback: "de" }, { tag: "fr", fallback: "de" }],
  layers: [{ name: "base", priority: 0 }],
  validation: { translationCompleteness: "warning", extraLocaleKeys: "error", emptyValues: "error" },
});
manifest.isManifest = true;

let snapshot: WorkspaceSnapshot = {
  root: "/mock/customer-product/Resources",
  catalog: {
    id: "customer-product",
    schemaVersion: 2,
    defaultLocale: "de",
    locales: [{ tag: "de" }, { tag: "en", fallback: "de" }, { tag: "fr", fallback: "de" }],
    layers: [{ name: "base", priority: 0 }],
  },
  documents: [
    manifest,
    document("product.de.json", "de", "base", resources("Speichern", "Abbrechen", "Willkommen zurück, {name}")),
    document("product.en.json", "en", "base", resources("Save", "Cancel", "Welcome back, {name}")),
    document("product.fr.json", "fr", "base", {
      schemaVersion: 2,
      catalog: "customer-product",
      locale: "fr",
      layer: "base",
      resources: { Common: { Save: "Enregistrer" } },
    }),
  ],
  diagnostics: [],
  success: true,
};

export const mockBridge: EditorBridge = {
  async load() {
    return structuredClone(snapshot);
  },
  async validate(path, content) {
    try {
      JSON.parse(content);
      return { success: true, diagnostics: [] };
    } catch (error) {
      return {
        success: false,
        diagnostics: [{
          id: "JSON",
          severity: "error",
          message: error instanceof Error ? error.message : "Invalid JSON",
          path,
          line: 1,
          column: 1,
          endLine: 1,
          endColumn: 1,
        }],
      };
    }
  },
  async save(path, content, revision) {
    const current = snapshot.documents.find((candidate) => candidate.path === path);
    if (current === undefined) return { ok: false, kind: "not-found", message: "Document not found." };
    if (current.revision !== revision) return { ok: false, kind: "conflict", message: "Mock document changed." };
    current.content = content;
    current.revision = crypto.randomUUID();
    snapshot = structuredClone(snapshot);
    return { ok: true, kind: "saved", snapshot: structuredClone(snapshot) };
  },
};

function document(
  path: string,
  locale: string | undefined,
  layer: string | undefined,
  value: Record<string, unknown>,
): EditorDocument {
  return {
    path,
    locale,
    layer,
    isManifest: false,
    revision: `mock-${path}`,
    content: `${JSON.stringify(value, null, 2)}\n`,
  };
}

function resources(save: string, cancel: string, welcome: string): Record<string, unknown> {
  return {
    schemaVersion: 2,
    catalog: "customer-product",
    locale: save === "Speichern" ? "de" : "en",
    layer: "base",
    resources: {
      Common: { Save: save, Cancel: cancel },
      Dashboard: {
        Welcome: {
          $value: welcome,
          $description: "Greeting on the dashboard",
          $tags: ["dashboard", "customer-facing"],
          $placeholders: { name: { type: "string" } },
        },
      },
      Files: {
        Selected: {
          $value: {
            inputs: { count: { type: "int64" } },
            selectors: [{ name: "quantity", input: "count", function: "plural" }],
            variants: [
              { match: { quantity: "one" }, value: save === "Speichern" ? "Eine Datei ausgewählt" : "One file selected" },
              { match: { quantity: "*" }, value: save === "Speichern" ? "{count} Dateien ausgewählt" : "{count} files selected" },
            ],
          },
        },
      },
    },
  };
}
