import { spawn, type ChildProcessWithoutNullStreams } from "node:child_process";
import { createInterface } from "node:readline";

/** Protocol spoken by `runic-translations serve`; see the tool's CompileServer. */
export const serveProtocol = "runic-translations-serve/1";

/** One diagnostic of a failed compiler request. */
export interface CompilerDiagnostic {
  readonly code: string;
  readonly severity: "error" | "warning" | "info";
  readonly message: string;
  /** Source location of an RTR translation diagnostic; absent for RCLI diagnostics. */
  readonly path?: string;
  readonly line?: number;
  readonly column?: number;
  readonly endLine?: number;
  readonly endColumn?: number;
  /** Documentation of an RTR translation diagnostic. */
  readonly helpUri?: string;
}

/** A compiler request that ran and failed, with the same text a one-shot invocation prints. */
export class CompilerFailure extends Error {
  readonly exitCode: number;
  readonly diagnostics: readonly CompilerDiagnostic[];
  /** The compiler's failure output, matching the `stderr` of a one-shot invocation error. */
  readonly stderr: string;

  constructor(message: string, exitCode: number, diagnostics: readonly CompilerDiagnostic[]) {
    super(message || `The Runic Translations compiler failed with exit code ${exitCode}.`);
    this.name = "CompilerFailure";
    this.exitCode = exitCode;
    this.diagnostics = diagnostics;
    this.stderr = message;
  }
}

/** The persistent compiler cannot serve this request; the caller should run the one-shot command. */
export class CompilerUnavailable extends Error {
  constructor(message: string) {
    super(message);
    this.name = "CompilerUnavailable";
  }
}

export interface GenerateRequest {
  readonly project: string;
  readonly output: string;
  readonly emit: readonly string[];
}

export interface PersistentCompilerOptions {
  readonly command: string;
  readonly commandArguments: readonly string[];
  readonly cwd: string;
  /** Called once when the service is disabled for the rest of the server's lifetime. */
  readonly onDisabled?: (reason: string) => void;
  /** Milliseconds to wait for the ready line. */
  readonly startTimeout?: number;
  /** Milliseconds to wait for one request before stopping the process and falling back. */
  readonly requestTimeout?: number;
}

interface ServeResponse {
  readonly id: number | null;
  readonly ok: boolean;
  readonly exitCode: number;
  readonly message?: string;
  readonly diagnostics?: readonly CompilerDiagnostic[];
}

interface Pending {
  readonly resolve: (response: ServeResponse) => void;
  readonly reject: (error: Error) => void;
}

const maximumConsecutiveCrashes = 3;
/** Default request timeout: generous, because a cold first compile of a large catalog can take seconds. */
export const defaultRequestTimeout = 120_000;
/** The id of the shutdown request; its reply is ignored. */
const shutdownId = 0;

// Stops the compiler and anything it started, such as the real tool behind `dotnet tool run`.
function killTree(child: ChildProcessWithoutNullStreams): void {
  if (child.exitCode !== null || child.signalCode !== null || child.pid === undefined) return;
  if (process.platform === "win32") {
    const killer = spawn("taskkill", ["/pid", String(child.pid), "/T", "/F"], { stdio: "ignore", windowsHide: true });
    killer.on("error", () => child.kill());
    return;
  }
  try {
    // The compiler leads its own process group (spawned detached), so this reaches its children too.
    process.kill(-child.pid, "SIGKILL");
  } catch {
    child.kill("SIGKILL");
  }
}

function setReferenced(child: ChildProcessWithoutNullStreams, referenced: boolean): void {
  for (const handle of [child, child.stdin, child.stdout, child.stderr] as unknown as { ref?: () => void; unref?: () => void }[])
    referenced ? handle.ref?.() : handle.unref?.();
}
const maximumStderrCharacters = 16 * 1024;

/**
 * Keeps one `runic-translations serve` process for a Vite dev server. The process starts on the
 * first request, restarts after a crash, and is disabled after repeated crashes or when the tool
 * does not support serve mode. Every failure to serve rejects with {@link CompilerUnavailable}, so
 * the caller can fall back to one-shot generation; compiler diagnostics reject with
 * {@link CompilerFailure}.
 */
export class PersistentCompiler {
  readonly #options: PersistentCompilerOptions;
  #process: ChildProcessWithoutNullStreams | undefined;
  #starting: Promise<ChildProcessWithoutNullStreams> | undefined;
  #pending = new Map<number, Pending>();
  #nextId = 1;
  #crashes = 0;
  #disabled: string | undefined;
  #closed = false;
  #stderr = "";

  constructor(options: PersistentCompilerOptions) {
    this.#options = options;
  }

  /** Why the service is disabled, or undefined while it can still serve requests. */
  get disabledReason(): string | undefined {
    return this.#disabled;
  }

  async generate(request: GenerateRequest): Promise<void> {
    const response = await this.#request({ method: "generate", ...request });
    if (!response.ok)
      throw new CompilerFailure(response.message ?? "", response.exitCode, response.diagnostics ?? []);
  }

  /** Asks the process to exit and stops it if it does not within the grace period. */
  async close(gracePeriod = 2000): Promise<void> {
    this.#closed = true;
    const child = this.#process ?? await this.#starting?.catch(() => undefined);
    this.#process = undefined;
    this.#starting = undefined;
    if (!child || child.exitCode !== null || child.signalCode !== null) return;
    const exited = new Promise<void>(resolve => child.once("exit", () => resolve()));
    try {
      child.stdin.end(`${JSON.stringify({ id: shutdownId, method: "shutdown" })}\n`);
    } catch {
      // The process is already gone; the exit wait below resolves immediately.
    }
    let timer: NodeJS.Timeout | undefined;
    const timeout = new Promise<"timeout">(resolve => { timer = setTimeout(() => resolve("timeout"), gracePeriod); });
    if (await Promise.race([exited, timeout]) === "timeout") killTree(child);
    clearTimeout(timer);
  }

