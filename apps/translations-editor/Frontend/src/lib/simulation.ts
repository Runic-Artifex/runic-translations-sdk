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

export type SimulationPreviewBlock = {
  name: string;
  occurrence: string;
  options: Record<string, string>;
  blocks: SimulationPreviewBlock[];
  nodes: SimulationPreviewNode[];
};

export type SimulationPreviewResult =
  | { kind: "text"; value: string }
  | { kind: "content"; nodes: SimulationPreviewNode[] }
  | { kind: "document"; blocks: SimulationPreviewBlock[] };

/**
 * The SDK's development pseudo-locales (runic.json `pseudoLocales`). The preview host generates them from the base
 * locale with the same code as a `--pseudo-locales` build: `en-XA` accents and lengthens, `ar-XB` mirrors right to left.
 */
export const pseudoLocales = ["en-XA", "ar-XB"] as const;

export type PseudoLocale = typeof pseudoLocales[number];

export function isPseudoLocale(value: string | null | undefined): value is PseudoLocale {
  return pseudoLocales.some((locale) => locale === value);
}

const pseudoLocalizationKey = "runic-translations.pseudo-localization";
const uiDirectionKey = "runic-translations.ui-direction";

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
