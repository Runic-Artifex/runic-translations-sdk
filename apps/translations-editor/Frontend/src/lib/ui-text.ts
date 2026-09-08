import { createContext } from "svelte";
import { m } from "virtual:runic-translations/editor";

/**
 * The editor's UI text is deliberately scoped to one rendered application.
 * A component never owns a locale; it asks this context for the currently
 * selected interface locale instead.
 */
export type UiTextArguments = Readonly<Record<string, string | number | boolean>>;

export interface UiText {
  text(key: string, argumentsValue?: UiTextArguments): string;
}

const [useUiText, setUiText] = createContext<UiText>();

export { setUiText };

export function getUiText(): UiText {
  return useUiText();
}

export function createUiText(locale: () => string): UiText {
  type Options = Readonly<{ locale?: string }>;
  const messages = m as unknown as Readonly<Record<string,
    (inputsOrOptions?: UiTextArguments | Options, options?: Options) => string>>;
  return {
    text(key: string, argumentsValue?: UiTextArguments): string {
      const message = messages[key];
      if (message === undefined) return `[[${key}]]`;
      const options = { locale: locale() };
      return argumentsValue === undefined ? message(options) : message(argumentsValue, options);
    },
  };
}

/** Keep notices as message identity and data until render so locale changes stay reactive. */
export interface UiNotice {
  code: string;
  args: Array<{ name: string; value?: string; number?: number }>;
  detail?: string;
}
export type UiMessage = string | UiNotice;

export class UiNoticeError extends Error {
  constructor(readonly notice: UiNotice) {
    super(notice.code);
    this.name = "UiNoticeError";
  }
}

export function notice(code: string, argumentsValue: UiTextArguments = {}): UiNotice {
  return {
    code,
    args: Object.entries(argumentsValue).map(([name, value]) => typeof value === "number"
      ? { name, number: value }
      : { name, value: String(value) }),
  };
}

export function displayNotice(value: UiMessage | undefined, ui: UiText): string {
  if (value === undefined) return "";
  if (typeof value === "string") return value;
  const argumentsValue: Record<string, string | number> = Object.fromEntries(
    value.args.map((argument) => [argument.name, argument.number ?? argument.value ?? ""]),
  );
  if (value.code === "ui_count_review_marked" && typeof argumentsValue.state === "string") {
    argumentsValue.state = ui.text("ui_review_state_" + argumentsValue.state.replaceAll("-", "_"));
  }
  if (value.code === "ui_backend_external_error") argumentsValue.detail = value.detail ?? "";
  const text = ui.text(value.code, Object.keys(argumentsValue).length === 0 ? undefined : argumentsValue);
  return value.detail === undefined || value.code === "ui_backend_external_error" ? text : `${text} ${value.detail}`;
}
