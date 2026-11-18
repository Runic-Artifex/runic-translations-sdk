import type { EditorMessagePreview } from "../contracts";
import type { EditorBridge } from "../editor-bridge";
import type { MessageArtifact, MessagePreviewResult } from "../message-composer";
import {
  createMessagePreviewOwnership,
  createMessagePreviewRequest,
  createMessagePreviewScheduler,
  createPreviewSamples,
  parseRenderedMessagePreview,
  routeMessagePreview,
  withPreviewSample,
} from "../message-preview.js";
import type { PseudoLocale } from "../simulation";
import { notice, type UiMessage } from "../ui-text";

export type PreviewRequest = ReturnType<typeof createMessagePreviewRequest>;

/**
 * Why a preview failed, for the previewed message: its primary diagnostic (its own error, not the first of the whole
 * catalog), else the first error, else the generic failure.
 */
export function previewFailure(result: EditorMessagePreview | undefined): UiMessage {
  const diagnostic = result?.primaryDiagnostic ?? result?.diagnostics.find(item => item.severity === "error");
  return diagnostic?.notice ?? (diagnostic?.message || undefined) ?? notice("ui_feedback_preview_failed");
}

export interface PreviewControllerOptions {
  bridge: Pick<EditorBridge, "previewMessage">;
  /** Whether the pseudo-locale simulation is on: the preview then also renders the pseudo-locale. */
  pseudoLocalization: () => boolean;
  /** The sample value of an input the message declares without one. */
  defaultSample: (type: string) => string;
  errorNotice: (error: unknown) => UiMessage;
  setTimer?: (callback: () => void, delay: number) => unknown;
  clearTimer?: (handle: unknown) => void;
}

/**
 * The compiler-rendered preview of the selected message: its sample inputs, the requested formatting locale and time
 * zone, the debounced requests, and the pseudo-locale rendering of the simulation.
 */
export class PreviewController {
  busy = $state(false);
  error = $state<UiMessage>();
  ast = $state.raw<MessageArtifact>();
  samples = $state.raw<Record<string, string>>(createPreviewSamples());
  result = $state.raw<MessagePreviewResult>();
  request = $state.raw<PreviewRequest>();
  // The preview's formatting: the locale a user requests (de-CH for de text) and the zone timeZone=local formats in.
  requestedLocale = $state("");
  timeZone = $state("");
  // The pseudo-locale the simulation previews, rendered by the host's SDK generator (en-XA unless chosen in the sidebar).
  pseudoLocale = $state<PseudoLocale>("en-XA");
  pseudoResult = $state.raw<MessagePreviewResult>();
  pseudoError = $state<UiMessage>();
  /** The result shown: the pseudo-locale's while the simulation is on. */
  simulatedResult = $derived.by(() => this.#options.pseudoLocalization() ? this.pseudoResult : this.result);

  readonly #options: PreviewControllerOptions;
  readonly #ownership = createMessagePreviewOwnership();
  readonly #scheduler: ReturnType<typeof createMessagePreviewScheduler>;

  constructor(options: PreviewControllerOptions) {
    this.#options = options;
    this.#scheduler = createMessagePreviewScheduler(
      options.setTimer ?? ((callback, delay) => setTimeout(callback, delay)),
      options.clearTimer ?? ((handle) => clearTimeout(handle as ReturnType<typeof setTimeout>)),
    );
  }

  /** Forgets the preview of the previous selection and cancels its requests. */
  reset(): void {
    this.ast = undefined;
    this.result = undefined;
    this.request = undefined;
    this.#ownership.reset();
    this.error = undefined;
    this.#scheduler.cancel();
    this.busy = false;
  }

  /** Starts from these sample values (a saved review's, or an example of the message). */
  useSamples(source: Record<string, string> | undefined): void {
    this.samples = createPreviewSamples(source);
  }

  setFormatting(requestedLocale: string, timeZone: string): void {
    this.requestedLocale = requestedLocale;
    this.timeZone = timeZone;
  }

