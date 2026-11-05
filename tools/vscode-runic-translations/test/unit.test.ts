import { test, expect } from "bun:test";
import { mkdtempSync, mkdirSync, readFileSync, writeFileSync, rmSync, symlinkSync, realpathSync } from "node:fs";
import { join } from "node:path";
import { tmpdir } from "node:os";
import { importArguments, nearestProjectManifest, projectManifest, serverLaunch, sourceWatchRoots, statusArguments, toolLaunch, xliffExportArguments, xliffImportDryRunArguments } from "../src/server.js";
import { previewDescription, renderedPreviewHtml, resolvePreviewHtml } from "../src/preview.js";
import type { Preview } from "../src/preview.js";
import { PreviewSession, sampleChoices, type PreviewHost, type PreviewState, type Samples } from "../src/preview-session.js";
import { ForwardedWatchers, type DisposableLike, type WatcherLike } from "../src/watchers.js";
import { OneTimeNotice, coverageLine, errorSummary, localeStatus, parseStatus, statusHtml, statusPresentation, type DocumentInfo } from "../src/status.js";
import { canonicalLocale, isOverriddenIn, libraryCatalogChoices, libraryInstallPattern, libraryDocumentProject, libraryLocaleChoices, libraryMessageChoices, messageLine, overrideSourcePath, overridesAddArguments, unsavedDocuments, type LibraryCatalogs } from "../src/libraries.js";

class MockWatcher implements WatcherLike<string> {
  readonly created: ((uri: string) => void)[] = [];
  readonly changed: ((uri: string) => void)[] = [];
  readonly deleted: ((uri: string) => void)[] = [];
  disposeCount = 0;
  onDidCreate(listener: (uri: string) => void): DisposableLike { this.created.push(listener); return this.subscription(this.created, listener); }
  onDidChange(listener: (uri: string) => void): DisposableLike { this.changed.push(listener); return this.subscription(this.changed, listener); }
  onDidDelete(listener: (uri: string) => void): DisposableLike { this.deleted.push(listener); return this.subscription(this.deleted, listener); }
  dispose(): void { this.disposeCount++; }
  fireChange(uri: string): void { for (const listener of [...this.changed]) listener(uri); }
  private subscription(listeners: ((uri: string) => void)[], listener: (uri: string) => void): DisposableLike {
    return { dispose: () => { const index = listeners.indexOf(listener); if (index >= 0) listeners.splice(index, 1); } };
  }
}

