/**
 * Structural copy of the framework-neutral Runic Translations locale contract.
 * Keeping this structural avoids making generated translations a runtime
 * dependency of the Svelte adapter.
 */
export interface LocaleSource<Locale extends string = string> {
  getLocale(): Locale;
  subscribe(listener: (locale: Locale) => void): () => void;
}

export interface MutableLocaleSource<Locale extends string = string> extends LocaleSource<Locale> {
  setLocale(locale: Locale): void;
}

export interface SvelteLocaleOptions<Locale extends string> {
  /**
   * Requests a locale change when the source itself is not authoritative.
   * A SvelteKit application normally uses this to navigate to a localized URL;
   * the navigation then updates the source.
   */
  readonly requestLocale?: (locale: Locale) => void | Promise<void>;
}

export class SvelteLocale<Locale extends string = string> {
  #locale: Locale;
  #disposed = $state(false);
  readonly #source: LocaleSource<Locale>;
  readonly #requestLocale: ((locale: Locale) => void | Promise<void>) | undefined;
  #unsubscribe: (() => void) | undefined;

  constructor(source: LocaleSource<Locale>, options: SvelteLocaleOptions<Locale> = {}) {
    this.#source = source;
    this.#requestLocale = options.requestLocale;
    this.#locale = $state.raw(source.getLocale());
    this.#unsubscribe = source.subscribe((locale) => {
      if (!this.#disposed) this.#locale = locale;
    });
  }

  get locale(): Locale {
    return this.#locale;
  }

  get disposed(): boolean {
    return this.#disposed;
  }

  /** A fresh object suitable for the generated message function's options argument. */
  get messageOptions(): Readonly<{ locale: Locale }> {
    return { locale: this.#locale };
  }

  /** Pulls the authoritative value after an external settings or navigation change. */
  refresh(): Locale {
    this.#assertActive();
    this.#locale = this.#source.getLocale();
    return this.#locale;
  }

  async setLocale(locale: Locale): Promise<void> {
    this.#assertActive();
    if (this.#requestLocale) {
      await this.#requestLocale(locale);
    } else if (isMutableLocaleSource(this.#source)) {
      this.#source.setLocale(locale);
    } else {
      throw new Error(
        "This locale source is read-only. Provide requestLocale to change it through application settings or navigation.",
      );
    }
    // Navigation can replace and dispose the providing layout before its
    // Promise settles. The destination owns the next locale state in that case.
    if (!this.#disposed) this.refresh();
  }

  dispose(): void {
    if (this.#disposed) return;
    this.#disposed = true;
    this.#unsubscribe?.();
    this.#unsubscribe = undefined;
  }

  #assertActive(): void {
    if (this.#disposed) throw new Error("The Svelte locale state is disposed.");
  }
}

export function createSvelteLocale<Locale extends string>(
  source: LocaleSource<Locale>,
  options: SvelteLocaleOptions<Locale> = {},
): SvelteLocale<Locale> {
  return new SvelteLocale(source, options);
}

function isMutableLocaleSource<Locale extends string>(
  source: LocaleSource<Locale>,
): source is MutableLocaleSource<Locale> {
  return "setLocale" in source && typeof source.setLocale === "function";
}
