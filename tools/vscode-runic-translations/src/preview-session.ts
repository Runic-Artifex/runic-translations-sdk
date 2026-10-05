import type { Preview } from "./preview.js";

export interface PreviewTarget { uri: string; key: string; locale: string; locales?: string[] }
export type Samples = Record<string, string>;
export interface PreviewState { target: PreviewTarget; samples: Samples; html: string }
export interface PreviewHost {
  revision(): number;
  load(target: PreviewTarget): Promise<Preview>;
  choose(preview: Preview, remembered?: Samples): Promise<Samples | undefined>;
  render(target: PreviewTarget, preview: Preview, samples: Samples): Promise<string>;
  publish(state: PreviewState): void;
  failed?(error: unknown): void;
}

export function exampleSamples(example: Record<string, unknown>, preview: Preview): Samples {
  return Object.fromEntries(preview.inputs.map(({ name }) => [name, name in example ? String(example[name]) : ""]));
}
export function sampleChoices(preview: Preview, remembered?: Samples) {
  return [
    ...(remembered ? [{ label: "Previous sample values", samples: exampleSamples(remembered, preview) }] : []),
    ...preview.examples.map((example, index) => ({
      label: `Example ${index + 1}`,
      description: Object.entries(example).map(([name, value]) => `${name}=${String(value)}`).join(", "),
      samples: exampleSamples(example, preview),
    })),
    { label: "Custom sample values", samples: undefined },
  ];
}

/** One panel and session cache. A newer selection always owns publication. */
export class PreviewSession {
  private generation = 0;
  private opening = false;
  private stateRevision = -1;
  private readonly remembered = new Map<string, { target: PreviewTarget; samples: Samples }>();
  state: PreviewState | undefined;
  constructor(private readonly host: PreviewHost, private readonly maximumSessions = 128) {
    if (!Number.isInteger(maximumSessions) || maximumSessions < 1) throw new RangeError("The preview session limit must be a positive integer.");
  }
  private identity(target: PreviewTarget) { return JSON.stringify([target.uri, target.key, target.locale]); }
  preferredLocale(uri: string, key: string): string | undefined {
    return [...this.remembered.values()].reverse().find(value => value.target.uri === uri && value.target.key === key)?.target.locale;
  }
  beginSelection(): void { this.generation++; this.opening = true; }
  async endSelection(): Promise<void> { this.opening = false; await this.refresh(); }

  async open(target: PreviewTarget, select?: (preview: Preview, remembered?: Samples) => Promise<Samples | undefined>): Promise<PreviewState | undefined> {
    const generation = ++this.generation;
    this.opening = true;
    try {
      const remembered = this.remembered.get(this.identity(target))?.samples;
      let samples = remembered;
      if (select || !remembered) {
        const preview = await this.loadCurrent(target, generation);
        if (!preview || generation !== this.generation) return;
        samples = await (select ?? this.host.choose)(preview, remembered ? { ...remembered } : undefined);
      }
      if (!samples || generation !== this.generation) return;
      return await this.renderLatest(target, samples, generation);
    } finally {
      if (generation === this.generation) await this.endSelection();
    }
  }
  async refresh(): Promise<PreviewState | undefined> {
    // An in-progress sample selection will render the latest revision itself.
    if (this.opening || !this.state) return;
    if (this.stateRevision === this.host.revision()) return this.state;
    return this.renderLatest(this.state.target, this.state.samples, ++this.generation);
  }
  close(): void { this.generation++; this.opening = false; this.state = undefined; }

  private async loadCurrent(target: PreviewTarget, generation: number): Promise<Preview | undefined> {
    for (let attempt = 0; generation === this.generation; attempt++) {
      try { return await this.host.load(target); }
      catch (error) { if (generation !== this.generation) return; if (!contentModified(error) || attempt >= 2) throw error; }
    }
  }
  private async renderLatest(target: PreviewTarget, chosen: Samples, generation: number): Promise<PreviewState | undefined> {
    let conflicts = 0;
    while (generation === this.generation) {
      const revision = this.host.revision();
      try {
        const preview = await this.loadCurrent(target, generation);
        if (!preview || generation !== this.generation) return;
        if (revision !== this.host.revision()) continue;
        const samples = exampleSamples(chosen, preview);
        const html = await this.host.render(target, preview, samples);
        if (generation !== this.generation) return;
        if (revision !== this.host.revision()) continue;
        const state = { target, samples, html };
        this.remembered.delete(this.identity(target));
        this.remembered.set(this.identity(target), { target, samples: { ...samples } });
        while (this.remembered.size > this.maximumSessions) this.remembered.delete(this.remembered.keys().next().value!);
        this.state = state;
        this.stateRevision = revision;
        this.host.publish(state);
        return state;
      } catch (error) {
        if (generation !== this.generation) return;
        if (revision !== this.host.revision()) continue;
        if (contentModified(error) && conflicts++ < 2) continue;
        this.host.failed?.(error);
        throw error;
      }
    }
  }
}
function contentModified(error: unknown): boolean {
  return typeof error === "object" && error !== null && "code" in error && error.code === -32801;
}
