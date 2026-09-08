import type { UiText } from "./ui-text";
import type { ThemeMode } from "./appearance";
import type { EditorReviewState } from "./contracts";
import type { EditorBridge } from "./editor-bridge";
import type { UiDirection } from "./simulation";

export type PaletteCommandGroupId = "workspace" | "document" | "review" | "view" | "help";

export interface PaletteCommand {
  readonly id: string;
  readonly title: string;
  readonly group: PaletteCommandGroupId;
  readonly keywords?: string;
  readonly keybinding?: string;
  readonly disabled?: boolean;
  readonly bridge?: keyof EditorBridge;
  run(): void;
}

export interface PaletteCommandGroup {
  readonly id: PaletteCommandGroupId;
  readonly label: string;
  readonly commands: readonly PaletteCommand[];
}

export interface EditorPaletteActions {
  reloadWorkspace(): void;
  openWorkspaceDialog(): void;
  createProject(): void;
  openInterchange(): void;
  saveDocument(): void;
  undo(): void;
  redo(): void;
  focusMessageSearch(): void;
  saveReview(): void;
  discardReview(): void;
  setMessageReviewState(state: EditorReviewState): void;
  markVisibleMessages(state: EditorReviewState): void;
  openTerminology(): void;
  openQualityReport(): void;
  showAbout(): void;
  createDiagnosticBundle(): void;
  setEditorMode(mode: "translation" | "raw"): void;
  selectLocale(locale: string): void;
  setUiLocale(locale: string): void;
  setThemeMode(mode: ThemeMode): void;
  toggleLanguagesSection(): void;
  toggleMessagesSection(): void;
  togglePseudoLocalization(): void;
  toggleUiDirection(): void;
  toggleArtifactPreview(): void;
}

export interface EditorPaletteState {
  readonly locales: ReadonlyArray<{ tag: string; name: string }>;
  readonly selectedLocale: string;
  readonly editorMode: "translation" | "raw";
  readonly uiLocale: string;
  readonly themeMode: ThemeMode;
  readonly workspaceReady: boolean;
  readonly searchAvailable: boolean;
  readonly documentDirty: boolean;
  readonly canUndo: boolean;
  readonly canRedo: boolean;
  readonly reviewEditable: boolean;
  readonly reviewDirty: boolean;
  readonly reviewError: boolean;
  readonly pseudoLocalization: boolean;
  readonly uiDirection: UiDirection;
  readonly artifactPreviewOpen: boolean;
}

const paletteGroupLabels: Record<PaletteCommandGroupId, string> = {
  workspace: "Workspace",
  document: "Document",
  review: "Review",
  view: "View",
  help: "Help",
};

const paletteGroupOrder: readonly PaletteCommandGroupId[] = [
  "workspace",
  "document",
  "review",
  "view",
  "help",
];

/** Bridge façade operations that map one-to-one onto a palette entry. */
export const paletteBridgeOperations = [
  "load",
  "save",
  "undo",
  "redo",
  "saveReview",
  "about",
  "createDiagnosticBundle",
] as const satisfies readonly (keyof EditorBridge)[];

