import * as vscode from "vscode";
import { LanguageClient, type LanguageClientOptions, type WorkspaceEdit as ProtocolEdit } from "vscode-languageclient/node";
import { serverLaunch } from "./server.js";
import { previewHtml, type Preview } from "./preview.js";

interface MessageInfo { key: string; localKey: string; isGroup: boolean; path: string[]; logicalPath: string[]; locale: string; locales: string[]; inputs: string[]; slots: string[] }
const clients = new Map<string, Promise<LanguageClient>>();
const watchers = new Map<string, vscode.FileSystemWatcher>();
let output: vscode.OutputChannel;

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
      const watcher = vscode.workspace.createFileSystemWatcher(new vscode.RelativePattern(folder, "**/{runic.json,*.rmf2}")); watchers.set(key, watcher);
      const options: LanguageClientOptions = {
        documentSelector: [{ scheme: "file", language: "rmf2", pattern: `${folder.uri.fsPath.replaceAll("\\", "/")}/**/*.rmf2` }, { scheme: "file", language: "json", pattern: `${folder.uri.fsPath.replaceAll("\\", "/")}/**/runic.json` }],
        workspaceFolder: folder, outputChannel: output,
        initializationOptions: { runicConfigurationSync: true },
        synchronize: { fileEvents: watcher },
      };
      const client = new LanguageClient("runicTranslations", `Runic: ${folder.name}`, { command: launch.command, args: launch.args, options: { cwd: launch.cwd } }, options);
      await client.start();
      return client;
    })();
    clients.set(key, pending);
    pending.catch(() => { if (clients.get(key) === pending) { clients.delete(key); watchers.get(key)?.dispose(); watchers.delete(key); } });
  }
  return pending;
}
async function active() {
  const editor = vscode.window.activeTextEditor;
  if (!editor || editor.document.languageId !== "rmf2") throw new Error("Place the cursor in an RMF2 resource.");
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
  output = vscode.window.createOutputChannel("Runic Translations"); context.subscriptions.push(output);
  const register = (name: string, action: () => Promise<unknown>) => context.subscriptions.push(vscode.commands.registerCommand(`runicTranslations.${name}`, async () => { try { return await action(); } catch (error) { output.appendLine(String(error)); await vscode.window.showErrorMessage(String(error)); } }));
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
  register("preview", async () => {
    const { editor, client, info } = await active();
    if (info.isGroup) throw new Error("Choose a message to preview.");
    const locale = await vscode.window.showQuickPick(info.locales, { placeHolder: "Preview locale" });
    if (!locale) return;
    const preview = await client.sendRequest<Preview>("workspace/executeCommand", { command: "runic.preview", arguments: [editor.document.uri.toString(), info.key, locale] });
    const samples: Record<string, string> = {};
    const example = preview.examples[0] ?? {};
    for (const name of Object.keys(preview.ast.inputs)) {
      const value = await vscode.window.showInputBox({ prompt: `Sample value for ${name}`, value: name in example ? String(example[name]) : "" });
      if (value === undefined) return;
      samples[name] = value;
    }
    const html = previewHtml(preview, samples);
    const panel = vscode.window.createWebviewPanel("runic.preview", `${info.key} · ${locale}`, vscode.ViewColumn.Beside, { enableScripts: false, localResourceRoots: [] });
    panel.webview.html = html;
  });
  return { ready: (uri: vscode.Uri) => clientFor(uri) };
}
export async function deactivate() {
  const active = [...clients.values()]; clients.clear();
  for (const watcher of watchers.values()) watcher.dispose(); watchers.clear();
  await Promise.all(active.map(async pending => { try { await (await pending).stop(); } catch { /* Startup failure is already reported. */ } }));
}
