import type { EditorMessageEntry } from "../contracts";
import type { Placement } from "../message-starter";

/** What an unsaved step changed, as Undo and Redo name it. */
export type DraftChangeKind = "edit" | "structure" | "quick-fix" | "copy-source" | "file-edit" | "recovery";

/**
 * A document's unsaved state at one point: its draft (`undefined` while it equals the saved text, so the document is
 * clean), the compiler's reading of that draft, the placements of starter messages in it and the starters of its
 * missing translations (opaque here; `MessageDraftStore` captures and restores them).
 */
export interface DraftState {
  draft: string | undefined;
  parsed?: { content: string; entries: EditorMessageEntry[] };
  placements: Readonly<Record<string, Placement>>;
  starters: Readonly<Record<string, unknown>>;
}

/** One unsaved change of one document: the state before and after it. */
export interface DraftStep {
  /** Identifies the step while typing may still extend it. */
  readonly id: number;
  /** Orders the steps of all documents: the latest change, undo or redo has the highest. */
  readonly order: number;
  readonly path: string;
  readonly kind: DraftChangeKind;
  /** The message the step changed (its canonical key), if it changed one. */
  readonly key?: string;
  /** The message's dotted path (`files.selected`), the form Undo and Redo name it by. */
  readonly message?: string;
  /** Typing in the same field (a composer pattern, the message source, the file) extends one step until a pause. */
  readonly field?: string;
  readonly before: DraftState;
  readonly after: DraftState;
  /** When the step last changed. */
  readonly at: number;
  readonly bytes: number;
}

export type DraftStepInput = Pick<DraftStep, "path" | "kind" | "key" | "message" | "field" | "before" | "after">;

export interface DraftHistoryOptions {
  now?: () => number;
  /** Steps kept per document; the oldest are dropped first. */
  maxSteps?: number;
  /** The estimated size of a document's undo and redo steps; the oldest undo steps are dropped first. */
  maxBytes?: number;
  /** Typing in one field after a longer pause starts a new step. */
  pause?: number;
}

type Stacks = { readonly undo: readonly DraftStep[]; readonly redo: readonly DraftStep[] };

const same = (left: DraftState, right: DraftState): boolean =>
  left.draft === right.draft && JSON.stringify(left.starters) === JSON.stringify(right.starters);

const stateBytes = (state: DraftState): number =>
  2 * (state.draft?.length ?? 0) +
  (state.parsed?.entries.reduce((total, entry) => total + 2 * entry.content.length + 512, 0) ?? 0) +
  2 * JSON.stringify(state.starters).length;

/**
 * The undo and redo stacks of the unsaved edits, one pair per document (#225 P4). Every committed draft change pushes a
 * step; typing in one field extends the latest step until a pause. A new step clears the document's redo stack. A
 * save, or any other change of a document's saved text, clears the document's stacks (`DraftStore`).
 */
