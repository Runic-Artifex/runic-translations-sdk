import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";

/** Read the real RMF2 resource source without applying an English fallback. */
export async function readUiMessages(locale) {
  const content = await readFile(new URL(`../../EditorResources/${locale}.rmf2`, import.meta.url), "utf8");
  return parseUiMessages(content, locale);
}

export function parseUiMessages(content, locale = "fixture") {
  const messages = Object.create(null);
  const groups = [];
  const lines = content.replaceAll("\r\n", "\n").split("\n");
  let active;
  const commit = () => {
    if (active === undefined) return;
    const key = [...active.path, active.name].join("_");
    assert.ok(!Object.hasOwn(messages, key), `${locale}: RMF2 paths collide at ${key}.`);
    messages[key] = active.lines.join("\n").replace(/^\s+/, "").trimEnd();
    active = undefined;
  };
  for (const line of lines) {
    const group = /^(\s*)([A-Za-z_][A-Za-z0-9_]*)\s*\{$/.exec(line);
    const entry = /^(\s*)([A-Za-z_][A-Za-z0-9_]*)\s*=\s*(.*)$/.exec(line);
    if (group !== null) { commit(); groups.push(group[2]); continue; }
    if (/^\s*}\s*$/.test(line)) { commit(); groups.pop(); continue; }
    if (entry !== null) { commit(); active = { path: [...groups], name: entry[2], lines: [entry[3]] }; continue; }
    if (active !== undefined) active.lines.push(line);
  }
  commit();
  return messages;
}
