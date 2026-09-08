// Independently rerunnable Linux acceptance against a prebuilt editor assembly.
// Does not build, install dependencies, or acquire browsers.
import { createHash } from "node:crypto";
import { spawn } from "node:child_process";
import { mkdtemp, mkdir, writeFile, readFile, rm, access } from "node:fs/promises";
import { resolve, join, dirname } from "node:path";
import { fileURLToPath } from "node:url";

const [assemblyArgument] = process.argv.slice(2);
if (!assemblyArgument || !process.env.WEBUI_BROWSER_PATH) throw new Error("Expected prebuilt editor DLL argument and project-pinned WEBUI_BROWSER_PATH.");
if (process.platform !== "linux") throw new Error("This Linux runner isolates editor state with XDG_STATE_HOME; use a platform-specific isolated profile on other systems.");
const assembly = resolve(assemblyArgument);
await access(assembly);
const assemblySha256 = createHash("sha256").update(await readFile(assembly)).digest("hex");
const fixtureDirectory = dirname(fileURLToPath(import.meta.url));
const reportDirectory = process.env.RUNIC_EDITOR_HOSTED_E2E_REPORT_DIR
  ? resolve(process.env.RUNIC_EDITOR_HOSTED_E2E_REPORT_DIR)
  : await mkdtemp(join(process.env.RUNIC_EDITOR_HOSTED_E2E_TEMP_ROOT ?? "/tmp", "runic-editor-hosted-report-"));
await mkdir(reportDirectory, { recursive: true });
// A rerun must not leave a prior run's screenshot or version in its new receipt.
for (const name of ["browser-metadata.json", "failure.png", "failure.html", "failure.json", "conflict-de.png", "repaired-en.png"])
  await rm(join(reportDirectory, name), { force: true });
const temporaryRoot = await mkdtemp(join(process.env.RUNIC_EDITOR_HOSTED_E2E_TEMP_ROOT ?? "/tmp", "runic-editor-hosted-work-"));
const workspace = join(temporaryRoot, "workspace");
const state = join(temporaryRoot, "state");
const driver = join(fixtureDirectory, "hosted-toml-ui.mjs");
const childEnvironment = { ...process.env, XDG_STATE_HOME: state, RUNIC_EDITOR_HOSTED_E2E_REPORT_DIR: reportDirectory };
let host;
let hostOutput = "";
let browserOutput = "";
let passed = false;
const runDriver = (argumentsValue) => new Promise((resolveRun, reject) => {
  const child = spawn(process.execPath, [driver, ...argumentsValue], { env: childEnvironment, stdio: ["ignore", "pipe", "pipe"] });
  let timedOut = false;
  const timer = setTimeout(() => { timedOut = true; child.kill("SIGTERM"); }, 120_000);
  child.stdout.on("data", (bytes) => { browserOutput += bytes; process.stdout.write(bytes); });
  child.stderr.on("data", (bytes) => { browserOutput += bytes; process.stderr.write(bytes); });
  child.on("error", (error) => { clearTimeout(timer); reject(error); });
  child.on("exit", (code) => {
    clearTimeout(timer);
    if (code === 0 && !timedOut) resolveRun();
    else reject(new Error(`Hosted TOML browser driver ${timedOut ? "timed out" : `exited ${code}`}.`));
  });
});
try {
  await runDriver(["--prepare", workspace]);
  const ready = new Promise((resolveReady, reject) => {
    host = spawn("dotnet", [assembly, "serve", "--workspace", workspace], {
      cwd: dirname(assembly), env: { ...childEnvironment, RUNIC_EDITOR_HOSTED_E2E_ASSETS: fixtureDirectory },
      stdio: ["ignore", "pipe", "pipe"],
    });
    const timer = setTimeout(() => reject(new Error("Hosted editor did not announce its loopback URL within 30 seconds.")), 30_000);
    const consume = (bytes) => {
      hostOutput += bytes;
      const match = /Runic Translations Editor is serving .* at (http:\/\/127\.0\.0\.1:\d+\/?)/.exec(hostOutput);
      if (match) { clearTimeout(timer); resolveReady(match[1]); }
    };
    host.stdout.on("data", consume);
    host.stderr.on("data", consume);
    host.on("error", (error) => { clearTimeout(timer); reject(error); });
    host.on("exit", (code) => { clearTimeout(timer); reject(new Error(`Hosted editor exited ${code} before readiness.\n${hostOutput}`)); });
  });
  await runDriver([await ready, workspace]);
  if (createHash("sha256").update(await readFile(assembly)).digest("hex") !== assemblySha256) throw new Error("Editor assembly changed during acceptance.");
  passed = true;
} finally {
  if (host?.pid !== undefined && host.exitCode === null) {
    const closed = new Promise((resolveClose) => host.once("exit", resolveClose));
    host.kill("SIGTERM");
    const timer = setTimeout(() => host.kill("SIGKILL"), 5000);
    await closed;
    clearTimeout(timer);
  }
  await writeFile(join(reportDirectory, "host.log"), hostOutput);
  await writeFile(join(reportDirectory, "browser.log"), browserOutput);
  const browserMetadata = await readFile(join(reportDirectory, "browser-metadata.json"), "utf8").then(JSON.parse).catch(() => ({}));
  await writeFile(join(reportDirectory, "result.json"), JSON.stringify({
    passed, assembly, assemblySha256, sourceRevision: process.env.GITHUB_SHA ?? null,
    platform: process.platform, architecture: process.arch, browserVersion: browserMetadata.browserVersion ?? null,
    runner: "real-hosted-toml-ui", workspaceRetained: !passed ? workspace : undefined,
  }, null, 2) + "\n");
  if (passed) await rm(temporaryRoot, { recursive: true });
  console.log(`Hosted TOML acceptance ${passed ? "passed" : "failed"}; reports: ${reportDirectory}`);
  if (!passed) console.log(`Task-owned failed workspace retained for diagnosis: ${temporaryRoot}`);
}
