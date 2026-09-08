import { readFile } from "node:fs/promises";

/** Read the real locale container with Bun's TOML parser, without English fallback. */
export async function readUiMessages(locale) {
  const content = await readFile(new URL(`../../EditorResources/${locale}.toml`, import.meta.url), "utf8");
  return Object.fromEntries(Object.entries(Bun.TOML.parse(content)).map(([key, message]) => [key, message.replace(/\n$/, "")]));
}
