import type { EditorBridge } from "../editor-bridge";
import type {
  EditorReviewEntry,
  EditorReviewSnapshot,
  EditorReviewState,
  EditorTerminologyEntry,
  WorkspaceSnapshot,
} from "../contracts";
import { createPreviewSamples } from "../message-preview.js";
import { effectiveReviewState, rebaseReview, reviewIdentity, reviewMap } from "../review-model";
import { notice, type UiMessage } from "../ui-text";

export interface ReviewStoreOptions {
  bridge: Pick<EditorBridge, "saveReview">;
  /** The open workspace; a save and an external change update its review data and history in place. */
  snapshot: () => WorkspaceSnapshot | undefined;
  errorNotice: (error: unknown) => UiMessage;
}

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
  dirty = $state(false);
  saving = $state(false);
  message = $state<UiMessage>();
  /** The entries by message and locale. */
  index = $derived(reviewMap(this.entries));

  #baseEntries: EditorReviewEntry[] = [];
  #baseTerminology: EditorTerminologyEntry[] = [];
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
    this.dirty = false;
    this.message = next.review?.notice ?? next.review?.error;
  }

  /** Drops the unsaved edits. */
  discard(): void {
    const snapshot = this.#options.snapshot();
    if (snapshot !== undefined) this.install(snapshot);
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
    const next: EditorReviewEntry = {
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
      implicit: setsState ? false : existing?.implicit ?? true,
    };
    this.entries = index < 0
      ? [...this.entries, next]
      : this.entries.map((entry, candidate) => candidate === index ? next : entry);
    this.dirty = true;
    this.message = undefined;
  }

  /** Records `state` for the base text the editor loaded, with the preview samples the reviewer used. */
  setState(key: string, locale: string, state: EditorReviewState, samples: Record<string, string>, translated: boolean): void {
    if (this.statesBlocked()) return;
    this.update(key, locale, { state, sourceFingerprint: this.fingerprint(key), samples: createPreviewSamples(samples) }, translated);
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
    this.dirty = true;
    this.message = notice("ui_count_review_marked", { count: keys.length, state });
  }

  addTerm(term: EditorTerminologyEntry): void {
    this.terminology = [...this.terminology, term];
    this.dirty = true;
  }

  removeTerm(index: number): void {
    this.terminology = this.terminology.filter((_, candidate) => candidate !== index);
    this.dirty = true;
  }

  /** Saves the edits through the bridge against the revision they started from. */
  async save(): Promise<void> {
    if (!this.dirty || this.saving || this.#options.snapshot()?.review?.error !== undefined) return;
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
        return;
      }
      this.entries = cloneReviewEntries(result.review.entries);
      this.terminology = structuredClone(result.review.terminology);
      this.#baseEntries = cloneReviewEntries(result.review.entries);
      this.#baseTerminology = structuredClone(result.review.terminology);
      this.revision = result.review.revision;
      this.dirty = false;
      // A state recorded for base text that changed on disk since it was loaded is saved as stale; say so.
      this.message = result.review.notice ?? notice("ui_feedback_workflow_saved");
      // The editor keeps the fingerprints of the base text it shows until the catalog is reloaded.
      const snapshot = this.#options.snapshot();
      if (snapshot !== undefined) snapshot.review = { ...result.review, fingerprints: snapshot.review?.fingerprints ?? result.review.fingerprints };
      if (snapshot !== undefined) snapshot.history = result.history;
    } catch (error) {
      this.message = this.#options.errorNotice(error);
    } finally {
      this.saving = false;
    }
  }
}