export function buildEditorCommandPalette(
  actions: EditorPaletteActions,
  state: EditorPaletteState,
  ui?: UiText,
): PaletteCommand[] {
  const commands: PaletteCommand[] = [];
  const push = (command: PaletteCommand): void => {
    commands.push(command);
  };

  push({
    id: "workspace.reload",
    title: (ui?.text("ui_palette_reload_workspace_files") ?? "Reload workspace files"),
    group: "workspace",
    keywords: "refresh rescan disk",
    disabled: !state.workspaceReady,
    bridge: "load",
    run: actions.reloadWorkspace,
  });
  push({
    id: "workspace.interchange",
    title: (ui?.text("ui_palette_import_or_export_interchange_files") ?? "Import or export interchange files…"),
    group: "workspace",
    keywords: "xliff xlf review json localization handoff",
    disabled: !state.workspaceReady,
    run: actions.openInterchange,
  });
  push({
    id: "workspace.open",
    title: (ui?.text("ui_palette_open_another_workspace") ?? "Open another workspace…"),
    group: "workspace",
    keywords: "switch directory catalog recent",
    disabled: !state.workspaceReady,
    run: actions.openWorkspaceDialog,
  });
  push({
    id: "workspace.new-project",
    title: (ui?.text("ui_palette_create_new_project") ?? "Create new project…"),
    group: "workspace",
    keywords: "wizard scaffold catalog",
    run: actions.createProject,
  });

  push({
    id: "document.save",
    title: (ui?.text("ui_palette_save_document") ?? "Save document"),
    group: "document",
    keybinding: "Ctrl+S",
    keywords: "write persist commit",
    disabled: !state.documentDirty,
    bridge: "save",
    run: actions.saveDocument,
  });
  push({
    id: "document.undo",
    title: (ui?.text("ui_palette_undo_saved_change") ?? "Undo saved change"),
    group: "document",
    keybinding: "Ctrl+Z",
    keywords: "revert history back",
    disabled: !state.canUndo,
    bridge: "undo",
    run: actions.undo,
  });
  push({
    id: "document.redo",
    title: (ui?.text("ui_palette_redo_saved_change") ?? "Redo saved change"),
    group: "document",
    keybinding: "Ctrl+Shift+Z",
    keywords: "history forward",
    disabled: !state.canRedo,
    bridge: "redo",
    run: actions.redo,
  });

  push({
    id: "review.save-workflow",
    title: (ui?.text("ui_palette_save_workflow_changes") ?? "Save workflow changes"),
    group: "review",
    keywords: "sidecar persist approve state terminology",
    disabled: !state.reviewDirty || state.reviewError,
    bridge: "saveReview",
    run: actions.saveReview,
  });
  push({
    id: "review.discard-workflow",
    title: (ui?.text("ui_palette_discard_workflow_changes") ?? "Discard workflow changes"),
    group: "review",
    keywords: "revert sidecar reset",
    disabled: !state.reviewDirty,
    run: actions.discardReview,
  });
  push({
    id: "review.state-needs-review",
    title: (ui?.text("ui_palette_mark_message_as_needs_review") ?? "Mark message as needs review"),
    group: "review",
    keywords: "flag current selection",
    disabled: !state.reviewEditable,
    run: () => actions.setMessageReviewState("needs-review"),
  });
  push({
    id: "review.state-approved",
    title: (ui?.text("ui_palette_approve_message") ?? "Approve message"),
    group: "review",
    keywords: "accept current selection",
    disabled: !state.reviewEditable,
    run: () => actions.setMessageReviewState("approved"),
  });
  push({
    id: "review.state-translated",
    title: (ui?.text("ui_palette_mark_message_as_translated") ?? "Mark message as translated"),
    group: "review",
    keywords: "current selection",
    disabled: !state.reviewEditable,
    run: () => actions.setMessageReviewState("translated"),
  });
  push({
    id: "review.state-draft",
    title: (ui?.text("ui_palette_mark_message_as_draft") ?? "Mark message as draft"),
    group: "review",
    keywords: "reset current selection",
    disabled: !state.reviewEditable,
    run: () => actions.setMessageReviewState("draft"),
  });
  push({
    id: "review.mark-visible-needs-review",
    title: (ui?.text("ui_palette_mark_visible_messages_needs_review") ?? "Mark visible messages needs review"),
    group: "review",
    keywords: "bulk filter all listed flag",
    disabled: !state.reviewEditable,
    run: () => actions.markVisibleMessages("needs-review"),
  });
  push({
    id: "review.mark-visible-approved",
    title: (ui?.text("ui_palette_approve_visible_messages") ?? "Approve visible messages"),
    group: "review",
    keywords: "bulk filter all listed accept",
    disabled: !state.reviewEditable,
    run: () => actions.markVisibleMessages("approved"),
  });
  push({
    id: "review.terminology",
    title: (ui?.text("ui_palette_open_project_terminology") ?? "Open project terminology…"),
    group: "review",
    keywords: "glossary terms preferred translations",
    disabled: !state.reviewEditable,
    run: actions.openTerminology,
  });
  push({
    id: "review.quality-report",
    title: (ui?.text("ui_palette_open_quality_report") ?? "Open quality report…"),
    group: "review",
    keywords: "findings csv export issues",
    disabled: !state.reviewEditable,
    run: actions.openQualityReport,
  });

  push({
    id: "view.mode-translation",
    title: (ui?.text("ui_palette_switch_to_translation_mode") ?? "Switch to translation mode"),
    group: "view",
    keywords: "simple editor structured",
    disabled: state.editorMode === "translation",
    run: () => actions.setEditorMode("translation"),
  });
  push({
    id: "view.mode-raw",
    title: (ui?.text("ui_palette_switch_to_raw_json_mode") ?? "Switch to source mode"),
    group: "view",
    keywords: "document source format json",
    disabled: state.editorMode === "raw",
    run: () => actions.setEditorMode("raw"),
  });
  for (const locale of state.locales) {
    push({
      id: `view.locale:${locale.tag}`,
      title: ui?.text("ui_palette_language", { locale: locale.tag }) ?? `Language · ${locale.tag}`,
      group: "view",
      keywords: `${locale.name} switch locale translate`,
      disabled: locale.tag === state.selectedLocale,
      run: () => actions.selectLocale(locale.tag),
    });
  }
  if (state.uiLocale !== "en") {
    push({
      id: "view.ui-locale-en",
      title: (ui?.text("ui_palette_interface_language_english") ?? "Interface language · English"),
      group: "view",
      keywords: "ui language english menues",
      run: () => actions.setUiLocale("en"),
    });
  }
  if (state.uiLocale !== "de") {
    push({
      id: "view.ui-locale-de",
      title: (ui?.text("ui_palette_interface_language_deutsch") ?? "Interface language · Deutsch"),
      group: "view",
      keywords: "ui language german",
      run: () => actions.setUiLocale("de"),
    });
  }
  for (const mode of ["light", "dark", "system"] as const) {
    push({
      id: `view.theme-${mode}`,
      title: ui?.text("ui_palette_theme", { theme: ui.text("ui_palette_theme_" + mode) }) ?? `Theme · ${mode[0].toLocaleUpperCase()}${mode.slice(1)}`,
      group: "view",
      keywords: "appearance color light dark system",
      disabled: state.themeMode === mode,
      run: () => actions.setThemeMode(mode),
    });
  }
  push({
    id: "view.focus-search",
    title: (ui?.text("ui_palette_focus_message_search") ?? "Focus message search"),
    group: "view",
    keywords: "filter find query input",
    disabled: !state.searchAvailable,
    run: actions.focusMessageSearch,
  });
  push({
    id: "view.toggle-languages",
    title: (ui?.text("ui_palette_toggle_languages_section") ?? "Toggle languages section"),
    group: "view",
    keywords: "collapse expand sidebar panel",
    run: actions.toggleLanguagesSection,
  });
  push({
    id: "view.toggle-messages",
    title: (ui?.text("ui_palette_toggle_messages_section") ?? "Toggle messages section"),
    group: "view",
    keywords: "collapse expand sidebar panel list",
    run: actions.toggleMessagesSection,
  });
  push({
    id: "view.toggle-pseudo-localization",
    title: state.pseudoLocalization ? (ui?.text("ui_palette_disable_pseudo_localization_simulation") ?? "Disable pseudo-localization simulation") : (ui?.text("ui_palette_enable_pseudo_localization_simulation") ?? "Enable pseudo-localization simulation"),
    group: "view",
    keybinding: "Alt+P",
    keywords: "pseudo fake accented brackets lengthening qa locale preview",
    run: actions.togglePseudoLocalization,
  });
  push({
    id: "view.toggle-rtl-simulation",
    title: state.uiDirection === "rtl" ? (ui?.text("ui_palette_simulate_right_to_left_layout_off") ?? "Simulate right-to-left layout off") : (ui?.text("ui_palette_simulate_right_to_left_layout") ?? "Simulate right-to-left layout"),
    group: "view",
    keybinding: "Alt+R",
    keywords: "rtl ltr direction bidi mirror arabic hebrew layout",
    run: actions.toggleUiDirection,
  });
  push({
    id: "view.toggle-artifact-preview",
    title: state.artifactPreviewOpen ? (ui?.text("ui_palette_hide_compiled_artifact_preview") ?? "Hide compiled artifact preview") : (ui?.text("ui_palette_show_compiled_artifact_preview") ?? "Show compiled artifact preview"),
    group: "view",
    keybinding: "Alt+B",
    keywords: "artifact compiled output side-by-side compare in-editor no launch",
    run: actions.toggleArtifactPreview,
  });

  push({
    id: "help.about",
    title: (ui?.text("ui_palette_about_diagnostics_information") ?? "About & diagnostics information"),
    group: "help",
    keywords: "version runtime product info",
    bridge: "about",
    run: actions.showAbout,
  });
  push({
    id: "help.diagnostic-bundle",
    title: (ui?.text("ui_palette_create_sanitized_diagnostic_bundle") ?? "Create sanitized diagnostic bundle"),
    group: "help",
    keywords: "zip privacy support report",
    bridge: "createDiagnosticBundle",
    run: actions.createDiagnosticBundle,
  });

  return commands;
}