test("local tool resolution respects a root manifest and preserves argument boundaries", () => {
  const root = mkdtempSync(join(tmpdir(), "runic-vscode-"));
  try {
    mkdirSync(join(root, ".config"));
    writeFileSync(join(root, ".config", "dotnet-tools.json"), JSON.stringify({ isRoot: true, tools: {} }));
    expect(() => serverLaunch(root, "dotnet", "")).toThrow("No project-local");
    writeFileSync(join(root, ".config", "dotnet-tools.json"), JSON.stringify({ isRoot: true, tools: { "dotnet-runic-translations": { commands: ["runic-translations"] } } }));
    expect(serverLaunch(root, "dotnet custom", "")).toEqual({ command: "dotnet custom", args: ["tool", "run", "runic-translations", "--", "lsp"], cwd: root });
    writeFileSync(join(root, "server with spaces.dll"), "fixture");
    expect(serverLaunch(root, "dotnet", "server with spaces.dll").args).toEqual([join(root, "server with spaces.dll"), "lsp"]);
  } finally { rmSync(root, { recursive: true }); }
});
test("XLIFF commands run the same local tool with the project's manifest and keep argument boundaries", () => {
  const root = mkdtempSync(join(tmpdir(), "runic-vscode-xliff-"));
  try {
    mkdirSync(join(root, ".config"));
    writeFileSync(join(root, ".config", "dotnet-tools.json"), JSON.stringify({ isRoot: true, tools: { "dotnet-runic-translations": { commands: ["runic-translations"] } } }));
    expect(projectManifest(root)).toBeUndefined();
    mkdirSync(join(root, "translations"));
    writeFileSync(join(root, "translations", "runic.json"), "{}");
    const project = projectManifest(root)!;
    expect(project).toBe(join(root, "translations", "runic.json"));
    expect(toolLaunch(root, "dotnet", "", xliffExportArguments(project, join(root, "out dir"))).args)
      .toEqual(["tool", "run", "runic-translations", "--", "xliff", "export", "--project", project, "--output", join(root, "out dir")]);
    expect(toolLaunch(root, "dotnet", "", xliffImportDryRunArguments(project, "de file.xliff")).args)
      .toEqual(["tool", "run", "runic-translations", "--", "xliff", "import", "--project", project, "--source", "de file.xliff", "--dry-run"]);
    writeFileSync(join(root, "runic.json"), "{}");
    expect(projectManifest(root)).toBe(join(root, "runic.json"));
  } finally { rmSync(root, { recursive: true }); }
});
test("XLIFF commands pick the project of the active file: the nearest runic.json above it within the folder", () => {
  const root = realpathSync(mkdtempSync(join(tmpdir(), "runic-vscode-nearest-")));
  try {
    mkdirSync(join(root, "apps", "shop", "translations"), { recursive: true });
    mkdirSync(join(root, "apps", "admin"), { recursive: true });
    writeFileSync(join(root, "runic.json"), "{}");
    writeFileSync(join(root, "apps", "shop", "translations", "runic.json"), "{}");
    expect(nearestProjectManifest(join(root, "apps", "shop", "translations", "de.rmf2"), root)).toBe(join(root, "apps", "shop", "translations", "runic.json"));
    expect(nearestProjectManifest(join(root, "apps", "admin", "en.rmf2"), root)).toBe(join(root, "runic.json"));
    expect(nearestProjectManifest(join(root, "apps", "admin", "en.rmf2"), join(root, "apps"))).toBeUndefined();
    expect(nearestProjectManifest(join(tmpdir(), "elsewhere.rmf2"), root)).toBeUndefined();
  } finally { rmSync(root, { recursive: true }); }
});
test("translation source watcher plans dedupe nested mounts and reject workspace escapes", () => {
  const root = mkdtempSync(join(tmpdir(), "runic-vscode-mounts-"));
  const outside = mkdtempSync(join(tmpdir(), "runic-vscode-outside-"));
  try {
    const canonicalRoot = realpathSync.native(root);
    mkdirSync(join(root, "translations"));
    mkdirSync(join(root, "feature"));
    mkdirSync(join(root, "feature", "nested"));
    if (process.platform !== "win32") symlinkSync(outside, join(root, "escaped-link"), "dir");
    writeFileSync(join(root, "translations", "runic.json"), JSON.stringify({
      schemaVersion: 1, catalog: "app",
      sourceRoots: [
        { path: "../feature", namespace: ["shop"] },
        { path: "../feature/nested", namespace: ["nested"] },
        { path: "../../outside-workspace", namespace: ["escape"] },
        ...(process.platform === "win32" ? [] : [{ path: "../escaped-link", namespace: ["linked"] }]),
      ],
    }));
    expect(sourceWatchRoots(root)).toEqual([canonicalRoot]);
    mkdirSync(join(root, "new-feature"));
    writeFileSync(join(root, "translations", "runic.json"), JSON.stringify({
      schemaVersion: 1, catalog: "app",
      sourceRoots: [{ path: "../new-feature", namespace: ["new-shop"] }],
    }));
    writeFileSync(join(root, "new-feature", "de.rmf2"), "title = Neu\n");
    // The extension refreshes its manually forwarded subscriptions from this
    // stable plan after the manifest event. The one containing watcher observes
    // the replacement mount without adding a duplicate recursive subscription.
    expect(sourceWatchRoots(root)).toEqual([canonicalRoot]);
  } finally {
    rmSync(root, { recursive: true });
    rmSync(outside, { recursive: true });
  }
});
test("refreshed source watchers forward events and dispose replaced subscriptions", () => {
  let roots = ["old"];
  const manifest = new MockWatcher();
  const created: MockWatcher[] = [];
  const events: { uri: string; type: number }[] = [];
  const forwarded = new ForwardedWatchers(
    manifest,
    () => roots,
    () => { const watcher = new MockWatcher(); created.push(watcher); return watcher; },
    (uri, type) => events.push({ uri, type }),
  );
  expect(created).toHaveLength(1);
  const old = created[0];
  roots = ["new"];
  manifest.fireChange("runic.json");
  expect(old.disposeCount).toBe(1);
  expect(created).toHaveLength(2);
  created[1].fireChange("new/de.rmf2");
  created[1].fireChange("new/de/title.mf2");
  expect(events).toEqual([
    { uri: "runic.json", type: 2 },
    { uri: "new/de.rmf2", type: 2 },
    { uri: "new/de/title.mf2", type: 2 },
  ]);
  forwarded.dispose();
  expect(manifest.disposeCount).toBe(1);
  expect(manifest.changed).toHaveLength(0);
  expect(created[1].disposeCount).toBe(1);
  expect(created[1].changed).toHaveLength(0);
});
test("execution-v2 preview selects server rendering and remains inert", async () => {
  let rendered = false;
  const html = await resolvePreviewHtml({
    key: "<key>", locale: "en", examples: [], inputs: [],
    ast: { astVersion: 5, profile: "rmf2-execution-v2", inputs: [] },
  }, {}, async () => {
    rendered = true;
    return { key: "<key>", locale: "en", runs: [
    { text: "<script>alert(1)</script>" },
    { name: "runic:link", text: null, children: [{ text: "Help" }] },
    { name: "runic:action", text: null, children: [{ text: "Retry" }] },
    { name: "shop:badge", text: null, children: [{ text: "Ready" }] },
    ] };
  });
  expect(rendered).toBe(true);
  expect(html).toContain("&lt;script&gt;"); expect(html).not.toContain("<script>");
  expect(html).toContain("aria-disabled=\"true\""); expect(html).toContain("<button disabled>"); expect(html).not.toContain("href=");
  expect(html).toContain("title=\"shop:badge\"");
});

