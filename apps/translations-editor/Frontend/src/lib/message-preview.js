/**
 * Captures the preview identity selected by the caller. Reactive selection may
 * change afterward without retargeting an already scheduled request.
 * @param {string} path
 * @param {string} content
 * @param {string} key
 * @param {string} locale
 */
export function createMessagePreviewRequest(path, content, key, locale) {
  return Object.freeze({ path, content, key, locale });
}

/**
 * Copies sample values into a dictionary with no inherited property names.
 * @param {Record<string, string> | undefined} [source]
 * @returns {Record<string, string>}
 */
export function createPreviewSamples(source) {
  /** @type {Record<string, string>} */
  const samples = Object.create(null);
  if (source !== undefined) {
    for (const [name, value] of Object.entries(source)) samples[name] = value;
  }
  return samples;
}

/**
 * @param {Record<string, string>} samples
 * @param {string} name
 * @param {string} fallback
 */
export function previewSampleOr(samples, name, fallback) {
  return Object.hasOwn(samples, name) ? samples[name] : fallback;
}

/**
 * @param {Record<string, string>} samples
 * @param {string} name
 * @param {string} value
 */
export function withPreviewSample(samples, name, value) {
  const next = createPreviewSamples(samples);
  next[name] = value;
  return next;
}

/**
 * Owns debounce and freshness for preview work without depending on reactive
 * component globals.
 * @param {(callback: () => void, delay: number) => unknown} setTimer
 * @param {(handle: unknown) => void} clearTimer
 */
export function createMessagePreviewScheduler(setTimer, clearTimer) {
  let epoch = 0;
  /** @type {unknown} */
  let handle;
  return Object.freeze({
    /** @param {number} delay @param {(epoch: number) => void} operation */
    schedule(delay, operation) {
      if (handle !== undefined) clearTimer(handle);
      const scheduledEpoch = ++epoch;
      handle = setTimer(() => {
        handle = undefined;
        operation(scheduledEpoch);
      }, delay);
      return scheduledEpoch;
    },
    cancel() {
      if (handle !== undefined) clearTimer(handle);
      handle = undefined;
      epoch += 1;
    },
    /** @param {number} candidate */
    isCurrent(candidate) {
      return candidate === epoch;
    },
  });
}

/**
 * Owns the invariant that samples may render only after the active request has
 * supplied its own AST. `begin` also supplies the complete stale-view
 * invalidation that a UI must apply before starting the debounce.
 */
export function createMessagePreviewOwnership() {
  /** @type {ReturnType<typeof createMessagePreviewRequest> | undefined} */
  let activeRequest;
  /** @type {ReturnType<typeof createMessagePreviewRequest> | undefined} */
  let parsedRequest;
  return Object.freeze({
    /** @param {ReturnType<typeof createMessagePreviewRequest>} request */
    begin(request) {
      activeRequest = request;
      parsedRequest = undefined;
      return Object.freeze({ request, ast: undefined, result: undefined, error: undefined });
    },
    /** @param {ReturnType<typeof createMessagePreviewRequest>} request */
    acceptParsed(request) {
      if (request !== activeRequest) return false;
      parsedRequest = request;
      return true;
    },
    /**
     * @param {ReturnType<typeof createMessagePreviewRequest> | undefined} request
     * @param {unknown} ast
     * @returns {request is ReturnType<typeof createMessagePreviewRequest>}
     */
    canRenderSample(request, ast) {
      return request !== undefined && ast !== undefined && request === activeRequest && request === parsedRequest;
    },
    reset() {
      activeRequest = undefined;
      parsedRequest = undefined;
    },
  });
}

/**
 * Runs the host request sequence for a captured selection. The selected v5
 * contract receives a second request carrying prototype-safe samples.
 * @param {(path: string, content: string, locale: string, key: string, samplesJson?: string) => Promise<any>} previewMessage
 * @param {{ readonly path: string, readonly content: string, readonly key: string, readonly locale: string }} request
 * @param {Record<string, string>} previousSamples
 * @param {(type: string) => string} defaultSample
 * @param {() => boolean} [isCurrent]
 */
export async function routeMessagePreview(previewMessage, request, previousSamples, defaultSample, isCurrent = () => true) {
  const initial = await previewMessage(request.path, request.content, request.locale, request.key);
  let samples = createPreviewSamples(previousSamples);
  if (!isCurrent() || !initial.success || typeof initial.astJson !== "string" || typeof initial.locale !== "string") {
    return { initial, ast: undefined, samples, rendered: undefined };
  }
  const ast = parseMessageArtifact(initial.astJson);
  samples = createPreviewSamples();
  for (const input of ast.inputs) {
    samples[input.name] = previewSampleOr(previousSamples, input.name, defaultSample(input.type));
  }
  const rendered = await previewMessage(request.path, request.content, request.locale, request.key, JSON.stringify(samples));
  return { initial, ast, samples, rendered };
}

