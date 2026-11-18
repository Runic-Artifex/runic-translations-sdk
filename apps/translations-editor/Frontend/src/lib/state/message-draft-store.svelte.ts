import type { EditorBridge } from "../editor-bridge";
import type { EditorMessageDraft, EditorMessageOperation, EditorMessageProjection } from "../contracts";
import { copySourcePlan, type PatternVariant } from "../message-starter";
import type { DraftOutcome, DraftStepInfo, DraftStore } from "./draft-store.svelte";
import type { DraftState } from "./draft-history.svelte";

/**
 * The starter of a translation that does not exist yet (#225 P2): standalone MF2 text shaped for the target locale
 * (`createMessageDraft`), edited in memory (`applyMessageDraftOperation`). While its patterns are all empty it is
 * nowhere but here; once one has text it is placed into the document's draft (`DraftStore.placeMessage`), and taken out
 * again when it is cleared back to empty.
 */
export interface MessageStarter {
  text: string;
  /** The text as created: every pattern empty. */
  initial: string;
  projection: EditorMessageProjection;
  /** The projection of `initial`. */
  initialProjection: EditorMessageProjection;
  /** The base message's `@maxLength`. */
  maxLength?: number;
  /** The document's draft holds the starter's text. */
  placed: boolean;
}

/** How a starter request ended; superseded or outdated requests end without an outcome. */
export type StarterOutcome =
  | { kind: "applied"; result: EditorMessageDraft }
  | { kind: "rejected"; result: EditorMessageDraft }
  | { kind: "failed"; error: unknown };

export interface MessageDraftStoreOptions {
  bridge: Pick<EditorBridge, "createMessageDraft" | "applyMessageDraftOperation">;
  drafts: DraftStore;
  /** The debounce of a pattern edit, inside the document's queue. */
  wait?: (milliseconds: number) => Promise<void>;
}

/** Where a starter belongs: the document the translation goes into, the message and the target locale. */
export interface StarterTarget { path: string; saved: string; key: string; locale: string }

const identityOf = (target: Pick<StarterTarget, "path" | "key" | "locale">) => `${target.path}\u0000${target.key}\u0000${target.locale}`;
const ofDocument = (identity: string, path: string) => identity.startsWith(`${path}\u0000`);

/**
 * The starters of missing translations, keyed by document, message and locale, so switching to another message and
 * back keeps one. Their operations run in the document's queue of the `DraftStore`, so a save waits for them and for
 * the placement they lead to. They take part in their document's undo history: each change records the starters as
 * they were before it, so undoing the insert of a starter returns to the empty starter.
 */
export class MessageDraftStore {
  starters = $state<Record<string, MessageStarter>>({});
  /** Starters being created. */
  loading = $state<Record<string, true>>({});
  /** Starter operations queued or running. */
  pending = $state(0);

  readonly #options: MessageDraftStoreOptions;
  readonly #versions = new Map<string, number>();
  /** Bumped on every change of a starter's text: an operation prepared for older text is dropped. */
  readonly #changes = new Map<string, number>();
  /**
   * The document state before a starter change whose placement has not committed yet: a quicker change of the same
   * starter, whose placement supersedes the first, starts from the same state.
   */
  readonly #pending = new Map<string, DraftState>();
  #session = 0;

  constructor(options: MessageDraftStoreOptions) {
    this.#options = options;
    options.drafts.attachStarters({
      capture: (path) => this.#capture(path),
      restore: (path, starters) => this.#restore(path, starters as Readonly<Record<string, MessageStarter>>),
    });
  }

  get(target: Pick<StarterTarget, "path" | "key" | "locale">): MessageStarter | undefined {
    return this.starters[identityOf(target)];
  }

  isLoading(target: Pick<StarterTarget, "path" | "key" | "locale">): boolean {
    return this.loading[identityOf(target)] === true;
  }

