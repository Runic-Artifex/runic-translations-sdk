import assert from "node:assert/strict";
import {
  createMessagePreviewRequest,
  createMessagePreviewOwnership,
  createMessagePreviewScheduler,
  createPreviewSamples,
  executeMessagePreview,
  flattenPreview,
  parseRenderedMessagePreview,
  previewSampleOr,
  routeMessagePreview,
  withPreviewSample,
} from "../src/lib/message-preview.js";
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

const selection = { key: "account_title", locale: "en" };
const rowRequest = createMessagePreviewRequest(
  "de.rmf2",
  "account_title = Konto\npayment_title = Zahlung\n",
  "payment_title",
  selection.locale,
);
selection.key = "payment_title";
assert.deepEqual(rowRequest, {
  path: "de.rmf2",
  content: "account_title = Konto\npayment_title = Zahlung\n",
  key: "payment_title",
  locale: "en",
}, "A message-row switch scheduled the new document with the previous key.");

const localeRequest = createMessagePreviewRequest(
  "fr.rmf2",
  "payment_title = Paiement\n",
  selection.key,
  "fr",
);
selection.locale = "fr";
assert.deepEqual(localeRequest, {
  path: "fr.rmf2",
  content: "payment_title = Paiement\n",
  key: "payment_title",
  locale: "fr",
}, "A locale switch scheduled the new document with the previous locale.");
assert.equal(Object.isFrozen(rowRequest) && Object.isFrozen(localeRequest), true,
  "Scheduled preview identity remained mutable after a selection change.");

function fakeScheduler() {
  let nextHandle = 0;
  const pending = new Map();
  const scheduler = createMessagePreviewScheduler(
    (callback) => { const handle = ++nextHandle; pending.set(handle, callback); return handle; },
    (handle) => pending.delete(handle),
  );
  return {
    scheduler,
    flush() {
      const callbacks = [...pending.values()];
      pending.clear();
      for (const callback of callbacks) callback();
    },
    get size() { return pending.size; },
  };
}

const rowDebounce = fakeScheduler();
let acceptedRequest;
rowDebounce.scheduler.schedule(450, () => { acceptedRequest = createMessagePreviewRequest("de.rmf2", "old", "account_title", "en"); });
rowDebounce.scheduler.schedule(450, () => { acceptedRequest = rowRequest; });
assert.equal(rowDebounce.size, 1, "A message-row switch left the previous preview debounce active.");
rowDebounce.flush();
assert.equal(acceptedRequest.key, "payment_title", "The debounced row preview retained the old key.");

const localeDebounce = fakeScheduler();
localeDebounce.scheduler.schedule(450, () => { acceptedRequest = rowRequest; });
localeDebounce.scheduler.schedule(450, () => { acceptedRequest = localeRequest; });
assert.equal(localeDebounce.size, 1, "A locale switch left the previous preview debounce active.");
localeDebounce.flush();
assert.equal(acceptedRequest.locale, "fr", "The debounced locale preview retained the old locale.");

