import * as vscode from "vscode";
import { execFile } from "node:child_process";
import { dirname, join } from "node:path";
import { LanguageClient, State, type LanguageClientOptions, type WorkspaceEdit as ProtocolEdit } from "vscode-languageclient/node";
import { importArguments, nearestProjectManifest, projectManifest, serverLaunch, sourceWatchRoots, statusArguments, toolLaunch, xliffExportArguments, xliffImportDryRunArguments } from "./server.js";
import { escapeHtml, resolvePreviewHtml, type Preview, type RenderedPreview } from "./preview.js";
import { PreviewSession, exampleSamples, sampleChoices, type Samples } from "./preview-session.js";
import { ForwardedWatchers } from "./watchers.js";
import { OneTimeNotice, errorSummary, localeStatus, parseStatus, statusHtml, statusPresentation, type DocumentInfo, type ServerState, type StatusResult } from "./status.js";
import { libraryCatalogChoices, libraryDocumentProject, libraryLocaleChoices, libraryMessageChoices, libraryScheme, messageLine, overrideSourcePath, overridesAddArguments, type LibraryCatalogs } from "./libraries.js";

const extensionId = "runic-artifex.runic-translations";
/**
 * The extension reports a failed start once per workspace folder, with install guidance, so the
 * client's own "couldn't create connection" notification is logged instead of shown while starting.
 * Errors after a successful start keep the client's default notifications.
 */
class RunicLanguageClient extends LanguageClient {
  public starting = true;
  public override error(message: string, data?: unknown, showNotification?: boolean | "force"): void {
    super.error(message, data, this.starting ? false : showNotification);
  }
}

interface MessageInfo { key: string; localKey: string; isGroup: boolean; path: string[]; logicalPath: string[]; locale: string; locales: string[]; pseudoLocales?: string[]; inputs: string[]; slots: string[] }
const clients = new Map<string, Promise<LanguageClient>>();
const watchers = new Map<string, ForwardedWatchers<vscode.Uri>>();
const reviewWatchers = new Map<string, vscode.Disposable>();
let output: vscode.LogOutputChannel;
let previewPanel: vscode.WebviewPanel | undefined;
let previewSession: PreviewSession | undefined;
let previewTimer: ReturnType<typeof setTimeout> | undefined;
let previewRevision = 0;
let previewSelection = 0;
let refreshPreviewSource: ((uri: vscode.Uri) => void) | undefined;
let statusItem: vscode.StatusBarItem | undefined;
let statusRevision = 0;
let statusTimer: ReturnType<typeof setTimeout> | undefined;
const serverStates = new Map<string, { state: ServerState; failure?: string }>();
const documentInfo = new Map<string, DocumentInfo>();
const startupNotice = new OneTimeNotice();
// runic-translations status per project (its runic.json path); refreshed after saves and watched file changes, since
// the command reads the files on disk. A catalog that does not compile has no report until it compiles again.
const projectStatus = new Map<string, { result?: StatusResult; pending?: Promise<void>; stale: boolean }>();
let statusPanel: vscode.WebviewPanel | undefined;

/** Runs the workspace's local runic-translations tool (the language server's) with other arguments. */
function runLocalTool(folder: vscode.WorkspaceFolder, toolArguments: string[], quiet = false): Promise<string> {
  return new Promise<string>((resolveRun, rejectRun) => {
    const config = vscode.workspace.getConfiguration("runicTranslations", folder.uri);
    const launch = toolLaunch(folder.uri.fsPath, config.get<string>("dotnetPath", "dotnet"), config.get<string>("serverAssembly", ""), toolArguments);
    if (!quiet) output.appendLine(`> runic-translations ${toolArguments.join(" ")}`);
    execFile(launch.command, launch.args, { cwd: launch.cwd, maxBuffer: 16 * 1024 * 1024 }, (error, stdout, stderr) => {
      if (!quiet && stdout) output.appendLine(stdout.trimEnd());
      if (!quiet && stderr) output.appendLine(stderr.trimEnd());
      if (error) rejectRun(Object.assign(new Error(`runic-translations ${toolArguments.slice(0, 2).join(" ")} failed (${(stderr || stdout).trim().split("\n").at(-1) ?? error.message}). See Runic: Show Output.`), { stdout }));
      else resolveRun(stdout);
    });
  });
}

/** Marks every project's status stale; the next status refresh reruns the command. */
function invalidateProjectStatus() {
  for (const entry of projectStatus.values()) entry.stale = true;
  scheduleStatusRefresh();
}