test("the preview names the requested locale, its resolution, the formatting locale and the time zone, and sets the direction", () => {
  const swiss = { key: "price", locale: "de", runs: [{ text: "1'234.5" }], requestedLocale: "de-CH", resolution: "parent" as const, formattingLocale: "de-CH", direction: "ltr" as const, timeZone: "Europe/Zurich" };
  expect(previewDescription(swiss)).toBe("de · requested de-CH (parent) · formats in de-CH · time zone Europe/Zurich · inert preview");
  expect(previewDescription({ key: "price", locale: "de", runs: [], requestedLocale: "de", resolution: "exact", formattingLocale: "de" })).toBe("de · inert preview");
  const mirrored = renderedPreviewHtml({ key: "price", locale: "ar-XB", runs: [{ text: "<b>" }], direction: "rtl" });
  expect(mirrored).toContain('lang="ar-XB" dir="rtl"');
  expect(mirrored).toContain("&lt;b&gt;");
  // An older server reports no direction: left to right.
  expect(renderedPreviewHtml({ key: "k", locale: "en", runs: [] })).toContain('dir="ltr"');
});

const metadata = (key = "count"): Preview => ({ key, locale: "en", inputs: [{ name: "count", type: "integer" }], examples: [{ count: 1 }, { count: 2 }], ast: { astVersion: 5, profile: "rmf2-execution-v2", inputs: [] } });
const target = (key = "count", locale = "en") => ({ uri: "file:///en.rmf2", key, locale });
function deferred<T>() {
  let resolve!: (value: T) => void;
  let reject!: (error: unknown) => void;
  const promise = new Promise<T>((done, fail) => { resolve = done; reject = fail; });
  return { promise, resolve, reject };
}
function fixture(overrides: Partial<PreviewHost> = {}, maximumSessions = 128) {
  const published: PreviewState[] = [];
  let choices = 0;
  const host: PreviewHost = { revision: () => 0, load: async () => metadata(), choose: async () => { choices++; return { count: "2" }; }, render: async (_, __, samples) => samples.count, publish: state => { published.push(state); }, ...overrides };
  return { session: new PreviewSession(host, maximumSessions), published, choices: () => choices };
}
test("all examples and custom samples are offered; session reuse is scoped by message and locale", async () => {
  expect(sampleChoices(metadata(), { count: "9" }).map(choice => [choice.label, choice.samples])).toEqual([
    ["Previous sample values", { count: "9" }], ["Example 1", { count: "1" }], ["Example 2", { count: "2" }], ["Custom sample values", undefined],
  ]);
  const { session, choices } = fixture();
  await session.open(target()); await session.open(target());
  expect(choices()).toBe(1);
  expect(session.state?.samples).toEqual({ count: "2" });
  expect(session.preferredLocale(target().uri, "count")).toBe("en");
  await session.open(target("another")); await session.open(target("count", "de"));
  expect(choices()).toBe(3);
  await session.open(target(), async () => ({ count: "7" }));
  session.close(); await session.open(target());
  expect(choices()).toBe(3);
  expect(session.state?.samples).toEqual({ count: "7" });
});
test("cancelled or late older selections cannot replace a newer preview or its remembered samples", async () => {
  const older = deferred<Samples | undefined>();
  const { session, published } = fixture();
  const pending = session.open(target(), async () => older.promise);
  await Promise.resolve(); await Promise.resolve();
  await session.open(target(), async () => ({ count: "8" }));
  older.resolve({ count: "1" }); await pending;
  expect(published.map(state => state.samples.count)).toEqual(["8"]);
  await session.open(target(), async () => undefined);
  await session.open(target());
  expect(session.state?.samples.count).toBe("8");
});
test("an older completed render cannot overwrite a new message selection", async () => {
  const started = deferred<void>(); const rendered = deferred<string>();
  const { session, published } = fixture({ render: async item => { if (item.key === "old") { started.resolve(); return rendered.promise; } return "new"; } });
  const old = session.open(target("old")); await started.promise;
  await session.open(target("new")); rendered.resolve("old"); await old;
  expect(published.map(state => state.target.key)).toEqual(["new"]);
});
test("refresh and edits during rendering publish only the latest unsaved revision with remembered samples", async () => {
  let revision = 1;
  const started = deferred<void>(); const rendered = deferred<string>();
  let renders = 0;
  const { session, published } = fixture({ revision: () => revision, render: async (_, __, samples) => { renders++; if (renders === 1) { started.resolve(); return rendered.promise; } return `revision ${revision}: ${samples.count}`; } });
  const initial = session.open(target()); await started.promise;
  revision = 2; rendered.resolve("stale revision 1"); await initial;
  expect(published.map(state => state.html)).toEqual(["revision 2: 2"]);
  revision = 3; await session.refresh();
  expect(session.state?.html).toBe("revision 3: 2");
});
test("content-modified responses retry and closing a panel invalidates in-flight results", async () => {
  let requests = 0;
  const { session } = fixture({ load: async () => { if (++requests === 1) throw { code: -32801 }; return metadata(); } });
  await session.open(target()); expect(session.state?.samples.count).toBe("2");
  const rendering = deferred<string>(); const started = deferred<void>();
  const closed = fixture({ render: async () => { started.resolve(); return rendering.promise; } });
  const pending = closed.session.open(target()); await started.promise;
  closed.session.close(); rendering.resolve("late"); await pending;
  expect(closed.published).toHaveLength(0);
  expect(closed.session.state).toBeUndefined();
});
test("cancelling sample selection still refreshes edits made while the picker was open", async () => {
  let revision = 1;
  const selection = deferred<Samples | undefined>();
  const started = deferred<void>();
  const { session, published } = fixture({ revision: () => revision, render: async () => `revision ${revision}` });
  await session.open(target());
  const choosing = session.open(target(), async () => { started.resolve(); return selection.promise; });
  await started.promise; revision = 2;
  await session.refresh();
  expect(published).toHaveLength(1);
  selection.resolve(undefined); await choosing;
  expect(session.state?.html).toBe("revision 2");
  expect(session.state?.samples.count).toBe("2");
});
test("session sample and preferred-locale cache evicts the least recently used identity", async () => {
  const { session, choices } = fixture({}, 2);
  await session.open(target("a")); await session.open(target("b"));
  await session.open(target("a")); await session.open(target("c"));
  expect(session.preferredLocale(target().uri, "b")).toBeUndefined();
  await session.open(target("a")); expect(choices()).toBe(3);
  await session.open(target("b")); expect(choices()).toBe(4);
  expect(session.state?.target.key).toBe("b");
});
test("a failed older render cannot publish an error over a newer preview", async () => {
  const started = deferred<void>(); const rendering = deferred<string>();
  const failures: unknown[] = [];
  const { session, published } = fixture({ failed: error => { failures.push(error); }, render: async item => { if (item.key === "old") { started.resolve(); return rendering.promise; } return "new"; } });
  const old = session.open(target("old")); await started.promise;
  await session.open(target("new")); rendering.reject(new Error("old failure")); await old;
  expect(failures).toHaveLength(0);
  expect(published.map(state => state.html)).toEqual(["new"]);
});
test("status shows the server state, the catalog and the file's locale", () => {
  const info: DocumentInfo = { project: "file:///w/runic.json", catalog: "app", baseLocale: "en", locale: "de" };
  expect(statusPresentation("running", info)).toEqual({ text: "$(globe) Runic: app · de", tooltip: "Runic language server: running\nCatalog: app\nLocale: de\nSelect to show the Runic Translations output.", error: false });
  expect(statusPresentation("running", { ...info, locale: "EN" }).tooltip).toContain("Locale: EN (base)");
  // runic.json has a catalog but no locale.
  expect(statusPresentation("running", { ...info, locale: null }).text).toBe("$(globe) Runic: app");
  expect(statusPresentation("running").text).toBe("$(globe) Runic");
  expect(statusPresentation("starting").text).toBe("$(sync~spin) Runic: starting");
  const failed = statusPresentation("failed", undefined, "No project-local runic-translations tool was found.");
  expect(failed.error).toBe(true);
  expect(failed.text).toBe("$(error) Runic: not running");
  expect(failed.tooltip).toContain("No project-local runic-translations tool was found.");
  expect(statusPresentation("stopped").text).toBe("$(circle-slash) Runic: stopped");
});
test("start failures are announced once per folder until reset", () => {
  const notice = new OneTimeNotice();
  expect(notice.claim("a")).toBe(true);
  expect(notice.claim("a")).toBe(false);
  expect(notice.claim("b")).toBe(true);
  notice.reset("a");
  expect(notice.claim("a")).toBe(true);
  notice.reset();
  expect(notice.claim("b")).toBe(true);
  expect(errorSummary(new Error("Error: first line\nstack detail"))).toBe("first line");
  expect(errorSummary("plain")).toBe("plain");
});
test("the VSIX validates runic.json against the schema copied from the specification at build time", () => {
  const here = join(import.meta.dir, "..");
  const manifest = JSON.parse(readFileSync(join(here, "package.json"), "utf8"));
  expect(manifest.contributes.jsonValidation).toEqual([{ fileMatch: "**/runic.json", url: "./dist/schemas/project-v1.schema.json" }]);
  const assets = readFileSync(join(here, "package-assets.mjs"), "utf8");
  expect(assets).toContain("join('dist', 'schemas', 'project-v1.schema.json')");
  expect(assets).toContain("'specs', 'translations', 'schemas', 'project-v1.schema.json'");
  const schema = JSON.parse(readFileSync(join(here, "..", "..", "specs", "translations", "schemas", "project-v1.schema.json"), "utf8"));
  expect(schema.$id).toBe("https://runic-artifex.eu/schemas/translations/project-v1.schema.json");
  // Every command the extension contributes is listed in the README's parity table.
  const readme = readFileSync(join(here, "README.md"), "utf8");
  for (const command of manifest.contributes.commands as { title: string }[])
    expect(readme).toContain(command.title.replace(/^Runic: /, ""));
});