  async #request(body: Readonly<Record<string, unknown>>): Promise<ServeResponse> {
    if (this.#closed) throw new CompilerUnavailable("The persistent compiler was shut down.");
    if (this.#disabled) throw new CompilerUnavailable(this.#disabled);
    const child = await this.#start();
    const id = this.#nextId++;
    const timeout = this.#options.requestTimeout ?? defaultRequestTimeout;
    return new Promise<ServeResponse>((resolve, reject) => {
      // A hung compile must not block later changes: stop the process; the next request restarts it.
      const timer = setTimeout(() => {
        if (!this.#pending.delete(id)) return;
        reject(new CompilerUnavailable(`The persistent compiler did not answer within ${timeout} ms.`));
        if (this.#process === child) this.#process = undefined;
        killTree(child);
      }, timeout);
      this.#pending.set(id, {
        resolve: response => { clearTimeout(timer); resolve(response); },
        reject: error => { clearTimeout(timer); reject(error); },
      });
      setReferenced(child, true);
      child.stdin.write(`${JSON.stringify({ id, ...body })}\n`, error => {
        if (!error || !this.#pending.delete(id)) return;
        clearTimeout(timer);
        reject(new CompilerUnavailable(`Could not send a request to the persistent compiler: ${error.message}`));
      });
    });
  }

  #start(): Promise<ChildProcessWithoutNullStreams> {
    if (this.#process) return Promise.resolve(this.#process);
    this.#starting ??= this.#spawn().then(child => {
      this.#process = child;
      this.#starting = undefined;
      return child;
    }, (error: Error) => {
      this.#starting = undefined;
      this.#disable(error.message);
      throw new CompilerUnavailable(error.message);
    });
    return this.#starting;
  }

  #spawn(): Promise<ChildProcessWithoutNullStreams> {
    const { command, commandArguments, cwd } = this.#options;
    return new Promise((resolve, reject) => {
      let ready = false;
      let settled = false;
      const child = spawn(command, [...commandArguments, "serve"], {
        cwd, stdio: ["pipe", "pipe", "pipe"], windowsHide: true, detached: process.platform !== "win32",
      });
      this.#stderr = "";
      const fail = (message: string) => {
        if (settled) return;
        settled = true;
        clearTimeout(timer);
        killTree(child);
        reject(new Error(message));
      };
      const timer = setTimeout(() => fail("The persistent compiler did not start in time."), this.#options.startTimeout ?? 60_000);
      child.on("error", error => fail(`Could not start the persistent compiler: ${error.message}`));
      child.stderr.setEncoding("utf8");
      child.stderr.on("data", (chunk: string) => { this.#stderr = (this.#stderr + chunk).slice(-maximumStderrCharacters); });
      const lines = createInterface({ input: child.stdout, crlfDelay: Infinity });
      lines.on("line", line => {
        let message: Record<string, unknown>;
        try {
          message = JSON.parse(line) as Record<string, unknown>;
        } catch {
          if (!ready) fail("The compiler does not support serve mode.");
          else this.#protocolError(child, "The persistent compiler wrote a line that is not JSON.");
          return;
        }
        if (!ready) {
          if (message.event !== "ready" || message.protocol !== serveProtocol)
            return fail(`The compiler speaks an unsupported serve protocol '${String(message.protocol)}'.`);
          ready = true;
          settled = true;
          clearTimeout(timer);
          // A dev server that exits without closing must not wait for an idle compiler; the compiler
          // exits on its own when its standard input closes. Requests reference it while they run.
          setReferenced(child, false);
          resolve(child);
          return;
        }
        // After close only the exit matters; the shutdown reply carries no result.
        if (this.#closed || message.id === shutdownId) return;
        // A null id answers a request the server could not read (for example an oversized one). Requests
        // are answered in order, so it is the oldest outstanding request.
        const id = message.id === null ? this.#pending.keys().next().value : message.id;
        const pending = typeof id === "number" ? this.#pending.get(id) : undefined;
        if (!pending) return this.#protocolError(child, "The persistent compiler answered an unknown request.");
        this.#pending.delete(id as number);
        this.#crashes = 0;
        if (this.#pending.size === 0) setReferenced(child, false);
        if (message.id === null) pending.reject(new CompilerUnavailable(`The persistent compiler rejected a request: ${String(message.message)}`));
        else pending.resolve(message as unknown as ServeResponse);
      });
      child.on("exit", (code, signal) => {
        const detail = this.#stderr.trim();
        const reason = `The persistent compiler exited (${signal ?? `code ${code}`})${detail ? `: ${detail}` : "."}`;
        if (!ready) return fail(reason);
        if (this.#process === child) this.#process = undefined;
        if (!this.#closed && ++this.#crashes >= maximumConsecutiveCrashes)
          this.#disable(`The persistent compiler exited ${this.#crashes} times in a row. ${reason}`);
        this.#rejectPending(new CompilerUnavailable(reason));
      });
    });
  }

  #protocolError(child: ChildProcessWithoutNullStreams, message: string): void {
    this.#rejectPending(new CompilerUnavailable(message));
    if (this.#process === child) this.#process = undefined;
    killTree(child);
  }

  #rejectPending(error: Error): void {
    const pending = [...this.#pending.values()];
    this.#pending.clear();
    for (const item of pending) item.reject(error);
  }

  #disable(reason: string): void {
    if (this.#disabled) return;
    this.#disabled = reason;
    this.#options.onDisabled?.(reason);
  }
}