/** The project's status, running the command when there is none or it is stale. */
function projectStatusFor(folder: vscode.WorkspaceFolder, project: string): Promise<StatusResult | undefined> {
  let entry = projectStatus.get(project);
  if (!entry) projectStatus.set(project, entry = { stale: true });
  const current = entry;
  if (current.stale && !current.pending) {
    current.stale = false;
    current.pending = runLocalTool(folder, statusArguments(project), true)
      .then(stdout => { current.result = parseStatus(stdout); }, () => { current.result = undefined; })
      .finally(() => { current.pending = undefined; });
  }
  return (current.pending ?? Promise.resolve()).then(() => current.result);
}

function isRunicDocument(document: vscode.TextDocument): boolean {
  return document.uri.scheme === "file" && (document.languageId === "rmf2" || /(^|\/)runic\.json$/.test(document.uri.path));
}
function setServerState(key: string, state: ServerState, failure?: string) {
  serverStates.set(key, { state, failure });
  refreshStatus();
}
/** Shows the server state, the catalog and the file's locale for the active Runic document. */
function refreshStatus() {
  if (!statusItem) return;
  const item = statusItem;
  const document = vscode.window.activeTextEditor?.document;
  const folder = document && isRunicDocument(document) ? vscode.workspace.getWorkspaceFolder(document.uri) : undefined;
  const key = folder?.uri.toString();
  const entry = key ? serverStates.get(key) : undefined;
  if (!document || !key || !entry) { item.hide(); return; }
  const revision = ++statusRevision;
  const uri = document.uri.toString();
  const show = (info?: DocumentInfo) => {
    if (revision !== statusRevision) return;
    const project = info?.project ? vscode.Uri.parse(info.project).fsPath : undefined;
    const coverage = project ? localeStatus(projectStatus.get(project)?.result, info?.locale) : undefined;
    const presentation = statusPresentation(entry.state, info, entry.failure, coverage);
    item.text = presentation.text;
    item.tooltip = presentation.tooltip;
    item.backgroundColor = presentation.error ? new vscode.ThemeColor("statusBarItem.errorBackground") : undefined;
    item.show();
  };
  show(documentInfo.get(uri));
  if (entry.state !== "running") return;
  void clients.get(key)?.then(client => client.sendRequest<DocumentInfo>("runic/documentInfo", { textDocument: { uri } }))
    .then(info => {
      documentInfo.set(uri, info); show(info);
      // Coverage for the file's locale, from the same status report as the command line.
      if (info.project && info.locale && folder) void projectStatusFor(folder, vscode.Uri.parse(info.project).fsPath).then(() => show(info));
    }, () => { /* The document is not open on the server yet; the next change refreshes. */ });
}
function scheduleStatusRefresh() {
  if (statusTimer) clearTimeout(statusTimer);
  statusTimer = setTimeout(() => { statusTimer = undefined; refreshStatus(); }, 300);
}
function reportStartFailure(key: string, error: unknown) {
  if (!startupNotice.claim(key)) return;
  void vscode.window.showErrorMessage(`Runic Translations could not start its language server. ${errorSummary(error)}`, "Show output", "How to install the tool").then(choice => {
    if (choice === "Show output") output.show(true);
    // The extension page shows the README of the installed version, including "Install the tool".
    else if (choice === "How to install the tool") void vscode.commands.executeCommand("extension.open", extensionId);
  });
}

interface PreviewOptions { locale?: string; exampleIndex?: number; samples?: Samples; requestedLocale?: string; timeZone?: string }

