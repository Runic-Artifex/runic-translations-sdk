import type { Plugin } from "vite";

export interface RunicTranslationsOptions {
  /** Absolute or working-directory-relative web-module-manifest-v1.json path. */
  readonly manifest: string;
  /** Authoring inputs to watch; regeneration remains owned by the host build. */
  readonly sourceFiles?: readonly string[];
  /** Optional pinned .NET-tool compiler invocation run before build and after watched authoring changes. */
  readonly compiler?: RunicTranslationsCompilerOptions;
}

export interface RunicTranslationsCompilerOptions {
  /** Catalog manifest passed to `runic-translations generate`. */
  readonly catalog: string;
  /** Resource documents or CLI-supported globs passed to the compiler. */
  readonly documents: readonly string[];
  /** Generated output root containing the configured web module manifest. */
  readonly output: string;
  /** Invocation working directory. Defaults to the Vite process working directory. */
  readonly cwd?: string;
  /** Compiler executable. Defaults to `dotnet`. */
  readonly command?: string;
  /** Arguments before `generate`. Defaults to the local-tool invocation. */
  readonly commandArguments?: readonly string[];
}

export declare function runicTranslations(options: RunicTranslationsOptions): Plugin;