export class DraftHistory {
  #stacks = $state.raw<ReadonlyMap<string, Stacks>>(new Map());
  /** Whether any document has a step to undo or to redo. */
  canUndo = $derived([...this.#stacks.values()].some((stacks) => stacks.undo.length > 0));
  canRedo = $derived([...this.#stacks.values()].some((stacks) => stacks.redo.length > 0));

  readonly #now: () => number;
  readonly #maxSteps: number;
  readonly #maxBytes: number;
  readonly #pause: number;
  #counter = 0;
  /** The step typing may extend; an undo, a redo or `seal` ends it. */
  #open: number | undefined;

  constructor(options: DraftHistoryOptions = {}) {
    this.#now = options.now ?? (() => Date.now());
    this.#maxSteps = options.maxSteps ?? 100;
    this.#maxBytes = options.maxBytes ?? 32 * 1024 * 1024;
    this.#pause = options.pause ?? 1000;
  }

  /**
   * Records a committed change. Typing in the step's field within the pause extends the latest step; a change that
   * restores the state the step started from removes it. A change that changed nothing records nothing.
   */
  record(input: DraftStepInput): DraftStep | undefined {
    const stacks = this.#stacks.get(input.path) ?? { undo: [], redo: [] };
    const top = stacks.undo.at(-1);
    const now = this.#now();
    if (input.field !== undefined && top !== undefined && top.field === input.field && this.#open === top.id &&
      now - top.at <= this.#pause) {
      const undo = stacks.undo.slice(0, -1);
      if (same(top.before, input.after)) {
        this.#open = undefined;
        this.#set(input.path, { undo, redo: [] });
        return undefined;
      }
      const step: DraftStep = { ...top, after: input.after, at: now, order: ++this.#counter, bytes: stateBytes(top.before) + stateBytes(input.after) };
      this.#set(input.path, this.#capped({ undo: [...undo, step], redo: [] }));
      return step;
    }
    if (same(input.before, input.after)) return undefined;
    const id = ++this.#counter;
    const step: DraftStep = { ...input, id, order: id, at: now, bytes: stateBytes(input.before) + stateBytes(input.after) };
    this.#open = input.field === undefined ? undefined : id;
    this.#set(input.path, this.#capped({ undo: [...stacks.undo, step], redo: [] }));
    return step;
  }

  peekUndo(path: string | undefined): DraftStep | undefined {
    return path === undefined ? undefined : this.#stacks.get(path)?.undo.at(-1);
  }

  peekRedo(path: string | undefined): DraftStep | undefined {
    return path === undefined ? undefined : this.#stacks.get(path)?.redo.at(-1);
  }

  /** The document Undo acts on: `preferred` (the selected one) while it has a step, else the one changed last. */
  undoTarget(preferred?: string): string | undefined {
    return this.#target(preferred, "undo");
  }

  /** The document Redo acts on: `preferred` while it has an undone step, else the one undone last. */
  redoTarget(preferred?: string): string | undefined {
    return this.#target(preferred, "redo");
  }

  /** Moves the document's latest step to its redo stack and returns it; the caller restores `before`. */
  takeUndo(path: string): DraftStep | undefined {
    const stacks = this.#stacks.get(path);
    const step = stacks?.undo.at(-1);
    if (stacks === undefined || step === undefined) return undefined;
    this.#open = undefined;
    const moved = { ...step, order: ++this.#counter };
    this.#set(path, { undo: stacks.undo.slice(0, -1), redo: [...stacks.redo, moved] });
    return moved;
  }

  /** Moves the document's latest undone step back to its undo stack and returns it; the caller restores `after`. */
  takeRedo(path: string): DraftStep | undefined {
    const stacks = this.#stacks.get(path);
    const step = stacks?.redo.at(-1);
    if (stacks === undefined || step === undefined) return undefined;
    this.#open = undefined;
    const moved = { ...step, order: ++this.#counter };
    this.#set(path, { undo: [...stacks.undo, moved], redo: stacks.redo.slice(0, -1) });
    return moved;
  }

  /** Ends the step typing would extend: the next change is a step of its own. */
  seal(): void {
    this.#open = undefined;
  }

  /** The documents with steps. */
  paths(): string[] {
    return [...this.#stacks.keys()];
  }

  depth(path: string): { undo: number; redo: number } {
    const stacks = this.#stacks.get(path);
    return { undo: stacks?.undo.length ?? 0, redo: stacks?.redo.length ?? 0 };
  }

  clear(path: string): void {
    if (!this.#stacks.has(path)) return;
    const next = new Map(this.#stacks);
    next.delete(path);
    this.#stacks = next;
  }

  clearAll(): void {
    if (this.#stacks.size > 0) this.#stacks = new Map();
    this.#open = undefined;
  }

  #target(preferred: string | undefined, stack: "undo" | "redo"): string | undefined {
    if (preferred !== undefined && (this.#stacks.get(preferred)?.[stack].length ?? 0) > 0) return preferred;
    let latest: DraftStep | undefined;
    for (const stacks of this.#stacks.values()) {
      const step = stacks[stack].at(-1);
      if (step !== undefined && (latest === undefined || step.order > latest.order)) latest = step;
    }
    return latest?.path;
  }

  #capped(stacks: Stacks): Stacks {
    const undo = [...stacks.undo];
    const redoBytes = stacks.redo.reduce((total, step) => total + step.bytes, 0);
    let bytes = redoBytes + undo.reduce((total, step) => total + step.bytes, 0);
    // The newest step is always kept, also one larger than the budget.
    while (undo.length > 1 && (undo.length > this.#maxSteps || bytes > this.#maxBytes)) bytes -= undo.shift()!.bytes;
    return { undo, redo: stacks.redo };
  }

  #set(path: string, stacks: Stacks): void {
    const next = new Map(this.#stacks);
    if (stacks.undo.length === 0 && stacks.redo.length === 0) next.delete(path);
    else next.set(path, stacks);
    this.#stacks = next;
  }
}

/**
 * Where the caret goes in a field whose text an undo or redo replaced: at the end of the changed run, so an undone
 * insertion leaves it where the insertion began and a redone one after it.
 */
export function caretAfterChange(previous: string, next: string): number {
  const shorter = Math.min(previous.length, next.length);
  let prefix = 0;
  while (prefix < shorter && previous[prefix] === next[prefix]) prefix += 1;
  let suffix = 0;
  while (suffix < shorter - prefix && previous[previous.length - 1 - suffix] === next[next.length - 1 - suffix]) suffix += 1;
  return next.length - suffix;
}
