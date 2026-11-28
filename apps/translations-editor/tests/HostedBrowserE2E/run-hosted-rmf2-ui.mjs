// Independently rerunnable Linux acceptance against a prebuilt editor assembly.
// It never builds, installs dependencies, or downloads a browser.
import { createHash } from "node:crypto";
import { spawn } from "node:child_process";
import { mkdtemp, mkdir, writeFile, readFile, rm, access } from "node:fs/promises";
import { resolve, join, dirname } from "node:path";
import { fileURLToPath } from "node:url";

const [assemblyArgument] = process.argv.slice(2);
if (!assemblyArgument || !process.env.PLAYWRIGHT_CHROMIUM_EXECUTABLE_PATH) throw new Error("Expected prebuilt editor DLL argument and project-pinned PLAYWRIGHT_CHROMIUM_EXECUTABLE_PATH.");
if (process.platform !== "linux") throw new Error("This Linux runner isolates editor state with XDG_STATE_HOME; use a platform-specific isolated profile on other systems.");
const assembly = resolve(assemblyArgument);
await access(assembly);
const assemblySha256 = createHash("sha256").update(await readFile(assembly)).digest("hex");
const fixtureDirectory = dirname(fileURLToPath(import.meta.url));
const reportDirectory = process.env.RUNIC_EDITOR_HOSTED_E2E_REPORT_DIR
  ? resolve(process.env.RUNIC_EDITOR_HOSTED_E2E_REPORT_DIR)
  : await mkdtemp(join(process.env.RUNIC_EDITOR_HOSTED_E2E_TEMP_ROOT ?? "/tmp", "runic-editor-hosted-report-"));
await mkdir(reportDirectory, { recursive: true });
for (const name of ["browser-metadata.json", "failure.png", "failure.html", "failure.json", "conflict-en.png", "repaired-en.png", "library-catalog.png"])
  await rm(join(reportDirectory, name), { force: true });
const temporaryRoot = await mkdtemp(join(process.env.RUNIC_EDITOR_HOSTED_E2E_TEMP_ROOT ?? "/tmp", "runic-editor-hosted-work-"));
const workspace = join(temporaryRoot, "workspace");
const state = join(temporaryRoot, "state");
const driver = join(fixtureDirectory, "hosted-rmf2-ui.mjs");
const childEnvironment = { ...process.env, XDG_STATE_HOME: state, RUNIC_EDITOR_HOSTED_E2E_REPORT_DIR: reportDirectory };
// Spell checking (stages/420-spelling.mjs): the host's system language is English, whose dictionaries the editor
// bundles; downloads and the system's Hunspell folders under XDG_DATA_HOME, XDG_DATA_DIRS and DICPATH are empty
// folders of this run; and HTTPS goes to a closed port, so no dictionary is ever downloaded.
const spellingData = join(temporaryRoot, "spelling");
const hostEnvironment = {
  ...childEnvironment,
  LANGUAGE: "en_US", LC_MESSAGES: "en_US.UTF-8",
  XDG_DATA_HOME: join(spellingData, "data"), XDG_DATA_DIRS: join(spellingData, "data-dirs"), DICPATH: join(spellingData, "dicpath"),
  HTTPS_PROXY: "http://127.0.0.1:9", https_proxy: "http://127.0.0.1:9", NO_PROXY: "127.0.0.1,localhost", no_proxy: "127.0.0.1,localhost",
};
delete hostEnvironment.LC_ALL;
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
    else reject(new Error(`Hosted RMF2 browser driver ${timedOut ? "timed out" : `exited ${code}`}.`));
  });
});
try {
  await runDriver(["--prepare", workspace]);
  // The editor state an earlier version left: a colour palette, which this version no longer owns (D074). The host
  // drops it on load instead of quarantining the record; the density-setting stage checks that.
  const stateDirectory = join(state, "RunicArtifex", "Runic.Translations.Editor");
  await mkdir(stateDirectory, { recursive: true });
  await writeFile(join(stateDirectory, "editor-state-v1.json"), JSON.stringify({
    $schema: "runic.translations.editor-local-state/1",
    entries: [{ key: "runic-translations.theme-mode", value: "dark" }, { key: "runic-translations.theme-palette", value: "moss" }],
  }));
  const ready = new Promise((resolveReady, reject) => {
    // Standard input stays open: a line written to it is the Runic Desktop host's request to stop serving.
    host = spawn("dotnet", [assembly, "serve", "--workspace", workspace], {
      cwd: dirname(assembly), env: hostEnvironment, stdio: ["pipe", "pipe", "pipe"],
    });
    const timer = setTimeout(() => reject(new Error("Hosted editor did not announce its loopback URL within 30 seconds.")), 30_000);
    // The serve-only protocol's first RUNIC_APPLICATION_URL= line, the window's capability URL. It admits one browser,
    // so the driver opens this full URL; the surface's root ("/") does not carry its token.
    const consume = (bytes) => {
      hostOutput += bytes;
      const match = /^RUNIC_APPLICATION_URL=(http:\/\/(?:127\.0\.0\.1|localhost):\d+\/\S*)$/m.exec(hostOutput);
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
    // A line on standard input stops serving; SIGTERM, then SIGKILL, when the host does not exit.
    host.stdin.end("stop\n");
    const terminate = setTimeout(() => host.kill("SIGTERM"), 5000);
    const kill = setTimeout(() => host.kill("SIGKILL"), 10_000);
    await closed;
    clearTimeout(terminate);
    clearTimeout(kill);
  }
  await writeFile(join(reportDirectory, "host.log"), hostOutput);
  await writeFile(join(reportDirectory, "browser.log"), browserOutput);
  const browserMetadata = await readFile(join(reportDirectory, "browser-metadata.json"), "utf8").then(JSON.parse).catch(() => ({}));
  await writeFile(join(reportDirectory, "result.json"), JSON.stringify({
    passed, assembly, assemblySha256, sourceRevision: process.env.GITHUB_SHA ?? null,
    platform: process.platform, architecture: process.arch, browserVersion: browserMetadata.browserVersion ?? null,
    runner: "real-hosted-rmf2-ui", workspaceRetained: !passed ? workspace : undefined,
  }, null, 2) + "\n");
  if (passed) await rm(temporaryRoot, { recursive: true });
  console.log(`Hosted RMF2 acceptance ${passed ? "passed" : "failed"}; reports: ${reportDirectory}`);
  if (!passed) console.log(`Task-owned failed workspace retained for diagnosis: ${temporaryRoot}`);
}
