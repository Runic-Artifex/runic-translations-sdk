import type { UiMessage } from "../ui-text";

/**
 * The page's two notices: the error of the latest user action (shown in the validation area, the welcome screen and
 * the open dialog) and the toolbar's report of the latest operation.
 */
export class EditorFeedback {
  clientError = $state<UiMessage>();
  operationMessage = $state<UiMessage>();

  /** Forgets both, as an action does before it reports its own outcome. */
  clear(): void {
    this.clientError = undefined;
    this.operationMessage = undefined;
  }
}
