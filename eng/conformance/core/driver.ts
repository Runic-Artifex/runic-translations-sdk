// The driver command, for any repository: runs, receipts, the status check, `cases`, `lint` and `list`
// (eng/conformance/README.md). A repository's own eng/conformance/cli.ts registers its corpus modules and calls main();
// the core never names a module, so it can be vendored unchanged into runic-cli-sdk and runic-translations-sdk
// (core/README.md).
import { appendFileSync, existsSync, mkdirSync, readFileSync, writeFileSync } from "node:fs";
import { dirname, join, relative, resolve } from "node:path";
import { Adapter, AdapterError } from "./adapter";
import { formatCases, type Case } from "./cases";
import { names } from "./names";
import { compare, formatStatus, lintStatus, statusFromReceipt, type Outcome, type Receipt, type StatusFile } from "./results";

export type RunContext = { supports: Set<string>; roles: Set<string>; timing: { receiveMs: number } };

// One corpus format the driver runs. `root` is the corpus directory, relative to the repository root; it holds the
// corpus's cases.json and status/.
export type CorpusModule = {
  root: string;
  load: (root: string) => Case[];
  run: (adapter: Adapter, c: Case, context: RunContext) => Promise<Outcome>;
};

export type Implementation = {
  id: string;
  corpus: string;
  roles: string[];
  build?: string[];
  command: string[];
  env?: Record<string, string>;
  status: string;
};

// `root` is the repository root, `dir` the directory with implementations.json (eng/conformance), `corpora` the
// repository's modules by corpus id, and `command` how the repository runs the driver, for its messages.
export type DriverOptions = { root: string; dir: string; corpora: Record<string, CorpusModule>; command?: string };

