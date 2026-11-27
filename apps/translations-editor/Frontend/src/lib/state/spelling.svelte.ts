import { getContext, setContext } from "svelte";
import type { EditorMisspelling, EditorSpellingLanguage } from "../contracts";
import type { EditorBridge } from "../editor-bridge";

/** Whether the editor checks spelling: `on` (the default) or `off`. */
export const spellingEnabledKey = "runic-translations.spelling";
/** Whether the system language's missing dictionary downloads without asking (D086.2): `on` (the default) or `off`. */
export const spellingDownloadKey = "runic-translations.spelling-download";

export type SpellingBridge = Pick<EditorBridge, "spellingStatus" | "checkSpelling" | "suggestSpelling" | "downloadSpellingDictionary" | "addSpellingWord">;
export type SpellingTextKind = "pattern" | "message";

/** What the composer says about a language's dictionary. */
export type SpellingNotice =
  | { kind: "offer"; locale: string; size: number; license?: string }
  | { kind: "downloading"; locale: string; automatic: boolean; percent: number }
  | { kind: "failed"; locale: string };

export interface SpellingControllerOptions {
  bridge: SpellingBridge;
  storage: { get(key: string): string | null; set(key: string, value: string): void };
  /** The interface locale, which names languages and formats sizes. */
  uiLocale: () => string;
  /** How long typing pauses before a text is checked. */
  delay?: number;
  /** How often a running download's progress is asked for. */
  pollInterval?: number;
}

const maximumCachedTexts = 600;

/**
 * The editor's own spell checking (D074.10): the host checks each text with the dictionary of its language, without
 * placeholders and markup, and the pattern box and the composer draw the marks; the webview's checker is off. The
 * system language's dictionary downloads on its own unless the setting is off; any other language is offered first
 * (D086.2).
 */
export class SpellingController {
  enabled = $state(true);
  automaticDownload = $state(true);
  /** What checks each of the project's languages, by locale. */
  languages = $state.raw<Readonly<Record<string, EditorSpellingLanguage>>>({});
  /** Languages whose download the user declined in this session. */
  declined = $state.raw<ReadonlySet<string>>(new Set());

  readonly #options: SpellingControllerOptions;
  #results = $state.raw<ReadonlyMap<string, readonly EditorMisspelling[]>>(new Map());
  readonly #queued = new Map<string, { locale: string; kind: SpellingTextKind; text: string }>();
  readonly #inFlight = new Set<string>();
  #locales: string[] = [];
  #restored = $state(false);
  #timer: ReturnType<typeof setTimeout> | undefined;
  #poll: ReturnType<typeof setTimeout> | undefined;

  constructor(options: SpellingControllerOptions) {
    this.#options = options;
  }

  /** The interface locale, which names languages and formats sizes. */
  get uiLocale(): string {
    return this.#options.uiLocale();
  }

  /**
   * Reads the stored settings and starts checking; nothing is checked or downloaded before. Without the stored
   * settings (`loaded` false: the native state did not load) nothing downloads on its own in this session, since the
   * user may have turned that off.
   */
  restore(loaded = true): void {
    this.enabled = loaded ? this.#options.storage.get(spellingEnabledKey) !== "off" : true;
    this.automaticDownload = loaded && this.#options.storage.get(spellingDownloadKey) !== "off";
    this.#restored = true;
    void this.refresh();
  }

  setEnabled(enabled: boolean): void {
    this.enabled = enabled;
    this.#store(spellingEnabledKey, enabled);
    if (enabled) void this.refresh();
  }

  setAutomaticDownload(automatic: boolean): void {
    this.automaticDownload = automatic;
    this.#store(spellingDownloadKey, automatic);
    if (automatic) void this.refresh();
  }

