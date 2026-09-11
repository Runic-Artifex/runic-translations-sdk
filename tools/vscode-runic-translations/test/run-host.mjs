import { mkdtemp, mkdir, cp, writeFile, readFile, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { resolve, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { spawn } from 'node:child_process';
const extension = fileURLToPath(new URL('../', import.meta.url));
const repository = resolve(extension, '../..');
const temporary = await mkdtemp(join(tmpdir(), 'runic-vscode-host-'));
let passed = false;
const environment = { ...process.env, ELECTRON_ENABLE_LOGGING: '1', RUNIC_VSCODE_RESULT_FILE: join(temporary, 'result.json'), XDG_RUNTIME_DIR: join(temporary, 'runtime') };
if (process.env.WAYLAND_DISPLAY && !process.env.WAYLAND_DISPLAY.startsWith('/') && process.env.XDG_RUNTIME_DIR) environment.WAYLAND_DISPLAY = join(process.env.XDG_RUNTIME_DIR, process.env.WAYLAND_DISPLAY);
await mkdir(environment.XDG_RUNTIME_DIR, { mode: 0o700 });
for (const name of ['VSCODE_IPC_HOOK_CLI', 'VSCODE_CLI', 'ELECTRON_RUN_AS_NODE']) delete environment[name];
try {
  const workspace = join(temporary, 'workspace');
  await cp(join(repository, 'specs/translations/examples/rmf2'), workspace, { recursive: true });
  await mkdir(join(workspace, '.vscode'));
  await writeFile(join(workspace, '.vscode/settings.json'), JSON.stringify({ 'runicTranslations.serverAssembly': join(repository, 'tools/dotnet-runic-translations/bin/Debug/net10.0/dotnet-runic-translations.dll') }));
  const child = spawn(process.env.VSCODE_EXECUTABLE_PATH || 'code', ['--new-window', '--wait', '--no-sandbox', '--disable-gpu', '--disable-extensions', '--disable-workspace-trust', '--skip-welcome', '--skip-release-notes', '--user-data-dir', join(temporary, 'profile'), '--extensions-dir', join(temporary, 'extensions'), `--extensionDevelopmentPath=${extension}`, `--extensionTestsPath=${join(extension, 'test/host.cjs')}`, workspace], { stdio: 'inherit', env: environment });
  const timeout = setTimeout(() => child.kill('SIGTERM'), 90000);
  const result = await new Promise((accept, reject) => { child.once('error', reject); child.once('exit', accept); });
  clearTimeout(timeout);
  const report = JSON.parse(await readFile(join(temporary, "result.json"), "utf8"));
  if (!report.passed) throw new Error(report.message || "Extension-host assertions did not finish.");
  console.log(report.message);
  if (result !== 0) throw new Error(`VS Code host tests exited with ${result}`);
  passed = true;
} finally {
  if (!passed) { await mkdir(join(extension, 'artifacts'), { recursive: true }); await cp(join(temporary, 'profile'), join(extension, 'artifacts/host-failure'), { recursive: true }).catch(() => {}); }
  await rm(temporary, { recursive: true, force: true });
}
