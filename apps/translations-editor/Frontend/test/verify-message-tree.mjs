// The message list as a namespace tree (#225 M1) and the hidden-selection note (P15), rendered by Svelte's server
// renderer: the ARIA tree pattern of the virtualized rows, the group rows' counts, and the message heading's dotted
// path with the code identifier as secondary text.
import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import { plugin } from "bun";
import { compile, compileModule } from "svelte/compiler";
import ts from "typescript";

globalThis.__runicTestUiText = {
  text: (key, values) => values === undefined ? `[${key}]` : `[${key} ${Object.entries(values).map(([name, value]) => `${name}=${value}`).join(" ")}]`,
};
plugin({
  name: "svelte-server-components",
  setup(build) {
    build.module("virtual:runic-translations/editor", () => ({ exports: { m: {} }, loader: "object" }));
    build.onLoad({ filter: /\/src\/lib\/ui-text\.ts$/ }, async ({ path }) => ({
      contents: (await readFile(path, "utf8")).replace("return useUiText();", "return globalThis.__runicTestUiText;"),
      loader: "ts",
    }));
    build.onLoad({ filter: /\.svelte\.(?:js|ts)$/ }, async ({ path }) => {
      const source = await readFile(path, "utf8");
      const javascript = path.endsWith(".ts") ? ts.transpileModule(source, {
        compilerOptions: { module: ts.ModuleKind.ESNext, target: ts.ScriptTarget.ES2022, verbatimModuleSyntax: false }, fileName: path,
      }).outputText : source;
      return { contents: compileModule(javascript, { filename: path, generate: "server", dev: false }).js.code, loader: "js" };
    });
    build.onLoad({ filter: /\.svelte$/ }, async ({ path }) => {
      const compiled = compile(await readFile(path, "utf8"), { filename: path, generate: "server", dev: false });
      return { contents: compiled.js.code, loader: "js" };
    });
  },
});

