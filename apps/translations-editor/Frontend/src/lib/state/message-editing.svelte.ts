import { tick } from "svelte";
import type { EditorDiagnostic, EditorDiagnosticQuickFix, EditorMessageOperation, WorkspaceSnapshot } from "../contracts";
import { placeDiagnostics, type DiagnosticSources } from "../diagnostic-scope";
import type { EditorBridge } from "../editor-bridge";
import { formatJson, type ResourceValue } from "../resource-model";
import { errorNotice, notice } from "../ui-text";
import type { DiagnosticsStore } from "./diagnostics-store.svelte";
import type { DraftOutcome, DraftStore } from "./draft-store.svelte";
import type { EditorFeedback } from "./feedback.svelte";
import type { MessageDraftStore, StarterOutcome } from "./message-draft-store.svelte";
import type { PreviewController } from "./preview-controller.svelte";
import type { RepairController } from "./repair-controller.svelte";
import type { SelectionController } from "./selection-controller.svelte";

export interface MessageEditingOptions {
  bridge: Pick<EditorBridge, "transformDocument">;
  snapshot: () => WorkspaceSnapshot | undefined;
  selection: SelectionController;
  drafts: DraftStore;
  messageDrafts: MessageDraftStore;
  diagnostics: DiagnosticsStore;
  preview: PreviewController;
  repair: RepairController;
  feedback: EditorFeedback;
  /** What the diagnostics of the workspace point into, to find the message a diagnostic is about. */
  diagnosticSources: () => DiagnosticSources;
  /** Whether an operation in progress (a save, a reload, a recovery) keeps the editor from changing. */
  editingBlocked: () => boolean;
}

/**
 * The edits of the selected message: typed text in the composer or the file view, the composer's authoring
 * operations, the starter of a missing translation and "Copy source", the quick fixes of diagnostics, and the
 * navigation from a diagnostic to its span.
 */
export class MessageEditingController {
  readonly #options: MessageEditingOptions;

  constructor(options: MessageEditingOptions) {
    this.#options = options;
  }

