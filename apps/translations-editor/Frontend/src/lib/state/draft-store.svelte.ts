import type { EditorBridge } from "../editor-bridge";
import type {
  EditorDiagnosticQuickFix,
  EditorDocumentDraft,
  EditorMessageEntry,
  EditorMessageOperation,
  WorkspaceSnapshot,
} from "../contracts";
import { buildRows } from "../resource-model";
import { nextPlacement, removePlacement, type Placement } from "../message-starter";
import { DraftHistory, type DraftChangeKind, type DraftHistoryOptions, type DraftState, type DraftStep } from "./draft-history.svelte";

/** The compiler's reading of a draft: its entries are current while the draft still has this content. */
export type ParsedDraft = { content: string; entries: EditorMessageEntry[] };
/** A draft kept in the native editor state for recovery after a crash or a closed window. */
export type StoredDraft = { content: string; baseRevision: string };

export interface DraftStorage {
  get(key: string): string | null;
  set(key: string, value: string): void;
  remove(key: string): void;
}

/**
 * How a pipeline request ended. A request whose answer arrived for an older draft, another workspace or a superseded
 * edit ends without an outcome: nothing changed.
 */
export type DraftOutcome =
  | { kind: "applied"; result: EditorDocumentDraft }
  | { kind: "rejected"; result: EditorDocumentDraft }
  | { kind: "failed"; error: unknown };

/** What an unsaved step records about a change: its kind, the message and the field typing extends a step in. */
export interface DraftStepInfo {
  kind: DraftChangeKind;
  key?: string;
  field?: string;
  /** The state the change started from, when it began before its request (a starter changed in memory first). */
  before?: DraftState;
}

/** The starters of missing translations, which the history captures and restores with their document. */
export interface DraftStarters {
  capture(path: string): Record<string, unknown>;
  restore(path: string, starters: Readonly<Record<string, unknown>>): void;
}

export interface DraftStoreOptions {
  bridge: Pick<EditorBridge, "transformDocument" | "applyAuthoringOperation" | "applyDiagnosticFix">;
  snapshot: () => WorkspaceSnapshot | undefined;
  storage: DraftStorage;
  /** Waits inside a document's queue: the debounce of a composer edit or a pattern edit. */
  wait?: (milliseconds: number) => Promise<void>;
  /** The timer of the raw-source validation debounce, which runs before a validation joins its document's queue. */
  setTimer?: (callback: () => void, milliseconds: number) => unknown;
  clearTimer?: (handle: unknown) => void;
  history?: DraftHistoryOptions;
}

type Ticket = { workspace: number; root: string | undefined };

interface DraftJob {
  path: string;
  ticket: Ticket;
  /** A newer request of the same identity supersedes this one until it calls the host. */
  supersede?: { identity: string; version: number };
  /** The debounce, inside the queue, after the document's earlier requests finished. */
  delay?: number;
  /** The text the request starts from. */
  source(): string;
  /** The draft generation the answer must still belong to; by default the one current when the request starts. */
  generation?: number;
  /** The host request; `undefined` skips it (a message the composer cannot edit). */
  request(content: string): Promise<EditorDocumentDraft> | undefined;
  /** A further freshness check of the answer. */
  current?(content: string): boolean;
  /** The diagnostic that marks a refused request. */
  rejection?: string;
  /** What an accepted answer updates: the draft and its parsed entries, only the parsed entries, or nothing. */
  commit: "draft" | "parsed" | "none";
  /** The unsaved step a committed draft change records. */
  step?: DraftStepInfo;
  done(outcome: DraftOutcome): void;
}

const validationIdentity = "\u0000validation";
const storageKey = (value: WorkspaceSnapshot): string => `runic-translations:drafts:1:${value.root}\n${value.catalog?.id ?? ""}`;

/**
 * The unsaved text of each document and the one pipeline that changes it. Composer edits, authoring operations,
 * raw-source validation and quick fixes run in one queue per document, and each answer is accepted only while the
 * workspace, the document's draft generation and the request itself are still current.
 */
