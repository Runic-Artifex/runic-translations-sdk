import type { Plugin } from "vite";

export interface RunicTextResourcesOptions {
  /** Absolute or working-directory-relative web-module-manifest-v1.json path. */
  readonly manifest: string;
  /** Authoring inputs to watch; regeneration remains owned by the host build. */
  readonly sourceFiles?: readonly string[];
}

export declare function runicTextResources(options: RunicTextResourcesOptions): Plugin;
