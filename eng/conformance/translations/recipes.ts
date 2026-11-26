// The corpus's pack recipes and macros (specs/translations/corpus/README.md, "Pack recipes" and "Macros"), applied by the
// driver: the adapter gets the resulting bytes, so every implementation reads the same pack.

// {"$text": c, "length": n}, {"$items": v, "count": n} and {"$arguments": v, "count": n, "prefix": p}, recursively.
export function expandMacros(value: any): any {
  if (Array.isArray(value)) return value.map(expandMacros);
  if (value === null || typeof value !== "object") return value;
  if ("$text" in value) return String(value.$text).repeat(value.length);
  if ("$items" in value) return Array.from({ length: value.count }, () => expandMacros(value.$items));
  if ("$arguments" in value) return Object.fromEntries(Array.from({ length: value.count }, (_, i) => [`${value.prefix}${i}`, expandMacros(value.$arguments)]));
  return Object.fromEntries(Object.entries(value).map(([k, v]) => [k, expandMacros(v)]));
}

const tokens = (pointer: string): string[] => {
  if (pointer === "") return [];
  if (!pointer.startsWith("/")) throw new Error(`invalid JSON pointer ${pointer}`);
  return pointer.slice(1).split("/").map((t) => t.replace(/~1/g, "/").replace(/~0/g, "~"));
};

const index = (array: any[], token: string, allowEnd: boolean): number => {
  if (allowEnd && token === "-") return array.length;
  if (!/^(0|[1-9][0-9]*)$/.test(token)) throw new Error(`invalid array index ${token}`);
  const i = Number(token);
  if (i > array.length || (!allowEnd && i === array.length)) throw new Error(`array index out of range: ${token}`);
  return i;
};

const isObject = (v: any) => v !== null && typeof v === "object" && !Array.isArray(v);

function get(document: any, pointer: string): any {
  let node = document;
  for (const token of tokens(pointer)) {
    if (Array.isArray(node)) node = node[index(node, token, false)];
    else if (isObject(node) && Object.hasOwn(node, token)) node = node[token];
    else throw new Error(`no value at ${pointer}`);
  }
  return node;
}

function parent(document: any, pointer: string): [any, string] {
  const parts = tokens(pointer);
  const container = get(document, parts.slice(0, -1).map((t) => "/" + t.replace(/~/g, "~0").replace(/\//g, "~1")).join(""));
  if (container === null || typeof container !== "object") throw new Error(`no container at ${pointer}`);
  return [container, parts[parts.length - 1]];
}

// "add" on an existing member replaces it in place (RFC 6902); a new member goes last, as JSON.parse keeps member order.
function add(document: any, pointer: string, value: any): any {
  if (pointer === "") return value;
  const [container, token] = parent(document, pointer);
  if (Array.isArray(container)) container.splice(index(container, token, true), 0, value);
  else container[token] = value;
  return document;
}

function remove(document: any, pointer: string) {
  if (pointer === "") throw new Error("cannot remove the document");
  const [container, token] = parent(document, pointer);
  if (Array.isArray(container)) container.splice(index(container, token, false), 1);
  else if (Object.hasOwn(container, token)) delete container[token];
  else throw new Error(`nothing to remove at ${pointer}`);
}

const deepEqual = (a: any, b: any): boolean => {
  if (Array.isArray(a)) return Array.isArray(b) && a.length === b.length && a.every((v, i) => deepEqual(v, b[i]));
  if (isObject(a)) return isObject(b) && Object.keys(a).length === Object.keys(b).length && Object.keys(a).every((k) => Object.hasOwn(b, k) && deepEqual(a[k], b[k]));
  return a === b;
};

// RFC 6902 over a parsed document; values are macro-expanded first. A failing test operation throws, so a changed artifact
// fails the recipe instead of mutating something else.
export function applyPatch(document: any, operations: any[]): any {
  for (const operation of operations) {
    const { op, path } = operation;
    const value = () => {
      if (!("value" in operation)) throw new Error(`${op} ${path} has no value`);
      return expandMacros(operation.value);
    };
    switch (op) {
      case "add":
        document = add(document, path, value());
        break;
      case "remove":
        remove(document, path);
        break;
      case "replace": {
        const [container, token] = parent(document, path);
        get(document, path);
        if (Array.isArray(container)) container[index(container, token, false)] = value();
        else container[token] = value();
        break;
      }
      case "move": {
        if (path.startsWith(operation.from + "/")) throw new Error(`move into its own child: ${path}`);
        const moved = get(document, operation.from);
        remove(document, operation.from);
        document = add(document, path, moved);
        break;
      }
      case "copy":
        document = add(document, path, structuredClone(get(document, operation.from)));
        break;
      case "test":
        if (!deepEqual(get(document, path), value())) throw new Error(`test failed at ${path}: ${JSON.stringify(get(document, path))}`);
        break;
      default:
        throw new Error(`unknown JSON Patch operation ${op}`);
    }
  }
  return document;
}

// A recipe applied to the base artifact's bytes: a patch, written back as compact JSON in member order, or a text edit of
// the first occurrence, for a pack that is not a JSON value with unique members.
export function applyRecipe(artifact: Uint8Array, recipe: any): Uint8Array {
  const text = new TextDecoder("utf-8", { fatal: true }).decode(artifact);
  if (recipe.textEdit) {
    const at = text.indexOf(recipe.textEdit.find);
    if (at < 0) throw new Error(`the text edit finds nothing: ${recipe.textEdit.find}`);
    return new TextEncoder().encode(text.slice(0, at) + recipe.textEdit.replace + text.slice(at + recipe.textEdit.find.length));
  }
  if (!Array.isArray(recipe.patch)) throw new Error("the case has no patch or textEdit");
  return new TextEncoder().encode(JSON.stringify(applyPatch(JSON.parse(text), recipe.patch)));
}
