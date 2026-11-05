import { dirname, join } from "node:path";

/** The URI scheme of the read-only library sources that "Go to Definition" opens from an override source. */
export const libraryScheme = "runic-library";

/**
 * The files a restore or an install rewrites (NuGet's restore output, npm/pnpm/Yarn/Bun install state and lockfiles): the
 * language server resolves library catalogs again when one changes.
 */
export const libraryInstallPattern = "**/{obj/project.assets.json,node_modules/.package-lock.json,node_modules/.modules.yaml,node_modules/.yarn-state.yml,package-lock.json,pnpm-lock.yaml,yarn.lock,bun.lock,bun.lockb}";

/** A locale of a library catalog as the application sees it (`runic/libraryCatalogs`). */
export interface LibraryLocale {
  tag: string;
  origin: "library" | "application";
  fallback: string | null;
  messages: number;
  libraryTranslated: number;
  overridden: number;
  covered: number;
  pending: boolean;
}

/** A message of a library catalog: its logical key, base text and the locales the application overrides it in. */
export interface LibraryMessage { key: string; path: string; baseText: string; inputs: string[]; overridden: string[]; changed: string[] }

/** An installed library catalog; `messages` and `locales` are present when the package that ships it is installed. */
export interface LibraryCatalog {
  catalog: string;
  package: string | null;
  version: string | null;
  origin: "nuget" | "npm" | null;
  overridden: boolean;
  problem: string | null;
  directory: string | null;
  baseLocale?: string;
  locales?: LibraryLocale[];
  messages?: LibraryMessage[];
}

/** The `runic/libraryCatalogs` answer for a project. */
export interface LibraryCatalogs { project: string; catalogs: LibraryCatalog[]; warnings: string[] }

export interface Choice<T> { label: string; description?: string; detail?: string; value: T }

/** The catalogs to choose from: installed ones first, overridden before the others. */
export function libraryCatalogChoices(result: LibraryCatalogs): Choice<LibraryCatalog>[] {
  return result.catalogs.filter(catalog => catalog.messages).sort((left, right) => Number(right.overridden) - Number(left.overridden) || left.catalog.localeCompare(right.catalog))
    .map(catalog => ({
      label: catalog.catalog,
      description: catalog.package ? `${catalog.package} ${catalog.version ?? ""}`.trim() : undefined,
      detail: catalog.overridden
        ? `Overridden in ${overriddenLocales(catalog).join(", ") || "no locale yet"}`
        : "Not overridden yet: runic.json gets an \"overrides\" entry",
      value: catalog,
    }));
}

function overriddenLocales(catalog: LibraryCatalog): string[] {
  return [...new Set((catalog.messages ?? []).flatMap(message => message.overridden))].sort();
}

/** The messages of a catalog with their base text; overridden and changed ones say so. */
export function libraryMessageChoices(catalog: LibraryCatalog): Choice<LibraryMessage>[] {
  return (catalog.messages ?? []).map(message => ({
    label: message.path,
    description: message.baseText.split("\n", 1)[0],
    detail: [
      message.overridden.length ? `Overridden in ${message.overridden.join(", ")}` : undefined,
      message.changed.length ? `library text changed since the override in ${message.changed.join(", ")}` : undefined,
      message.inputs.length ? `Inputs: ${message.inputs.join(", ")}` : undefined,
    ].filter(Boolean).join("; ") || undefined,
    value: message,
  }));
}

/** The locales to override a message in: the library's, then those the application adds, with coverage and fallback. */
export function libraryLocaleChoices(catalog: LibraryCatalog, message?: LibraryMessage): Choice<string>[] {
  return (catalog.locales ?? []).map(locale => ({
    label: locale.tag,
    description: locale.origin === "library"
      ? `library${locale.tag === catalog.baseLocale ? " base locale" : ""}`
      : `added by the application, falls back to ${locale.fallback ?? catalog.baseLocale}`,
    detail: `${locale.covered}/${locale.messages} messages in its own text` + (message && isOverriddenIn(message, locale.tag) ? "; this message is overridden here" : ""),
    value: locale.tag,
  }));
}

/**
 * The canonical BCP 47 form of a typed locale tag (`de_ch` and `DE-ch` become `de-CH`), as the CLI names override
 * sources; undefined when it is not a well-formed tag.
 */
export function canonicalLocale(input: string): string | undefined {
  const value = input.trim().replaceAll("_", "-");
  if (!value) return undefined;
  try {
    return Intl.getCanonicalLocales(value)[0];
  } catch (error) {
    if (error instanceof RangeError) return undefined;
    throw error;
  }
}

/** Whether the application already overrides `message` in `locale`; locale tags compare case-insensitively. */
export function isOverriddenIn(message: Pick<LibraryMessage, "overridden">, locale: string): boolean {
  const wanted = locale.toLowerCase();
  return message.overridden.some(tag => tag.toLowerCase() === wanted);
}

/** An open document as far as the unsaved-changes check needs it. */
export interface OpenDocument { fsPath: string; isDirty: boolean }

/**
 * The open documents with unsaved changes among `paths` (file system paths compared case-sensitively, except on
 * Windows), which a CLI writing those files would overwrite underneath the editor.
 */
export function unsavedDocuments<T extends OpenDocument>(documents: readonly T[], paths: readonly string[], platform: string = process.platform): T[] {
  const key = (path: string) => platform === "win32" ? path.toLowerCase() : path;
  const wanted = new Set(paths.map(key));
  return documents.filter(document => document.isDirty && wanted.has(key(document.fsPath)));
}

/** `runic-translations overrides add <catalog> --locale <tag> [--key <key>]` for the project of `manifest` (its runic.json). */
export function overridesAddArguments(manifest: string, catalog: string, locale: string, key?: string): string[] {
  return ["overrides", "add", catalog, "--locale", locale, ...(key ? ["--key", key] : []), "--project", dirname(manifest)];
}

/** The override source of a locale: `overrides/<catalog>/<locale>.rmf2` beside runic.json. */
export function overrideSourcePath(manifest: string, catalog: string, locale: string): string {
  return join(dirname(manifest), "overrides", catalog, `${locale}.rmf2`);
}

/** The line of a message's name in an RMF2 source (its last path segment inside its groups), or 0. */
export function messageLine(text: string, path: string): number {
  const segments = path.split(".");
  const lines = text.split(/\r?\n/);
  let depth = 0;
  for (let index = 0; index < lines.length; index++) {
    const line = lines[index].trim();
    if (line.startsWith("#")) continue;
    const match = /^([A-Za-z_][A-Za-z0-9_]*)\s*([={])/.exec(line);
    if (match && match[1] === segments[depth]) {
      if (depth === segments.length - 1 && match[2] === "=") return index;
      if (match[2] === "{") depth++;
    }
  }
  return 0;
}

/** The application's runic.json (a file URI) that a `runic-library:` URI names in its `project` query parameter. */
export function libraryDocumentProject(query: string): string | undefined {
  // The query as the server wrote it (project=<encoded file URI>), or with its "=" encoded as well by a URI round trip.
  for (const [candidate, decode] of [[query, true], [safeDecode(query), false]] as const) {
    for (const part of candidate.split("&")) {
      const separator = part.indexOf("=");
      if (separator > 0 && part.slice(0, separator) === "project") return decode ? safeDecode(part.slice(separator + 1)) : part.slice(separator + 1);
    }
  }
  return undefined;
}

function safeDecode(value: string): string {
  try { return decodeURIComponent(value); } catch { return value; }
}