export class DraftStore {
  drafts = $state<Record<string, string>>({});
  parsed = $state<Record<string, ParsedDraft>>({});
  generations = $state<Record<string, number>>({});
  /** Drafts stored by an earlier session of this workspace, offered for recovery. */
  recovered = $state<Record<string, StoredDraft>>({});
  /** Authoring operations queued or running. */
  authoringPending = $state(0);
  /** A raw-source validation is scheduled or running. */
  validating = $state(false);
  /**
   * Whether any document has unsaved text. A draft is kept only while it differs from the saved document, so this is
   * the unsaved-work signal of the documents.
   */
  hasDrafts = $derived(Object.keys(this.drafts).length > 0);
  /** The documents with unsaved text, in the workspace's order. */
  dirtyPaths = $derived(this.#ordered(Object.keys(this.drafts)));
  /** The undo and redo steps of the unsaved edits, per document. */
  readonly history: DraftHistory;

  readonly #options: DraftStoreOptions;
  readonly #queues = new Map<string, Promise<void>>();
  readonly #versions = new Map<string, number>();
  /** Messages the saved documents lack that `placeMessage` put into a draft, by document and key. */
  readonly #placed = new Map<string, Placement>();
  #workspace = 0;
  #validationTimer: unknown;
  #starters: DraftStarters | undefined;
  /** The saved text each document with steps had when its first step was recorded; a change of it clears them. */
  readonly #historyBase = new Map<string, string | undefined>();

  constructor(options: DraftStoreOptions) {
    this.#options = options;
    this.history = new DraftHistory(options.history);
  }

  /** The starters of missing translations take part in the history of their document. */
  attachStarters(starters: DraftStarters): void {
    this.#starters = starters;
  }

  /** Bumped whenever a snapshot is installed: answers for an earlier one are dropped. */
  get workspaceGeneration(): number {
    return this.#workspace;
  }

  nextWorkspace(): number {
    return ++this.#workspace;
  }

  /** A document is dirty while its draft differs from the saved text; a draft equal to it is never kept. */
  isDirty(path: string | undefined): boolean {
    return path !== undefined && this.drafts[path] !== undefined;
  }

  /** The saved text of `path` in the open workspace. */
  saved(path: string): string | undefined {
    return this.#options.snapshot()?.documents.find((document) => document.path === path)?.content;
  }

  content(path: string, saved: string): string {
    return this.drafts[path] ?? saved;
  }

  generation(path: string): number {
    return this.generations[path] ?? 0;
  }

  /**
   * Sets the document's text. Text equal to the saved document is no draft: the draft is dropped, also from the
   * recovery copy once it is persisted. Either way the generation advances, so answers for the earlier text are dropped.
   */
  set(path: string, content: string): void {
    if (content === this.saved(path)) delete this.drafts[path];
    else this.drafts[path] = content;
    this.generations[path] = this.generation(path) + 1;
  }

  /** Replaces the drafts (recovered or merged drafts); each starts a new generation and equal ones are dropped. */
  replace(drafts: Record<string, string>): void {
    this.drafts = {};
    for (const [path, content] of Object.entries(drafts)) this.set(path, content);
  }

  /** Drops the drafts that equal the saved text of a newly installed snapshot, and their recovery copy. */
  prune(): void {
    // A document whose saved text changed (it was saved, or a saved change was undone) starts a new history.
    for (const path of this.history.paths()) {
      if (this.#historyBase.get(path) !== this.saved(path)) this.#clearHistory(path);
    }
    const equal = Object.keys(this.drafts).filter((path) => this.drafts[path] === this.saved(path));
    if (equal.length === 0) return;
    for (const path of equal) delete this.drafts[path];
    this.persist();
  }

  /** A save of `content` at `generation` left a newer draft of `path`. */
  hasNewer(path: string, content: string, generation: number): boolean {
    return this.generation(path) !== generation || this.drafts[path] !== content;
  }

  /** Forgets the saved document's draft. Its generation stays, so an answer for the saved text is still dropped. */
  drop(path: string): void {
    this.#forgetPlaced(path);
    this.#clearHistory(path);
    delete this.drafts[path];
    this.generations[path] = this.generation(path) + 1;
    this.persist();
  }

  /** A newly opened workspace: no drafts, and the stored ones offered for recovery. */
  reset(next: WorkspaceSnapshot): void {
    this.drafts = {};
    this.parsed = {};
    this.generations = {};
    this.#placed.clear();
    this.#clearAllHistory();
    this.recovered = this.#readStored(next);
  }

  /** Discards every draft and its stored copy. */
  discard(): void {
    this.clearStored(this.#options.snapshot());
    this.drafts = {};
    this.generations = {};
    this.#placed.clear();
    this.#clearAllHistory();
  }

  /**
   * Restores the recovered drafts and forgets the offer. Each restored draft starts a new generation, so a save of it
   * is not mistaken for a newer draft; one equal to the saved text is dropped.
   */
  restoreRecovered(): void {
    const paths = Object.keys(this.recovered);
    const before = new Map(paths.map((path) => [path, this.capture(path)]));
    this.replace(Object.fromEntries(Object.entries(this.recovered).map(([path, draft]) => [path, draft.content])));
    this.recovered = {};
    this.persist();
    // Each restored document's recovery is one step, which Undo takes back.
    for (const path of paths) this.recordStep(path, { kind: "recovery" }, before.get(path)!);
  }

  /** Declines the recovered drafts; the stored copy then holds only this session's drafts. */
  discardRecovered(): void {
    this.recovered = {};
    this.persist();
  }

  persist(): void {
    const current = this.#options.snapshot();
    if (current === undefined) return;
    if (Object.keys(this.drafts).length === 0) {
      this.clearStored(current);
      return;
    }
    const stored: Record<string, StoredDraft> = {};
    for (const [path, content] of Object.entries(this.drafts)) {
      const document = current.documents.find((candidate) => candidate.path === path);
      if (document !== undefined) stored[path] = { content, baseRevision: document.revision };
    }
    this.#options.storage.set(storageKey(current), JSON.stringify({ version: 1, documents: stored }));
  }

  clearStored(value: WorkspaceSnapshot | undefined): void {
    if (value !== undefined) this.#options.storage.remove(storageKey(value));
  }

  /** The document's unsaved state as a step records it. */
  capture(path: string): DraftState {
    const draft = this.drafts[path];
    const parsed = this.parsed[path];
    const placements: Record<string, Placement> = {};
    for (const [identity, placement] of this.#placed) if (identity.startsWith(`${path}\u0000`)) placements[identity] = placement;
    return {
      draft,
      parsed: parsed !== undefined && parsed.content === (draft ?? this.saved(path)) ? parsed : undefined,
      placements,
      starters: this.#starters?.capture(path) ?? {},
    };
  }

  /** Records a change of the document that ran outside the queue (raw-source typing, a starter changed in memory). */
  recordStep(path: string, info: DraftStepInfo, before: DraftState): DraftStep | undefined {
    const depth = this.history.depth(path);
    if (depth.undo + depth.redo === 0) this.#historyBase.set(path, this.saved(path));
    return this.history.record({
      path, kind: info.kind, key: info.key, message: this.#messagePath(info.key), field: info.field, before, after: this.capture(path),
    });
  }

  /**
   * Takes back the document's latest unsaved step: its draft, compiler reading, placements and starters return to the
   * state before it, and the recovery copy follows. A draft equal to the saved text is none, so the document can be
   * clean again. The draft's generation advances, so answers for the undone text are dropped.
   */
  undo(path: string): DraftStep | undefined {
    const step = this.history.takeUndo(path);
    if (step !== undefined) this.#restore(path, step.before);
    return step;
  }

  /** Applies the document's latest undone step again. */
  redo(path: string): DraftStep | undefined {
    const step = this.history.takeRedo(path);
    if (step !== undefined) this.#restore(path, step.after);
    return step;
  }

  /**
   * Reads the document's current text again (after an undo or redo): its entries and diagnostics, queued behind the
   * document's other requests. A document that does not exist yet and has no draft has nothing to read.
   */
  refresh(path: string, done: (outcome: DraftOutcome) => void): Promise<void> {
    if (this.drafts[path] === undefined && this.saved(path) === undefined) return Promise.resolve();
    return this.#submit({
      path,
      ticket: this.#ticket(),
      source: () => this.drafts[path] ?? this.saved(path) ?? "",
      request: (content) => this.#options.bridge.transformDocument(path, content),
      commit: "parsed",
      done,
    });
  }

  /** The document's queued requests; a save waits for them. */
  settled(path: string): Promise<void> | undefined {
    return this.#queues.get(path);
  }

  /** Every document's queued requests, also those of a document whose first edit has no draft yet. */
  async settledAll(): Promise<void> {
    while (this.#queues.size > 0) await Promise.all([...this.#queues.values()]);
  }

  /** Parses a recovered draft; resolves whether its entries were stored (the draft did not change meanwhile). */
  async parse(path: string, content: string): Promise<boolean> {
    const result = await this.#options.bridge.transformDocument(path, content);
    if (this.drafts[path] !== content) return false;
    this.parsed[path] = { content, entries: result.entries };
    return true;
  }

  /** A composer edit: the message's new value, written into the document's draft after a short debounce. */
  transform(path: string, saved: string, key: string, value: string, done: (outcome: DraftOutcome) => void): Promise<void> {
    return this.#submit({
      path,
      ticket: this.#ticket(),
      supersede: this.#supersede(`${path}\u0000${key}`),
      delay: 150,
      source: () => this.content(path, saved),
      request: (content) => this.#options.bridge.transformDocument(path, content, key, value),
      rejection: "EDITOR-TRANSFORM",
      commit: "draft",
      step: { kind: "edit", key, field: `${path}\u0000${key}\u0000value` },
      done,
    });
  }

  /** A structured authoring operation of the visual composer; a pattern edit is debounced and superseded. */
  applyOperation(
    path: string,
    saved: string,
    key: string,
    locale: string,
    operation: EditorMessageOperation,
    done: (outcome: DraftOutcome) => void,
  ): Promise<void> {
    const supersede = this.#supersede(`${path}\u0000${key}\u0000${operation.variantId ?? operation.kind}`);
    const pattern = operation.kind === "set-pattern";
    this.authoringPending += 1;
    return this.#submit({
      path,
      ticket: this.#ticket(),
      supersede: pattern ? supersede : undefined,
      delay: pattern ? 120 : undefined,
      source: () => this.content(path, saved),
      request: (content) => {
        const entry = buildRows(this.#options.snapshot(), this.drafts, this.parsed)
          .find((row) => row.key === key)?.cells[locale]?.entry;
        if (entry?.authoring?.supported !== true) return undefined;
        return this.#options.bridge.applyAuthoringOperation(path, content, key, entry.authoring.revision, operation);
      },
      rejection: "EDITOR-AUTHORING",
      commit: "draft",
      step: pattern
        ? { kind: "edit", key, field: `${path}\u0000${key}\u0000pattern\u0000${operation.variantId ?? ""}` }
        : { kind: "structure", key },
      done,
    }).finally(() => { this.authoringPending -= 1; });
  }

