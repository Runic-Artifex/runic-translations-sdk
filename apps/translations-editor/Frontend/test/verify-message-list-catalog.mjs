import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import { parse } from "svelte/compiler";
import { readUiMessages } from "./ui-messages.mjs";

const source = await readFile(new URL("../src/lib/MessageList.svelte", import.meta.url), "utf8");
const page = await readFile(new URL("../src/routes/+page.svelte", import.meta.url), "utf8");
const [english, german] = await Promise.all([readUiMessages("en"), readUiMessages("de")]);
const required = [
  "app_messages", "app_message_bulk_actions", "app_visible_messages", "app_mark_for_review", "app_approve_translations",
  "app_add_message", "app_no_matching_messages", "app_missing_translation", "app_translated", "app_structured", "app_stale", "app_review",
];
for (const key of required) {
  assert.equal(typeof english[key], "string", `English project omits ${key}.`);
  assert.equal(typeof german[key], "string", `German project omits ${key}.`);
  assert.match(page, new RegExp(`m\\.${key}\\(options\\)`), `The page does not call generated message ${key}.`);
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