  /** Compiles and renders `key` of the document text after a debounce; a newer request supersedes it. */
  schedule(path: string, content: string, key: string, locale: string): void {
    const request = createMessagePreviewRequest(path, content, key, locale, { requestedLocale: this.requestedLocale, timeZone: this.timeZone });
    const beginning = this.#ownership.begin(request);
    this.request = beginning.request;
    this.ast = beginning.ast;
    this.result = beginning.result;
    this.error = beginning.error;
    this.busy = true;
    this.#scheduler.schedule(450, (epoch) => {
      void routeMessagePreview(
        (requestPath, requestContent, requestLocale, requestKey, samplesJson, requestedLocale, timeZone) =>
          this.#options.bridge.previewMessage(requestPath, requestContent, requestLocale, requestKey, samplesJson, requestedLocale, timeZone),
        request,
        this.samples,
        this.#options.defaultSample,
        () => this.#scheduler.isCurrent(epoch),
      ).then((routed) => {
        if (!this.#scheduler.isCurrent(epoch)) return;
        const result = routed.initial;
        if (!result.success || result.astJson === undefined || result.locale === undefined) {
          this.ast = undefined;
          this.result = undefined;
          this.error = previewFailure(result);
          return;
        }
        const ast = routed.ast as MessageArtifact;
        if (!this.#ownership.acceptParsed(request)) return;
        this.ast = ast;
        this.samples = routed.samples;
        this.error = undefined;
        const rendered = routed.rendered;
        if (!rendered?.success || rendered.renderedJson === undefined) {
          this.result = undefined;
          this.error = previewFailure(rendered);
          return;
        }
        this.result = parseRenderedMessagePreview(rendered.renderedJson);
        return this.#renderPseudo(request, routed.samples, () => this.#scheduler.isCurrent(epoch));
      }).catch((error) => {
        if (this.#scheduler.isCurrent(epoch)) this.error = this.#options.errorNotice(error);
      }).finally(() => {
        if (this.#scheduler.isCurrent(epoch)) this.busy = false;
      });
    });
  }

  /** Renders the parsed message again with one changed sample value. */
  updateSample(name: string, value: string): void {
    this.samples = withPreviewSample(this.samples, name, value);
    const request = this.request;
    if (!this.#ownership.canRenderSample(request, this.ast)) return;
    this.busy = true;
    this.#scheduler.schedule(150, (epoch) => {
      void this.#renderCompiler(request, epoch).catch((error) => {
        if (this.#scheduler.isCurrent(epoch)) this.error = this.#options.errorNotice(error);
      }).finally(() => {
        if (this.#scheduler.isCurrent(epoch)) this.busy = false;
      });
    });
  }

  async #renderCompiler(request: PreviewRequest, epoch: number): Promise<void> {
    const result = await this.#options.bridge.previewMessage(
      request.path,
      request.content,
      request.locale,
      request.key,
      JSON.stringify(this.samples),
      request.requestedLocale,
      request.timeZone,
    );
    if (!this.#scheduler.isCurrent(epoch)) return;
    if (!result.success || result.renderedJson === undefined) {
      this.result = undefined;
      this.error = previewFailure(result);
      return;
    }
    this.result = parseRenderedMessagePreview(result.renderedJson);
    this.error = undefined;
    await this.#renderPseudo(request, this.samples, () => this.#scheduler.isCurrent(epoch));
  }

  // The simulated pseudo-locale, generated from the base locale by the host as a --pseudo-locales build does. Its
  // formatting follows the pseudo-locale, so only the time zone carries over.
  async #renderPseudo(request: PreviewRequest, samples: Record<string, string>, isCurrent: () => boolean): Promise<void> {
    if (!this.#options.pseudoLocalization()) {
      this.pseudoResult = undefined;
      this.pseudoError = undefined;
      return;
    }
    const result = await this.#options.bridge.previewMessage(request.path, request.content, this.pseudoLocale, request.key, JSON.stringify(samples), undefined, request.timeZone);
    if (!isCurrent()) return;
    if (!result.success || result.renderedJson === undefined) {
      this.pseudoResult = undefined;
      this.pseudoError = previewFailure(result);
      return;
    }
    this.pseudoResult = parseRenderedMessagePreview(result.renderedJson);
    this.pseudoError = undefined;
  }
}