test("coverage for the file's locale comes from runic-translations status, with its threshold and completeness policy", () => {
  const stdout = JSON.stringify({ protocol: "runic.commandline/1", command: "status", success: true, exitCode: 0, payloadType: "runic.translations.status/1",
    payload: { belowThreshold: false, projects: [{ project: "runic.json", catalog: "app", baseLocale: "en", messages: 40, reviewFile: "app.review.json", locales: [
      { locale: "en", translated: 40, fallback: 0, missing: 0, coverage: 100, belowThreshold: false },
      { locale: "de-CH", fallbackLocale: "de", translated: 30, fallback: 5, missing: 5, coverage: 87.5, stale: 1, minimumCoverage: 90, belowThreshold: true, completeness: "warning" },
    ] }] }, fault: null, diagnostics: [] });
  const result = parseStatus(stdout);
  const swiss = localeStatus(result, "DE-ch")!;
  expect(coverageLine(swiss)).toBe("Coverage de-CH: 87.5% (30 translated, 5 from de, 5 missing, 1 stale), below its minimum 90%; missing translations are warnings");
  const info: DocumentInfo = { project: "file:///w/runic.json", catalog: "app", baseLocale: "en", locale: "de-CH" };
  const presentation = statusPresentation("running", info, undefined, swiss);
  expect(presentation.text).toBe("$(globe) Runic: app · de-CH 87.5%");
  expect(presentation.tooltip).toContain("Coverage de-CH: 87.5%");
  const html = statusHtml(result);
  expect(html).toContain("<th scope=\"col\">Stale</th>"); expect(html).toContain('class="below"'); expect(html).toContain("warning"); expect(html).not.toContain("<script");
  expect(() => parseStatus(JSON.stringify({ success: false, payloadType: null, payload: null, fault: { message: "The catalog does not compile." } }))).toThrow("The catalog does not compile.");
  expect(statusArguments("/w/runic.json")).toEqual(["status", "--project", "/w/runic.json", "--format", "json"]);
});