export function filterCommands(
  commands: readonly PaletteCommand[],
  query: string,
): PaletteCommand[] {
  const needle = query.trim().toLocaleLowerCase();
  if (needle === "") return [...commands];
  const ranked: Array<{ command: PaletteCommand; score: number }> = [];
  for (const command of commands) {
    const score = matchScore(command, needle);
    if (score !== undefined) ranked.push({ command, score });
  }
  return ranked.sort((left, right) => right.score - left.score).map((entry) => entry.command);
}

function matchScore(command: PaletteCommand, needle: string): number | undefined {
  const title = command.title.toLocaleLowerCase();
  const direct = title.indexOf(needle);
  if (direct >= 0) {
    const wordStart = direct === 0 || title[direct - 1] === " ";
    return 1000 - Math.min(direct, 100) + (wordStart ? 250 : 0);
  }
  const haystack = `${title} ${command.keywords ?? ""}`.toLocaleLowerCase();
  let cursor = 0;
  let previous = -1;
  let gaps = 0;
  for (const character of needle) {
    const found = haystack.indexOf(character, cursor);
    if (found < 0) return undefined;
    if (previous >= 0 && found > previous + 1) gaps += found - previous - 1;
    previous = found;
    cursor = found + 1;
  }
  return 400 - Math.min(gaps, 300);
}

export function groupCommands(commands: readonly PaletteCommand[], ui?: UiText): PaletteCommandGroup[] {
  const grouped = new Map<PaletteCommandGroupId, PaletteCommand[]>();
  for (const command of commands) {
    const bucket = grouped.get(command.group);
    if (bucket === undefined) grouped.set(command.group, [command]);
    else bucket.push(command);
  }
  return [...grouped]
    .sort((left, right) =>
      paletteGroupOrder.indexOf(left[0]) - paletteGroupOrder.indexOf(right[0]))
    .map(([id, groupCommands]) => ({
      id,
      label: ui?.text("ui_palette_group_" + id) ?? paletteGroupLabels[id],
      commands: groupCommands,
    }));
}

export function movePaletteSelection(
  activeIndex: number,
  delta: number,
  count: number,
): number {
  if (count <= 0) return 0;
  const next = (activeIndex + delta) % count;
  return next < 0 ? next + count : next;
}
