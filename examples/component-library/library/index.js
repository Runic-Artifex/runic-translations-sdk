// The library imports its own prebuilt catalog by a relative path. An application whose Vite build uses
// runicTranslations({ dependencies: "auto" }) redirects these imports to the catalog regenerated with its overrides
// and locales; without it, the prebuilt catalog applies.
import { m } from "./dist/translations/acme.widgets.esm-v5/messages.js";

/** The storage meter's heading. */
export const storageTitle = () => m.storage_title();

/** The number of files and the space they use, or a hint while there are none. */
export function describeStorage({ files, used, total }) {
  return files === 0 ? m.storage_empty() : `${m.storage_files({ count: files })} · ${m.storage_usage({ used, total })}`;
}
