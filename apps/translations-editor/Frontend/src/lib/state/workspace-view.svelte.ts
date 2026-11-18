import type { EditorReviewState, WorkspaceSnapshot } from "../contracts";
import type { DiagnosticSources } from "../diagnostic-scope";
import type { EditorLabels } from "../editor-labels";
import type { MessageGroupCountKind, MessageListItem } from "../MessageList.svelte";
import { createMessageSearchIndex } from "../message-search";
import { adjacentMessage, messageTreeOrder, nextMessageKind, type NextMessageDirection, type NextMessageKind } from "../next-message";
import { localeCoverage, preview, type TranslationRow } from "../resource-model";
import {
  bidiIssues,
  effectiveReviewState,
  isStale,
  qualityIssues,
  reviewIdentity,
  translationSuggestions,
  withCurrentReview,
} from "../review-model";
import type { UiDirection } from "../simulation";
import { sourceDiff } from "../source-diff";
import type { UiText } from "../ui-text";
import type { DiagnosticsStore } from "./diagnostics-store.svelte";
import type { DraftStore } from "./draft-store.svelte";
import type { ReviewStore } from "./review-store.svelte";
import type { SelectionController } from "./selection-controller.svelte";

export interface WorkspaceViewOptions {
  snapshot: () => WorkspaceSnapshot | undefined;
  selection: SelectionController;
  drafts: Pick<DraftStore, "drafts" | "parsed">;
  review: ReviewStore;
  diagnostics: Pick<DiagnosticsStore, "displayed">;
  /** The current review samples of the preview, recorded with a review state. */
  samples: () => Record<string, string>;
  /** Whether review data cannot change now (an operation runs, or the review file still needs its migration). */
  reviewBlocked: () => boolean;
  labels: () => EditorLabels;
  localeName: (tag: string) => string;
  uiDirection: () => UiDirection;
  ui: UiText;
}

/**
 * What the open catalog shows for the selected locale: coverage per locale, the quality findings, the message list
 * with its filters, and the review of the selected message; with the review actions on the selection and the list.
 */
