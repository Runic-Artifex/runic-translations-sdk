import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import { parse } from "svelte/compiler";

const source = await readFile(new URL("../src/lib/MessageList.svelte", import.meta.url), "utf8");
const page = await readFile(new URL("../src/routes/+page.svelte", import.meta.url), "utf8");
const english = JSON.parse(await readFile(new URL("../../EditorResources/editor.en.json", import.meta.url), "utf8"));
const german = JSON.parse(await readFile(new URL("../../EditorResources/editor.de.json", import.meta.url), "utf8"));
const required = [
  "Messages", "MessageBulkActions", "VisibleMessages", "MarkForReview", "ApproveTranslations",
  "AddMessage", "NoMatchingMessages", "MissingTranslation", "Translated", "Structured", "Stale", "Review",
];
for (const key of required) {
  assert.equal(typeof english.resources.App[key], "string", `English catalog omits App.${key}.`);
  assert.equal(typeof german.resources.App[key], "string", `German catalog omits App.${key}.`);
  assert.match(page, new RegExp(`m\\$App\\$${key}\\(options\\)`), `The page does not resolve App.${key} from the catalog.`);
}

const literalText = [];
const literalAccessibleAttributes = [];
visit(parse(source).html, (node, parent) => {
  if (node.type === "Text" && parent?.type !== "Attribute" && node.data.trim() !== "") literalText.push(node.data.trim());
  if (node.type === "Attribute" && ["aria-label", "title", "placeholder"].includes(node.name) &&
    node.value?.some((value) => value.type === "Text" && value.data.trim() !== "")) {
    literalAccessibleAttributes.push(node.name);
  }
});
assert.deepEqual(literalText, [], `MessageList gained uncataloged visible text: ${literalText.join(" | ")}`);
assert.deepEqual(literalAccessibleAttributes, [],
  `MessageList gained an uncataloged accessible label: ${literalAccessibleAttributes.join(", ")}`);

console.log("PASS: MessageList user-visible text and accessible labels are catalog-provided in both editor locales.");

function visit(value, action, parent) {
  if (value === null || typeof value !== "object") return;
  if (typeof value.type === "string") action(value, parent);
  for (const child of Object.values(value)) {
    if (Array.isArray(child)) for (const item of child) visit(item, action, value);
    else visit(child, action, value);
  }
}