const draftDebounce = fakeScheduler();
const draftOwnership = createMessagePreviewOwnership();
const changedDraft = createMessagePreviewRequest("fr.rmf2", "payment_title = changed", "payment_title", "fr");
let activeAst = { astVersion: 5, profile: "rmf2-execution-v2", inputs: [{ name: "oldInput", type: "string" }] };
let activeResult = { kind: "text", value: "old render" };
let activeError = "old error";
let draftSamples = withPreviewSample(createPreviewSamples(), "oldInput", "old");
let draftRoute;
const draftCalls = [];
const beginning = draftOwnership.begin(changedDraft);
const activeRequest = beginning.request;
activeAst = beginning.ast;
activeResult = beginning.result;
activeError = beginning.error;
assert.equal(activeAst, undefined, "Beginning a changed draft retained the previous AST.");
assert.equal(activeResult, undefined, "Beginning a changed draft retained the previous render.");
assert.equal(activeError, undefined, "Beginning a changed draft retained the previous error.");
draftDebounce.scheduler.schedule(450, (epoch) => {
  draftRoute = routeMessagePreview(async (path, content, locale, key, samplesJson) => {
    draftCalls.push({ path, content, locale, key, samplesJson });
    return samplesJson === undefined
      ? {
          success: true,
          locale,
          astJson: JSON.stringify({
            astVersion: 5,
            profile: "rmf2-execution-v2",
            inputs: [{ name: "newInput", type: "string" }],
          }),
          diagnostics: [],
        }
      : { success: true, locale, renderedJson: '{"key":"payment_title","locale":"fr","runs":[{"text":"new render"}]}', diagnostics: [] };
  }, changedDraft, draftSamples, () => "new default", () => draftDebounce.scheduler.isCurrent(epoch)).then((next) => {
    assert.equal(draftOwnership.acceptParsed(changedDraft), true,
      "The active changed draft could not claim its parsed AST.");
    activeAst = next.ast;
    draftSamples = next.samples;
    return next;
  });
});
// This models an input event queued from the old controls before Svelte removes
// them. It may update remembered samples, but cannot replace the pending parse.
draftSamples = withPreviewSample(draftSamples, "oldInput", "queued edit");
if (draftOwnership.canRenderSample(activeRequest, activeAst)) {
  draftDebounce.scheduler.schedule(150, () => { throw new Error("A stale sample edit replaced the required parse."); });
}
assert.equal(draftDebounce.size, 1, "A stale sample edit canceled the changed draft's parse debounce.");
draftDebounce.flush();
const changedResult = await draftRoute;
assert.equal(draftCalls.length, 2, "The changed AST 5 draft did not complete parse and render routing.");
assert.deepEqual(activeAst.inputs.map((input) => input.name), ["newInput"],
  "Preview controls retained the previous AST 5 input schema.");
assert.equal(Object.hasOwn(draftSamples, "oldInput"), false, "A removed AST 5 input survived the new parse.");
assert.equal(draftSamples.newInput, "new default", "The new AST 5 input did not receive its default sample.");
assert.equal(parseRenderedMessagePreview(changedResult.rendered.renderedJson).value, "new render",
  "The changed draft did not render from the new AST 5 route.");

const hostileNames = ["__proto__", "constructor", "toString"];
let hostileSamples = createPreviewSamples();
for (const name of hostileNames) {
  assert.equal(previewSampleOr(hostileSamples, name, "missing"), "missing",
    `Inherited property '${name}' was mistaken for an entered preview sample.`);
  hostileSamples = withPreviewSample(hostileSamples, name, `value:${name}`);
}
const routedCalls = [];
const routed = await routeMessagePreview(async (path, content, locale, key, samplesJson) => {
  routedCalls.push({ path, content, locale, key, samplesJson });
  return samplesJson === undefined
    ? {
        success: true,
        locale,
        astJson: JSON.stringify({
          astVersion: 5,
          profile: "rmf2-execution-v2",
          inputs: hostileNames.map((name) => ({ name, type: "string" })),
        }),
        diagnostics: [],
      }
    : { success: true, locale, renderedJson: '{"key":"payment_title","locale":"fr","runs":[{"text":"safe"}]}', diagnostics: [] };
}, localeRequest, hostileSamples, () => "default");
assert.equal(routedCalls.length, 2, "AST 5 preview did not use the required two-stage host route.");
assert.deepEqual(routedCalls.map(({ key, locale }) => ({ key, locale })), [
  { key: "payment_title", locale: "fr" },
  { key: "payment_title", locale: "fr" },
], "AST 5 routing lost the captured row or locale between host requests.");
assert.equal(Object.getPrototypeOf(routed.samples), null, "Routed preview samples regained an object prototype.");
const hostileRoundTrip = JSON.parse(routedCalls[1].samplesJson);
for (const name of hostileNames) {
  assert.equal(Object.hasOwn(hostileRoundTrip, name), true,
    `Preview sample '${name}' was omitted during AST 5 request serialization.`);
  assert.equal(hostileRoundTrip[name], `value:${name}`,
    `Preview sample '${name}' was corrupted during AST 5 request serialization.`);
}
let staleCurrent = true;
let staleCalls = 0;
await routeMessagePreview(async () => {
  staleCalls += 1;
  staleCurrent = false;
  return routed.initial;
}, rowRequest, hostileSamples, () => "default", () => staleCurrent);
assert.equal(staleCalls, 1, "A superseded AST 5 preview issued its second host request.");

console.log("PASS: editor preview captures row/locale identity, routes prototype-safe AST 5 samples, keeps runs inert, and rejects cross-profile execution.");
