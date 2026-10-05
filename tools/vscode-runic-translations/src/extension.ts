import * as vscode from "vscode";
import { LanguageClient, type LanguageClientOptions, type WorkspaceEdit as ProtocolEdit } from "vscode-languageclient/node";
import { serverLaunch, sourceWatchRoots } from "./server.js";
import { escapeHtml, resolvePreviewHtml, type Preview, type RenderedPreview } from "./preview.js";
import { PreviewSession, exampleSamples, sampleChoices, type Samples } from "./preview-session.js";
import { ForwardedWatchers } from "./watchers.js";

interface MessageInfo { key: string; localKey: string; isGroup: boolean; path: string[]; logicalPath: string[]; locale: string; locales: string[]; inputs: string[]; slots: string[] }
const clients = new Map<string, Promise<LanguageClient>>();
const watchers = new Map<string, ForwardedWatchers<vscode.Uri>>();
let output: vscode.LogOutputChannel;
let previewPanel: vscode.WebviewPanel | undefined;
let previewSession: PreviewSession | undefined;
let previewTimer: ReturnType<typeof setTimeout> | undefined;
let previewRevision = 0;
let previewSelection = 0;
let refreshPreviewSource: ((uri: vscode.Uri) => void) | undefined;

interface PreviewOptions { locale?: string; exampleIndex?: number; samples?: Samples }
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
        }
      };
      const watcher = new ForwardedWatchers(
        manifestWatcher,
        () => sourceWatchRoots(folder.uri.fsPath),
        root => vscode.workspace.createFileSystemWatcher(new vscode.RelativePattern(vscode.Uri.file(root), "**/*.{mf2,rmf2}")),
        forward,
      );
      watchers.set(key, watcher);
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
      client = new LanguageClient("runicTranslations", `Runic: ${folder.name}`, { command: launch.command, args: launch.args, options: { cwd: launch.cwd } }, options);
      await client.start();
      // Initial open notifications are scheduled during client initialization.
      // Custom requests must wait for them, including after a server restart.
      await Promise.all(openingDocuments);
      started = true;
      openingDocuments.length = 0;
      return client;
    })();
    clients.set(key, pending);
    pending.catch(() => { if (clients.get(key) === pending) { clients.delete(key); watchers.get(key)?.dispose(); watchers.delete(key); } });
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
  const register = (name: string, action: (options?: PreviewOptions) => Promise<unknown>) => context.subscriptions.push(vscode.commands.registerCommand(`runicTranslations.${name}`, async (options?: PreviewOptions) => { try { return await action(options); } catch (error) { output.appendLine(String(error)); await vscode.window.showErrorMessage(String(error)); } }));
  const session = previewSession = new PreviewSession({
    revision: () => previewRevision,
    load: async target => (await clientFor(vscode.Uri.parse(target.uri))).sendRequest<Preview>("workspace/executeCommand", { command: "runic.preview", arguments: [target.uri, target.key, target.locale] }),
    choose: chooseSamples,
    render: async (target, preview, samples) => resolvePreviewHtml(preview, samples, async () =>
      (await clientFor(vscode.Uri.parse(target.uri))).sendRequest<RenderedPreview>("workspace/executeCommand", { command: "runic.renderPreview", arguments: [target.uri, target.key, target.locale, samples] })),
    publish: state => {
      if (!previewPanel) {
        const panel = previewPanel = vscode.window.createWebviewPanel("runic.preview", "Runic Preview", vscode.ViewColumn.Beside, { enableScripts: false, localResourceRoots: [] });
        panel.onDidDispose(() => { if (previewPanel === panel) { previewPanel = undefined; session.close(); } }, undefined, context.subscriptions);
      }
      previewPanel.title = `${state.target.key} · ${state.target.locale}`;
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
  register("restart", async () => { await deactivate(); vscode.workspace.textDocuments.forEach(start); });
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
  const openPreview = async (options?: PreviewOptions, changeSamples = false, changeLocale = false) => {
    const selection = ++previewSelection;
    session.beginSelection();
    try {
      let target;
      if ((changeSamples || changeLocale) && session.state) {
        target = session.state.target;
        if (changeLocale) {
          const locale = options?.locale ?? await vscode.window.showQuickPick(target.locales ?? [target.locale], { placeHolder: "Preview locale" });
          if (!locale || selection !== previewSelection) return;
          if (!(target.locales ?? [target.locale]).includes(locale)) throw new Error("Choose an available preview locale.");
          target = { ...target, locale };
        }
      } else {
        const { editor, info } = await active();
        if (selection !== previewSelection) return;
        if (info.isGroup) throw new Error("Choose a message to preview.");
        const uri = editor.document.uri.toString();
        const rememberedLocale = session.preferredLocale(uri, info.key);
        const locale = options?.locale ?? (rememberedLocale && info.locales.includes(rememberedLocale) ? rememberedLocale : info.locales.length === 1 ? info.locales[0] : await vscode.window.showQuickPick(info.locales, { placeHolder: "Preview locale" }));
        if (!locale || selection !== previewSelection) return;
        if (!info.locales.includes(locale)) throw new Error("Choose an available preview locale.");
        target = { uri, key: info.key, locale, locales: info.locales };
      }
      const select = options?.samples ? async () => options.samples : options?.exampleIndex !== undefined ? async (preview: Preview) => {
        const example = preview.examples[options.exampleIndex!];
        if (!example) throw new Error("Choose an available preview example.");
        return exampleSamples(example, preview);
      } : changeSamples ? chooseSamples : undefined;
      return await session.open(target, select);
    } finally { if (selection === previewSelection) await session.endSelection(); }
  };
  register("preview", openPreview);
  register("previewSamples", options => openPreview(options, true));
  register("previewLocale", options => openPreview(options, false, true));
  return { ready: (uri: vscode.Uri) => clientFor(uri), previewState: () => session.state };
}
export async function deactivate() {
  previewSelection++;
  if (previewTimer) clearTimeout(previewTimer);
  previewTimer = undefined;
  previewSession?.close(); previewPanel?.dispose(); previewPanel = undefined;
  const active = [...clients.values()]; clients.clear();
  for (const watcher of watchers.values()) watcher.dispose(); watchers.clear();
  await Promise.all(active.map(async pending => { try { await (await pending).stop(); } catch { /* Startup failure is already reported. */ } }));
}
