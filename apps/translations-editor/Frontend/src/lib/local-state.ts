/**
 * One native, per-user store owns preferences, recents, and recovery drafts.
 * The browser receives an in-memory projection only; it never writes these
 * records to an origin/profile-owned storage area.
 */
export interface LocalStateSummary {
  readonly entries: number;
  readonly bytes: number;
  readonly preferenceEntries: number;
  readonly recentProjectEntries: number;
  readonly draftEntries: number;
  readonly recovered: boolean;
}

const exactKeys = new Set([
  "runic-translations.theme-mode",
  "runic-translations.theme-palette",
  "runic-translations.pseudo-localization",
  "runic-translations.ui-direction",
  "runic-translations.ui-locale",
  "runic-translations:recent:1",
  "runic.sidebar.languages",
  "runic.sidebar.messages",
  "runic.sidebar.languages-share",
]);
const draftPrefix = "runic-translations:drafts:1:";
const values = new Map<string, string>();
const pendingKeys = new Set<string>();
const listeners = new Set<() => void>();
let initialized: Promise<boolean> | undefined;
// The queue of native writes. It never rejects: a failed write is kept as `unsaved` instead, so a write that a reload
// or a lost host interrupts cannot surface as an unhandled rejection.
let persistence = Promise.resolve();
// The projection holds changes the native record may not have yet: set by every change, cleared when a write starts,
// and set again when that write fails.
let unsaved = false;
let lastFailure: unknown;
let recovered = false;
let bridge: LocalStateBridge | undefined;

export interface LocalStateBridge {
  loadLocalState(): Promise<{ entries: readonly { key: string; value: string }[]; recovered: boolean }>;
  saveLocalState(entries: { key: string; value: string }[]): Promise<{ recovered: boolean }>;
  clearLocalState(): Promise<{ removedEntries: number; recovered: boolean }>;
}

/** Configures the one typed native bridge that owns this state. */
export function configureLocalEditorState(next: LocalStateBridge): void {
  if (bridge !== undefined && bridge !== next) {
    throw new Error("The local editor state is already bound to a different bridge.");
  }
  bridge = next;
}

/** Loads the application-owned native record before preferences are read. */
export async function loadLocalEditorState(): Promise<boolean> {
  if (initialized !== undefined) return initialized;
  initialized = requireBridge().loadLocalState().then((state) => {
    // A user can interact while the bridge handshake is completing. Preserve
    // those local edits over the just-loaded snapshot, then queue their full
    // atomic replacement behind this initialization.
    const pending = new Map([...values].filter(([key]) => pendingKeys.has(key)));
    values.clear();
    for (const entry of state.entries) values.set(entry.key, entry.value);
    for (const [key, value] of pending) values.set(key, value);
    pendingKeys.clear();
    recovered = state.recovered;
    notify();
    return state.recovered;
  });
  return initialized;
}

/** Gets one current value from the in-memory projection of native state. */
export function getLocalEditorState(key: string): string | null {
  return values.get(key) ?? null;
}

/**
 * Updates the projection and serializes a complete native replacement. Writes
 * are queued so rapid edits cannot publish an older snapshot after a newer one.
 */
export function setLocalEditorState(key: string, value: string): void {
  requireOwnedKey(key);
  values.set(key, value);
  pendingKeys.add(key);
  queuePersist();
  notify();
}

/** Removes one application-owned value and publishes the resulting snapshot. */
export function removeLocalEditorState(key: string): void {
  requireOwnedKey(key);
  if (!values.delete(key)) return;
  pendingKeys.add(key);
  queuePersist();
  notify();
}

/** Lets small leaf components refresh after the asynchronous native load. */
export function subscribeLocalEditorState(listener: () => void): () => void {
  listeners.add(listener);
  return () => listeners.delete(listener);
}

/** Returns a privacy-bounded inventory; it never exposes values to the UI. */
export function inspectLocalEditorState(): LocalStateSummary {
  let bytes = 0;
  let preferenceEntries = 0;
  let recentProjectEntries = 0;
  let draftEntries = 0;
  for (const [key, value] of values) {
    bytes += byteLength(key) + byteLength(value);
    if (key === "runic-translations:recent:1") recentProjectEntries++;
    else if (key.startsWith(draftPrefix)) draftEntries++;
    else preferenceEntries++;
  }
  return {
    entries: values.size,
    bytes,
    preferenceEntries,
    recentProjectEntries,
    draftEntries,
    recovered,
  };
}

/** Clears only editor-owned native state, never workspace files or browser data. */
export async function clearLocalEditorState(): Promise<number> {
  // A clear is ordered after any interaction already queued by the UI; without
  // this, a late save could recreate a record the user explicitly removed.
  await persistence;
  const result = await requireBridge().clearLocalState();
  values.clear();
  pendingKeys.clear();
  unsaved = false;
  recovered = result.recovered;
  notify();
  return result.removedEntries;
}

/**
 * Waits for queued native persistence; useful before an orderly host shutdown. Rejects with the last failure when the
 * native record is still behind the projection afterwards.
 */
export async function flushLocalEditorState(): Promise<void> {
  await persistence;
  if (unsaved) throw lastFailure ?? new Error("The local editor state has not been written yet.");
}

/**
 * The host connection opened (again): writes the projection once more when a lost connection interrupted its last
 * write. The host may have applied that write before the reply was lost; a complete replacement is safe to repeat.
 */
export function resumeLocalEditorState(): void {
  if (unsaved) queuePersist();
}

function queuePersist(): void {
  unsaved = true;
  persistence = persistence.then(persist);
}

async function persist(): Promise<void> {
  try {
    // A failed load leaves the native record unknown; replacing it with this projection alone would drop the
    // entries the load could not read, so the change stays in memory only.
    await initialized;
  } catch (error) {
    lastFailure = error;
    return;
  }
  unsaved = false;
  const entries = [...values]
    .sort(([left], [right]) => left.localeCompare(right))
    .map(([key, value]) => ({ key, value }));
  try {
    const state = await requireBridge().saveLocalState(entries);
    recovered = state.recovered;
  } catch (error) {
    // A reload, navigation or closed window ends the Bridge while the write is in flight. The next change, or
    // `resumeLocalEditorState` after a reconnect, writes the projection again.
    unsaved = true;
    lastFailure = error;
    console.warn("The editor could not save its local state; it is saved again when the host reconnects.", error);
  }
}

function requireOwnedKey(key: string): void {
  if (!exactKeys.has(key) && !key.startsWith(draftPrefix)) {
    throw new Error("Attempted to persist state that is not owned by the translations editor.");
  }
}

function requireBridge(): LocalStateBridge {
  if (bridge === undefined) throw new Error("The local editor state has not been bound to the native bridge.");
  return bridge;
}

function notify(): void {
  for (const listener of listeners) listener();
}

function byteLength(value: string): number {
  return new TextEncoder().encode(value).byteLength;
}
