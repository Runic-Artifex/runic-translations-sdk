import { executeMessagePreview, flattenPreview } from "../src/lib/message-preview.js";

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

console.log("PASS: editor preview matches the normalized ESM AST semantics and keeps hostile markup inert.");