/** Catalog locales, then the configured pseudo-locales, which the server generates from the base locale. */
function localeChoices(locales: string[], pseudoLocales: string[] = []): vscode.QuickPickItem[] {
  return [...locales.map(label => ({ label })), ...pseudoLocales.map(label => ({ label, description: "pseudo-locale, generated from the base locale (read-only)" }))];
}
async function pickLocale(locales: string[], pseudoLocales?: string[]): Promise<string | undefined> {
  return (await vscode.window.showQuickPick(localeChoices(locales, pseudoLocales), { placeHolder: "Preview locale" }))?.label;
}
const hostTimeZone = "Host time zone (local)";
async function pickTimeZone(current?: string): Promise<string | undefined> {
  const zones = typeof Intl.supportedValuesOf === "function" ? Intl.supportedValuesOf("timeZone") : [];
  const choice = await vscode.window.showQuickPick([
    { label: hostTimeZone, description: current ? undefined : "current" },
    { label: "utc", description: current === "utc" ? "current" : undefined },
    ...zones.map(label => ({ label, description: label === current ? "current" : undefined })),
  ], { placeHolder: "Time zone that timeZone=local (the default of :datetime styles) formats in" });
  return choice === undefined ? undefined : choice.label === hostTimeZone ? "" : choice.label;
}
async function chooseSamples(preview: Preview, remembered?: Samples): Promise<Samples | undefined> {
  if (!preview.inputs.length) return {};
  const choice = await vscode.window.showQuickPick(sampleChoices(preview, remembered), { placeHolder: "Preview sample values" });
  if (!choice) return;
  if (choice.samples) return choice.samples;
  const samples: Samples = {};
  const defaults = remembered ?? exampleSamples(preview.examples[0] ?? {}, preview);
  for (const { name } of preview.inputs) {
    const value = await vscode.window.showInputBox({ prompt: `Sample value for ${name}`, value: defaults[name] ?? "" });
    if (value === undefined) return;
    samples[name] = value;
  }
  return samples;
}

