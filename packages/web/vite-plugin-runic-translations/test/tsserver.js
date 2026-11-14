// Drives the workspace's tsserver over its stdio protocol, as an editor does: Go to Definition at 1-based positions of a file.
// Shared by the plugin tests and the compiler suite's declaration map case.
import { spawn } from "node:child_process";
import { readFile } from "node:fs/promises";
import { createRequire } from "node:module";

const tsserver = createRequire(import.meta.url).resolve("typescript/lib/tsserver.js");

/** The definitions at each position as "path:line:offset" strings (1-based), in the order of the positions. */
export async function definitions(file, positions) {
  const server = spawn(process.execPath, [tsserver, "--disableAutomaticTypingAcquisition"], { stdio: ["pipe", "pipe", "inherit"] });
  let buffer = Buffer.alloc(0), sequence = 0;
  const pending = new Map();
  server.stdout.on("data", chunk => {
    buffer = Buffer.concat([buffer, chunk]);
    for (;;) {
      const text = buffer.toString("latin1");
      const header = /Content-Length: (\d+)\r\n\r\n/.exec(text);
      if (!header) break;
      const start = header.index + header[0].length, length = Number(header[1]);
      if (buffer.length < start + length) break;
      const message = JSON.parse(buffer.subarray(start, start + length).toString("utf8"));
      buffer = buffer.subarray(start + length);
      if (message.type === "response" && pending.has(message.request_seq)) {
        pending.get(message.request_seq)(message);
        pending.delete(message.request_seq);
      }
    }
  });
  const request = (command, args) => new Promise(resolve => {
    const id = ++sequence;
    pending.set(id, resolve);
    server.stdin.write(JSON.stringify({ seq: id, type: "request", command, arguments: args }) + "\n");
  });
  try {
    // open has no response; the definition requests that follow it are answered in order.
    server.stdin.write(JSON.stringify({ seq: ++sequence, type: "request", command: "open", arguments: { file, fileContent: await readFile(file, "utf8") } }) + "\n");
    const results = [];
    for (const [line, offset] of positions) {
      const response = await request("definition", { file, line, offset });
      if (!response.success) throw new Error(`tsserver definition ${line}:${offset}: ${response.message}`);
      results.push(response.body.map(item => `${item.file}:${item.start.line}:${item.start.offset}`));
    }
    return results;
  } finally {
    server.kill();
  }
}
