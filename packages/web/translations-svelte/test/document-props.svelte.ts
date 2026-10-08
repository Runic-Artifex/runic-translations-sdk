import type { DocumentNode } from "../src/document/index.js";

/** Reactive fixture props, so a test can replace a mounted document's content in place. */
export function documentProps(nodes: readonly DocumentNode[], headingBase = 2) {
  const props = $state({ nodes, headingBase });
  return props;
}