async function clientFor(uri: vscode.Uri): Promise<LanguageClient> {
  if (!vscode.workspace.isTrusted) throw new Error("Trust this workspace before running its local language server.");
  const folder = vscode.workspace.getWorkspaceFolder(uri);
  if (!folder) throw new Error("Open the containing workspace folder to use Runic language support.");
  const key = folder.uri.toString();
  let pending = clients.get(key);
  if (!pending) {
    pending = (async () => {
      setServerState(key, "starting");
      try {
        const config = vscode.workspace.getConfiguration("runicTranslations", uri);
        const launch = serverLaunch(folder.uri.fsPath, config.get<string>("dotnetPath", "dotnet"), config.get<string>("serverAssembly", ""));
        // The manifest is also synchronized by the LSP and must be watched even
        // when a project has no translation files yet.
        const manifestWatcher = vscode.workspace.createFileSystemWatcher(new vscode.RelativePattern(folder, "**/runic.json"));
        let client: LanguageClient | undefined;
        let started = false;
        const forward = (uri: vscode.Uri, type: 1 | 2 | 3) => {
          if (started) {
            client?.sendNotification("workspace/didChangeWatchedFiles", { changes: [{ uri: uri.toString(), type }] });
            refreshPreviewSource?.(uri);
            invalidateProjectStatus();
          }
        };
        const watcher = new ForwardedWatchers(
          manifestWatcher,
          () => sourceWatchRoots(folder.uri.fsPath),
          root => vscode.workspace.createFileSystemWatcher(new vscode.RelativePattern(vscode.Uri.file(root), "**/*.{mf2,rmf2}")),
          forward,
        );
        watchers.set(key, watcher);
        // The review file changes the server's RTR0090 warnings and hover review state.
        const reviewWatcher = vscode.workspace.createFileSystemWatcher(new vscode.RelativePattern(folder, "**/*.review.json"));
        reviewWatcher.onDidCreate(uri => forward(uri, 1)); reviewWatcher.onDidChange(uri => forward(uri, 2)); reviewWatcher.onDidDelete(uri => forward(uri, 3));
        reviewWatchers.set(key, reviewWatcher);
        const openingDocuments: Promise<void>[] = [];
        const options: LanguageClientOptions = {
          documentSelector: [{ scheme: "file", language: "rmf2", pattern: `${folder.uri.fsPath.replaceAll("\\", "/")}/**/*.{mf2,rmf2}` }, { scheme: "file", language: "json", pattern: `${folder.uri.fsPath.replaceAll("\\", "/")}/**/runic.json` }],
          workspaceFolder: folder, outputChannel: output,
          initializationOptions: { runicConfigurationSync: true },
          // Watchers are forwarded explicitly so a manifest refresh can replace
          // external mount subscriptions after the client has started.
          synchronize: {},
          middleware: { didOpen: (document, next) => {
            const sent = next(document);
            if (!started) openingDocuments.push(sent);
            return sent;
          } },
        };
        const runic = client = new RunicLanguageClient("runicTranslations", `Runic: ${folder.name}`, { command: launch.command, args: launch.args, options: { cwd: launch.cwd } }, options);
        runic.onDidChangeState(event => {
          // A restart clears the map before stopping the old client; only the current client reports.
          if (clients.get(key) !== pending) return;
          if (event.newState === State.Running) setServerState(key, "running");
          else if (event.newState === State.Stopped && !runic.starting) setServerState(key, "stopped");
        });
        try { await runic.start(); } finally { runic.starting = false; }
        // Initial open notifications are scheduled during client initialization.
        // Custom requests must wait for them, including after a server restart.
        await Promise.all(openingDocuments);
        started = true;
        openingDocuments.length = 0;
        setServerState(key, "running");
        startupNotice.reset(key);
        return runic;
      } catch (error) {
        setServerState(key, "failed", errorSummary(error));
        reportStartFailure(key, error);
        throw error;
      }
    })();
    clients.set(key, pending);
    pending.catch(() => { if (clients.get(key) === pending) { clients.delete(key); watchers.get(key)?.dispose(); watchers.delete(key); reviewWatchers.get(key)?.dispose(); reviewWatchers.delete(key); } });
  }
  return pending;
}
async function active() {
  const editor = vscode.window.activeTextEditor;
  if (!editor || editor.document.languageId !== "rmf2") throw new Error("Place the cursor in a Runic MF2 resource.");
  const client = await clientFor(editor.document.uri);
  const info = await client.sendRequest<MessageInfo>("runic/message", { textDocument: { uri: editor.document.uri.toString() }, position: client.code2ProtocolConverter.asPosition(editor.selection.active) });
  return { editor, client, info };
}
async function apply(client: LanguageClient, command: string, args: unknown[]) {
  const edit = await client.sendRequest<ProtocolEdit>("workspace/executeCommand", { command, arguments: args });
  if (!await vscode.workspace.applyEdit(await client.protocol2CodeConverter.asWorkspaceEdit(edit))) throw new Error("The workspace changed or the edit could not be applied. Retry from the current resource.");
}
async function renamePart(part: "Input" | "Slot") {
  const { editor, client, info } = await active();
  if (info.isGroup) throw new Error("Choose a message rather than a group.");
  const names = part === "Input" ? info.inputs : info.slots;
  const oldName = await vscode.window.showQuickPick(names, { placeHolder: `Choose the ${part.toLowerCase()} to rename in resource sources` });
  if (!oldName) return;
  const name = await vscode.window.showInputBox({ value: oldName, prompt: "Resource sources only. Application bindings and generated API call sites need their language's refactoring support.", validateInput: value => /^[A-Za-z_][A-Za-z0-9_]*$/.test(value) ? undefined : "Use an identifier." });
  if (!name || name === oldName) return;
  await apply(client, `runic.rename${part}`, [editor.document.uri.toString(), info.localKey, oldName, name]);
}
export function activate(context: vscode.ExtensionContext) {
  output = vscode.window.createOutputChannel("Runic Translations", { log: true }); context.subscriptions.push(output);
  const item = statusItem = vscode.window.createStatusBarItem("runicTranslations.status", vscode.StatusBarAlignment.Left, 100);
  item.name = "Runic Translations"; item.command = "runicTranslations.showOutput";
  context.subscriptions.push(item, { dispose: () => { if (statusTimer) clearTimeout(statusTimer); statusItem = undefined; } },
    vscode.commands.registerCommand("runicTranslations.showOutput", () => output.show(true)),
    vscode.window.onDidChangeActiveTextEditor(() => refreshStatus()),
    vscode.workspace.onDidChangeTextDocument(event => { if (isRunicDocument(event.document)) scheduleStatusRefresh(); }),
    // status reads the files on disk, so coverage follows saves (watched changes from other tools also invalidate it).
    vscode.workspace.onDidSaveTextDocument(document => { if (isRunicDocument(document) || document.uri.path.endsWith(".review.json")) invalidateProjectStatus(); }));
  const register = (name: string, action: (options?: PreviewOptions) => Promise<unknown>) => context.subscriptions.push(vscode.commands.registerCommand(`runicTranslations.${name}`, async (options?: PreviewOptions) => { try { return await action(options); } catch (error) { output.appendLine(String(error)); await vscode.window.showErrorMessage(String(error)); } }));
  const session = previewSession = new PreviewSession({
    revision: () => previewRevision,
    load: async target => (await clientFor(vscode.Uri.parse(target.uri))).sendRequest<Preview>("workspace/executeCommand", { command: "runic.preview", arguments: [target.uri, target.key, target.locale] }),
    // The requested locale and time zone go to the runtime formatting; null keeps the catalog locale and the host's zone.
    choose: chooseSamples,
    render: async (target, preview, samples) => resolvePreviewHtml(preview, samples, async () =>
      (await clientFor(vscode.Uri.parse(target.uri))).sendRequest<RenderedPreview>("workspace/executeCommand", { command: "runic.renderPreview", arguments: [target.uri, target.key, target.locale, samples, target.requestedLocale ?? null, target.timeZone ?? null] })),
    publish: state => {
      if (!previewPanel) {
        const panel = previewPanel = vscode.window.createWebviewPanel("runic.preview", "Runic Preview", vscode.ViewColumn.Beside, { enableScripts: false, localResourceRoots: [] });
        panel.onDidDispose(() => { if (previewPanel === panel) { previewPanel = undefined; session.close(); } }, undefined, context.subscriptions);
      }
      previewPanel.title = `${state.target.key} · ${state.target.requestedLocale && state.target.requestedLocale !== state.target.locale ? `${state.target.locale} (${state.target.requestedLocale})` : state.target.locale}`;
      previewPanel.webview.html = state.html;
    },
    failed: error => {
      if (previewPanel) previewPanel.webview.html = `<!doctype html><html><head><meta http-equiv="Content-Security-Policy" content="default-src 'none'"></head><body><h2>Preview unavailable</h2><p>${escapeHtml(String(error))}</p></body></html>`;
    },
  });
  const changed = (uri: vscode.Uri) => {
    if (!/\.(r?mf2)$/.test(uri.path) && !uri.path.endsWith("/runic.json")) return;
    previewRevision++;
    if (previewTimer) clearTimeout(previewTimer);
    previewTimer = setTimeout(() => {
      previewTimer = undefined;
      void session.refresh().catch(error => {
        output.appendLine(String(error));
      });
    }, 150);
  };
  refreshPreviewSource = changed;
  context.subscriptions.push(vscode.workspace.onDidChangeTextDocument(event => changed(event.document.uri)), vscode.workspace.onDidCloseTextDocument(document => changed(document.uri)), { dispose: () => { if (previewTimer) clearTimeout(previewTimer); session.close(); previewPanel?.dispose(); } });
  const start = (document: vscode.TextDocument) => { if (document.languageId === "rmf2" && document.uri.scheme === "file") void clientFor(document.uri).catch(error => output.appendLine(String(error))); };
  context.subscriptions.push(vscode.workspace.onDidOpenTextDocument(start), vscode.workspace.onDidGrantWorkspaceTrust(() => vscode.workspace.textDocuments.forEach(start)));
  vscode.workspace.textDocuments.forEach(start);
  register("restart", async () => { await deactivate(); startupNotice.reset(); vscode.workspace.textDocuments.forEach(start); });
  register("renameResource", async () => {
    const { editor, client, info } = await active();
    const name = await vscode.window.showInputBox({ value: info.path.at(-1), prompt: "Rename in resource sources and runic.json only. Update application call sites separately." });
    if (name && name !== info.path.at(-1)) await apply(client, "runic.renameResource", [editor.document.uri.toString(), info.logicalPath, name]);
  });
  register("renameInput", () => renamePart("Input")); register("renameSlot", () => renamePart("Slot"));
  register("extractGroup", async () => {
    const { editor, client, info } = await active();
    if (!info.isGroup) throw new Error("Place the cursor on the group declaration to extract.");
    await apply(client, "runic.extractGroup", [editor.document.uri.toString(), info.path]);
  });
  register("inlineResource", async () => {
    const { editor, client } = await active();
    const destination = await vscode.window.showOpenDialog({ canSelectMany: false, filters: { "RMF2 resource": ["rmf2"] }, openLabel: "Choose ancestor resource" });
    if (destination?.[0]) await apply(client, "runic.inlineResource", [editor.document.uri.toString(), destination[0].toString()]);
  });
  const openPreview = async (options?: PreviewOptions, changeSamples = false, changeLocale = false, changeFormatting?: "requestedLocale" | "timeZone") => {
    const selection = ++previewSelection;
    session.beginSelection();
    try {
      let target;
      if ((changeSamples || changeLocale || changeFormatting) && session.state) {
        target = session.state.target;
        if (changeLocale) {
          const available = [...(target.locales ?? [target.locale]), ...(target.pseudoLocales ?? [])];
          const locale = options?.locale ?? await pickLocale(target.locales ?? [target.locale], target.pseudoLocales);
          if (!locale || selection !== previewSelection) return;
          if (!available.includes(locale)) throw new Error("Choose an available preview locale.");
          target = { ...target, locale };
        }
        if (changeFormatting === "requestedLocale") {
          const requested = options?.requestedLocale ?? await vscode.window.showInputBox({ value: target.requestedLocale ?? "",
            prompt: `The locale a user requests, such as de-CH for ${target.locale} content: numbers and dates format in it when it shares the language. Empty: ${target.locale}.` });
          if (requested === undefined || selection !== previewSelection) return;
          target = { ...target, requestedLocale: requested.trim() || undefined };
        }
        if (changeFormatting === "timeZone") {
          const zone = options?.timeZone ?? await pickTimeZone(target.timeZone);
          if (zone === undefined || selection !== previewSelection) return;
          target = { ...target, timeZone: zone.trim() || undefined };
        }
        if (changeLocale && options?.requestedLocale !== undefined) target = { ...target, requestedLocale: options.requestedLocale || undefined };
        if (changeLocale && options?.timeZone !== undefined) target = { ...target, timeZone: options.timeZone || undefined };
      } else {
        const { editor, info } = await active();
        if (selection !== previewSelection) return;
        if (info.isGroup) throw new Error("Choose a message to preview.");
        const uri = editor.document.uri.toString();
        const rememberedLocale = session.preferredLocale(uri, info.key);
        const pseudoLocales = info.pseudoLocales ?? [];
        const available = [...info.locales, ...pseudoLocales];
        const locale = options?.locale ?? (rememberedLocale && available.includes(rememberedLocale) ? rememberedLocale : available.length === 1 ? available[0] : await pickLocale(info.locales, pseudoLocales));
        if (!locale || selection !== previewSelection) return;
        if (!available.includes(locale)) throw new Error("Choose an available preview locale.");
        // A regional locale and time zone chosen for the open panel carry over to the next message.
        const formatting = session.state?.target;
        target = { uri, key: info.key, locale, locales: info.locales, pseudoLocales,
          requestedLocale: options?.requestedLocale ?? formatting?.requestedLocale, timeZone: options?.timeZone ?? formatting?.timeZone };
        if (options?.requestedLocale === "") target.requestedLocale = undefined;
        if (options?.timeZone === "") target.timeZone = undefined;
      }
      const select = options?.samples ? async () => options.samples : options?.exampleIndex !== undefined ? async (preview: Preview) => {
        const example = preview.examples[options.exampleIndex!];
        if (!example) throw new Error("Choose an available preview example.");
        return exampleSamples(example, preview);
      } : changeSamples ? chooseSamples : undefined;
      return await session.open(target, select);
    } finally { if (selection === previewSelection) await session.endSelection(); }
  };
  // XLIFF interchange through the project-local tool (runic-translations xliff), the same code as the Translations
  // Editor and the command line. Import is a dry run: it reports each unit's outcome and writes nothing.
  // The project of the active file (the nearest runic.json above it, as the language server resolves it); without one,
  // the workspace's only project, or the one the user picks among them.
  const interchangeFolder = async (): Promise<{ folder: vscode.WorkspaceFolder; project: string } | undefined> => {
    if (!vscode.workspace.isTrusted) throw new Error("Trust this workspace before running its local Runic tool.");
    const active = vscode.window.activeTextEditor?.document.uri;
    const activeFolder = active?.scheme === "file" ? vscode.workspace.getWorkspaceFolder(active) : undefined;
    if (active && activeFolder) {
      const project = nearestProjectManifest(active.fsPath, activeFolder.uri.fsPath);
      if (project) return { folder: activeFolder, project };
    }
    const found = new Set<string>();
    for (const folder of vscode.workspace.workspaceFolders ?? []) {
      const conventional = projectManifest(folder.uri.fsPath);
      if (conventional) found.add(conventional);
    }
    for (const uri of await vscode.workspace.findFiles("**/runic.json", "**/{node_modules,bin,obj,.git}/**", 200)) found.add(uri.fsPath);
    const projects = [...found].sort();
    if (projects.length === 0) throw new Error("No runic.json project in the workspace.");
    let project = projects[0];
    if (projects.length > 1) {
      const picked = await vscode.window.showQuickPick(projects.map(path => ({ label: vscode.workspace.asRelativePath(path), path })),
        { placeHolder: "Choose the Runic translation project" });
      if (!picked) return undefined;
      project = picked.path;
    }
    const folder = vscode.workspace.getWorkspaceFolder(vscode.Uri.file(project));
    if (!folder) throw new Error("The runic.json project is outside the workspace folders.");
    return { folder, project };
  };
  const runTool = (folder: vscode.WorkspaceFolder, toolArguments: string[]) => runLocalTool(folder, toolArguments);
  register("exportXliff", async () => {
    const chosen = await interchangeFolder();
    if (!chosen) return;
    const { folder, project } = chosen;
    const target = await vscode.window.showOpenDialog({ canSelectFiles: false, canSelectFolders: true, canSelectMany: false,
      defaultUri: vscode.Uri.file(dirname(project)), openLabel: "Export XLIFF here" });
    if (!target?.[0]) return;
    const result = await runTool(folder, xliffExportArguments(project, target[0].fsPath));
    const written = result.split("\n").filter(line => line.startsWith("wrote ")).length;
    void vscode.window.showInformationMessage(`Exported ${written} XLIFF document(s) to ${target[0].fsPath}.`);
  });
  register("importXliffDryRun", async () => {
    const chosen = await interchangeFolder();
    if (!chosen) return;
    const { folder, project } = chosen;
    const source = await vscode.window.showOpenDialog({ canSelectMany: false, defaultUri: vscode.Uri.file(join(dirname(project))),
      filters: { "XLIFF 2.1": ["xliff", "xlf"] }, openLabel: "Check import" });
    if (!source?.[0]) return;
    const result = await runTool(folder, xliffImportDryRunArguments(project, source[0].fsPath));
    const summary = result.split("\n").find(line => line.startsWith("dry run: ")) ?? "Dry run finished.";
    output.show(true);
    void vscode.window.showInformationMessage(`${summary} Nothing was written; run runic-translations xliff import or the Translations Editor to apply it.`);
  });
  // Library catalogs (T5d): "Go to Definition" in an override source opens the library's source as a read-only
  // runic-library: document, read through the language server of the application's folder.
  context.subscriptions.push(vscode.workspace.registerTextDocumentContentProvider(libraryScheme, {
    provideTextDocumentContent: async uri => {
      const project = libraryDocumentProject(uri.query);
      if (!project) throw new Error("The library document names no project.");
      const client = await clientFor(vscode.Uri.parse(project));
      return (await client.sendRequest<{ text: string }>("runic/libraryDocument", { uri: uri.toString() })).text;
    },
  }));
  // Override library message…: choose an installed library catalog, a message and a locale; runic-translations overrides
  // add writes translations/overrides/<catalog>/<locale>.rmf2 seeded with the library's text (listing the catalog in
  // runic.json "overrides" when needed), and the file opens at the message.
  register("overrideLibraryMessage", async () => {
    const chosen = await interchangeFolder();
    if (!chosen) return;
    const { folder, project } = chosen;
    const client = await clientFor(vscode.Uri.file(project));
    const result = await client.sendRequest<LibraryCatalogs>("runic/libraryCatalogs", { uri: vscode.Uri.file(project).toString() });
    for (const warning of result.warnings) output.appendLine(warning);
    const catalogs = libraryCatalogChoices(result);
    if (!catalogs.length) throw new Error("No installed package packs a library catalog. Restore the project (dotnet restore, npm install) first.");
    const catalog = (await vscode.window.showQuickPick(catalogs, { placeHolder: "Library catalog to override", matchOnDescription: true }))?.value;
    if (!catalog) return;
    const message = (await vscode.window.showQuickPick(libraryMessageChoices(catalog), { placeHolder: `Message of ${catalog.catalog} to override`, matchOnDescription: true, matchOnDetail: true }))?.value;
    if (!message) return;
    const other = { label: "Another locale…", description: "a locale the library lacks", value: "" };
    let locale = (await vscode.window.showQuickPick([...libraryLocaleChoices(catalog, message), other], { placeHolder: `Locale to override ${message.path} in` }))?.value;
    if (locale === "") locale = await vscode.window.showInputBox({ prompt: "BCP 47 locale tag the application adds, such as fr or de-CH", validateInput: value => /^[A-Za-z]{2,8}([-_][A-Za-z0-9]{1,8})*$/.test(value) ? undefined : "Use a BCP 47 locale tag." });
    if (!locale) return;
    if (!message.overridden.includes(locale)) await runTool(folder, overridesAddArguments(project, catalog.catalog, locale, message.key));
    const path = overrideSourcePath(project, catalog.catalog, locale);
    const document = await vscode.workspace.openTextDocument(vscode.Uri.file(path));
    const line = messageLine(document.getText(), message.path);
    await vscode.window.showTextDocument(document, { selection: new vscode.Range(line, 0, line, 0) });
  });
  // Translation status: the same report as runic-translations status and the Translations Editor's language list.
  register("showStatus", async () => {
    const chosen = await interchangeFolder();
    if (!chosen) return;
    const result = parseStatus(await runTool(chosen.folder, statusArguments(chosen.project)));
    projectStatus.set(chosen.project, { result, stale: false });
    refreshStatus();
    if (!statusPanel) {
      const panel = statusPanel = vscode.window.createWebviewPanel("runic.status", "Translation Status", vscode.ViewColumn.Beside, { enableScripts: false, localResourceRoots: [] });
      panel.onDidDispose(() => { if (statusPanel === panel) statusPanel = undefined; }, undefined, context.subscriptions);
    }
    statusPanel.title = `Translation Status · ${result.projects[0]?.catalog ?? ""}`;
    statusPanel.webview.html = statusHtml(result);
    statusPanel.reveal(undefined, true);
    return result;
  });
  // Migration from .resx or i18next through the project-local tool's importer (the Translations Editor's too): a dry
  // run first, whose outcome is shown, then the import after confirmation.
  register("importCatalog", async () => {
    const chosen = await interchangeFolder();
    if (!chosen) return;
    const { folder, project } = chosen;
    const format = await vscode.window.showQuickPick([
      { label: ".resx", description: "Strings.resx with its Strings.<locale>.resx satellites", from: "resx" as const },
      { label: "i18next", description: "<lng>.json, or a directory of <lng>.json or <lng>/<namespace>.json files", from: "i18next" as const },
    ], { placeHolder: "Import from" });
    if (!format) return;
    const picked = await vscode.window.showOpenDialog({ canSelectMany: false, canSelectFiles: true, canSelectFolders: format.from === "i18next",
      defaultUri: folder.uri, filters: format.from === "resx" ? { ".resx": ["resx"] } : { "i18next JSON": ["json"] }, openLabel: "Check import" });
    if (!picked?.[0]) return;
    const source = picked[0].fsPath;
    const dryRun = await runTool(folder, importArguments(project, format.from, source, { dryRun: true }));
    output.show(true);
    const summary = dryRun.split("\n").find(line => line.startsWith("dry run: ")) ?? "Dry run finished.";
    const kept = dryRun.split("\n").some(line => line.includes("were kept; --overwrite replaces them"));
    const choices = kept ? ["Import", "Import and overwrite existing"] : ["Import"];
    const choice = await vscode.window.showInformationMessage(`${summary} Nothing was written yet; the output lists each message.`, { modal: true }, ...choices);
    if (!choice) return;
    const result = await runTool(folder, importArguments(project, format.from, source, { dryRun: false, overwrite: choice === "Import and overwrite existing" }));
    const written = result.split("\n").filter(line => line.startsWith("wrote ")).length;
    invalidateProjectStatus();
    void vscode.window.showInformationMessage(`Imported ${format.label}: wrote ${written} file(s). See Runic: Show Output for each message.`);
  });
  register("preview", openPreview);
  register("previewSamples", options => openPreview(options, true));
  register("previewLocale", options => openPreview(options, false, true));
  register("previewRequestedLocale", options => openPreview(options, false, false, "requestedLocale"));
  register("previewTimeZone", options => openPreview(options, false, false, "timeZone"));
  refreshStatus();
  return { ready: (uri: vscode.Uri) => clientFor(uri), previewState: () => session.state, statusHtml: () => statusPanel?.webview.html,
    status: () => statusItem?.text, serverState: (uri: vscode.Uri) => { const folder = vscode.workspace.getWorkspaceFolder(uri); return folder && serverStates.get(folder.uri.toString())?.state; } };
}
export async function deactivate() {
  previewSelection++;
  if (previewTimer) clearTimeout(previewTimer);
  previewTimer = undefined;
  previewSession?.close(); previewPanel?.dispose(); previewPanel = undefined;
  const active = [...clients.values()]; clients.clear();
  serverStates.clear(); documentInfo.clear(); projectStatus.clear(); statusPanel?.dispose(); statusPanel = undefined; refreshStatus();
  for (const watcher of watchers.values()) watcher.dispose(); watchers.clear();
  for (const watcher of reviewWatchers.values()) watcher.dispose(); reviewWatchers.clear();
  await Promise.all(active.map(async pending => { try { await (await pending).stop(); } catch { /* Startup failure is already reported. */ } }));
}
