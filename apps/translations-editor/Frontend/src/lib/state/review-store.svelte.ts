import type { EditorBridge } from "../editor-bridge";
import type {
  EditorReviewEntry,
  EditorReviewSnapshot,
  EditorReviewState,
  EditorTerminologyEntry,
  WorkspaceSnapshot,
} from "../contracts";
import { createPreviewSamples } from "../message-preview.js";
import {
  effectiveReviewState,
  emptyReviewEntry,
  rebaseReview,
  reviewIdentity,
  reviewMap,
  sameReviewEntry,
  sameTerminology,
} from "../review-model";
import { notice, type UiMessage } from "../ui-text";
import type { DraftStorage } from "./draft-store.svelte";

/** One unsaved review change: the user's entry (absent: removed) and the saved entry it started from (absent: new). */
export type ReviewChange = { entry?: EditorReviewEntry; base?: EditorReviewEntry };

/**
 * The unsaved review changes of a workspace, kept in the native editor state for recovery after a crash or a closed
 * window: only the changed entries with the data they started from, so a restore can rebase them onto the review file.
 */
export type StoredReview = {
  entries: ReviewChange[];
  terminology?: { local: EditorTerminologyEntry[]; base: EditorTerminologyEntry[] };
};

export interface ReviewStoreOptions {
  bridge: Pick<EditorBridge, "saveReview">;
  /** The open workspace; a save and an external change update its review data and history in place. */
  snapshot: () => WorkspaceSnapshot | undefined;
  errorNotice: (error: unknown) => UiMessage;
  /** The native editor state that keeps the recovery copy of unsaved review changes. */
  storage?: DraftStorage;
}

const storageKey = (value: WorkspaceSnapshot): string => `runic-translations:review-drafts:1:${value.root}\n${value.catalog?.id ?? ""}`;

// A rebase keeps the user's entries, which are state proxies: `structuredClone` cannot copy those, a snapshot can.
function cloneReviewEntries(entries: readonly EditorReviewEntry[]): EditorReviewEntry[] {
  return entries.map((entry) => ({ ...$state.snapshot(entry), samples: createPreviewSamples(entry.samples) }));
}

/**
 * The review states, notes and terminology of the open workspace with the user's unsaved edits. The data the edits
 * started from is kept, so a save (or a review file another writer changed) re-applies only the user's changes on top
 * of the files on disk.
 */
