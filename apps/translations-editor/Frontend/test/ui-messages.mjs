import { readdir, readFile } from "node:fs/promises";

export async function readUiMessages(locale) {
  const directory = new URL(`../../EditorResources/${locale}/`, import.meta.url);
  const files = (await readdir(directory)).filter((file) => file.endsWith(".mf2")).sort();
  return Object.fromEntries(await Promise.all(files.map(async (file) => [
    file.slice(0, -4),
    (await readFile(new URL(file, directory), "utf8")).replace(/\n$/, ""),
  ])));
}
