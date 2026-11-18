<script lang="ts">
  import type { EditorCatalog } from "#lib/contracts.js";
  import type { EditorLabels } from "#lib/editor-labels.js";
  import type { TranslationRow } from "#lib/resource-model.js";
  import type { DiagnosticsStore } from "#lib/state/diagnostics-store.svelte.js";
  import type { DraftStore } from "#lib/state/draft-store.svelte.js";
  import type { MessageEditingController } from "#lib/state/message-editing.svelte.js";
  import type { WorkspaceView } from "#lib/state/workspace-view.svelte.js";
  import { displayNotice, getUiText, type UiMessage } from "#lib/ui-text.js";
  import ValidationPanel from "#lib/ValidationPanel.svelte";

  let {
    catalog,
    row,
    locale,
    drafts,
    diagnostics,
    view,
    editing,
    labels,
    clientError,
  }: {
    catalog: EditorCatalog;
    /** The selected message, whose diagnostics are listed first. */
    row: TranslationRow | undefined;
    locale: string;
    drafts: Pick<DraftStore, "validating">;
    diagnostics: DiagnosticsStore;
    view: Pick<WorkspaceView, "diagnosticSources">;
    editing: Pick<MessageEditingController, "selectDiagnostic" | "applyDiagnosticFix">;
    labels: EditorLabels;
    clientError: UiMessage | undefined;
  } = $props();
  const ui = getUiText();
</script>

<!-- The diagnostics of the selected message, then every other error, with the error of the latest action. -->
<ValidationPanel
  busy={drafts.validating}
  diagnostics={diagnostics.displayed}
  clientError={clientError === undefined ? undefined : displayNotice(clientError, ui)}
  errorCount={diagnostics.errorCount}
  warningCount={diagnostics.warningCount}
  validLabel={labels.valid}
  invalidLabel={labels.invalid}
  diagnosticsLabel={labels.diagnostics}
  schemaVersion={catalog.schemaVersion}
  sources={view.diagnosticSources}
  selection={row === undefined ? undefined : { key: row.key, locale }}
  onselect={(diagnostic) => void editing.selectDiagnostic(diagnostic)}
  onfix={(diagnostic, fix) => void editing.applyDiagnosticFix(diagnostic, fix)}
/>