  /** Creates the empty starter of a missing translation, unless one exists or is being created. */
  async open(target: Pick<StarterTarget, "path" | "key" | "locale">): Promise<StarterOutcome | undefined> {
    const identity = identityOf(target);
    if (this.starters[identity] !== undefined || this.loading[identity]) return undefined;
    const session = this.#session;
    this.loading[identity] = true;
    try {
      const result = await this.#options.bridge.createMessageDraft(target.key, target.locale);
      if (session !== this.#session) return undefined;
      if (!result.success || result.projection === undefined) return { kind: "rejected", result };
      this.starters[identity] = {
        text: result.text, initial: result.text, projection: result.projection, initialProjection: result.projection,
        maxLength: result.maxLength, placed: false,
      };
      return { kind: "applied", result };
    } catch (error) {
      return session === this.#session ? { kind: "failed", error } : undefined;
    } finally {
      if (session === this.#session) delete this.loading[identity];
    }
  }

  /**
   * A composer operation on the starter, applied in memory. A pattern edit is debounced and superseded by a newer edit
   * of the same variant. `placed` receives the outcome of the placement into the document that a change leads to.
   */
  apply(target: StarterTarget, operation: EditorMessageOperation, done: (outcome: StarterOutcome) => void, placed?: (outcome: DraftOutcome) => void): Promise<void> {
    const identity = identityOf(target);
    const pattern = operation.kind === "set-pattern";
    const supersede = `${identity}\u0000${pattern ? operation.variantId ?? "" : operation.kind}`;
    const version = (this.#versions.get(supersede) ?? 0) + 1;
    if (pattern) this.#versions.set(supersede, version);
    const latest = () => !pattern || this.#versions.get(supersede) === version;
    const session = this.#session;
    this.pending += 1;
    return this.#options.drafts.queue(target.path, async () => {
      if (pattern) {
        if (!latest()) return;
        await (this.#options.wait ?? ((milliseconds) => new Promise<void>((resolve) => setTimeout(resolve, milliseconds))))(120);
        if (!latest()) return;
      }
      const starter = this.starters[identity];
      if (session !== this.#session || starter === undefined) return;
      const change = this.#changes.get(identity) ?? 0;
      try {
        const result = await this.#options.bridge.applyMessageDraftOperation(target.key, target.locale, starter.text, starter.projection.revision, operation);
        if (session !== this.#session || this.starters[identity] === undefined || (this.#changes.get(identity) ?? 0) !== change) return;
        if (!result.success || result.projection === undefined) {
          done({ kind: "rejected", result });
          return;
        }
        this.#change(target, result.text, result.projection, pattern
          ? { kind: "edit", key: target.key, field: `${target.path}\u0000${target.key}\u0000pattern\u0000${operation.variantId ?? ""}` }
          : { kind: "structure", key: target.key }, placed);
        done({ kind: "applied", result });
      } catch (error) {
        done({ kind: "failed", error });
      }
    }).finally(() => { this.pending -= 1; });
  }

  /**
   * "Copy source": every starter variant gets the base variant's pattern with the same keys, else the base's
   * catch-all pattern. The operations run on the starter text one after another and change the starter once, so the
   * document receives one edit; a refused operation leaves the starter as it was.
   */
  copySource(target: StarterTarget, base: readonly PatternVariant[], done: (outcome: StarterOutcome) => void, placed?: (outcome: DraftOutcome) => void): Promise<void> {
    const identity = identityOf(target);
    const session = this.#session;
    this.pending += 1;
    return this.#options.drafts.queue(target.path, async () => {
      const starter = this.starters[identity];
      if (session !== this.#session || starter === undefined) return;
      const change = this.#changes.get(identity) ?? 0;
      let text = starter.text;
      let projection = starter.projection;
      let last: EditorMessageDraft | undefined;
      try {
        for (const step of copySourcePlan(projection.variants, base)) {
          const variant = projection.variants.find((candidate) => candidate.keys.join("\u0000") === step.keys.join("\u0000"));
          if (variant === undefined) continue;
          const result = await this.#options.bridge.applyMessageDraftOperation(target.key, target.locale, text, projection.revision,
            { kind: "set-pattern", variantId: variant.id, pattern: step.pattern });
          if (session !== this.#session) return;
          if (!result.success || result.projection === undefined) {
            done({ kind: "rejected", result });
            return;
          }
          text = result.text;
          projection = result.projection;
          last = result;
        }
        if (last === undefined || this.starters[identity] === undefined || (this.#changes.get(identity) ?? 0) !== change) return;
        this.#change(target, text, projection, { kind: "copy-source", key: target.key }, placed);
        done({ kind: "applied", result: last });
      } catch (error) {
        done({ kind: "failed", error });
      }
    }).finally(() => { this.pending -= 1; });
  }

  /**
   * The starter's MF2 source typed by hand (the composer's source view, a suggestion). The text goes into the
   * document's draft unless it is empty or the untouched starter; once it is there the message is the document's, and
   * the composer edits it there, so the starter is handed over. Empty text returns the starter to its untouched state.
   */
  edit(target: StarterTarget, text: string, placed?: (outcome: DraftOutcome) => void): void {
    const identity = identityOf(target);
    const starter = this.starters[identity];
    if (starter === undefined) return;
    const step: DraftStepInfo = { kind: "edit", key: target.key, field: `${target.path}\u0000${target.key}\u0000value`, before: this.#before(target) };
    this.#changes.set(identity, (this.#changes.get(identity) ?? 0) + 1);
    const untouched = text.trim() === "" || text === starter.initial;
    starter.text = untouched ? starter.initial : text;
    if (untouched) starter.projection = starter.initialProjection;
    if (!untouched) {
      starter.placed = true;
      void this.#options.drafts.placeMessage(target.path, target.saved, target.key, text, (outcome) => {
        if (outcome.kind === "applied" && this.starters[identity]?.text === text) {
          delete this.starters[identity];
          this.#pending.delete(identity);
        }
        this.#settle(identity, text);
        placed?.(outcome);
      }, step);
    } else if (starter.placed) {
      starter.placed = false;
      void this.#options.drafts.placeMessage(target.path, target.saved, target.key, undefined, (outcome) => {
        this.#settle(identity, starter.initial);
        placed?.(outcome);
      }, step);
    } else this.#settle(identity, starter.text);
  }

  /** Keeps only the starters `keep` accepts, such as those whose message the saved document still lacks. */
  retain(keep: (target: Pick<StarterTarget, "path" | "key" | "locale">) => boolean): void {
    for (const identity of Object.keys(this.starters)) {
      const [path, key, locale] = identity.split("\u0000");
      if (!keep({ path, key, locale })) delete this.starters[identity];
    }
  }

  /** Forgets every starter, as when the drafts are discarded or another workspace opens. */
  reset(): void {
    this.#session += 1;
    this.starters = {};
    this.loading = {};
    this.#changes.clear();
    this.#pending.clear();
  }

  #change(target: StarterTarget, text: string, projection: EditorMessageProjection, info: DraftStepInfo, placed?: (outcome: DraftOutcome) => void): void {
    const identity = identityOf(target);
    const starter = this.starters[identity];
    if (starter === undefined) return;
    const step: DraftStepInfo = { ...info, before: this.#before(target) };
    this.#changes.set(identity, (this.#changes.get(identity) ?? 0) + 1);
    starter.text = text;
    starter.projection = projection;
    const report = (outcome: DraftOutcome) => {
      this.#settle(identity, text);
      placed?.(outcome);
    };
    if (!projection.empty) {
      starter.placed = true;
      void this.#options.drafts.placeMessage(target.path, target.saved, target.key, text, report, step);
    } else if (starter.placed) {
      starter.placed = false;
      void this.#options.drafts.placeMessage(target.path, target.saved, target.key, undefined, report, step);
    } else {
      // A change of an empty starter that the document does not see (a variant added before any text) is a step too.
      this.#options.drafts.recordStep(target.path, step, step.before!);
      this.#pending.delete(identity);
    }
  }

  /** The state a starter change starts from: that of a change whose placement is still pending, else the current one. */
  #before(target: StarterTarget): DraftState {
    const identity = identityOf(target);
    const pending = this.#pending.get(identity);
    if (pending !== undefined) return pending;
    const state = this.#options.drafts.capture(target.path);
    this.#pending.set(identity, state);
    return state;
  }

