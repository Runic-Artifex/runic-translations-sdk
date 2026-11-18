/**
 * How keyboard shortcuts are shown (#225 P10): ⌘ on macOS, Ctrl elsewhere. The editor's keyboard handling accepts both
 * modifiers on every platform (editor-keyboard.ts); this only decides what the toolbar, the command palette and
 * tooltips display.
 */
export type ShortcutPlatform = "mac" | "other";

/** The parts of `navigator` the platform is read from. */
export interface ShortcutNavigator {
  readonly platform?: string;
  readonly userAgent?: string;
  readonly userAgentData?: { readonly platform?: string };
}

/** The platform whose shortcut notation the editor shows: macOS (and iOS) use ⌘, every other platform Ctrl. */
export function detectShortcutPlatform(source: ShortcutNavigator | undefined = globalThis.navigator): ShortcutPlatform {
  const platform = source?.userAgentData?.platform || source?.platform || source?.userAgent || "";
  return /mac|iphone|ipad|ipod/i.test(platform) ? "mac" : "other";
}

let detected: ShortcutPlatform | undefined;

/** The platform of this window, detected once. */
export function shortcutPlatform(): ShortcutPlatform {
  return detected ??= detectShortcutPlatform();
}

const macSymbols: Record<string, string> = { Ctrl: "⌃", Alt: "⌥", Shift: "⇧", Mod: "⌘" };
const modifierOrder = ["Ctrl", "Alt", "Shift", "Mod"];

/**
 * The displayed form of a shortcut written as `Mod+Shift+Z`: `Mod` is the command modifier (⌘ on macOS, Ctrl
 * elsewhere), followed by `Ctrl`, `Alt` and `Shift` in any order and one key. macOS shows the symbols in Apple's order
 * without separators (`⇧⌘Z`); other platforms spell the modifiers out (`Ctrl+Shift+Z`).
 */
export function shortcutLabel(binding: string, platform: ShortcutPlatform = shortcutPlatform()): string {
  const parts = binding.split("+").filter((part) => part !== "");
  const key = parts.pop() ?? "";
  const modifiers = modifierOrder.filter((modifier) => parts.includes(modifier));
  if (platform === "mac") return modifiers.map((modifier) => macSymbols[modifier]).join("") + key;
  // Elsewhere the command modifier is Ctrl; Ctrl+Mod is still one Ctrl.
  const named = [...new Set(modifiers.map((modifier) => modifier === "Mod" ? "Ctrl" : modifier))]
    .sort((left, right) => modifierOrder.indexOf(left) - modifierOrder.indexOf(right));
  return [...named, key].join("+");
}