/**
 * Accepts only the selected normalized preview contract. The rest of the v5
 * semantic tree remains opaque to the frontend; execution belongs to the host.
 * @param {string} astJson
 * @returns {import("./message-model").MessageArtifact}
 */
export function parseMessageArtifact(astJson) {
  const ast = JSON.parse(astJson);
  if (!isRecord(ast) || ast.astVersion !== 5 || ast.profile !== "rmf2-execution-v2" || !Array.isArray(ast.inputs)) {
    throw new TypeError("The compiler host returned an unsupported message preview contract.");
  }
  const names = new Set();
  const inputTypes = new Set(["string", "bool", "int64", "decimal", "date", "time", "instant", "uuid"]);
  for (const input of ast.inputs) {
    if (!isRecord(input) || !hasExactKeys(input, ["name", "type"]) ||
        typeof input.name !== "string" || input.name.length === 0 ||
        typeof input.type !== "string" || !inputTypes.has(input.type) || names.has(input.name)) {
      throw new TypeError("The compiler host returned an invalid message preview input contract.");
    }
    names.add(input.name);
  }
  return /** @type {import("./message-model").MessageArtifact} */ (/** @type {unknown} */ (ast));
}

/**
 * Converts the compiler host's JSON preview runs to the same inert semantic
 * result consumed by InlinePreview. Markup names and options remain data.
 * @param {string} renderedJson
 * @returns {{ kind: "text", value: string } | { kind: "content", nodes: PreviewNode[] }}
 */
export function parseRenderedMessagePreview(renderedJson) {
  const document = JSON.parse(renderedJson);
  if (!isRecord(document) || typeof document.key !== "string" ||
      typeof document.locale !== "string" || !Array.isArray(document.runs) ||
      !hasExactKeys(document, ["key", "locale", "runs"])) {
    throw new TypeError("The compiler host returned an invalid rendered message preview.");
  }
  const nodes = document.runs.map(renderedRun);
  return hasMarkup(nodes)
    ? { kind: "content", nodes }
    : { kind: "text", value: flattenPreview(nodes) };
}

/** @param {unknown} value @returns {PreviewNode} */
function renderedRun(value) {
  if (!isRecord(value)) invalidRenderedRun();
  if (typeof value.text === "string") {
    const textOnly = hasExactKeys(value, ["text"]);
    const fullRun = hasExactKeys(value, ["name", "text", "options", "children"]);
    if (!textOnly && !fullRun) invalidRenderedRun();
    if (fullRun && (typeof value.name !== "string" || !isStringRecord(value.options) ||
        !Array.isArray(value.children) || value.children.length !== 0)) {
      invalidRenderedRun();
    }
    return { kind: "text", value: value.text };
  }
  if (value.text !== null || typeof value.name !== "string" || value.name.length === 0 ||
      !isStringRecord(value.options) || !Array.isArray(value.children) ||
      !hasExactKeys(value, ["name", "text", "options", "children"])) {
    invalidRenderedRun();
  }
  return {
    kind: "element",
    name: value.name,
    attributes: { ...value.options },
    children: value.children.map(renderedRun),
  };
}

/** @param {unknown} value @returns {value is Record<string, unknown>} */
function isRecord(value) {
  return value !== null && typeof value === "object" && !Array.isArray(value);
}

/** @param {unknown} value @returns {value is Record<string, string>} */
function isStringRecord(value) {
  return isRecord(value) && Object.values(value).every((item) => typeof item === "string");
}

/** @param {Record<string, unknown>} value @param {string[]} expected */
function hasExactKeys(value, expected) {
  const keys = Object.keys(value);
  return keys.length === expected.length && keys.every((key) => expected.includes(key));
}

/** @returns {never} */
function invalidRenderedRun() {
  throw new TypeError("The compiler host returned an invalid rendered message run.");
}

/** @param {PreviewNode[]} nodes @returns {string} */
export function flattenPreview(nodes) {
  return nodes.map((node) => node.kind === "text" ? node.value : flattenPreview(node.children)).join("");
}

/** @param {PreviewNode[]} nodes */
function hasMarkup(nodes) {
  return nodes.some((node) => node.kind === "element");
}

/** @typedef {{ kind: "text", value: string } | { kind: "element", name: string, attributes: Record<string, string>, children: PreviewNode[] }} PreviewNode */
