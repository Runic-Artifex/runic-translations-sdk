import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";

/** Read the real locale container with Bun's TOML parser, without English fallback. */
export async function readUiMessages(locale) {
  const content = await readFile(new URL(`../../EditorResources/${locale}.toml`, import.meta.url), "utf8");
  return parseUiMessages(content, locale);
}

export function parseUiMessages(content, locale = "fixture") {
  const messages = Object.create(null);
  function collect(table, path = [], row = false) {
    for (const [segment, value] of Object.entries(table)) {
      if (row && segment === "_id") continue;
      const keyPath = [...path, segment];
      const key = keyPath.join("_");
      if (typeof value === "string") {
        assert.ok(!Object.hasOwn(messages, key), `${locale}: TOML paths collide at ${key}.`);
        // Preserve decoded values exactly: terminal newlines can be meaningful MF2.
        messages[key] = value;
      } else if (Array.isArray(value)) {
        const identifiers = new Set();
        for (const item of value) {
          assert.ok(item !== null && Object.prototype.toString.call(item) === "[object Object]",
            `${locale}: ${key} requires table rows.`);
          assert.ok(typeof item._id === "string" && /^[A-Za-z_][A-Za-z0-9_]*$/.test(item._id),
            `${locale}: ${key} requires a valid row _id.`);
          assert.ok(!identifiers.has(item._id), `${locale}: ${key} has duplicate row _id ${item._id}.`);
          identifiers.add(item._id);
          collect(item, [...keyPath, item._id], true);
        }
      } else {
        assert.ok(value !== null && Object.prototype.toString.call(value) === "[object Object]",
          `${locale}: ${key} must be a message string or a TOML table.`);
        collect(value, keyPath);
      }
    }
  }
  collect(Bun.TOML.parse(content));
  return messages;
}
