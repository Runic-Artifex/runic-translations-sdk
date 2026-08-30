import { createContext } from "svelte";
import { m } from "virtual:runic-translations/editor";

/**
 * The editor's UI text is deliberately scoped to one rendered application.
 * A component never owns a locale; it asks this context for the currently
 * selected interface locale instead.
 */
export interface UiText {
  text(key: string): string;
}

const [useUiText, setUiText] = createContext<UiText>();

export { setUiText };

export function getUiText(): UiText {
  return useUiText();
}

export function createUiText(locale: () => string): UiText {
  const messages = m as Readonly<Record<string, (options?: Readonly<{ locale?: string }>) => string>>;
  return {
    text(key: string): string {
      const message = messages[key];
      if (message === undefined) return `[[${key}]]`;
      return message({ locale: locale() });
    },
  };
}