  /**
   * Places a message the saved document lacks (a missing translation started empty in the composer) into the
   * document's draft: inserted on its first text and updated after that, as a composer edit; with `value` undefined it
   * is taken out again, so a translation cleared back to empty leaves the text the document had without it (the saved
   * text when nothing else changed, which is then no draft). A newer placement of the same message supersedes it.
   */
  placeMessage(
    path: string,
    saved: string,
    key: string,
    value: string | undefined,
    done: (outcome: DraftOutcome) => void,
    step: DraftStepInfo = { kind: "edit", key, field: `${path}\u0000${key}\u0000value` },
  ): Promise<void> {
    const identity = `${path}\u0000${key}`;
    let source: string | undefined;
    return this.#submit({
      path,
      ticket: this.#ticket(),
      supersede: this.#supersede(identity),
      delay: 150,
      source: () => this.content(path, saved),
      request: (content) => {
        source = content;
        if (value !== undefined) return this.#options.bridge.transformDocument(path, content, key, value);
        const placed = this.#placed.get(identity);
        const without = placed === undefined ? undefined : removePlacement(content, placed);
        // Nothing was placed (the insertion was superseded before it ran), or the message cannot be found again.
        if (without === undefined) return undefined;
        return this.#options.bridge.transformDocument(path, without);
      },
      rejection: "EDITOR-TRANSFORM",
      commit: "draft",
      step,
      done: (outcome) => {
        if (outcome.kind === "applied" && source !== undefined) {
          const next = value === undefined ? undefined : nextPlacement(this.#placed.get(identity), source, outcome.result.content);
          if (next === undefined) this.#placed.delete(identity);
          else this.#placed.set(identity, next);
          // A document that does not exist yet has no saved text to fall back to: back at its empty start, no draft.
          if (value === undefined && this.saved(path) === undefined && outcome.result.content === saved) {
            delete this.drafts[path];
            this.persist();
          }
        }
        done(outcome);
      },
    });
  }

  /**
   * Runs `task` in the document's queue, after the document's earlier requests and before later ones; a save waits for
   * it as for them. The task reports its own failures.
   */
  queue(path: string, task: () => Promise<void>): Promise<void> {
    return this.#enqueue(path, () => task().catch(() => undefined));
  }

  /**
   * Validates a raw-source draft the user typed: debounced, then queued behind the document's other requests, so its
   * entries never overwrite those of a composer edit that changed the draft meanwhile. Only the latest validation
   * counts.
   */
  validate(path: string, content: string, done: (outcome: DraftOutcome) => void): void {
    const clearTimer = this.#options.clearTimer ?? ((handle) => clearTimeout(handle as ReturnType<typeof setTimeout>));
    const setTimer = this.#options.setTimer ?? ((callback, milliseconds) => setTimeout(callback, milliseconds));
    if (this.#validationTimer !== undefined) clearTimer(this.#validationTimer);
    const supersede = this.#supersede(validationIdentity);
    const ticket = this.#ticket();
    const generation = this.generation(path);
    const latest = () => this.#versions.get(validationIdentity) === supersede.version;
    this.validating = true;
    this.#validationTimer = setTimer(() => {
      void this.#submit({
        path,
        ticket,
        supersede,
        generation,
        source: () => content,
        request: (source) => this.#options.bridge.transformDocument(path, source),
        current: latest,
        commit: "parsed",
        // A superseded validation reports nothing, also no failure.
        done: (outcome) => {
          if (!latest()) return;
          this.validating = false;
          done(outcome);
        },
      }).finally(() => {
        // Also a validation dropped for a newer draft or workspace ends the busy state.
        if (latest()) this.validating = false;
      });
    }, 350);
  }

  /**
   * Applies a quick fix to the document's draft or, with `repair`, to the text of the repair dialog, which then
   * receives the answer instead of the draft.
   */
  applyFix(
    path: string,
    saved: string,
    fix: EditorDiagnosticQuickFix,
    repair: { text(): string } | undefined,
    done: (outcome: DraftOutcome) => void,
    key?: string,
  ): Promise<void> {
    return this.#submit({
      path,
      ticket: this.#ticket(),
      source: () => repair?.text() ?? this.content(path, saved),
      request: (content) => this.#options.bridge.applyDiagnosticFix(path, content, fix),
      current: repair === undefined ? undefined : (content) => repair.text() === content,
      rejection: "EDITOR-FIX",
      commit: repair === undefined ? "draft" : "none",
      step: { kind: "quick-fix", key },
      done,
    });
  }

  #ordered(paths: string[]): string[] {
    const order = this.#options.snapshot()?.documents.map((document) => document.path) ?? [];
    const rank = (path: string) => { const index = order.indexOf(path); return index < 0 ? order.length : index; };
    return paths.sort((left, right) => rank(left) - rank(right) || left.localeCompare(right));
  }

  #ticket(): Ticket {
    return { workspace: this.#workspace, root: this.#options.snapshot()?.root };
  }

  #supersede(identity: string): { identity: string; version: number } {
    const version = (this.#versions.get(identity) ?? 0) + 1;
    this.#versions.set(identity, version);
    return { identity, version };
  }

  #workspaceCurrent(ticket: Ticket): boolean {
    return this.#workspace === ticket.workspace && this.#options.snapshot()?.root === ticket.root;
  }

  #fresh(job: DraftJob): boolean {
    return this.#workspaceCurrent(job.ticket) &&
      (job.supersede === undefined || this.#versions.get(job.supersede.identity) === job.supersede.version) &&
      (job.generation === undefined || this.generation(job.path) === job.generation);
  }

  // The one queue of a document: each request starts after the earlier ones finished and never rejects.
  #submit(job: DraftJob): Promise<void> {
    return this.#enqueue(job.path, () => this.#run(job));
  }

  #enqueue(path: string, run: () => Promise<void>): Promise<void> {
    const pending = (this.#queues.get(path) ?? Promise.resolve()).then(run);
    this.#queues.set(path, pending);
    void pending.finally(() => { if (this.#queues.get(path) === pending) this.#queues.delete(path); });
    return pending;
  }

  #forgetPlaced(path: string): void {
    for (const identity of [...this.#placed.keys()]) if (identity.startsWith(`${path}\u0000`)) this.#placed.delete(identity);
  }

  async #run(job: DraftJob): Promise<void> {
    try {
      if (!this.#fresh(job)) return;
      if (job.delay !== undefined) {
        await (this.#options.wait ?? ((milliseconds) => new Promise<void>((resolve) => setTimeout(resolve, milliseconds))))(job.delay);
        if (!this.#fresh(job)) return;
      }
      const content = job.source();
      const generation = job.generation ?? this.generation(job.path);
      const request = job.request(content);
      if (request === undefined) return;
      const result = await request;
      if (!this.#workspaceCurrent(job.ticket) || this.generation(job.path) !== generation || job.current?.(content) === false) return;
      if (job.rejection !== undefined && result.diagnostics.some((diagnostic) => diagnostic.id === job.rejection)) {
        job.done({ kind: "rejected", result });
        return;
      }
      const before = job.commit === "draft" && job.step !== undefined ? this.#stepStart(job.path, job.step) : undefined;
      if (job.commit !== "none") this.parsed[job.path] = { content: result.content, entries: result.entries };
      if (job.commit === "draft") {
        this.set(job.path, result.content);
        this.persist();
      }
      job.done({ kind: "applied", result });
      // After `done`, which may hand a starter over or drop a draft: the step ends in the state the change left.
      if (before !== undefined) this.recordStep(job.path, job.step!, before);
    } catch (error) {
      job.done({ kind: "failed", error });
    }
  }

  /**
   * The state a committed change started from: the one its caller captured (a starter changes in memory before its
   * placement runs) while the document's draft is still the one it saw, else the current one with the caller's starters.
   */
  #stepStart(path: string, step: DraftStepInfo): DraftState {
    const current = this.capture(path);
    if (step.before === undefined) return current;
    return step.before.draft === current.draft ? step.before : { ...current, starters: step.before.starters };
  }

  #restore(path: string, state: DraftState): void {
    if (state.draft === undefined || state.draft === this.saved(path)) {
      delete this.drafts[path];
      this.generations[path] = this.generation(path) + 1;
    } else this.set(path, state.draft);
    if (state.parsed !== undefined) this.parsed[path] = state.parsed;
    this.#forgetPlaced(path);
    for (const [identity, placement] of Object.entries(state.placements)) this.#placed.set(identity, placement);
    this.#starters?.restore(path, state.starters);
    this.persist();
  }

  /** The dotted path of the message `key` (`files.selected`), as the editor shows it; the key when it is unknown. */
  #messagePath(key: string | undefined): string | undefined {
    if (key === undefined) return undefined;
    const named = (entries: readonly EditorMessageEntry[] | undefined) =>
      entries?.find((entry) => entry.key === key && (entry.path?.length ?? 0) > 0)?.path.join(".");
    for (const parsed of Object.values(this.parsed)) {
      const path = named(parsed.entries);
      if (path !== undefined) return path;
    }
    for (const document of this.#options.snapshot()?.documents ?? []) {
      const path = named(document.entries);
      if (path !== undefined) return path;
    }
    return key;
  }

  #clearHistory(path: string): void {
    this.history.clear(path);
    this.#historyBase.delete(path);
  }

  #clearAllHistory(): void {
    this.history.clearAll();
    this.#historyBase.clear();
  }

  #readStored(value: WorkspaceSnapshot): Record<string, StoredDraft> {
    try {
      const raw = this.#options.storage.get(storageKey(value));
      if (raw === null) return {};
      const parsed = JSON.parse(raw) as { version?: unknown; documents?: unknown };
      if (parsed.version !== 1 || typeof parsed.documents !== "object" || parsed.documents === null) return {};
      const recovered: Record<string, StoredDraft> = {};
      for (const [path, candidate] of Object.entries(parsed.documents as Record<string, unknown>)) {
        const document = value.documents.find((item) => item.path === path);
        if (document === undefined || typeof candidate !== "object" || candidate === null) continue;
        const draft = candidate as Partial<StoredDraft>;
        if (typeof draft.content === "string" && typeof draft.baseRevision === "string") recovered[path] = draft as StoredDraft;
      }
      return recovered;
    } catch {
      return {};
    }
  }
}
