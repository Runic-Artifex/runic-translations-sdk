// The driver's side of the adapter protocol (PROTOCOL.md): one implementation under test as a child process that
// reads one JSON request per line on stdin and writes one JSON response per line on stdout. stderr is the adapter's
// log; the driver keeps its tail for failure output.
import type { Subprocess } from "bun";
import { names } from "./names";

export class AdapterError extends Error {
  constructor(readonly code: string, message: string) {
    super(message);
  }
}

export type AdapterSpec = { command: string[]; cwd?: string; env?: Record<string, string> };

export class Adapter {
  private proc!: Subprocess<"pipe", "pipe", "pipe">;
  private seq = 0;
  private pending = new Map<number, { resolve: (v: any) => void; reject: (e: Error) => void; timer: Timer }>();
  private stderrTail: string[] = [];
  private buffer = "";
  private exited = false;
  describe: any;

  constructor(private readonly spec: AdapterSpec, private readonly log?: (line: string) => void) {}

  async start(): Promise<any> {
    this.exited = false;
    this.proc = Bun.spawn(this.spec.command, {
      cwd: this.spec.cwd,
      env: { ...process.env, ...this.spec.env },
      stdin: "pipe",
      stdout: "pipe",
      stderr: "pipe",
    });
    this.pump(this.proc.stdout, (line) => this.onLine(line));
    this.pump(this.proc.stderr, (line) => {
      this.stderrTail.push(line);
      if (this.stderrTail.length > 40) this.stderrTail.shift();
      this.log?.(line);
    });
    this.proc.exited.then((code) => {
      this.exited = true;
      for (const [, p] of this.pending) {
        clearTimeout(p.timer);
        p.reject(new AdapterError("crashed", `the adapter exited with code ${code}\n${this.tail()}`));
      }
      this.pending.clear();
    });
    // The first request is always describe: it checks the protocol before anything else runs.
    this.describe = await this.call("describe", {}, 120_000);
    if (this.describe?.protocol !== names.adapterProtocol && !String(this.describe?.protocol).endsWith(".conformance-adapter/1"))
      throw new AdapterError("protocol", `the adapter speaks ${this.describe?.protocol}, not ${names.adapterProtocol}`);
    return this.describe;
  }

  tail(): string {
    return this.stderrTail.length ? "  adapter stderr (last lines):\n" + this.stderrTail.map((l) => "    " + l).join("\n") : "";
  }

  clearTail() {
    this.stderrTail = [];
  }

  get alive() {
    return !this.exited;
  }

  // One request. `budgetMs` bounds the wait for the response; a stuck adapter is killed and the call fails.
  call(op: string, args: object, budgetMs = 30_000): Promise<any> {
    if (this.exited) return Promise.reject(new AdapterError("crashed", "the adapter is not running"));
    const seq = ++this.seq;
    return new Promise((resolve, reject) => {
      const timer = setTimeout(() => {
        this.pending.delete(seq);
        reject(new AdapterError("timeout", `the adapter did not answer ${op} within ${budgetMs} ms`));
        this.kill();
      }, budgetMs);
      this.pending.set(seq, { resolve, reject, timer });
      this.proc.stdin.write(JSON.stringify({ seq, op, args }) + "\n");
      this.proc.stdin.flush();
    });
  }

  kill() {
    try {
      this.proc.kill();
    } catch {}
  }

  async stop() {
    if (this.exited) return;
    try {
      await this.call("shutdown", {}, 5_000);
    } catch {}
    this.proc.stdin.end();
    await Promise.race([this.proc.exited, Bun.sleep(5_000)]);
    this.kill();
  }

  private onLine(line: string) {
    if (!line.trim()) return;
    let message: any;
    try {
      message = JSON.parse(line);
    } catch {
      this.stderrTail.push(`(stdout, not protocol) ${line}`);
      return;
    }
    const p = this.pending.get(message.seq);
    if (!p) return;
    this.pending.delete(message.seq);
    clearTimeout(p.timer);
    if (message.error) p.reject(new AdapterError(message.error.code ?? "failed", message.error.message ?? "failed"));
    else p.resolve(message.ok);
  }

  private async pump(stream: ReadableStream<Uint8Array>, onLine: (line: string) => void) {
    const decoder = new TextDecoder();
    let buffer = "";
    for await (const chunk of stream) {
      buffer += decoder.decode(chunk, { stream: true });
      let index: number;
      while ((index = buffer.indexOf("\n")) >= 0) {
        onLine(buffer.slice(0, index).replace(/\r$/, ""));
        buffer = buffer.slice(index + 1);
      }
    }
    if (buffer) onLine(buffer);
  }
}
