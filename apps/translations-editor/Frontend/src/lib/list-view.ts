import type { EditorMessageProjection, EditorMessageVariant, EditorReviewState } from "./contracts";
import { sourceTextView, type SourceTextView } from "./message-composer";
import { messageTreeOrder } from "./next-message";
import type { MessageStructure, ResourceEntry, TranslationRow } from "./resource-model";
import type { StateMarkKind } from "./state-mark";

/**
 * The two views of the open catalog (D074.1): the workbench (tree, editor, rail) and the list, one row per message with
 * the source text and the translation side by side, for bulk translation.
 */
export const workspaceLayouts = ["workbench", "list"] as const;
export type WorkspaceLayout = typeof workspaceLayouts[number];
/** The view the editor shows, kept in the native editor state with the other preferences. */
export const workspaceLayoutKey = "runic-translations.workspace-layout";

/** The stored view; the workbench unless the list was chosen. */
export function readWorkspaceLayout(value: string | null): WorkspaceLayout {
  return value === "list" ? "list" : "workbench";
}

/**
 * The one variant a list row edits inline: a supported message without selectors, whose text is one pattern (text with
 * placeholders). Plural forms, other variants and messages the visual composer cannot show open in the workbench.
 */
export function inlineVariant(authoring: Pick<EditorMessageProjection, "supported" | "selectors" | "variants"> | undefined): EditorMessageVariant | undefined {
  if (authoring?.supported !== true || authoring.selectors.length !== 0 || authoring.variants.length !== 1) return undefined;
  return authoring.variants[0];
}

/** A row's translation state in the selected locale, as the State column names it. */
export type ListRowStateName = "missing" | "outdated" | "needs-review" | "approved" | "translated" | "draft";

export interface ListRowState {
  readonly name: ListRowStateName;
  /** The brand's shape: hollow square missing, filled done, diagonal outdated (D074.3). */
  readonly mark: StateMarkKind;
}

export interface ListMessageRow {
  readonly kind: "message";
  /** `m:` + key, unique over the rows. */
  readonly id: string;
  readonly key: string;
  /** The display form of the message's name: its logical path, dotted (`common.cancel`). */
  readonly dottedPath: string;
  /** The namespace the row is listed under: the dotted path without the last segment; empty at the top level. */
  readonly group: string;
  /** The base locale's text beside a translation; absent while the source text itself is listed. */
  readonly source?: SourceTextView;
  /** The selected locale's text; absent while it is missing. */
  readonly text?: SourceTextView;
  readonly missing: boolean;
  /** Absent for the source text (the base locale, or a content-editing project): it has no translation states. */
  readonly state?: ListRowState;
  /** Whether the row's text is one pattern, edited in the row; else the row shows a summary and opens in the workbench. */
  readonly inline: boolean;
  readonly structure?: MessageStructure;
  /** The developers' note (the message's description). */
  readonly note?: string;
}

export interface ListGroupRow {
  readonly kind: "group";
  /** `g:` + group, unique over the rows (a group's messages are contiguous in tree order). */
  readonly id: string;
  /** The namespace, dotted; empty for messages at the top level. */
  readonly group: string;
  readonly messages: number;
  /** Missing translations among them; 0 for the source text. */
  readonly missing: number;
}

export type ListRow = ListGroupRow | ListMessageRow;

export interface ListRowOptions {
  /** The selected locale. */
  readonly locale: string;
  /** The catalog's base locale, whose text is the source of every translation. */
  readonly baseLocale: string;
  /** Whether the selected locale holds the source text (the base locale, or a content-editing project). */
  readonly sourceText: boolean;
  /** The review state of a translated or missing message in the selected locale, and whether it is outdated. */
  readonly review: (key: string, translated: boolean) => { state: EditorReviewState; stale: boolean };
}

/** The display path of a row: its logical path, or its key when the compiler has not described it. */
function rowPath(row: Pick<TranslationRow, "key" | "path">): readonly string[] {
  return row.path.length > 0 ? row.path : [row.key];
}

/** Whether a row whose text is `entry` (or, while missing, whose source is `sourceEntry`) is edited in the row. */
function editsInline(row: TranslationRow, entry: ResourceEntry | undefined, sourceEntry: ResourceEntry | undefined): boolean {
  if (entry !== undefined) return inlineVariant(entry.authoring) !== undefined;
  // A missing translation's starter is shaped like the source text: plural forms stay plural forms.
  if (sourceEntry === undefined) return false;
  return sourceEntry.authoring === undefined ? !row.structured : inlineVariant(sourceEntry.authoring) !== undefined;
}

function rowState(review: { state: EditorReviewState; stale: boolean }, missing: boolean): ListRowState {
  if (missing) return { name: "missing", mark: "missing" };
  if (review.stale) return { name: "outdated", mark: "outdated" };
  if (review.state === "needs-review") return { name: "needs-review", mark: "review" };
  return { name: review.state, mark: "done" };
}

/**
 * The list's rows: the messages `rows` holds (those the search and filter let through), in the order of the namespace
 * tree, each namespace under a header row naming it with its counts.
 */
export function buildListRows(rows: readonly TranslationRow[], options: ListRowOptions): ListRow[] {
  const byKey = new Map(rows.map((row) => [row.key, row]));
  const order = messageTreeOrder(rows.map((row) => ({ key: row.key, path: rowPath(row) })));
  const result: ListRow[] = [];
  let header: { index: number; group: string; messages: number; missing: number } | undefined;
  const closeHeader = (): void => {
    if (header === undefined) return;
    result[header.index] = { kind: "group", id: `g:${header.index}:${header.group}`, group: header.group, messages: header.messages, missing: header.missing };
  };
  for (const key of order) {
    const row = byKey.get(key);
    if (row === undefined) continue;
    const path = rowPath(row);
    const group = path.slice(0, -1).join(".");
    if (header === undefined || header.group !== group) {
      closeHeader();
      header = { index: result.length, group, messages: 0, missing: 0 };
      result.push(undefined as unknown as ListRow);
    }
    const entry = row.cells[options.locale]?.entry;
    const sourceEntry = row.cells[options.baseLocale]?.entry;
    const missing = !options.sourceText && entry === undefined;
    header.messages += 1;
    if (missing) header.missing += 1;
    result.push({
      kind: "message",
      id: `m:${row.key}`,
      key: row.key,
      dottedPath: path.join("."),
      group,
      source: options.sourceText ? undefined : sourceTextView(sourceEntry),
      text: sourceTextView(entry),
      missing,
      state: options.sourceText ? undefined : rowState(options.review(row.key, entry !== undefined), missing),
      inline: editsInline(row, entry, options.sourceText ? undefined : sourceEntry),
      structure: row.structure,
      note: row.description ?? entry?.description ?? sourceEntry?.description,
    });
  }
  closeHeader();
  return result;
}

/** The message rows of `rows`, in their order: what Up and Down step through. */
export function listMessageRows(rows: readonly ListRow[]): ListMessageRow[] {
  return rows.filter((row): row is ListMessageRow => row.kind === "message");
}
