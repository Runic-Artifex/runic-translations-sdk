import type { EditorBridge } from "../editor-bridge";
import type {
  EditorDiagnosticQuickFix,
  EditorDocumentDraft,
  EditorMessageEntry,
  EditorMessageOperation,
  WorkspaceSnapshot,
} from "../contracts";
import { buildRows } from "../resource-model";

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

export interface DraftStoreOptions {
  bridge: Pick<EditorBridge, "transformDocument" | "applyAuthoringOperation" | "applyDiagnosticFix">;
  snapshot: () => WorkspaceSnapshot | undefined;
  storage: DraftStorage;
  /** Waits inside a document's queue: the debounce of a composer edit or a pattern edit. */
  wait?: (milliseconds: number) => Promise<void>;
  /** The timer of the raw-source validation debounce, which runs before a validation joins its document's queue. */
  setTimer?: (callback: () => void, milliseconds: number) => unknown;
  clearTimer?: (handle: unknown) => void;
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
  /** Whether any document has a draft: the unsaved-work signal of the documents. */
  hasDrafts = $derived(Object.keys(this.drafts).length > 0);

  readonly #options: DraftStoreOptions;
  readonly #queues = new Map<string, Promise<void>>();
  readonly #versions = new Map<string, number>();
  #workspace = 0;
  #validationTimer: unknown;

  constructor(options: DraftStoreOptions) {
    this.#options = options;
  }

  /** Bumped whenever a snapshot is installed: answers for an earlier one are dropped. */
  get workspaceGeneration(): number {
    return this.#workspace;
  }

  nextWorkspace(): number {
    return ++this.#workspace;
  }

  /** A document is dirty while it has a draft, also one equal to the saved text. */
  isDirty(path: string | undefined): boolean {
    return path !== undefined && this.drafts[path] !== undefined;
  }

  content(path: string, saved: string): string {
    return this.drafts[path] ?? saved;
  }

  generation(path: string): number {
    return this.generations[path] ?? 0;
  }

  set(path: string, content: string): void {
    this.drafts[path] = content;
    this.generations[path] = this.generation(path) + 1;
  }

  /** Replaces the drafts, keeping their generations (recovered or merged drafts). */
  replace(drafts: Record<string, string>): void {
    this.drafts = drafts;
  }

  /** A save of `content` at `generation` left a newer draft of `path`. */
  hasNewer(path: string, content: string, generation: number): boolean {
    return this.generations[path] !== generation || this.drafts[path] !== content;
  }

  /** Forgets the saved document's draft. */
  drop(path: string): void {
    delete this.drafts[path];
    delete this.generations[path];
    this.persist();
  }

  /** A newly opened workspace: no drafts, and the stored ones offered for recovery. */
  reset(next: WorkspaceSnapshot): void {
    this.drafts = {};
    this.parsed = {};
    this.generations = {};
    this.recovered = this.#readStored(next);
  }

  /** Discards every draft and its stored copy. */
  discard(): void {
    this.clearStored(this.#options.snapshot());
    this.drafts = {};
    this.generations = {};
  }

  /** Restores the recovered drafts and forgets the offer. */
  restoreRecovered(): void {
    this.drafts = Object.fromEntries(Object.entries(this.recovered).map(([path, draft]) => [path, draft.content]));
    this.recovered = {};
    this.persist();
  }

  /** Declines the recovered drafts. */
  discardRecovered(): void {
    this.recovered = {};
    this.clearStored(this.#options.snapshot());
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

  /** The document's queued requests; a save waits for them. */
  settled(path: string): Promise<void> | undefined {
    return this.#queues.get(path);
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
      done,
    }).finally(() => { this.authoringPending -= 1; });
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
  ): Promise<void> {
    return this.#submit({
      path,
      ticket: this.#ticket(),
      source: () => repair?.text() ?? this.content(path, saved),
      request: (content) => this.#options.bridge.applyDiagnosticFix(path, content, fix),
      current: repair === undefined ? undefined : (content) => repair.text() === content,
      rejection: "EDITOR-FIX",
      commit: repair === undefined ? "draft" : "none",
      done,
    });
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
    const pending = (this.#queues.get(job.path) ?? Promise.resolve()).then(() => this.#run(job));
    this.#queues.set(job.path, pending);
    void pending.finally(() => { if (this.#queues.get(job.path) === pending) this.#queues.delete(job.path); });
    return pending;
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
      if (job.commit !== "none") this.parsed[job.path] = { content: result.content, entries: result.entries };
      if (job.commit === "draft") {
        this.set(job.path, result.content);
        this.persist();
      }
      job.done({ kind: "applied", result });
    } catch (error) {
      job.done({ kind: "failed", error });
    }
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