  /** The project's languages changed: asks what checks them. */
  setLocales(locales: readonly string[]): void {
    const next = [...new Set(locales.filter((locale) => locale !== ""))].sort();
    if (next.join("\n") === this.#locales.join("\n")) return;
    this.#locales = next;
    void this.refresh();
  }

  /** Asks the host what checks each language; starts the system language's download when that is allowed. */
  async refresh(): Promise<void> {
    if (!this.#restored || !this.enabled || this.#locales.length === 0) return;
    try {
      const status = await this.#options.bridge.spellingStatus(this.#locales, this.automaticDownload);
      this.#update(status.languages);
    } catch {
      // Without the host the page shows no marks; the next change asks again.
    }
  }

  /**
   * The misspelled words of `text` in `locale`, as UTF-16 offsets. A text not checked yet has none until its result
   * arrives; asking queues the check.
   */
  marks(locale: string, text: string, kind: SpellingTextKind = "pattern"): readonly EditorMisspelling[] {
    if (!this.#restored || !this.enabled || locale === "" || text.trim() === "") return [];
    const language = this.languages[locale];
    if (language !== undefined && language.state !== "ready") return [];
    const key = cacheKey(locale, kind, text);
    const result = this.#results.get(key);
    if (result !== undefined) return result;
    if (!this.#inFlight.has(key) && !this.#queued.has(key)) {
      this.#queued.set(key, { locale, kind, text });
      this.#schedule();
    }
    return [];
  }

  async suggest(locale: string, word: string): Promise<readonly string[]> {
    try {
      return (await this.#options.bridge.suggestSpelling(locale, word)).suggestions;
    } catch {
      return [];
    }
  }

  /** Adds `word` to the user's own word list, which every Runic application shares, and checks the texts again. */
  async addWord(word: string): Promise<void> {
    await this.#options.bridge.addSpellingWord(word);
    this.#results = new Map();
  }

  /** The user asked for `locale`'s dictionary. */
  async download(locale: string): Promise<void> {
    const language = await this.#options.bridge.downloadSpellingDictionary(locale);
    this.#update([language]);
  }

  /** The user declined `locale`'s dictionary for this session. */
  decline(locale: string): void {
    this.declined = new Set([...this.declined, locale]);
  }

  /** What the composer says about `locale`'s dictionary, if anything. */
  notice(locale: string): SpellingNotice | undefined {
    if (!this.enabled) return undefined;
    const language = this.languages[locale];
    switch (language?.state) {
      case "downloading": {
        const total = language.downloadSize ?? 0;
        const percent = total > 0 ? Math.min(100, Math.round(((language.bytesReceived ?? 0) / total) * 100)) : 0;
        return { kind: "downloading", locale, automatic: language.automatic, percent };
      }
      case "failed": return { kind: "failed", locale };
      case "downloadable":
        // The system language's dictionary is on its way (the next status says so), unless the setting is off.
        if (language.systemLanguage && this.automaticDownload) return undefined;
        if (this.declined.has(locale)) return undefined;
        return { kind: "offer", locale, size: language.downloadSize ?? 0, license: language.license };
      default: return undefined;
    }
  }

  dispose(): void {
    clearTimeout(this.#timer);
    clearTimeout(this.#poll);
  }

  #update(languages: readonly EditorSpellingLanguage[]): void {
    const next = { ...this.languages };
    let installed = false;
    for (const language of languages) {
      const before = next[language.locale]?.state;
      if (language.state === "ready" && before !== undefined && before !== "ready") installed = true;
      next[language.locale] = language;
    }
    this.languages = next;
    // Texts checked without a dictionary are checked again with the new one.
    if (installed) this.#results = new Map();
    clearTimeout(this.#poll);
    if (Object.values(next).some((language) => language.state === "downloading"))
      this.#poll = setTimeout(() => void this.refresh(), this.#options.pollInterval ?? 500);
  }

  #store(key: string, on: boolean): void {
    try {
      this.#options.storage.set(key, on ? "on" : "off");
    } catch {
      // Without the native state (a test page), the setting lasts for the session only.
    }
  }

  #schedule(): void {
    if (this.#timer !== undefined) return;
    this.#timer = setTimeout(() => {
      this.#timer = undefined;
      void this.#flush();
    }, this.#options.delay ?? 150);
  }

  async #flush(): Promise<void> {
    const batches = new Map<string, { locale: string; kind: SpellingTextKind; entries: [string, string][] }>();
    for (const [key, { locale, kind, text }] of this.#queued) {
      const batchKey = `${locale}\u0000${kind}`;
      const batch = batches.get(batchKey) ?? { locale, kind, entries: [] };
      batch.entries.push([key, text]);
      batches.set(batchKey, batch);
      this.#inFlight.add(key);
    }
    this.#queued.clear();
    for (const { locale, kind, entries } of batches.values()) {
      try {
        const result = await this.#options.bridge.checkSpelling(locale,
          entries.map(([key, text]) => ({ id: key, text, kind })));
        if (result.state !== "ready") {
          // The language has no dictionary (yet): its state decides what the composer offers.
          void this.refresh();
          continue;
        }
        const next = new Map(this.#results);
        for (const item of result.results) next.set(item.id, item.misspellings);
        while (next.size > maximumCachedTexts) next.delete(next.keys().next().value!);
        this.#results = next;
      } catch {
        // A lost host leaves the texts unmarked; a later edit asks again.
      } finally {
        for (const [key] of entries) this.#inFlight.delete(key);
      }
    }
  }
}

function cacheKey(locale: string, kind: SpellingTextKind, text: string): string {
  return `${locale}\u0000${kind}\u0000${text}`;
}

const spellingContext = Symbol("spelling");

export function setSpelling(controller: SpellingController): void {
  setContext(spellingContext, controller);
}

/** The page's spell checking; `undefined` where a component is rendered without it. */
export function getSpelling(): SpellingController | undefined {
  return getContext<SpellingController | undefined>(spellingContext);
}

/** The marks of `marks` that lie in `[start, start + length)`, relative to `start`. */
export function marksWithin(marks: readonly EditorMisspelling[], start: number, length: number): EditorMisspelling[] {
  return marks
    .filter((mark) => mark.start >= start && mark.start + mark.length <= start + length)
    .map((mark) => ({ ...mark, start: mark.start - start }));
}

/** The mark at or around `caret`, the word a suggestion would replace. */
export function markAt(marks: readonly EditorMisspelling[], caret: number): EditorMisspelling | undefined {
  return marks.find((mark) => caret >= mark.start && caret <= mark.start + mark.length);
}
