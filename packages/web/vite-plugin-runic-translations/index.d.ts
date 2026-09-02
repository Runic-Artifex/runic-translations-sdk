import type { Plugin } from "vite";

export interface RunicTranslationsOptions {
  /** Runic translation directory or runic.json path. Defaults to ./translations. */
  readonly project?: string;
  /** Generated artifact directory for project mode. Defaults to ./.runic/translations. */
  readonly output?: string;
  /** Pre-generated web-module-manifest-v1.json path when generation is owned by another build. */
  readonly manifest?: string;
  /** Authoring inputs to watch; regeneration remains owned by the host build. */
  readonly sourceFiles?: readonly string[];
  /** Project-mode invocation working directory. Defaults to the Vite process working directory. */
  readonly cwd?: string;
  /** Project-mode compiler executable. Defaults to `dotnet`. */
  readonly command?: string;
  /** Project-mode arguments before `generate`. Defaults to the local-tool invocation. */
  readonly commandArguments?: readonly string[];
}

export declare function runicTranslations(options?: RunicTranslationsOptions): Plugin;