  /** Reports a composer edit's change of a document's draft and refreshes the preview of the message it changed. */
  #transformDone(path: string, key: string, locale: string): (outcome: DraftOutcome) => void {
    const { selection, feedback } = this.#options;
    return (outcome) => {
      if (outcome.kind === "failed") {
        feedback.clientError = errorNotice(outcome.error);
        return;
      }
      const result = outcome.result;
      if (outcome.kind === "rejected") {
        feedback.clientError = result.diagnostics[0]?.notice ?? result.diagnostics[0]?.message;
        return;
      }
      this.#options.diagnostics.record("transform", path, result);
      if (selection.selectedDocumentPath === path && selection.selectedKey === key && selection.selectedLocale === locale) {
        this.#options.preview.schedule(path, result.content, key, locale);
      }
    };
  }

  #starterDone = (outcome: StarterOutcome): void => {
    const { feedback } = this.#options;
    if (outcome.kind === "failed") feedback.clientError = errorNotice(outcome.error);
    else if (outcome.kind === "rejected") {
      const diagnostic = outcome.result.diagnostics.find((candidate) => candidate.id === "EDITOR-AUTHORING") ?? outcome.result.diagnostics[0];
      feedback.clientError = diagnostic?.notice ?? diagnostic?.message;
    }
  };

  /** "Copy source": the starter of the missing translation gets the source text, variant by variant. */
  copySource(): void {
    const { selection, feedback } = this.#options;
    const target = selection.currentStarterTarget;
    const sourceAuthoring = selection.sourceAuthoring;
    if (this.#options.editingBlocked() || target === undefined || selection.currentStarter === undefined || sourceAuthoring?.supported !== true) return;
    feedback.clear();
    void this.#options.messageDrafts.copySource(target, sourceAuthoring.variants, this.#starterDone, this.#transformDone(target.path, target.key, target.locale));
  }

  /** Text typed in the file view (raw mode), or in the composer's message source. */
  edit(value: string): void {
    const { selection, drafts, feedback, diagnostics } = this.#options;
    if (this.#options.editingBlocked()) return;
    if (selection.mode !== "raw") {
      this.editResourceValue(value);
      return;
    }
    selection.editorText = value;
    feedback.clear();
    const document = selection.currentDocument;
    if (document === undefined) {
      feedback.clientError = notice("ui_feedback_no_resource");
      return;
    }
    try {
      const before = drafts.capture(document.path);
      drafts.set(document.path, value);
      drafts.persist();
      // The file's own text now decides what its missing translations hold.
      this.#options.messageDrafts.retain((target) => target.path !== document.path);
      // Typing in the file view is one step per pause, like typing in the composer.
      drafts.recordStep(document.path, { kind: "file-edit", field: `${document.path}\u0000file` }, before);
      drafts.validate(document.path, value, (outcome) => {
        if (outcome.kind === "applied") diagnostics.record("validation", document.path, outcome.result);
        else if (outcome.kind === "failed") feedback.clientError = errorNotice(outcome.error);
      });
    } catch (error) {
      feedback.clientError = errorNotice(error);
      diagnostics.record("edit", document.path, { success: false, diagnostics: [] });
    }
  }

  /** The message's new MF2 source from the composer. */
  editResourceValue(resourceValue: ResourceValue): void {
    const { selection, feedback } = this.#options;
    if (this.#options.editingBlocked()) return;
    if (typeof resourceValue !== "string") return;
    selection.editorText = resourceValue;
    feedback.clear();
    const document = selection.currentDocument;
    if (document === undefined) {
      feedback.clientError = notice("ui_feedback_no_resource");
      return;
    }
    const path = document.path;
    const key = selection.selectedKey;
    const locale = selection.selectedLocale;
    const target = selection.currentStarterTarget;
    // MF2 source typed for a starter: it goes into the document unless it is empty or the untouched starter.
    if (target !== undefined && selection.currentStarter !== undefined) {
      this.#options.messageDrafts.edit(target, resourceValue, this.#transformDone(path, key, locale));
      return;
    }
    void this.#options.drafts.transform(path, document.content, key, resourceValue, this.#transformDone(path, key, locale));
  }

  /** A structural change from the composer (a variant, a selector, a placeholder). */
  applyAuthoringOperation(operation: EditorMessageOperation): Promise<void> {
    const { selection, feedback, diagnostics } = this.#options;
    if (this.#options.editingBlocked() || selection.currentDocument === undefined) return Promise.resolve();
    feedback.clear();
    const target = selection.currentStarterTarget;
    if (target !== undefined && selection.currentStarter !== undefined) {
      return this.#options.messageDrafts.apply(target, operation, this.#starterDone, this.#transformDone(target.path, target.key, target.locale));
    }
    const document = selection.currentDocument;
    const path = document.path;
    const key = selection.selectedKey;
    const locale = selection.selectedLocale;
    return this.#options.drafts.applyOperation(path, document.content, key, locale, operation, (outcome) => {
      if (outcome.kind === "failed") {
        feedback.clientError = errorNotice(outcome.error);
        return;
      }
      const result = outcome.result;
      if (outcome.kind === "rejected") {
        feedback.clientError = result.diagnostics.find(diagnostic => diagnostic.id === "EDITOR-AUTHORING")?.message;
        diagnostics.record("authoring", path, result);
        return;
      }
      diagnostics.record("authoring", path, result);
      if (selection.selectedDocumentPath === path && selection.selectedKey === key && selection.selectedLocale === locale) {
        selection.editorText = result.entries.find((candidate) => candidate.key === key)?.content ?? selection.editorText;
        this.#options.preview.schedule(path, result.content, key, locale);
      }
    });
  }

  /** A translation-memory suggestion becomes the message's text. */
  applySuggestion(value: string): void {
    this.#options.selection.mode = "translation";
    this.editResourceValue(value);
  }

  /** Formats the file view's JSON as a step of its own. */
  formatRaw(): void {
    const { selection, drafts } = this.#options;
    if (this.#options.editingBlocked()) return;
    if (selection.mode !== "raw") return;
    try {
      const formatted = formatJson(selection.editorText);
      // Formatting is a step of its own, not part of the typing before or after it.
      drafts.history.seal();
      this.edit(formatted);
      drafts.history.seal();
    } catch (error) {
      this.#options.feedback.clientError = errorNotice(error);
    }
  }

  /**
   * Shows where a diagnostic points: its message and locale are selected and its span is selected in the file view,
   * or in the repair dialog for a malformed file. A diagnostic of older text is located in the current text first.
   */
  async selectDiagnostic(diagnostic: EditorDiagnostic): Promise<void> {
    const { selection, drafts, repair, feedback } = this.#options;
    const document = this.#options.snapshot()?.documents.find((candidate) => candidate.path === diagnostic.path);
    if (document === undefined) return;
    // The message the diagnostic is about becomes the selected one, so the panel lists it under that message.
    const about = placeDiagnostics([diagnostic], this.#options.diagnosticSources())[0];
    const workspaceVersion = drafts.workspaceGeneration;
    const content = repair.document?.path === document.path ? repair.text : drafts.content(document.path, document.content);
    const digest = await crypto.subtle.digest("SHA-256", new TextEncoder().encode(content));
    const revision = Array.from(new Uint8Array(digest), byte => byte.toString(16).padStart(2, "0")).join("");
    let current = diagnostic;
    if (diagnostic.sourceRevision !== revision) {
      // Started without an awaiting caller (`void selectDiagnostic(…)`): a failed request is reported, not left unhandled.
      const result = await this.#options.bridge.transformDocument(document.path, content).catch((error: unknown) => {
        if (drafts.workspaceGeneration !== workspaceVersion) return undefined;
        if (repair.document?.path === document.path) repair.message = errorNotice(error);
        else feedback.clientError = errorNotice(error);
        return undefined;
      });
      if (result === undefined || drafts.workspaceGeneration !== workspaceVersion) return;
      current = result.diagnostics.find(candidate => candidate.id === diagnostic.id && candidate.message === diagnostic.message) ?? diagnostic;
      // A removed diagnostic cannot navigate with stale source coordinates.
      if (current.sourceRevision !== revision) return;
      const latestContent = repair.document?.path === document.path ? repair.text : drafts.content(document.path, document.content);
      if (latestContent !== content) return;
      this.#options.diagnostics.record("navigation", document.path, result);
    }
    if (drafts.workspaceGeneration !== workspaceVersion || current.span === undefined) return;
    const liveContent = repair.document?.path === document.path ? repair.text : drafts.content(document.path, document.content);
    if (liveContent !== content) return;
    if (document.locale !== undefined) selection.selectedLocale = document.locale;
    // A diagnostic located in another locale's file (RTR0090 points at the base text) still selects its message there.
    if (about?.key !== undefined && selection.rows.some((row) => row.key === about.key)) selection.selectedKey = about.key;
    selection.selectedDocumentPath = document.path;
    selection.selectedDocumentViewState = undefined;
    if (document.isMalformed) {
      if (repair.document?.path !== document.path) repair.begin(document);
      await tick();
      if (repair.input !== null && repair.text === content) {
        repair.input.focus();
        repair.input.setSelectionRange(current.span.startUtf16, current.span.startUtf16 + current.span.lengthUtf16);
      }
    } else {
      selection.mode = "raw";
      selection.editorText = content;
      selection.revealSource(current.span.startUtf16, current.span.startUtf16 + current.span.lengthUtf16);
    }
  }

  /** Applies a diagnostic's quick fix to the document's draft, or to the repair text of a malformed file. */
  async applyDiagnosticFix(diagnostic: EditorDiagnostic, fix: EditorDiagnosticQuickFix): Promise<void> {
    const { selection, repair, feedback } = this.#options;
    const document = this.#options.snapshot()?.documents.find(candidate => candidate.path === diagnostic.path);
    if (document === undefined || this.#options.editingBlocked()) return;
    const path = document.path;
    const repairing = repair.document?.path === path;
    await this.#options.drafts.applyFix(path, document.content, fix, repairing ? { text: () => repair.text } : undefined, (outcome) => {
      if (outcome.kind === "failed") {
        if (repairing) repair.message = errorNotice(outcome.error); else feedback.clientError = errorNotice(outcome.error);
        return;
      }
      const result = outcome.result;
      if (outcome.kind === "rejected") {
        const message = result.diagnostics.find(diagnostic => diagnostic.id === "EDITOR-FIX")?.message;
        if (repairing) repair.message = message;
        else feedback.clientError = message;
        return;
      }
      if (repairing) {
        repair.text = result.content;
        repair.validation = result;
      } else {
        this.#options.diagnostics.record("quick-fix", path, result);
        if (selection.selectedDocumentPath === path) {
          selection.editorText = selection.mode === "raw" ? result.content : result.entries.find(entry => entry.key === selection.selectedKey)?.content ?? selection.editorText;
          if (selection.mode === "translation") this.#options.preview.schedule(path, result.content, selection.selectedKey, selection.selectedLocale);
        }
      }
    }, diagnostic.key);
  }
}