export class WorkspaceView {
  /** What the validation area reads to name a diagnostic's message and its line in it (#225 P6, P12). */
  diagnosticSources = $derived.by((): DiagnosticSources => ({
    documents: this.#options.snapshot()?.documents ?? [],
    drafts: this.#options.drafts.drafts,
    parsed: this.#options.drafts.parsed,
  }));
  messageSearch = $derived(createMessageSearchIndex(this.#rows()));
  /** Coverage from runic-translations status (Tooling), the numbers of the command line, for the saved files. */
  localeSummaries = $derived.by(() => {
    const snapshot = this.#options.snapshot();
    return (snapshot?.catalog?.locales ?? []).map((locale) => {
      const state = localeCoverage(snapshot?.status, this.#rows(), locale.tag);
      return {
        tag: locale.tag,
        name: this.#options.localeName(locale.tag),
        fallback: locale.fallback,
        translated: state.translated,
        fallbackCount: state.fallback,
        missing: state.missing,
        stale: state.stale,
        completeness: state.completeness,
        total: state.total,
        percent: state.percent,
        isSource: locale.tag === snapshot?.catalog?.defaultLocale,
      };
    });
  });
  /** The SDK's diagnostics for each row (RTR0010, RTR0090, RTR0094–RTR0098) beside the editor-only checks. */
  localeQuality = $derived.by(() => qualityIssues(
    this.#rows(),
    this.#defaultLocale(),
    this.#locale(),
    withCurrentReview(this.#options.diagnostics.displayed, this.#options.review.index),
    this.#options.review.terminology,
    this.#options.ui,
  ));
  localeQualityFindings = $derived.by(() => {
    const locale = this.#locale();
    const bidi = bidiIssues(
      this.#rows().map((row) => ({
        key: row.key,
        locale,
        text: preview(row.cells[locale]?.entry),
      })),
      this.#options.uiDirection(),
      this.#options.ui,
    );
    if (bidi.length === 0) return this.localeQuality;
    return [...this.localeQuality, ...bidi].sort((left, right) =>
      left.key.localeCompare(right.key) || left.kind.localeCompare(right.kind));
  });
  qualityKeySet = $derived(new Set(this.localeQualityFindings.map((issue) => issue.key)));
  filterOptions = $derived.by(() => {
    const labels = this.#options.labels();
    const rows = this.#rows();
    const locale = this.#locale();
    const reviewIndex = this.#options.review.index;
    return [
      { value: "all" as const, label: labels.all, count: rows.length },
      { value: "missing" as const, label: labels.missing, count: rows.filter((row) => row.cells[locale]?.entry === undefined).length },
      { value: "structured" as const, label: labels.structured, count: rows.filter((row) => row.structured).length },
      { value: "needs-review" as const, label: labels.review, count: rows.filter((row) => effectiveReviewState(reviewIndex.get(reviewIdentity(row.key, locale)), row.cells[locale]?.entry !== undefined) === "needs-review").length },
      { value: "stale" as const, label: labels.stale, count: rows.filter((row) => isStale(reviewIndex.get(reviewIdentity(row.key, locale)))).length },
      { value: "quality" as const, label: this.#options.ui.text("ui_page_quality_title"), count: this.qualityKeySet.size },
    ];
  });
  /** The messages the search and filter let through. */
  visibleRows = $derived.by(() => {
    const { selection, review } = this.#options;
    const searchMatches = this.messageSearch.matchingRows(selection.query);
    const filter = selection.filter;
    const locale = this.#locale();
    return this.#rows().filter((row) => {
      const cell = row.cells[locale];
      if (filter === "missing" && cell?.entry !== undefined) return false;
      if (filter === "structured" && !row.structured) return false;
      const entry = review.index.get(reviewIdentity(row.key, locale));
      if (filter === "needs-review" && effectiveReviewState(entry, cell?.entry !== undefined) !== "needs-review") return false;
      if (filter === "stale" && !isStale(entry)) return false;
      if (filter === "quality" && !this.qualityKeySet.has(row.key)) return false;
      return searchMatches.has(row);
    });
  });
  messageListItems = $derived.by((): MessageListItem[] => {
    const locale = this.#locale();
    return this.visibleRows.map((row) => {
      const cell = row.cells[locale];
      const rowReview = this.#options.review.index.get(reviewIdentity(row.key, locale));
      return {
        key: row.key,
        path: row.path,
        preview: preview(cell?.entry, this.#options.ui),
        missing: cell?.entry === undefined,
        structured: row.structured,
        structure: row.structure,
        stale: isStale(rowReview),
        needsReview: effectiveReviewState(rowReview, cell?.entry !== undefined) === "needs-review",
      };
    });
  });
  /** The keys of the messages in tree order, which Next and Previous follow. */
  treeOrder = $derived.by(() => messageTreeOrder(this.#rows()));
  /** What Next and Previous step through for the list's filter: missing messages, or the outdated or to-review ones. */
  stepKind = $derived.by((): NextMessageKind => nextMessageKind(this.#options.selection.filter));
  /** #225 P15: the selection stays, but the list says when the search or filter hides it. */
  selectedHiddenByFilter = $derived.by(() => this.#options.selection.selectedRow !== undefined &&
    !this.visibleRows.some((row) => row.key === this.#options.selection.selectedKey));
  /** The counts a collapsed group names for the selected locale: those the current filter does not already imply. */
  messageGroupCounts = $derived.by((): MessageGroupCountKind[] => {
    const filter = this.#options.selection.filter;
    return filter === "missing" || filter === "stale" || filter === "needs-review" ? [] : ["missing", "stale", "needsReview"];
  });
  currentReview = $derived.by(() => this.#options.review.index.get(reviewIdentity(this.#options.selection.selectedKey, this.#locale())));
  currentReviewState = $derived.by(() => effectiveReviewState(this.currentReview, this.#options.selection.currentCell?.entry !== undefined));
  currentIsStale = $derived(isStale(this.currentReview));
  /** The base text a stale state was recorded for next to the current one, when the review file kept it (#225 P20). */
  currentSourceDiff = $derived.by(() => {
    if (!this.currentIsStale) return undefined;
    const { selection } = this.#options;
    const recorded = this.#options.review.recordedSource(selection.selectedKey, selection.selectedLocale);
    const current = selection.selectedRow?.cells[this.#defaultLocale()]?.entry?.value;
    return recorded === undefined || typeof current !== "string" ? undefined : sourceDiff(recorded, current);
  });
  currentQuality = $derived.by(() => this.localeQualityFindings.filter((issue) => issue.key === this.#options.selection.selectedKey));
  memorySuggestions = $derived.by(() => translationSuggestions(
    this.#rows(),
    this.#defaultLocale(),
    this.#locale(),
    this.#options.selection.selectedKey,
  ));
  malformedDocuments = $derived.by(() => this.#options.snapshot()?.documents.filter((document) => document.isMalformed) ?? []);

  readonly #options: WorkspaceViewOptions;

  constructor(options: WorkspaceViewOptions) {
    this.#options = options;
  }

  #rows() {
    return this.#options.selection.rows;
  }

  #locale(): string {
    return this.#options.selection.selectedLocale;
  }

  #defaultLocale(): string {
    return this.#options.snapshot()?.catalog?.defaultLocale ?? "";
  }

  // Whether the locale has the message: a new review entry starts from its implicit state.
  #translatedIn(key: string, locale: string): boolean {
    return this.#rows().find((candidate) => candidate.key === key)?.cells[locale]?.entry !== undefined;
  }

  /** Whether `row` is of `kind` in the selected locale, as the list's filters decide it. */
  hasKind(row: TranslationRow, kind: NextMessageKind): boolean {
    const locale = this.#locale();
    const translated = row.cells[locale]?.entry !== undefined;
    if (kind === "missing") return !translated;
    const entry = this.#options.review.index.get(reviewIdentity(row.key, locale));
    return kind === "stale" ? isStale(entry) : effectiveReviewState(entry, translated) === "needs-review";
  }

  /**
   * The message Next (or Previous) selects: the one after the selection in tree order among the messages the list
   * shows that are of `kind`, wrapping at the end.
   */
  adjacent(kind: NextMessageKind, direction: NextMessageDirection): { key: string; wrapped: boolean } | undefined {
    const candidates = new Set(this.visibleRows.filter((row) => this.hasKind(row, kind)).map((row) => row.key));
    return adjacentMessage(this.treeOrder, candidates, this.#options.selection.selectedKey, direction);
  }

  setCurrentReviewState(state: EditorReviewState): void {
    if (this.#options.reviewBlocked()) return;
    const { selectedKey, selectedLocale } = this.#options.selection;
    this.#options.review.setState(selectedKey, selectedLocale, state, this.#options.samples(), this.#translatedIn(selectedKey, selectedLocale));
  }

  /** The translator checked a stale state against the current source text and confirms it (#225 P20). */
  confirmCurrentReview(): void {
    if (this.#options.reviewBlocked()) return;
    const { selectedKey, selectedLocale } = this.#options.selection;
    this.#options.review.confirm(selectedKey, selectedLocale, this.#options.samples(), this.#translatedIn(selectedKey, selectedLocale));
  }

  setCurrentReviewNote(note: string): void {
    if (this.#options.reviewBlocked()) return;
    const { selectedKey, selectedLocale } = this.#options.selection;
    this.#options.review.setNote(selectedKey, selectedLocale, note, this.#translatedIn(selectedKey, selectedLocale));
  }

  /** Records `state` for every message the list shows. */
  markVisible(state: EditorReviewState): void {
    if (this.#options.reviewBlocked()) return;
    this.#options.review.mark(this.visibleRows.map((row) => row.key), this.#locale(), state);
  }
}
