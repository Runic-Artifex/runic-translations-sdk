// The spell checking controller (D074.10, D086.2): nothing is asked before the settings load, the system language's
// dictionary downloads on its own only while the setting is on, any other language is offered first, marks come from
// the host in batches and are checked again after a download or an added word.
import assert from "node:assert/strict";
import "./svelte-modules.mjs";

const { SpellingController, markAt, marksWithin, spellingDownloadKey, spellingEnabledKey } =
  await import("../src/lib/state/spelling.svelte.ts");

const tick = (ms = 5) => new Promise((resolve) => setTimeout(resolve, ms));

function language(locale, state, extra = {}) {
  return { locale, state, systemLanguage: false, automatic: false, ...extra };
}

function createController(stored = {}) {
  const values = new Map(Object.entries(stored));
  const calls = [];
  const states = {
    en: language("en", "ready", { dictionary: "en_US", source: "bundled" }),
    de: language("de", "downloadable", { systemLanguage: true, downloadSize: 4_400_000 }),
    fr: language("fr", "downloadable", { downloadSize: 1_200_000, license: "MPL-2.0" }),
  };
  const bridge = {
    async spellingStatus(locales, automaticDownload) {
      calls.push(["status", locales.join(","), automaticDownload]);
      if (automaticDownload && states.de.state === "downloadable") states.de = { ...states.de, state: "downloading", automatic: true, bytesReceived: 1_100_000 };
      return { systemLanguage: "de-DE", languages: locales.map((locale) => states[locale]) };
    },
    async checkSpelling(locale, texts) {
      calls.push(["check", locale, texts.length]);
      const state = states[locale].state;
      return {
        locale, state,
        results: texts.map(({ id, text }) => ({
          id,
          misspellings: state !== "ready" ? [] : [...text.matchAll(/\bteh\b/g)].map((match) => ({ start: match.index, length: 3, word: "teh" })),
        })),
      };
    },
    async suggestSpelling(locale, word) { return { locale, word, suggestions: ["the"] }; },
    async downloadSpellingDictionary(locale) {
      calls.push(["download", locale]);
      states[locale] = { ...states[locale], state: "downloading", bytesReceived: 0 };
      return states[locale];
    },
    async addSpellingWord(word) { calls.push(["add", word]); return { word, added: true }; },
  };
  const controller = new SpellingController({
    bridge,
    storage: { get: (key) => values.get(key) ?? null, set: (key, value) => values.set(key, value) },
    uiLocale: () => "en",
    delay: 1,
    pollInterval: 5,
  });
  return { controller, calls, values, states };
}

// Nothing before the stored settings are read; then the project's languages are asked for with the setting.
{
  const { controller, calls } = createController({ [spellingDownloadKey]: "off" });
  controller.setLocales(["fr", "en", "de", "en"]);
  assert.deepEqual(controller.marks("en", "teh cat"), []);
  await tick();
  assert.deepEqual(calls, [], "The controller asked the host before the settings loaded.");
  controller.restore();
  await tick();
  assert.deepEqual(calls[0], ["status", "de,en,fr", false], "The setting did not keep the system language's download off.");
  assert.equal(controller.automaticDownload, false);
  assert.deepEqual(controller.notice("de"), { kind: "offer", locale: "de", size: 4_400_000, license: undefined },
    "With the setting off, the system language's dictionary is offered like any other.");
  controller.dispose();
}

// The system language downloads on its own and says so; any other language is offered until the user answers.
{
  const { controller, calls, states } = createController();
  controller.restore();
  controller.setLocales(["en", "de", "fr"]);
  await tick();
  assert.deepEqual(calls[0], ["status", "de,en,fr", true]);
  assert.deepEqual(controller.notice("de"), { kind: "downloading", locale: "de", automatic: true, percent: 25 });
  assert.deepEqual(controller.notice("fr"), { kind: "offer", locale: "fr", size: 1_200_000, license: "MPL-2.0" });
  assert.equal(controller.notice("en"), undefined, "A ready language shows a notice.");
  assert.ok(!calls.some(([kind]) => kind === "download"), "A language other than the system's downloaded without asking.");
  controller.decline("fr");
  assert.equal(controller.notice("fr"), undefined, "A declined offer is shown again in the same session.");
  // The download finishes: the next poll sees the dictionary, and the language's texts are checked.
  states.de = language("de", "ready", { systemLanguage: true, dictionary: "de_DE", source: "downloaded" });
  await tick(20);
  assert.equal(controller.languages.de.state, "ready", "The running download was not polled.");
  assert.equal(controller.notice("de"), undefined);
  await controller.download("fr");
  assert.deepEqual(calls.at(-1), ["download", "fr"]);
  assert.equal(controller.notice("fr").kind, "downloading");
  controller.dispose();
}

// Marks: texts are checked in one batch per language, cached, and checked again after an added word.
{
  const { controller, calls } = createController();
  controller.restore();
  controller.setLocales(["en"]);
  await tick();
  assert.deepEqual(controller.marks("en", "teh one"), []);
  assert.deepEqual(controller.marks("en", "and teh two"), []);
  await tick(10);
  assert.equal(calls.filter(([kind]) => kind === "check").length, 1, "Two texts were not checked in one batch.");
  assert.deepEqual(controller.marks("en", "and teh two"), [{ start: 4, length: 3, word: "teh" }]);
  assert.deepEqual(controller.marks("en", "teh one"), [{ start: 0, length: 3, word: "teh" }]);
  await controller.addWord("teh");
  assert.deepEqual(calls.at(-1), ["add", "teh"]);
  controller.marks("en", "teh one");
  await tick(10);
  assert.equal(calls.filter(([kind]) => kind === "check").length, 2, "An added word did not check the texts again.");
  assert.deepEqual(await controller.suggest("en", "teh"), ["the"]);
  controller.dispose();
}

// The settings are stored, and checking off shows no marks.
{
  const { controller, values } = createController();
  controller.restore();
  controller.setEnabled(false);
  controller.setAutomaticDownload(false);
  assert.equal(values.get(spellingEnabledKey), "off");
  assert.equal(values.get(spellingDownloadKey), "off");
  assert.deepEqual(controller.marks("en", "teh"), []);
  assert.equal(controller.notice("fr"), undefined);
  controller.dispose();
}

// Without the stored settings nothing downloads on its own.
{
  const { controller, calls } = createController();
  controller.restore(false);
  controller.setLocales(["de"]);
  await tick();
  assert.deepEqual(calls[0], ["status", "de", false]);
  controller.dispose();
}

// The pattern box maps the pattern's marks to its text segments, and finds the word at the caret.
{
  const marks = [{ start: 0, length: 3, word: "teh" }, { start: 12, length: 4, word: "fiel" }];
  assert.deepEqual(marksWithin(marks, 10, 8), [{ start: 2, length: 4, word: "fiel" }]);
  assert.deepEqual(markAt(marks, 3), marks[0]);
  assert.equal(markAt(marks, 5), undefined);
}

console.log("PASS: spelling waits for its settings, downloads the system language's dictionary on its own unless turned off, offers any other language first, and checks texts in batches again after a download or an added word.");
