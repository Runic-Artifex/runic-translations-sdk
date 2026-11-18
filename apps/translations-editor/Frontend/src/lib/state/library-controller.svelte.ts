import type { EditorLibraryResult, EditorLibraryView } from "../contracts";
import type { EditorBridge } from "../editor-bridge";
import { errorNotice, notice, type UiNotice } from "../ui-text";

export interface LibraryControllerOptions {
  bridge: Pick<EditorBridge,
    | "loadLibrary" | "overrideLibraryMessage" | "saveLibraryOverride" | "revertLibraryOverride"
    | "keepLibraryOverride" | "addLibraryLocale">;
  hasUnsavedWork: () => boolean;
  /** runic.json changed on disk: with unsaved work the reload waits and the page says so, else it reloads now. */
  projectChanged: (deferred: boolean) => Promise<void>;
}

/**
 * The library catalogs dialog (T5d): a library is layer 0 and read-only, the application's overrides of its messages
 * are layer 1. Every write is checked against the revision the dialog loaded.
 */
export class LibraryController {
  open = $state(false);
  busy = $state(false);
  view = $state.raw<EditorLibraryView>();
  notice = $state<UiNotice>();
  /** A write was refused because a file changed since the dialog loaded it: the dialog keeps its drafts. */
  conflict = $state(false);
  locale = $state("");

  readonly #options: LibraryControllerOptions;

  constructor(options: LibraryControllerOptions) {
    this.#options = options;
  }

  /** Opens the dialog on the library `catalog`. */
  async openLibrary(catalog: string): Promise<void> {
    this.open = true;
    await this.run(() => this.#options.bridge.loadLibrary(catalog));
  }

  /**
   * Runs a library operation; resolves whether it succeeded. A refused write (a file changed since the dialog loaded
   * it) keeps the dialog's drafts and offers a reload.
   */
  async run(operation: () => Promise<EditorLibraryResult>): Promise<boolean> {
    if (this.busy) return false;
    this.busy = true;
    this.notice = undefined;
    this.conflict = false;
    try {
      const result = await operation();
      if (!result.ok) {
        this.notice = result.message ?? notice("ui_backend_external_error");
        this.conflict = result.conflict === true;
      }
      if (result.library) {
        this.view = result.library;
        // The canonical tag the backend wrote (fr-ca → fr-CA), else the current one if it still exists.
        if (result.locale && result.library.locales.some((locale) => locale.tag === result.locale)) this.locale = result.locale;
        else if (!result.library.locales.some((locale) => locale.tag === this.locale)) this.locale = result.library.locales[0]?.tag ?? "";
      }
      // The catalog joined runic.json "overrides": the project changed on disk. With unsaved work the reload waits, and
      // the page says so.
      if (result.projectChanged) await this.#options.projectChanged(this.#options.hasUnsavedWork());
      return result.ok;
    } catch (error) {
      this.notice = errorNotice(error);
      return false;
    } finally {
      this.busy = false;
    }
  }

  select(catalog: string): void {
    void this.run(() => this.#options.bridge.loadLibrary(catalog));
  }

  override(key: string, locale: string, text: string, expectedRevision: string): Promise<boolean> {
    return this.run(() => this.#options.bridge.overrideLibraryMessage({ catalog: this.view!.catalog, key, locale, text, expectedRevision }));
  }

  saveOverride(key: string, locale: string, text: string, expectedRevision: string): Promise<boolean> {
    return this.run(() => this.#options.bridge.saveLibraryOverride({ catalog: this.view!.catalog, key, locale, text, expectedRevision }));
  }

  revert(key: string, locale: string, expectedRevision: string): void {
    void this.run(() => this.#options.bridge.revertLibraryOverride({ catalog: this.view!.catalog, key, locale, expectedRevision }));
  }

  keep(key: string, locale: string, expectedRevision: string): void {
    void this.run(() => this.#options.bridge.keepLibraryOverride({ catalog: this.view!.catalog, key, locale, expectedRevision }));
  }

  addLocale(locale: string): void {
    void this.run(() => this.#options.bridge.addLibraryLocale(this.view!.catalog, locale));
  }

  reload(): void {
    void this.run(() => this.#options.bridge.loadLibrary(this.view!.catalog));
  }
}
