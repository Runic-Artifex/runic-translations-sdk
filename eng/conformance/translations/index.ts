// The Translations corpus module (eng/conformance/README.md, "Translations"): its cases are in
// specs/translations/conformance/cases.json, its status files in specs/translations/conformance/status.
import type { CorpusModule } from "../core/index";
import { loadTranslationsCorpus } from "./corpus";
import { runTranslationsCase, setCorpusRoot } from "./run";

export const translations: CorpusModule = {
  root: "specs/translations/conformance",
  load: (root) => {
    setCorpusRoot(root);
    return loadTranslationsCorpus(root);
  },
  run: runTranslationsCase,
};