export function createDriver({ root: ROOT, dir, corpora, command: name = "bun run conformance" }: DriverOptions) {
  const ARTIFACTS = join(ROOT, "artifacts/conformance");

  const expand = (text: string) =>
    text.replace(/\$\{(\w+)\}/g, (_, name) => (name === "ROOT" ? ROOT : name === "CONFIGURATION" ? (process.env.CONFIGURATION ?? "Debug") : (process.env[name] ?? "")));

  // implementations.json; a repository without one has no implementations yet.
  function implementations(): Record<string, Implementation> {
    const path = join(dir, "implementations.json");
    if (!existsSync(path)) return {};
    const raw = JSON.parse(readFileSync(path, "utf8"));
    const out: Record<string, Implementation> = {};
    for (const [id, entry] of Object.entries<any>(raw.implementations ?? {}))
      out[id] = { id, ...entry, build: entry.build?.map(expand), command: entry.command.map(expand) };
    return out;
  }

  // A deviation names a row of a difference table as <file>#<anchor>, the file relative to the repository root.
  function anchorExists(reference: string): boolean {
    const [file, anchor] = reference.split("#");
    const path = join(ROOT, file);
    return existsSync(path) && readFileSync(path, "utf8").includes(`id="${anchor}"`);
  }

  async function main(args: string[] = process.argv.slice(2)): Promise<number> {
    const flag = (name: string) => {
      const index = args.indexOf(name);
      return index >= 0 ? args[index + 1] : undefined;
    };
    const has = (name: string) => args.includes(name);
    const tty = process.stdout.isTTY;
    const color = (code: number) => (text: string) => (tty ? `\x1b[${code}m${text}\x1b[0m` : text);
    const red = color(31), green = color(32), yellow = color(33), dim = color(2);

    function module(impl: Implementation): CorpusModule {
      const found = corpora[impl.corpus];
      if (!found) throw new Error(`unknown corpus ${impl.corpus} of ${impl.id}; this repository's modules: ${Object.keys(corpora).join(", ") || "none"}`);
      return found;
    }

    function corpusRoot(impl: Implementation) {
      return resolve(flag("--corpus") ?? join(ROOT, module(impl).root));
    }

    function build(impl: Implementation) {
      if (!impl.build || has("--no-build")) return;
      console.log(dim(`building the ${impl.id} adapter: ${impl.build.join(" ")}`));
      const result = Bun.spawnSync(impl.build, { cwd: ROOT, env: { ...process.env, ...impl.env }, stdout: "pipe", stderr: "pipe" });
      if (result.exitCode !== 0) {
        process.stdout.write(result.stdout.toString() + result.stderr.toString());
        throw new Error(`the ${impl.id} adapter did not build (exit ${result.exitCode})`);
      }
    }

    async function run(impl: Implementation): Promise<number> {
      const corpusModule = module(impl);
      const root = corpusRoot(impl);
      const corpus = corpusModule.load(root);
      let cases = corpus;
      const only = flag("--only"), set = flag("--set");
      if (only) cases = cases.filter((c) => c.id.includes(only));
      if (set) cases = cases.filter((c) => c.set === set || c.set === `scenarios.${set}`);
      build(impl);
      const scale = Number(flag("--timeout-scale") ?? process.env.RAX_TEST_TIMEOUT_SCALE ?? 1);
      const timing = { receiveMs: Math.round(5000 * scale) };
      const jobs = Math.max(1, Number(flag("--jobs") ?? 1));
      const verbose = has("--verbose");
      mkdirSync(ARTIFACTS, { recursive: true });
      const logFile = join(ARTIFACTS, `${impl.id}.adapter.log`);
      writeFileSync(logFile, "");
      const log = (line: string) => appendFileSync(logFile, line + "\n");
      const spec = { command: impl.command, cwd: ROOT, env: impl.env };

      const started = Date.now();
      const results: Record<string, Outcome> = {};
      let describe: any;
      const queue = [...cases];
      let done = 0;
      const worker = async () => {
        let adapter = new Adapter(spec, log);
        describe = await adapter.start();
        const supports = new Set<string>([...(describe.features ?? []), ...(describe.capabilities ?? [])]);
        const roles = new Set<string>(impl.roles);
        for (let c = queue.shift(); c; c = queue.shift()) {
          const t0 = performance.now();
          let outcome: Outcome;
          adapter.clearTail();
          try {
            outcome = await corpusModule.run(adapter, c, { supports, roles, timing });
          } catch (e) {
            const message = e instanceof Error ? e.message : String(e);
            outcome = { outcome: "fail", detail: `harness: ${message}${e instanceof AdapterError ? "\n" + adapter.tail() : ""}` };
            if (!adapter.alive) {
              adapter = new Adapter(spec, log);
              await adapter.start();
            }
          }
          outcome.ms = Math.round(performance.now() - t0);
          results[c.id] = outcome;
          done++;
          if (verbose || outcome.outcome === "fail") printOutcome(c, outcome);
          else if (tty) process.stdout.write(`\r${dim(`${done}/${cases.length}`)}`);
        }
        await adapter.stop();
      };
      await Promise.all(Array.from({ length: jobs }, worker));
      if (tty) process.stdout.write("\r");

      const receipt: Receipt = {
        format: names.receipt,
        corpus: { id: impl.corpus, root: relative(ROOT, root) || ".", commit: gitHead() },
        implementation: { id: impl.id, name: describe?.implementation?.name, version: describe?.implementation?.version },
        adapter: { protocol: describe?.protocol, command: impl.command.map((part) => part.replace(ROOT, ".")) },
        run: { started: new Date(started).toISOString(), durationMs: Date.now() - started, filter: only ?? set },
        results: Object.fromEntries(cases.map((c) => [c.id, results[c.id]])),
      };
      const receiptPath = flag("--receipt") ?? join(ARTIFACTS, `${impl.id}.receipt.json`);
      writeFileSync(receiptPath, JSON.stringify(receipt, null, 1) + "\n");
      summary(cases, results);
      console.log(dim(`receipt: ${relative(ROOT, receiptPath)}   adapter log: ${relative(ROOT, logFile)}   ${((Date.now() - started) / 1000).toFixed(1)} s`));

      const statusPath = join(root, impl.status);
      if (has("--write-status")) {
        const previous: StatusFile | undefined = existsSync(statusPath) ? JSON.parse(readFileSync(statusPath, "utf8")) : undefined;
        const status = statusFromReceipt(receipt, impl.roles, previous?.implementation.maturity ?? "development", previous);
        if (only || set) for (const [id, entry] of Object.entries(previous?.cases ?? {})) status.cases[id] ??= entry;
        status.cases = Object.fromEntries(corpus.filter((c) => status.cases[c.id]).map((c) => [c.id, status.cases[c.id]]));
        mkdirSync(dirname(statusPath), { recursive: true });
        writeFileSync(statusPath, formatStatus(status));
        console.log(`wrote ${relative(ROOT, statusPath)}`);
        return 0;
      }
      const staleCases = casesProblem(impl.corpus, root, corpus);
      if (staleCases) console.log(yellow(staleCases));
      return check(impl, statusPath, receipt, corpus.map((c) => c.id), Boolean(only || set)) || (staleCases ? 1 : 0);
    }

    function check(impl: Implementation, statusPath: string, receipt: Receipt, ids: string[], filtered: boolean): number {
      if (!existsSync(statusPath)) {
        console.log(yellow(`no status file ${relative(ROOT, statusPath)}: run with --write-status to start one`));
        return 1;
      }
      const status: StatusFile = JSON.parse(readFileSync(statusPath, "utf8"));
      const lint = lintStatus(status, anchorExists);
      const findings = compare(receipt, status, ids, filtered);
      for (const problem of lint) console.log(yellow(`status: ${problem}`));
      const byKind = new Map<string, number>();
      for (const f of findings) byKind.set(f.kind, (byKind.get(f.kind) ?? 0) + 1);
      for (const f of findings) {
        const tag = f.kind === "regression" ? red("REGRESSION") : f.kind === "fixed" ? green("FIXED") : yellow(f.kind.toUpperCase());
        console.log(`${tag} ${f.id}: ${f.text.split("\n")[0]}`);
      }
      // FIXED blocks only in CI (the ratchet); locally it asks for the status to be updated. Lint problems of a
      // development implementation, such as TODO issues, do not block its local runs.
      const blocking = findings.filter((f) => f.kind !== "fixed" || process.env.CI).length + (status.implementation.maturity !== "development" ? lint.length : 0);
      const label = relative(ROOT, statusPath);
      console.log(
        blocking
          ? red(`status check: ${[...byKind].map(([k, n]) => `${n} ${k}`).concat(lint.length ? [`${lint.length} status-file problems`] : []).join(", ")}`)
          : green(`status check: the run matches ${label}${byKind.get("fixed") ? ` (${byKind.get("fixed")} fixed: update the status)` : ""}`),
      );
      return blocking ? 1 : 0;
    }

    const casesPath = (root: string) => join(root, "cases.json");

    // A problem with the corpus's committed case list, or null when it is current.
    function casesProblem(corpus: string, root: string, cases: Case[]): string | null {
      const path = casesPath(root);
      if (!existsSync(path)) return `${relative(ROOT, path)} is missing: run ${name} cases --write`;
      return readFileSync(path, "utf8") === formatCases(names.cases, corpus, cases)
        ? null
        : `${relative(ROOT, path)} is out of date: run ${name} cases --write`;
    }

    function cases(): number {
      let stale = 0;
      if (!Object.keys(corpora).length) console.log(dim("no corpus modules yet"));
      for (const [id, corpusModule] of Object.entries(corpora)) {
        const root = resolve(flag("--corpus") ?? join(ROOT, corpusModule.root));
        const list = corpusModule.load(root);
        if (has("--write")) {
          writeFileSync(casesPath(root), formatCases(names.cases, id, list));
          console.log(`wrote ${relative(ROOT, casesPath(root))} (${list.length} cases)`);
          continue;
        }
        const problem = casesProblem(id, root, list);
        console.log(problem ? yellow(problem) : green(`${relative(ROOT, casesPath(root))} is current (${list.length} cases)`));
        if (problem) stale++;
      }
      return stale ? 1 : 0;
    }

    // Every status file of every implementation, without running anything: its own rules, and a status for exactly the
    // cases of its corpus.
    function lint(): number {
      let problems = 0;
      const all = Object.values(implementations());
      if (!all.length) console.log(dim("no implementations yet"));
      for (const impl of all) {
        const root = corpusRoot(impl);
        const path = join(root, impl.status);
        const label = relative(ROOT, path);
        if (!existsSync(path)) {
          console.log(yellow(`${label}: missing`));
          problems++;
          continue;
        }
        const status: StatusFile = JSON.parse(readFileSync(path, "utf8"));
        const ids = module(impl).load(root).map((c) => c.id);
        const found = [
          ...lintStatus(status, anchorExists),
          ...ids.filter((id) => !status.cases[id]).map((id) => `${id}: no status for this case`),
          ...Object.keys(status.cases).filter((id) => !ids.includes(id)).map((id) => `${id}: the corpus has no such case`),
        ];
        if (status.implementation.id !== impl.id) found.push(`implementation.id is ${status.implementation.id}, not ${impl.id}`);
        for (const problem of found) console.log(yellow(`${label}: ${problem}`));
        if (!found.length) console.log(green(`${label}: ${ids.length} cases, no problems`));
        problems += found.length;
      }
      return problems ? 1 : 0;
    }

    function printOutcome(c: Case, o: Outcome) {
      const mark = o.outcome === "pass" ? green("pass") : o.outcome === "fail" ? red("FAIL") : o.outcome === "unsupported" ? yellow("unsupported") : dim("excluded");
      console.log(`${mark} ${c.id} ${dim(`${o.ms} ms`)}`);
      if (o.outcome === "fail" && o.detail) {
        console.log(o.detail.split("\n").map((line) => "    " + line).join("\n"));
        console.log(dim(`    file: ${c.file}   rerun: --only ${c.id}`));
      }
    }

    function summary(cases: Case[], results: Record<string, Outcome>) {
      const rows = new Map<string, Record<string, number>>();
      for (const c of cases) {
        const row = rows.get(c.set) ?? rows.set(c.set, { pass: 0, fail: 0, unsupported: 0, excluded: 0 }).get(c.set)!;
        row[results[c.id]?.outcome ?? "fail"]++;
      }
      const total = { pass: 0, fail: 0, unsupported: 0, excluded: 0 };
      const cells = (r: Record<string, number>) =>
        `${String(r.pass).padStart(5)} ${(r.fail ? red : String)(String(r.fail).padStart(5))} ${String(r.unsupported).padStart(5)} ${String(r.excluded).padStart(5)}`;
      console.log(`${"set".padEnd(40)} ${"pass".padStart(5)} ${"fail".padStart(5)} ${"unsup".padStart(5)} ${"excl".padStart(5)}`);
      for (const [set, row] of rows) {
        for (const k of Object.keys(total) as (keyof typeof total)[]) total[k] += row[k];
        console.log(`${set.padEnd(40)} ${cells(row)}`);
      }
      console.log(`${"total".padEnd(40)} ${cells(total)}`);
    }

    function gitHead(): string | undefined {
      const result = Bun.spawnSync(["git", "-C", ROOT, "rev-parse", "HEAD"]);
      return result.exitCode === 0 ? result.stdout.toString().trim() : undefined;
    }

    const pad = " ".repeat(`usage: ${name} `.length);
    const usage = `usage: ${name} <implementation> [--only <text>] [--set <set>] [--jobs <n>] [--verbose] [--no-build]
${pad}[--receipt <file>] [--write-status] [--corpus <dir>] [--timeout-scale <x>]
       ${name} list | cases [--write] | lint | check <implementation> [--receipt <file>]`;
    const none = !Object.keys(corpora).length;

    const [command, target] = args;
    const all = implementations();
    switch (command) {
      case undefined:
      case "--help":
      case "-h":
        console.log(usage);
        // A repository whose vendored core has no modules yet: running the command is not an error.
        if (none) console.log("no corpus modules yet: this repository registers none in eng/conformance/cli.ts");
        return command || none ? 0 : 2;
      case "list":
        if (!Object.keys(all).length) console.log(dim("no implementations yet"));
        for (const i of Object.values(all)) console.log(`${i.id.padEnd(16)} ${i.corpus.padEnd(10)} ${i.roles.join(",").padEnd(10)} ${i.command.join(" ").replace(ROOT, ".")}`);
        return 0;
      case "cases":
        return cases();
      case "lint":
        return lint();
      case "check": {
        const impl = all[target];
        if (!impl) throw new Error(`unknown implementation ${target}; see ${name} list`);
        const receipt: Receipt = JSON.parse(readFileSync(flag("--receipt") ?? join(ARTIFACTS, `${impl.id}.receipt.json`), "utf8"));
        const root = corpusRoot(impl);
        return check(impl, join(root, impl.status), receipt, module(impl).load(root).map((c) => c.id), Boolean(receipt.run.filter));
      }
      default: {
        const id = command === "run" ? target : command;
        const impl = all[id];
        if (!impl) {
          console.log(`unknown implementation ${id}${none ? " (this repository has no corpus modules yet)" : ""}\n${usage}`);
          return 2;
        }
        return run(impl);
      }
    }
  }

  return { root: ROOT, corpora, implementations, anchorExists, main };
}