const { render } = await import("svelte/server");
const { createRawSnippet } = await import("svelte");
const { default: MessageList } = await import("../src/lib/MessageList.svelte");
const { default: MessageHeading } = await import("../src/lib/MessageHeading.svelte");
const html = (component, props) => render(component, { props }).body.replace(/<!--[^>]*-->/g, "");
// A tag, with quoted attribute values that may contain ">" (Tailwind classes such as `[&>svg]:size-4`).
const tag = String.raw`(?:[^>"]|"[^"]*")*>`;
const text = (markup) => markup.replace(new RegExp(`<${tag}`, "g"), "").replace(/\s+/g, " ").trim();
const treeItems = (markup) => [...markup.matchAll(new RegExp(`<div(?=[^>]*role="treeitem")${tag}`, "g"))].map((match) => attributes(match[0]));
const attributes = (tag) => Object.fromEntries([...tag.matchAll(/([a-z-]+)="([^"]*)"/g)].map((match) => [match[1], match[2]]));

const item = (key, path, status = {}) => ({
  key, path, preview: `Text of ${key}`, missing: false, structured: false, stale: false, needsReview: false, ...status,
});
const labels = Object.fromEntries(["messages", "bulkActions", "visibleMessages", "markForReview", "approveTranslations", "addMessage",
  "noMatchingMessages", "missingTranslation", "translated", "plural", "variants", "formatted", "stale", "review"].map((name) => [name, `#${name}`]));
const listProps = (overrides = {}) => ({
  items: [
    item("common_cancel", ["common", "cancel"], { missing: true }),
    item("common_save", ["common", "save"], { stale: true }),
    item("title", ["title"]),
  ],
  selectedKey: "common_save",
  visibleCount: 3,
  noResultsLabel: "none",
  labels,
  toolbar: createRawSnippet(() => ({ render: () => "<div></div>" })),
  onselect() {}, onadd() {}, onmarkreview() {}, onapprove() {},
  ...overrides,
});

// The tree: a labelled tree inside the messages landmark, rows as tree items with level, position and state.
{
  const markup = html(MessageList, listProps());
  assert.match(markup, /<nav[^>]*aria-label="#messages"[^>]*>[\s\S]*<div[^>]*role="tree"[^>]*aria-label="#messages"/,
    "The rows are a labelled tree inside the messages landmark.");
  const items = treeItems(markup);
  assert.deepEqual(items.map((row) => [row["data-row-id"], row["aria-level"], row["aria-posinset"], row["aria-setsize"]]), [
    ["g:common", "1", "1", "2"], ["m:common_cancel", "2", "1", "2"], ["m:common_save", "2", "2", "2"], ["m:title", "1", "2", "2"],
  ], "Each row names its level and position among its siblings.");
  const group = items[0];
  assert.equal(group["aria-expanded"], "true", "A group row states that it is expanded.");
  assert.equal(group["aria-label"], "[ui_message_tree_group_label name=common count=2], [ui_message_tree_group_missing count=1], [ui_message_tree_group_stale count=1]",
    "A group row names its messages and the work in it.");
  assert.equal(items[2]["aria-selected"], "true", "The selected message is the selected tree item.");
  assert.equal(items[1]["aria-selected"], "false");
  assert.equal(items[2]["data-message-key"], "common_save");
  assert.equal(items[2]["aria-label"], "save: Text of common_save, #stale", "A message row is named by its last segment, text and state.");
  assert.equal(items[2].title, "common.save\nText of common_save", "The tooltip shows the dotted path.");
  assert.deepEqual(items.map((row) => row.tabindex), ["-1", "-1", "0", "-1"], "Only the selected message is in the tab order (roving focus).");
  assert.match(text(markup), /common \[ui_message_tree_group_missing count=1\] ?\[ui_message_tree_group_stale count=1\] 2/,
    "A group row shows its name, its work and its number of messages.");
  assert.doesNotMatch(markup, /data-testid="selected-message-hidden"/);
}

// The counts a group names follow the filter: with the Missing filter on, the number of messages says it all.
{
  const markup = html(MessageList, listProps({ groupCounts: [], filterKey: "missing\n" }));
  const group = treeItems(markup).find((row) => row["data-row-id"] === "g:common");
  assert.equal(group["aria-label"], "[ui_message_tree_group_label name=common count=2]");
}

// P15: the selection stays when the filter hides it, and the list says so with a way back.
{
  const markup = html(MessageList, listProps({
    items: [item("title", ["title"])], selectedKey: "common_save", selectedHidden: true, onclearfilter() {},
  }));
  const note = /<div[^>]*data-testid="selected-message-hidden"[^>]*>[\s\S]*?<\/button>/.exec(markup)?.[0] ?? "";
  assert.match(note, /role="status"/, "The note is announced.");
  assert.match(text(note), /\[ui_message_tree_selected_hidden\] \[ui_message_tree_show_all\]/);
  assert.deepEqual([...markup.matchAll(/data-row-id="([^"]+)"/g)].map((match) => match[1]), ["m:title"],
    "The list shows only what the filter matches; the first row is the tab stop.");
  assert.match(markup, /data-row-id="m:title"[^>]*tabindex="0"|tabindex="0"[^>]*data-row-id="m:title"/);
}

// A flat catalog lists its messages as before: no groups, one level.
{
  const markup = html(MessageList, listProps({ items: [item("cancel", ["cancel"]), item("save", ["save"])], selectedKey: "save" }));
  const rows = treeItems(markup);
  assert.deepEqual(rows.map((row) => [row["data-row-id"], row["aria-level"]]), [["m:cancel", "1"], ["m:save", "1"]]);
  assert.equal(rows.some((row) => "aria-expanded" in row), false);
}

// The heading: the dotted path is the title, the code identifier secondary text.
{
  const props = { messageKey: "common_cancel", messagePath: ["common", "cancel"], tags: [], locale: "en", onrename() {}, onduplicate() {}, ondelete() {} };
  const markup = html(MessageHeading, props);
  assert.match(markup, /<h2[^>]*>common\.cancel<\/h2>/, "The title is the dotted path.");
  assert.match(text(/<p[^>]*data-testid="message-code-identifier"[^>]*>[\s\S]*?<\/p>/.exec(markup)[0]), /^\[ui_message_heading_code_identifier\] common_cancel$/);
  const flat = html(MessageHeading, { ...props, messageKey: "cancel", messagePath: ["cancel"] });
  assert.match(flat, /<h2[^>]*>cancel<\/h2>/);
  assert.doesNotMatch(flat, /message-code-identifier/, "A flat message has no second spelling to show.");
}

console.log("PASS: the message list is an ARIA tree of namespaces with counts and roving focus, notes a selection the filter hides, and the heading shows the dotted path.");
