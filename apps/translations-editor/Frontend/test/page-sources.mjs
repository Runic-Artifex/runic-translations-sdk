// The editor page's sources: the composition root (src/routes/+page.svelte) and the modules it composes, the screens
// (src/lib/screens/), dialogs (src/lib/dialogs/), controllers (src/lib/state/) and the page's own helpers. Checks of
// what the page does read these, wherever in them the code now lives.
import { readdir, readFile } from "node:fs/promises";

const source = new URL("../src/", import.meta.url);
const pageHelpers = ["lib/editor-labels.ts", "lib/draft-fields.ts"];

/** Each page source as [path below src/, text], the composition root first. */
export async function readPageSourceFiles() {
  const paths = ["routes/+page.svelte", ...pageHelpers];
  for (const directory of ["lib/screens", "lib/dialogs", "lib/state"]) {
    const files = (await readdir(new URL(`${directory}/`, source))).filter((file) => /\.svelte$|\.svelte\.ts$/.test(file)).sort();
    paths.push(...files.map((file) => `${directory}/${file}`));
  }
  return Promise.all(paths.map(async (path) => [path, await readFile(new URL(path, source), "utf8")]));
}

/** The page sources as one text. */
export async function readPageSources() {
  return (await readPageSourceFiles()).map(([, text]) => text).join("\n");
}
