const assert = require('node:assert/strict');
const vscode = require('vscode');
let stage = 'activation and native providers';
exports.run = async function () {
  const root = vscode.workspace.workspaceFolders[0].uri;
  const en = vscode.Uri.joinPath(root, 'en.rmf2');
  const document = await vscode.workspace.openTextDocument(en);
  const editor = await vscode.window.showTextDocument(document);
  const extension = vscode.extensions.getExtension('runic-artifex.runic-translations');
  assert.ok(extension);
  const api = await extension.activate(); const client = await api.ready(en);
  const position = document.positionAt(document.getText().indexOf('payment ='));
  editor.selection = new vscode.Selection(position, position);
  const symbols = await vscode.commands.executeCommand('vscode.executeDocumentSymbolProvider', en);
  assert.ok(symbols.some(symbol => symbol.name === 'payment'));
  const info = await client.sendRequest('runic/message', { textDocument: { uri: en.toString() }, position });
  assert.equal(info.key, 'payment'); assert.ok(info.slots.includes('retry'));
  const definitions = await vscode.commands.executeCommand('vscode.executeDefinitionProvider', en, position);
  assert.equal(definitions.length, 2);
  const preview = await client.sendRequest('workspace/executeCommand', { command: 'runic.preview', arguments: [en.toString(), 'payment', 'de'] });
  assert.equal(preview.ast.astVersion, 5); assert.ok(preview.examples.length > 0);
  assert.deepEqual(preview.inputs.map(input => input.name), ['count', 'tone']);
  const rendered = await client.sendRequest('workspace/executeCommand', {command: 'runic.renderPreview', arguments: [en.toString(), 'payment', 'en', {count: '1', tone: 'positive'}]});
  assert.ok(JSON.stringify(rendered.runs).includes('runic:action'));
  assert.ok(JSON.stringify(rendered.runs).includes('positive'));
  await assert.rejects(client.sendRequest('workspace/executeCommand', {command: 'runic.renderPreview', arguments: [en.toString(), 'payment', 'en', {count: 'invalid', tone: 'positive'}]}));
  stage = 'all examples and session sample reuse';
  // Two samples in an unsaved buffer must both be exposed by the compiler and
  // selecting the second one must skip per-input prompts and survive reopening.
  const exampleEdit = new vscode.WorkspaceEdit();
  const examplePosition = document.positionAt(document.getText().indexOf('payment ='));
  exampleEdit.insert(en, examplePosition, '@example {"count":0,"tone":"neutral"}\n');
  assert.equal(await vscode.workspace.applyEdit(exampleEdit), true);
  const samplePosition = document.positionAt(document.getText().indexOf('payment ='));
  editor.selection = new vscode.Selection(samplePosition, samplePosition);
  const examples = await client.sendRequest('workspace/executeCommand', {command: 'runic.preview', arguments: [en.toString(), 'payment', 'en']});
  assert.equal(examples.examples.length, 2);
  const selected = await vscode.commands.executeCommand('runicTranslations.preview', {locale: 'en', exampleIndex: 1});
  assert.deepEqual(selected.samples, {count: '0', tone: 'neutral'});
  assert.ok(selected.html.includes('Ready'));
  assert.ok(!selected.html.includes('<button disabled>'));
  await vscode.window.showTextDocument(document, vscode.ViewColumn.One);
  const reused = await vscode.commands.executeCommand('runicTranslations.preview');
  assert.deepEqual(reused.samples, selected.samples, 'Reopening must reuse locale and values without prompts');
  const changedSamples = await vscode.commands.executeCommand('runicTranslations.previewSamples', {exampleIndex: 0});
  assert.deepEqual(changedSamples.samples, {count: '1', tone: 'positive'});
  assert.ok(changedSamples.html.includes('<button disabled>'));
  await vscode.commands.executeCommand('runicTranslations.previewSamples', {exampleIndex: 1});
  const german = await vscode.commands.executeCommand('runicTranslations.previewLocale', {locale: 'de', exampleIndex: 1});
  assert.equal(german.target.locale, 'de');
  assert.ok(german.html.includes('Bereit'));
  await vscode.commands.executeCommand('runicTranslations.previewLocale', {locale: 'en'});
  const latestEdit = new vscode.WorkspaceEdit();
  stage = 'latest unsaved preview refresh';
  for (const match of document.getText().matchAll(/Ready/g)) {
    latestEdit.replace(en, new vscode.Range(document.positionAt(match.index), document.positionAt(match.index + match[0].length)), 'Latest unsaved');
  }
  assert.equal(await vscode.workspace.applyEdit(latestEdit), true);
  assert.equal(document.isDirty, true);
  await waitFor(() => api.previewState()?.html.includes('Latest unsaved'), 'Preview did not refresh the unsaved buffer');
  assert.deepEqual(api.previewState().samples, {count: '0', tone: 'neutral'});
  const canonical = await client.sendRequest('workspace/executeCommand', {command: 'runic.renderPreview', arguments: [en.toString(), 'payment', 'en', selected.samples]});
  assert.ok(JSON.stringify(canonical.runs).includes('Latest unsaved'));
  stage = 'rename and server restart';
  const rename = await vscode.commands.executeCommand('vscode.executeDocumentRenameProvider', en, samplePosition, 'receipt');
  assert.ok(rename.size >= 3, 'Rename must include source locales and slot configuration');
  stage = 'renamed unsaved preview contract';
  assert.equal(await vscode.workspace.applyEdit(rename), true);
  const nextPosition = document.positionAt(document.getText().indexOf('receipt ='));
  const changed = await client.sendRequest('runic/message', { textDocument: { uri: en.toString() }, position: nextPosition });
  assert.equal(changed.key, 'receipt');
  const nextPreview = await client.sendRequest('workspace/executeCommand', { command: 'runic.preview', arguments: [en.toString(), 'receipt', 'en'] });
  assert.equal(nextPreview.ast.astVersion, 5, 'Rename did not retain the selected semantic contract');
  assert.deepEqual(nextPreview.inputs.map(input => input.name), ['count', 'tone']);
  const nextRendered = await client.sendRequest('workspace/executeCommand', {command: 'runic.renderPreview', arguments: [en.toString(), 'receipt', 'en', {count: '1', tone: 'positive'}]});
  assert.ok(JSON.stringify(nextRendered.runs).includes('runic:action'));
  assert.ok(JSON.stringify(nextRendered.runs).includes('positive'));
  stage = 'server restart synchronization';
  await vscode.commands.executeCommand('runicTranslations.restart');
  const restarted = await api.ready(en);
  const afterRestart = await restarted.sendRequest('runic/message', { textDocument: { uri: en.toString() }, position: nextPosition });
  assert.equal(afterRestart.key, 'receipt', 'Restart lost an unsaved resource');
  const app = vscode.Uri.joinPath(root, 'app.cs');
  await vscode.workspace.fs.writeFile(app, Buffer.from('class App { }'));
  await assert.rejects(restarted.sendRequest('textDocument/rename', { textDocument: {uri: en.toString()}, position: nextPosition, newName: 'unsafe' }), /Rename refused/);
  const explicit = await restarted.sendRequest('workspace/executeCommand', {command: 'runic.renameResource', arguments: [en.toString(), ['receipt'], 'confirmed']});
  assert.ok(explicit.documentChanges.length >= 3);
  assert.ok(!JSON.stringify(explicit).includes('app.cs'));
  await vscode.workspace.saveAll(false);
  require('node:fs').writeFileSync(process.env.RUNIC_VSCODE_RESULT_FILE, JSON.stringify({passed:true,message:'PASS VS Code extension-host assertions.'}));
  console.log('PASS VS Code activation, native providers, all preview examples, sample reuse, unsaved refresh, source edits and restart.');
};
async function waitFor(predicate, message) {
  const deadline = Date.now() + 10000;
  while (Date.now() < deadline) { if (predicate()) return; await new Promise(resolve => setTimeout(resolve, 50)); }
  assert.fail(message);
}
const execute = exports.run;
exports.run = async () => {
  try { await execute(); }
  catch (error) { require('node:fs').writeFileSync(process.env.RUNIC_VSCODE_RESULT_FILE, JSON.stringify({ passed: false, message: `${stage}: ${error.stack || String(error)}` })); throw error; }
};
