export const uiDirections = ["ltr", "rtl"] as const;

export type UiDirection = typeof uiDirections[number];

export type SimulationPreviewNode =
  | { kind: "text"; value: string }
  | {
      kind: "element";
      name: string;
      attributes: Record<string, string>;
      children: SimulationPreviewNode[];
    };

export type SimulationPreviewResult =
  | { kind: "text"; value: string }
  | { kind: "content"; nodes: SimulationPreviewNode[] };

export interface SimulationOptions {
  readonly pseudoLocalization: boolean;
}

const pseudoLocalizationKey = "runic-translations.pseudo-localization";
const uiDirectionKey = "runic-translations.ui-direction";

// Deterministic pseudo-localization map: every entry is a single precomposed
// code point so transforms stay length-predictable across platforms.
const pseudoLetterMap: Record<string, string> = {
  a: "à", b: "ḃ", c: "ć", d: "ď", e: "é", f: "ḟ", g: "ĝ", h: "ĥ", i: "ï",
  j: "ĵ", k: "ķ", l: "ĺ", m: "ḿ", n: "ń", o: "ö", p: "ṕ", r: "ŕ", s: "ś",
  t: "ť", u: "ü", w: "ŵ", y: "ÿ", z: "ź",
  A: "À", B: "Ḃ", C: "Ć", D: "Ď", E: "É", F: "Ḟ", G: "Ĝ", H: "Ĥ", I: "Ï",
  J: "Ĵ", K: "Ķ", L: "Ĺ", M: "Ḿ", N: "Ń", O: "Ö", P: "Ṕ", R: "Ŕ", S: "Ś",
  T: "Ť", U: "Ü", W: "Ŵ", Y: "Ÿ", Z: "Ź",
};
const pseudoVowels = new Set(["a", "e", "i", "o", "u", "A", "E", "I", "O", "U"]);

/** Accent-substitutes and lengthens one text segment; deterministic and pure. */
export function pseudoLocalizeText(value: string): string {
  let output = "";
  for (const character of value) {
    const mapped = pseudoLetterMap[character] ?? character;
    output += mapped;
    if (pseudoVowels.has(character)) output += mapped;
  }
  return output;
}

/**
 * Applies the session simulation to a message-preview result. Text values are
 * transformed; markup names and attributes are preserved byte-for-byte so the
 * semantic data tree stays inert.
 */
export function simulatePreviewResult(
  result: SimulationPreviewResult,
  options: SimulationOptions,
): SimulationPreviewResult {
  if (!options.pseudoLocalization) return result;
  if (result.kind === "text") {
    return { kind: "text", value: `[${pseudoLocalizeText(result.value)}]` };
  }
  return {
    kind: "content",
    nodes: [
      { kind: "text", value: "[" },
      ...result.nodes.map(simulateNode),
      { kind: "text", value: "]" },
    ],
  };
}

function simulateNode(node: SimulationPreviewNode): SimulationPreviewNode {
  if (node.kind === "text") return { kind: "text", value: pseudoLocalizeText(node.value) };
  return {
    kind: "element",
    name: node.name,
    attributes: { ...node.attributes },
    children: node.children.map(simulateNode),
  };
}

export function readUiSimulation(read: (key: string) => string | null = () => null): { pseudoLocalization: boolean; direction: UiDirection } {
  const storedPseudo = read(pseudoLocalizationKey);
  const storedDirection = read(uiDirectionKey);
  return {
    pseudoLocalization: storedPseudo === "true",
    direction: isUiDirection(storedDirection) ? storedDirection : "ltr",
  };
}

export function saveUiSimulation(
  pseudoLocalization: boolean,
  direction: UiDirection,
  write: (key: string, value: string) => void = () => undefined,
): void {
  write(pseudoLocalizationKey, String(pseudoLocalization));
  write(uiDirectionKey, direction);
}

function isUiDirection(value: string | null): value is UiDirection {
  return uiDirections.some((direction) => direction === value);
}