  /** A placement of `text` finished: once it is the starter's latest text, the next change starts a new state. */
  #settle(identity: string, text: string): void {
    const starter = this.starters[identity];
    if (starter === undefined || starter.text === text) this.#pending.delete(identity);
  }

  #capture(path: string): Record<string, MessageStarter> {
    const captured: Record<string, MessageStarter> = {};
    for (const [identity, starter] of Object.entries(this.starters)) {
      if (ofDocument(identity, path)) captured[identity] = $state.snapshot(starter) as MessageStarter;
    }
    return captured;
  }

  /**
   * Returns the document's starters to a captured state; operations prepared for the replaced starters are dropped. A
   * starter the state lacks is dropped too (its message is the document's there); opening the message creates an
   * empty one again.
   */
  #restore(path: string, starters: Readonly<Record<string, MessageStarter>>): void {
    for (const identity of Object.keys(this.starters)) {
      if (ofDocument(identity, path) && !(identity in starters)) delete this.starters[identity];
    }
    for (const [identity, starter] of Object.entries(starters)) this.starters[identity] = structuredClone(starter);
    for (const identity of [...this.#changes.keys(), ...Object.keys(starters)]) {
      if (ofDocument(identity, path)) this.#changes.set(identity, (this.#changes.get(identity) ?? 0) + 1);
    }
    for (const identity of [...this.#pending.keys()]) if (ofDocument(identity, path)) this.#pending.delete(identity);
  }
}