test("catalog import runs the tool's importer: a dry run first, then the import, optionally overwriting", () => {
  expect(importArguments("/w/runic.json", "resx", "/w/Strings resx/Strings.resx", { dryRun: true }))
    .toEqual(["import", "--from", "resx", "/w/Strings resx/Strings.resx", "--project", "/w/runic.json", "--dry-run"]);
  expect(importArguments("/w/runic.json", "i18next", "/w/locales", { dryRun: false, overwrite: true }))
    .toEqual(["import", "--from", "i18next", "/w/locales", "--project", "/w/runic.json", "--overwrite"]);
});
test("Override Library Message offers installed catalogs, messages and locales, and runs overrides add for the project", () => {
  const result: LibraryCatalogs = {
    project: "file:///work/shop/translations/runic.json", warnings: [],
    catalogs: [
      { catalog: "acme.chart", package: "Acme.Chart", version: "2.0.0", origin: "nuget", overridden: false, problem: null, directory: null, baseLocale: "en",
        locales: [{ tag: "en", origin: "library", fallback: null, messages: 1, libraryTranslated: 1, overridden: 0, covered: 1, pending: false }],
        messages: [{ key: "legend", path: "legend", baseText: "Legend", inputs: [], overridden: [], changed: [] }] },
      { catalog: "acme.missing", package: null, version: null, origin: null, overridden: true, problem: "not installed", directory: null },
      { catalog: "acme.table", package: "Acme.Table", version: "1.0.0", origin: "nuget", overridden: true, problem: null, directory: null, baseLocale: "en",
        locales: [
          { tag: "en", origin: "library", fallback: null, messages: 2, libraryTranslated: 2, overridden: 0, covered: 2, pending: false },
          { tag: "de-CH", origin: "application", fallback: "de", messages: 2, libraryTranslated: 0, overridden: 1, covered: 1, pending: false },
        ],
        messages: [
          { key: "header_sort", path: "header.sort", baseText: "Sort by {$column}", inputs: ["$column: string"], overridden: ["de-CH"], changed: ["de-CH"] },
          { key: "rows", path: "rows", baseText: ".input {$count :integer}\n.match $count", inputs: [], overridden: [], changed: [] },
        ] },
    ],
  };
  const catalogs = libraryCatalogChoices(result);
  expect(catalogs.map(choice => choice.label)).toEqual(["acme.table", "acme.chart"]);
  expect(catalogs[0].detail).toBe("Overridden in de-CH");
  expect(catalogs[1].detail).toContain("runic.json gets an \"overrides\" entry");
  const messages = libraryMessageChoices(catalogs[0].value);
  expect(messages.map(choice => [choice.label, choice.description])).toEqual([["header.sort", "Sort by {$column}"], ["rows", ".input {$count :integer}"]]);
  expect(messages[0].detail).toBe("Overridden in de-CH; library text changed since the override in de-CH; Inputs: $column: string");
  const locales = libraryLocaleChoices(catalogs[0].value, messages[0].value);
  expect(locales.map(choice => choice.description)).toEqual(["library base locale", "added by the application, falls back to de"]);
  expect(locales[1].detail).toBe("1/2 messages in its own text; this message is overridden here");
  const manifest = join("/work", "shop", "translations", "runic.json");
  expect(overridesAddArguments(manifest, "acme.table", "fr", "header_sort")).toEqual(["overrides", "add", "acme.table", "--locale", "fr", "--key", "header_sort", "--project", join("/work", "shop", "translations")]);
  expect(overrideSourcePath(manifest, "acme.table", "fr")).toBe(join("/work", "shop", "translations", "overrides", "acme.table", "fr.rmf2"));
  expect(messageLine("# header {\n#   sort = x\n# }\nrows = Rows\nheader {\n  sort = Trier\n}\n", "header.sort")).toBe(5);
  expect(messageLine("rows = Rows\n", "missing")).toBe(0);
});
test("typed override locales are canonicalized and compared case-insensitively", () => {
  expect(canonicalLocale("de_ch")).toBe("de-CH");
  expect(canonicalLocale(" DE-ch ")).toBe("de-CH");
  expect(canonicalLocale("zh_hant_tw")).toBe("zh-Hant-TW");
  expect(canonicalLocale("fr")).toBe("fr");
  expect(canonicalLocale("not a locale")).toBeUndefined();
  expect(canonicalLocale("de--CH")).toBeUndefined();
  expect(canonicalLocale("")).toBeUndefined();
  const message = { overridden: ["de-CH"] };
  expect(isOverriddenIn(message, "de-ch")).toBe(true);
  expect(isOverriddenIn(message, "DE-CH")).toBe(true);
  expect(isOverriddenIn(message, "de")).toBe(false);
});
test("overrides add stops for unsaved edits to the files it writes", () => {
  const source = join("/work", "overrides", "acme.table", "de-CH.rmf2");
  const manifest = join("/work", "runic.json");
  const documents = [
    { fsPath: source, isDirty: true },
    { fsPath: manifest, isDirty: false },
    { fsPath: join("/work", "other.rmf2"), isDirty: true },
  ];
  expect(unsavedDocuments(documents, [source, manifest], "linux")).toEqual([documents[0]]);
  expect(unsavedDocuments(documents, [source.toUpperCase(), manifest], "linux")).toEqual([]);
  expect(unsavedDocuments(documents, [source.toUpperCase(), manifest], "win32")).toEqual([documents[0]]);
  expect(unsavedDocuments([{ fsPath: manifest, isDirty: true }], [source, manifest], "linux")).toHaveLength(1);
});
test("library documents name their project in the query, encoded or not", () => {
  const project = "file:///work/my%20shop/translations/runic.json";
  expect(libraryDocumentProject(`project=${encodeURIComponent(project)}`)).toBe(project);
  expect(libraryDocumentProject(encodeURIComponent(`project=${project}`))).toBe(project);
  expect(libraryDocumentProject("other=1")).toBeUndefined();
  expect(statusPresentation("running", { project: null, catalog: "acme.table", baseLocale: null, locale: "fr", library: "Acme.Table 1.0.0" }).tooltip)
    .toContain("Catalog: acme.table (library Acme.Table 1.0.0, overridden by the application)");
});

test("the restore and install files of every package manager are watched for library catalogs", () => {
  for (const file of ["obj/project.assets.json", "node_modules/.package-lock.json", "node_modules/.modules.yaml", "node_modules/.yarn-state.yml", "package-lock.json", "pnpm-lock.yaml", "yarn.lock", "bun.lock"])
    expect(libraryInstallPattern).toContain(file);
});
