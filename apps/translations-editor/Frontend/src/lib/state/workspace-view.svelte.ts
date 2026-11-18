import type { EditorReviewState, WorkspaceSnapshot } from "../contracts";
import type { DiagnosticSources } from "../diagnostic-scope";
import type { EditorLabels } from "../editor-labels";
import type { MessageGroupCountKind, MessageListItem } from "../MessageList.svelte";
import type { MessageFilter } from "../MessageToolbar.svelte";
import { createMessageSearchIndex } from "../message-search";
import { adjacentMessage, messageTreeOrder, nextMessageKind, type NextMessageDirection, type NextMessageKind } from "../next-message";
import { projectKind, type ProjectKind } from "../project-kind";
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

/** The list filters that mean something in a translation project only. */
const translationFilters: ReadonlySet<MessageFilter> = new Set(["missing", "needs-review", "stale"]);

/**
 * What the open catalog shows for the selected locale: coverage per locale, the quality findings, the message list
 * with its filters, and the review of the selected message; with the review actions on the selection and the list.
 */
export class WorkspaceView {
  /**
   * A translation project or a content-editing project (D074.2), derived here once from the snapshot; the components
   * and controllers read it here. Adding a second language changes the snapshot, and with it the kind.
   */
  projectKind = $derived.by((): ProjectKind => projectKind(this.#options.snapshot()?.catalog));
  /** Whether the open project is a content-editing project: one language, nothing to translate or review. */
  contentProject = $derived(this.projectKind === "content");
  /**
   * The list's filter as it applies. In a content-editing project a translation filter (missing, outdated, to review),
   * kept from the time the project had more languages, shows every text.
   */
  filter = $derived.by((): MessageFilter => {
    const filter = this.#options.selection.filter;
    return this.contentProject && translationFilters.has(filter) ? "all" : filter;
  });
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
  /** The list's filters; a content-editing project has no missing, outdated or to-review texts to filter by. */
  filterOptions = $derived.by(() => {
    const labels = this.#options.labels();
    const rows = this.#rows();
    const locale = this.#locale();
    const reviewIndex = this.#options.review.index;
    const all = { value: "all" as const, label: labels.all, count: rows.length };
    const structured = { value: "structured" as const, label: labels.structured, count: rows.filter((row) => row.structured).length };
    const quality = { value: "quality" as const, label: this.#options.ui.text("ui_page_quality_title"), count: this.qualityKeySet.size };
    if (this.contentProject) return [all, structured, quality];
    return [
      all,
      { value: "missing" as const, label: labels.missing, count: rows.filter((row) => row.cells[locale]?.entry === undefined).length },
      structured,
      { value: "needs-review" as const, label: labels.review, count: rows.filter((row) => effectiveReviewState(reviewIndex.get(reviewIdentity(row.key, locale)), row.cells[locale]?.entry !== undefined) === "needs-review").length },
      { value: "stale" as const, label: labels.stale, count: rows.filter((row) => isStale(reviewIndex.get(reviewIdentity(row.key, locale)))).length },
      quality,
    ];
  });
  /** The messages the search and filter let through. */
  visibleRows = $derived.by(() => {
    const { selection, review } = this.#options;
    const searchMatches = this.messageSearch.matchingRows(selection.query);
    const filter = this.filter;
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
    const content = this.contentProject;
    return this.visibleRows.map((row) => {
      const cell = row.cells[locale];
      // A content-editing project has no translation states: nothing is missing, outdated or waiting for review.
      const rowReview = content ? undefined : this.#options.review.index.get(reviewIdentity(row.key, locale));
      return {
        key: row.key,
        path: row.path,
        preview: preview(cell?.entry, this.#options.ui),
        missing: !content && cell?.entry === undefined,
        structured: row.structured,
        structure: row.structure,
        stale: isStale(rowReview),
        needsReview: !content && effectiveReviewState(rowReview, cell?.entry !== undefined) === "needs-review",
      };
    });
  });
  /** The keys of the messages in tree order, which Next and Previous follow. */
  treeOrder = $derived.by(() => messageTreeOrder(this.#rows()));
  /**
   * What Next and Previous step through for the list's filter: missing messages, or the outdated or to-review ones; in
   * a content-editing project, every text.
   */
  stepKind = $derived.by((): NextMessageKind => this.contentProject ? "text" : nextMessageKind(this.filter));
  /** #225 P15: the selection stays, but the list says when the search or filter hides it. */
  selectedHiddenByFilter = $derived.by(() => this.#options.selection.selectedRow !== undefined &&
    !this.visibleRows.some((row) => row.key === this.#options.selection.selectedKey));
  /** The counts a collapsed group names for the selected locale: those the current filter does not already imply. */
  messageGroupCounts = $derived.by((): MessageGroupCountKind[] => {
    const filter = this.filter;
    if (this.contentProject) return [];
    return filter === "missing" || filter === "stale" || filter === "needs-review" ? [] : ["missing", "stale", "needsReview"];
  });
  /**
   * The review entry of the selection. A content-editing project has no review workflow: a review file kept from the
   * time it had more languages stays on disk untouched, but its states do not apply.
   */
  currentReview = $derived.by(() => this.contentProject
    ? undefined
    : this.#options.review.index.get(reviewIdentity(this.#options.selection.selectedKey, this.#locale())));
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

  // Review data cannot change now, or the project has no review workflow (a content-editing project).
  #reviewUnavailable(): boolean {
    return this.contentProject || this.#options.reviewBlocked();
  }

  // Whether the locale has the message: a new review entry starts from its implicit state.
  #translatedIn(key: string, locale: string): boolean {
    return this.#rows().find((candidate) => candidate.key === key)?.cells[locale]?.entry !== undefined;
  }

  /** Whether `row` is of `kind` in the selected locale, as the list's filters decide it; every row is a text. */
  hasKind(row: TranslationRow, kind: NextMessageKind): boolean {
    if (kind === "text") return true;
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
    if (this.#reviewUnavailable()) return;
    const { selectedKey, selectedLocale } = this.#options.selection;
    this.#options.review.setState(selectedKey, selectedLocale, state, this.#options.samples(), this.#translatedIn(selectedKey, selectedLocale));
  }

  /** The translator checked a stale state against the current source text and confirms it (#225 P20). */
  confirmCurrentReview(): void {
    if (this.#reviewUnavailable()) return;
    const { selectedKey, selectedLocale } = this.#options.selection;
    this.#options.review.confirm(selectedKey, selectedLocale, this.#options.samples(), this.#translatedIn(selectedKey, selectedLocale));
  }

  setCurrentReviewNote(note: string): void {
    if (this.#reviewUnavailable()) return;
    const { selectedKey, selectedLocale } = this.#options.selection;
    this.#options.review.setNote(selectedKey, selectedLocale, note, this.#translatedIn(selectedKey, selectedLocale));
  }

  /** Records `state` for every message the list shows. */
  markVisible(state: EditorReviewState): void {
    if (this.#reviewUnavailable()) return;
    this.#options.review.mark(this.visibleRows.map((row) => row.key), this.#locale(), state);
  }
}
