import assert from "node:assert/strict";
import { executeMessagePreview, flattenPreview, parseRenderedMessagePreview } from "../src/lib/message-preview.js";
import { sourceMessageToArtifact, toStructuredMessage } from "../src/lib/message-composer.ts";

const artifact = {
  astVersion: 2,
  inputs: {
    count: { type: "int", format: "plain" },
    delta: { type: "number", format: "plain" },
    owner: { type: "string", format: "plain" },
  },
  selectors: [
    { name: "quantity", input: "count", function: "plural" },
    { name: "ownerKind", input: "owner", function: "literal" },
  ],
  variants: [
    {
      matches: { quantity: "one", ownerKind: "admin" },
      nodes: [{ kind: "text", value: "Exactly " }, { kind: "input", input: "count" }],
    },
    {
      matches: { quantity: "*", ownerKind: "*" },
      nodes: [{
        kind: "markup",
        name: "script",
        attributes: { tone: "critical", payload: "<img src=x onerror=alert(1)>" },
        children: [
          { kind: "format", input: "count", function: "integer", format: "grouped" },
          { kind: "text", value: " items for " },
          { kind: "input", input: "owner" },
        ],
      }, { kind: "text", value: ", " }, {
        kind: "format", input: "delta", function: "relativeTime", format: "plain",
        unit: "day", numeric: "auto",
      }],
    },
  ],
};

const exact = executeMessagePreview(artifact, "en", { count: "1", delta: "-1", owner: "admin" });
if (exact.kind !== "text" || exact.value !== "Exactly 1") throw new Error("Exact multi-selector preview diverged.");

const rich = executeMessagePreview(artifact, "en", { count: "1234", delta: "-1", owner: "guest" });
if (rich.kind !== "content") throw new Error("Semantic markup did not produce structured content.");
if (rich.nodes[0].kind !== "element" || rich.nodes[0].name !== "script") throw new Error("Markup name was altered.");
if (rich.nodes[0].attributes.payload !== "<img src=x onerror=alert(1)>") throw new Error("Markup attributes were altered.");
if (flattenPreview(rich.nodes) !== "1,234 items for guest, yesterday") throw new Error("Formatted preview diverged from generated ESM semantics.");
if (typeof rich.nodes[0] !== "object" || "outerHTML" in rich.nodes[0]) throw new Error("Semantic data became an HTML node.");

const inferredArtifact = sourceMessageToArtifact(toStructuredMessage("Welcome back, {name}"));
if (inferredArtifact.inputs.name?.type !== "string" || inferredArtifact.inputs.name.format !== "none") {
  throw new Error("Plain-message placeholders were not inferred as string inputs.");
}
const inferred = executeMessagePreview(inferredArtifact, "en", { name: "Viktor" });
if (inferred.kind !== "text" || inferred.value !== "Welcome back, Viktor") {
  throw new Error("An inferred plain-message placeholder could not be previewed.");
}

const rmf2 = {
  astVersion: 4, contentLocale: "en",
  inputs: { count: { type: "int", format: "plain" }, tone: { type: "string", format: "none" } },
  selectors: [{ name: "count", input: "count", function: "plural" }],
  variants: [
    { matches: { count: "*" }, nodes: [{ kind: "text", value: "Fallback" }] },
    { matches: { count: "0" }, nodes: [{ kind: "markup", name: "shop:badge", standalone: false, attributes: { tone: "tone" }, variableOptions: ["tone"], children: [{ kind: "text", value: "Empty" }] }] },
  ],
};
const result = executeMessagePreview(rmf2, "de", { count: "0", tone: "positive" });
if (result.kind !== "content" || result.nodes[0].attributes.tone !== "positive" || flattenPreview(result.nodes) !== "Empty") throw new Error("RMF2 exact numeric selection or dynamic markup options diverged.");

const executionV2 = {
  astVersion: 5,
  profile: "rmf2-execution-v2",
  inputs: [{ name: "target", type: "string" }],
};
assert.throws(
  () => executeMessagePreview(executionV2, "en", { target: "account" }),
  /must be rendered by the compiler host/,
  "The v5 AST was incorrectly coerced through the v4 JavaScript evaluator.",
);

const serverText = parseRenderedMessagePreview(JSON.stringify({
  key: "plain",
  locale: "en",
  runs: [{ text: "Rendered by the compiler" }],
}));
assert.deepEqual(serverText, { kind: "text", value: "Rendered by the compiler" });

const serverContent = parseRenderedMessagePreview(JSON.stringify({
  key: "rich",
  locale: "en",
  runs: [{
    name: "runic:link",
    text: null,
    options: { ref: "account", hostile: "<img src=x onerror=alert(1)>" },
    children: [{ name: "text", text: "Account", options: {}, children: [] }],
  }, {
    name: "runic:action",
    text: null,
    options: { ref: "save" },
    children: [{ name: "text", text: "Save", options: {}, children: [] }],
  }, {
    name: "app:badge",
    text: null,
    options: { tone: "positive" },
    children: [],
  }],
}));
assert.equal(serverContent.kind, "content");
assert.deepEqual(serverContent.nodes.map((node) => node.kind === "element" ? node.name : node.kind), [
  "runic:link", "runic:action", "app:badge",
]);
assert.equal(serverContent.nodes[0].attributes.hostile, "<img src=x onerror=alert(1)>");
assert.equal(flattenPreview(serverContent.nodes), "AccountSave");
assert.equal("outerHTML" in serverContent.nodes[0], false, "Server semantic runs became active HTML nodes.");
assert.throws(
  () => parseRenderedMessagePreview('{"key":"bad","locale":"en","runs":[{"name":"runic:link","text":null,"options":{"ref":1},"children":[]}]}'),
  /invalid rendered message run/,
  "Malformed server semantic options were accepted.",
);
assert.throws(
  () => parseRenderedMessagePreview('{"key":"bad","locale":"en","runs":[{"name":"runic:action","text":null,"options":{},"children":[],"callback":"alert(1)"}]}'),
  /invalid rendered message run/,
  "An active callback field escaped the bounded inert run shape.",
);

console.log("PASS: editor preview routes v2/v4 locally, keeps v5 compiler runs inert, and rejects cross-profile execution.");
