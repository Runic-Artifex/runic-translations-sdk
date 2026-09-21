// RMF2 inline renderer ABI 1. UI factories return values, never HTML strings.
export const rmf2RuntimeAbiVersion = 1;
function freezeRmf2Contract(value) {
  if (value && typeof value === "object") {
    for (const child of Object.values(value)) freezeRmf2Contract(child);
    Object.freeze(value);
  }
  return value;
}
export function linkBinding({ href }) {
  if (typeof href !== "string" || !href.trim() || !["http:", "https:", "mailto:", "tel:"].includes(new URL(href, "https://runic.invalid/").protocol)) throw new TypeError("Invalid application link destination.");
  return Object.freeze({ kind: "runic:link", href });
}
export function actionBinding({ onActivate }) {
  if (typeof onActivate !== "function") throw new TypeError("An action requires an application callback.");
  return Object.freeze({ kind: "runic:action", onActivate });
}
export function iconBinding({ asset, decorative, accessibleName }) {
  if (asset == null || typeof decorative !== "boolean" || (!decorative && typeof accessibleName !== "function")) throw new TypeError("A meaningful icon requires a localized accessibleName(locale) function.");
  return Object.freeze({ kind: "runic:icon", asset, decorative, accessibleName });
}
export function enumOption(values, defaultValue) {
  if (!Array.isArray(values) || !values.length || values.some(x => typeof x !== "string") || (defaultValue !== undefined && !values.includes(defaultValue))) throw new TypeError("Invalid enum option.");
  return Object.freeze({ type: "enum", values: Object.freeze([...values]), ...(defaultValue === undefined ? {} : { default: defaultValue }) });
}
export function defineMarkup(contract) {
  if (!contract || !/^[A-Za-z_][\w-]*:[A-Za-z_][\w-]*$/.test(contract.name) || contract.name.startsWith("runic:") || !["paired", "standalone"].includes(contract.kind) || contract.children !== (contract.kind === "standalone" ? "none" : "inline") || typeof contract.interactive !== "boolean" || !["children", "lineBreak", "alternateText", "explicit", "omit"].includes(contract.plainText) || (contract.options !== undefined && (!contract.options || typeof contract.options !== "object" || Array.isArray(contract.options)))) throw new TypeError("Invalid custom markup contract.");
  return freezeRmf2Contract(JSON.parse(JSON.stringify(contract)));
}
export function bindMarkup(contract, render) {
  if (typeof render !== "function") throw new TypeError("A markup renderer must be a function.");
  return Object.freeze({ contract, render });
}
function contractChildren(contract) { return contract.children ?? (contract.kind === "standalone" ? "none" : "inline"); }
function optionContractsMatch(declared, supplied) {
  declared ??= {};
  const actual = supplied ?? {};
  const names = Object.keys(declared);
  return names.length === Object.keys(actual).length && names.every(name => {
    const expected = declared[name];
    const received = actual[name];
    return received && received.type === expected.type && JSON.stringify(received.values ?? []) === JSON.stringify(expected.values ?? []) && (received.default ?? null) === (expected.default ?? null) && !!received.literalOnly === !!expected.literalOnly;
  });
}
function rendererContractMatches(declared, supplied, expectedName) {
  return supplied && supplied.name === (declared.name ?? expectedName) && supplied.kind === declared.kind && contractChildren(supplied) === contractChildren(declared) && supplied.plainText === declared.plainText && supplied.interactive === declared.interactive && optionContractsMatch(declared.options, supplied.options);
}
function rmf2Option(schema, value) {
  if (typeof value !== "string") return false;
  switch (schema.type) {
    case "enum": return schema.values.includes(value);
    case "boolean": return value === "true" || value === "false";
    case "number": return value.trim() !== "" && Number.isFinite(Number(value));
    default: return true;
  }
}
export function createInlineRenderer(factory, bindings = []) {
  return createInlineRendererCore(factory, bindings, false);
}
function createInlineRendererCore(factory, bindings = [], allowUnboundCustom = false) {
  if (!factory || typeof factory.text !== "function" || typeof factory.element !== "function") throw new TypeError("An inline renderer requires text and element factories.");
  const custom = new Map();
  for (const binding of bindings) {
    const declared = binding?.contract && rmf2Contract.contracts[binding.contract.name];
    if (!declared || binding.contract.name.startsWith("runic:") || custom.has(binding.contract.name) || !rendererContractMatches(declared, binding.contract, binding.contract.name)) throw new TypeError("Incompatible or duplicate renderer contract.");
    custom.set(binding.contract.name, binding.render);
  }
  function render(content, { slots = {} } = {}) {
    const requirements = rmf2Contract.messages[content.key]?.slots;
    if (content.kind !== "localized-content" || !requirements) throw new TypeError("Unknown RMF2 content contract.");
    for (const [ref, contract] of Object.entries(requirements)) {
      const binding = slots[ref];
      if (binding?.kind !== contract.kind) throw new TypeError(`Missing or incompatible slot '${ref}'.`);
      if (binding.kind === "runic:link") linkBinding(binding);
      else if (binding.kind === "runic:action") actionBinding(binding);
      else iconBinding(binding);
    }
    const counts = Object.create(null);
    let count = 0;
    function visit(nodes, interactive = false, depth = 0, path = "") {
      if (depth > 16) throw new RangeError("Inline nesting exceeds 16 levels.");
      return nodes.map((node, index) => {
        if (++count > 4096) throw new RangeError("Inline node limit exceeded.");
        if (node.kind === "text") return node;
        const contract = rmf2Contract.contracts[node.name];
        if (!contract || node.kind !== "element" || (contract.kind === "standalone") !== node.standalone || (interactive && contract.interactive)) throw new TypeError("Unknown or invalid inline markup.");
        const options = node.attributes;
        let binding;
        for (const [name, value] of Object.entries(options)) {
          if (name === "ref" && ["runic:link", "runic:action", "runic:icon"].includes(node.name)) {
            binding = slots[value];
            if (requirements[value]?.kind !== node.name || binding?.kind !== node.name) throw new TypeError(`Invalid slot '${value}'.`);
            counts[value] = (counts[value] ?? 0) + 1;
          } else if (!contract.options[name] || !rmf2Option(contract.options[name], value)) throw new TypeError(`Invalid option '${name}'.`);
        }
        if (["runic:link", "runic:action", "runic:icon"].includes(node.name) && !binding) throw new TypeError("Missing functional slot ref.");
        for (const name of Object.keys(contract.options)) if (!Object.hasOwn(options, name)) throw new TypeError(`Missing option '${name}'.`);
        const occurrence = `${content.key}:${node.occurrence ?? `${path}${index}`}`;
        const children = visit(node.children, interactive || contract.interactive, depth + 1, `${path}${index}.`);
        const renderer = custom.get(node.name);
        const element = { name: node.name, children, options, binding, occurrence, locale: content.locale, standalone: node.standalone };
        if (!renderer && !node.name.startsWith("runic:") && !allowUnboundCustom) throw new TypeError(`No renderer linked for '${node.name}'.`);
        return element;
      });
    }
    const output = visit(content.nodes);
    for (const [ref, requirement] of Object.entries(requirements)) if ((counts[ref] ?? 0) < requirement.min || (counts[ref] ?? 0) > requirement.max) throw new TypeError(`Slot multiplicity mismatch for '${ref}'.`);
    function materialize(node) {
      if (node.kind === "text") return factory.text(node.value);
      const element = { ...node, children: node.children.map(materialize) };
      return custom.has(node.name) ? custom.get(node.name)(element) : factory.element(element);
    }
    return output.map(materialize);
  }
  return Object.freeze({ render, extend(extra) { return createInlineRendererCore(factory, [...bindings, ...extra], allowUnboundCustom); } });
}
export function toPlainText(content, { slots = {}, allowActionLabels = false, annotateLinkDestinations = false, custom = [] } = {}) {
  const renderer = createInlineRendererCore({
    text: value => value,
    element({ name, children, binding, locale }) {
      if (name === "runic:br") return "\n";
      if (name === "runic:action" && !allowActionLabels) throw new TypeError("Action labels require explicit projection policy.");
      if (name === "runic:icon") {
        if (binding.decorative) return "";
        const label = binding.accessibleName(locale);
        if (typeof label !== "string" || !label.trim()) throw new TypeError("Meaningful icon alternate text is empty.");
        return label;
      }
      const contract = rmf2Contract.contracts[name];
      if (!name.startsWith("runic:") && contract?.plainText === "lineBreak") return "\n";
      if (!name.startsWith("runic:") && contract?.plainText === "omit") return "";
      if (!name.startsWith("runic:") && (contract?.plainText === "explicit" || contract?.plainText === "alternateText")) throw new TypeError("Custom markup requires an explicit plain-text adapter.");
      const text = children.join("");
      return name === "runic:link" && annotateLinkDestinations ? `${text} (${binding.href})` : text;
    },
  }, custom, true);
  return renderer.render(content, { slots }).join("");
}
export function createDomInlineRenderer(document, custom = []) {
  return createInlineRenderer({
    text: value => document.createTextNode(value),
    element({ name, children, binding, locale }) {
      const tags = { "runic:strong": "strong", "runic:em": "em", "runic:bold": "span", "runic:italic": "span", "runic:code": "code", "runic:br": "br", "runic:link": "a", "runic:action": "button" };
      if (name === "runic:icon") {
        const node = typeof binding.asset === "function" ? binding.asset(document) : binding.asset?.cloneNode?.(true);
        if (!node || typeof node.setAttribute !== "function") throw new TypeError("The application icon asset must create a DOM element.");
        if (binding.decorative) { node.setAttribute("aria-hidden", "true"); node.removeAttribute("aria-label"); }
        else {
          const label = binding.accessibleName(locale);
          if (typeof label !== "string" || !label.trim()) throw new TypeError("Meaningful icon alternate text is empty.");
          node.setAttribute("role", "img"); node.setAttribute("aria-label", label);
        }
        return node;
      }
      const node = document.createElement(tags[name]);
      if (name === "runic:bold") node.style.fontWeight = "bold";
      if (name === "runic:italic") node.style.fontStyle = "italic";
      if (name === "runic:link") node.href = binding.href;
      if (name === "runic:action") { node.type = "button"; node.addEventListener("click", binding.onActivate); }
      node.append(...children);
      return node;
    },
  }, custom);
}
export function validateRmf2Plan(key, ast, locale) {
  if (ast.contentLocale !== rmf2Contract.messages[key]?.contentLocales[locale]) return "locale-mismatch";
  const required = rmf2Contract.messages[key]?.slots;
  if (!required) return "unknown-key";
  for (const variant of ast.variants) {
    const counts = Object.create(null);
    function visit(nodes, interactive = false) {
      for (const node of nodes) {
        if (node.kind !== "markup") continue;
        const tag = rmf2Contract.contracts[node.name];
        if (!tag || (tag.kind === "standalone") !== node.standalone || (node.standalone && node.children.length) || (tag.interactive && interactive)) return "malformed-pattern";
        const variables = new Set(node.variableOptions);
        if (variables.size !== node.variableOptions.length || [...variables].some(name => !Object.hasOwn(node.attributes, name))) return "malformed-pattern";
        for (const [name, value] of Object.entries(node.attributes)) {
          if (name === "ref" && ["runic:link","runic:action","runic:icon"].includes(node.name)) {
            if (variables.has(name) || required[value]?.kind !== node.name) return "argument-contract-mismatch";
            counts[value] = (counts[value] ?? 0) + 1;
          } else {
            const option = tag.options[name];
            if (!option) return "argument-contract-mismatch";
            if (variables.has(name)) {
              const input = ast.inputs[value];
              if (!input || option.literalOnly || !(option.type === "number" ? ["number","int"].includes(input.type) : option.type === "boolean" ? input.type === "bool" : input.type === "string")) return "argument-contract-mismatch";
            } else if (!rmf2Option(option, value)) return "argument-contract-mismatch";
          }
        }
        if (["runic:link","runic:action","runic:icon"].includes(node.name) && !Object.hasOwn(node.attributes,"ref")) return "argument-contract-mismatch";
        if (Object.keys(tag.options).some(name => !Object.hasOwn(node.attributes,name))) return "argument-contract-mismatch";
        const error = visit(node.children, interactive || tag.interactive); if (error) return error;
      }
      return null;
    }
    const error = visit(variant.nodes); if (error) return error;
    for (const [ref, bounds] of Object.entries(required)) if ((counts[ref] ?? 0) < bounds.min || (counts[ref] ?? 0) > bounds.max) return "argument-contract-mismatch";
  }
  return null;
}