export class ReviewStore {
  entries = $state<EditorReviewEntry[]>([]);
  terminology = $state<EditorTerminologyEntry[]>([]);
  revision = $state<string>();
  saving = $state(false);
  message = $state<UiMessage>();
  /** Unsaved review changes stored by an earlier session of this workspace, offered for recovery. */
  recovered = $state.raw<StoredReview>();
  /** The entries by message and locale. */
  index = $derived(reviewMap(this.entries));
  /** The entries and terminology that differ from the saved data they started from. */
  changes = $derived.by((): StoredReview => this.#changes());
  /** Whether review data differs from the saved data; an edit back to the saved value is no change. */
  dirty = $derived(this.changes.entries.length > 0 || this.changes.terminology !== undefined);

  #baseEntries = $state.raw<EditorReviewEntry[]>([]);
  #baseTerminology = $state.raw<EditorTerminologyEntry[]>([]);
  #baseIndex = $derived(reviewMap(this.#baseEntries));
  readonly #options: ReviewStoreOptions;

  constructor(options: ReviewStoreOptions) {
    this.#options = options;
  }

  /** Installs the workspace's saved review data, dropping unsaved edits. */
  install(next: WorkspaceSnapshot): void {
    this.entries = cloneReviewEntries(next.review?.entries ?? []);
    this.terminology = structuredClone(next.review?.terminology ?? []);
    this.#baseEntries = cloneReviewEntries(next.review?.entries ?? []);
    this.#baseTerminology = structuredClone(next.review?.terminology ?? []);
    this.revision = next.review?.revision;
    this.message = next.review?.notice ?? next.review?.error;
  }

  /** A newly opened workspace: its saved review data, and the stored unsaved changes offered for recovery. */
  reset(next: WorkspaceSnapshot): void {
    this.install(next);
    this.recovered = this.#readStored(next);
  }

  /** Drops the unsaved edits and their recovery copy. */
  discard(): void {
    const snapshot = this.#options.snapshot();
    if (snapshot === undefined) return;
    this.install(snapshot);
    this.persist();
  }

  /**
   * Restores the recovered changes on top of the review data loaded now, as an external change is rebased: an entry
   * the review file changed meanwhile keeps the recovered value and its old starting point, so saving reports the
   * conflict instead of losing either side. Unsaved changes made in this session before the restore are replaced.
   */
  restoreRecovered(): void {
    const stored = this.recovered;
    this.recovered = undefined;
    if (stored === undefined) return;
    const saved = this.#baseEntries;
    const savedTerminology = this.#baseTerminology;
    const rebased = rebaseReview(
      stored.entries.flatMap((change) => change.entry === undefined ? [] : [change.entry]),
      stored.entries.flatMap((change) => change.base === undefined ? [] : [change.base]),
      saved,
      stored.terminology?.local ?? savedTerminology,
      stored.terminology?.base ?? savedTerminology,
      savedTerminology,
    );
    // A recovered state was recorded for the base text of its session; against other base text it is stale.
    this.entries = cloneReviewEntries(rebased.entries.map((entry) => {
      const fingerprint = this.fingerprint(entry.key);
      return entry.implicit === true || fingerprint === undefined ? entry : { ...entry, stale: entry.sourceFingerprint !== fingerprint };
    }));
    this.terminology = structuredClone(rebased.terminology);
    this.#baseEntries = cloneReviewEntries(rebased.baseEntries);
    this.#baseTerminology = structuredClone(rebased.baseTerminology);
    this.message = rebased.conflicts.length === 0
      ? undefined
      : notice("ui_feedback_review_external_conflict", { entries: rebased.conflicts.slice(0, 10).join(", ") });
    this.persist();
  }

  /** Declines the recovered changes; the recovery copy then holds only this session's unsaved changes. */
  discardRecovered(): void {
    this.recovered = undefined;
    this.persist();
  }

  /** Writes the unsaved changes to the recovery copy, or removes it when there are none. */
  persist(): void {
    const storage = this.#options.storage;
    const snapshot = this.#options.snapshot();
    if (storage === undefined || snapshot === undefined) return;
    if (!this.dirty) {
      storage.remove(storageKey(snapshot));
      return;
    }
    storage.set(storageKey(snapshot), JSON.stringify({ version: 1, ...$state.snapshot(this.changes) }));
  }

  // Another writer (runic-translations review mark or xliff import, the language server) changed the review file.
  // Without unsaved review edits the new data is installed; with them, the edits are kept on top of it. The editor
  // keeps the fingerprints of the base text it shows: those change only when the catalog is reloaded.
  applyExternal(incoming: EditorReviewSnapshot): void {
    const snapshot = this.#options.snapshot();
    if (snapshot === undefined) return;
    const fingerprints = snapshot.review?.fingerprints;
    snapshot.review = { ...incoming, fingerprints };
    if (!this.dirty) {
      this.install(snapshot);
      this.message = notice("ui_feedback_review_external");
      return;
    }
    const rebased = rebaseReview(this.entries, this.#baseEntries, incoming.entries, this.terminology, this.#baseTerminology, incoming.terminology);
    this.entries = cloneReviewEntries(rebased.entries);
    this.terminology = rebased.terminology;
    this.#baseEntries = cloneReviewEntries(rebased.baseEntries);
    this.#baseTerminology = rebased.baseTerminology;
    this.revision = incoming.revision;
    this.message = rebased.conflicts.length === 0
      ? notice("ui_feedback_review_external_kept")
      : notice("ui_feedback_review_external_conflict", { entries: rebased.conflicts.slice(0, 10).join(", ") });
    this.persist();
  }

  // The SDK per-unit fingerprint of the base message as the editor loaded it (undefined: the base locale has errors).
  fingerprint(key: string): string | undefined {
    return this.#options.snapshot()?.review?.fingerprints?.[key];
  }

  // Review states are recorded for the base text the reviewer saw; without its fingerprint none can be recorded.
  statesBlocked(): boolean {
    if (this.#options.snapshot()?.review?.fingerprints !== undefined) return false;
    this.message = notice("ui_review_states_need_base");
    return true;
  }

  /** Changes one entry; `translated` says whether the locale has the message (the implicit state of a new entry). */
  update(key: string, locale: string, patch: Partial<EditorReviewEntry>, translated: boolean): void {
    const identity = reviewIdentity(key, locale);
    const index = this.entries.findIndex((entry) => reviewIdentity(entry.key, entry.locale) === identity);
    const existing = index < 0 ? undefined : this.entries[index];
    const setsState = patch.state !== undefined;
    const edited: EditorReviewEntry = {
      key,
      locale,
      state: patch.state ?? effectiveReviewState(existing, translated),
      // An empty note clears it, also a note that came from XLIFF.
      note: "note" in patch ? (patch.note === "" ? undefined : patch.note) : existing?.note,
      // A state the user sets is recorded for the base text the editor loaded.
      sourceFingerprint: setsState ? patch.sourceFingerprint : existing?.sourceFingerprint,
      samples: createPreviewSamples(patch.samples ?? existing?.samples),
      stale: setsState ? patch.sourceFingerprint !== this.fingerprint(key) : existing?.stale,
      // A note or samples alone record no review state.
      // A new entry records no state; an existing one keeps its own (a missing flag reads as recorded, as sameReviewEntry does).
      implicit: setsState ? false : existing === undefined ? true : existing.implicit === true,
    };
    // An edit back to the saved entry is no change: the saved entry is restored, and a new entry that records nothing
    // is dropped.
    const base = this.#baseIndex.get(identity);
    const next = base !== undefined && sameReviewEntry(edited, base) ? cloneReviewEntries([base])[0]
      : base === undefined && emptyReviewEntry(edited) ? undefined : edited;
    this.entries = next === undefined ? this.entries.filter((_, candidate) => candidate !== index)
      : index < 0 ? [...this.entries, next]
      : this.entries.map((entry, candidate) => candidate === index ? next : entry);
    this.message = undefined;
    this.persist();
  }

  /** Records `state` for the base text the editor loaded, with the preview samples the reviewer used. */
  setState(key: string, locale: string, state: EditorReviewState, samples: Record<string, string>, translated: boolean): void {
    if (this.statesBlocked()) return;
    this.update(key, locale, { state, sourceFingerprint: this.fingerprint(key), samples: createPreviewSamples(samples) }, translated);
  }

  /**
   * Confirms the entry's current state for the base text the editor loaded (#225 P20): the translator checked a stale
   * state against the current source text. It is a review change like any other, written by the one save.
   */
  confirm(key: string, locale: string, samples: Record<string, string>, translated: boolean): void {
    const state = effectiveReviewState(this.index.get(reviewIdentity(key, locale)), translated);
    this.setState(key, locale, state, samples, translated);
  }

  setNote(key: string, locale: string, note: string, translated: boolean): void {
    this.update(key, locale, { note }, translated);
  }

  /** Records `state` for every listed message in `locale`. */
  mark(keys: readonly string[], locale: string, state: EditorReviewState): void {
    if (this.statesBlocked()) return;
    const next = [...this.entries];
    const byIdentity = reviewMap(next);
    for (const key of keys) {
      const identity = reviewIdentity(key, locale);
      const existing = byIdentity.get(identity);
      const entry: EditorReviewEntry = {
        key,
        locale,
        state,
        note: existing?.note,
        sourceFingerprint: this.fingerprint(key),
        samples: createPreviewSamples(existing?.samples),
        stale: false,
        implicit: false,
      };
      const index = next.findIndex((candidate) => reviewIdentity(candidate.key, candidate.locale) === identity);
      if (index < 0) next.push(entry);
      else next[index] = entry;
      byIdentity.set(identity, entry);
    }
    this.entries = next;
    this.message = notice("ui_count_review_marked", { count: keys.length, state });
    this.persist();
  }

  addTerm(term: EditorTerminologyEntry): void {
    this.terminology = [...this.terminology, term];
    this.persist();
  }

  removeTerm(index: number): void {
    this.terminology = this.terminology.filter((_, candidate) => candidate !== index);
    this.persist();
  }

  /**
   * Saves the edits through the bridge against the revision they started from. Resolves whether no unsaved review
   * changes are left: `false` when the save was refused or failed, or the review file cannot be written.
   */
  async save(): Promise<boolean> {
    if (!this.dirty) return true;
    if (this.saving || this.#options.snapshot()?.review?.error !== undefined) return false;
    this.saving = true;
    this.message = undefined;
    try {
      const result = await this.#options.bridge.saveReview({
        expectedRevision: this.revision,
        entries: this.entries.map((entry) => ({ ...entry, samples: createPreviewSamples(entry.samples) })),
        terminology: this.terminology.map((term) => ({ ...term })),
        original: this.#baseEntries.map((entry) => ({ ...entry, samples: createPreviewSamples(entry.samples) })),
        originalTerminology: this.#baseTerminology.map((term) => ({ ...term })),
      });
      if (!result.ok || result.review === undefined) {
        this.message = result.message ?? notice("ui_feedback_review_save_failed");
        return false;
      }
      this.entries = cloneReviewEntries(result.review.entries);
      this.terminology = structuredClone(result.review.terminology);
      this.#baseEntries = cloneReviewEntries(result.review.entries);
      this.#baseTerminology = structuredClone(result.review.terminology);
      this.revision = result.review.revision;
      // A state recorded for base text that changed on disk since it was loaded is saved as stale; say so.
      this.message = result.review.notice ?? notice("ui_feedback_workflow_saved");
      // The editor keeps the fingerprints of the base text it shows until the catalog is reloaded.
      const snapshot = this.#options.snapshot();
      if (snapshot !== undefined) snapshot.review = { ...result.review, fingerprints: snapshot.review?.fingerprints ?? result.review.fingerprints };
      if (snapshot !== undefined) snapshot.history = result.history;
      this.persist();
      return true;
    } catch (error) {
      this.message = this.#options.errorNotice(error);
      return false;
    } finally {
      this.saving = false;
    }
  }

  #changes(): StoredReview {
    const entries: ReviewChange[] = [];
    for (const entry of this.entries) {
      const base = this.#baseIndex.get(reviewIdentity(entry.key, entry.locale));
      if (base === undefined ? !emptyReviewEntry(entry) : !sameReviewEntry(entry, base)) entries.push({ entry, base });
    }
    for (const base of this.#baseEntries) {
      if (!this.index.has(reviewIdentity(base.key, base.locale))) entries.push({ base });
    }
    const terminology = sameTerminology(this.terminology, this.#baseTerminology)
      ? undefined
      : { local: this.terminology, base: this.#baseTerminology };
    return { entries, terminology };
  }

  #readStored(value: WorkspaceSnapshot): StoredReview | undefined {
    try {
      const raw = this.#options.storage?.get(storageKey(value)) ?? null;
      if (raw === null) return undefined;
      const parsed = JSON.parse(raw) as { version?: unknown; entries?: unknown; terminology?: unknown };
      if (parsed.version !== 1 || !Array.isArray(parsed.entries)) return undefined;
      const isEntry = (candidate: unknown): candidate is EditorReviewEntry => typeof candidate === "object" && candidate !== null &&
        typeof (candidate as EditorReviewEntry).key === "string" && typeof (candidate as EditorReviewEntry).locale === "string" &&
        typeof (candidate as EditorReviewEntry).state === "string";
      const entries: ReviewChange[] = [];
      for (const candidate of parsed.entries as unknown[]) {
        if (typeof candidate !== "object" || candidate === null) continue;
        const { entry, base } = candidate as { entry?: unknown; base?: unknown };
        if ((entry !== undefined && !isEntry(entry)) || (base !== undefined && !isEntry(base)) || (entry === undefined && base === undefined)) continue;
        entries.push({ entry: entry as EditorReviewEntry | undefined, base: base as EditorReviewEntry | undefined });
      }
      const terms = parsed.terminology as StoredReview["terminology"] | undefined;
      const terminology = terms !== undefined && Array.isArray(terms?.local) && Array.isArray(terms?.base) ? terms : undefined;
      return entries.length > 0 || terminology !== undefined ? { entries, terminology } : undefined;
    } catch {
      return undefined;
    }
  }
}
