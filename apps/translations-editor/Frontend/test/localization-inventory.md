# Editor localization inventory

The runtime catalog is `EditorResources/runic.json` with `sourceLayout: locale-toml`, `en.toml`, and `de.toml`. The example workspace uses `en.toml`, `de.toml`, and `fr.toml`. Migration uses the shared authoring transaction and retains decoded MF2 source; no private inputs are used.

| Surface | Locale keys / ownership |
| --- | --- |
| Workspace chooser, repair, recovery, create-project wizard, structural mutation dialogs, about/diagnostics, terminology, quality report, previews | `ui_page_*`, `ui_confirm_*`, `ui_feedback_*`, `ui_count_*`; page calls `ui.text` at render or stores a structured notice |
| Message list, filters, save state | `app_*`, `ui_resource_*`, `ui_count_resource_variants`; generated message functions or reactive UI context |
| Command palette, groups, language and theme actions | `ui_command_palette_*`, `ui_palette_*`; locale context supplied to command construction and grouping |
| Shared sidebar, dialogs, sheets, loading indicators | `ui_a11y_toggle_sidebar`, `ui_sidebar_*`, `ui_page_close`, `ui_loading`; hidden mobile description and screen-reader labels included |
| Inline editor, structured-message composer, pattern editor | `ui_inline_*`, `ui_composer_*`, `ui_pattern_*`, `ui_common_*`; parameterized accessible names |
| Toolbar, settings, sidebar panels, language summaries | `ui_toolbar_*`, `ui_settings_*`, `ui_project_*`, `ui_sidebar_*`, `ui_locale_*` |
| Workflow, quality findings and reports | `ui_workflow_*`, `ui_quality_*`, `ui_review_state_*`; quality findings regenerate when interface locale changes |
| XLIFF and review interchange | `ui_interchange_*`, `ui_count_exported_*`, `ui_backend_refusal_*`; typed backend notices render through the current locale |
| Hosted connection initialization and capability validation | `ui_host_*`; typed `UiNoticeError` preserves notice identity until rendering |
| Backend editor validation, conflicts, recovery, path checks, picker, history and diagnostic operations | `ui_backend_*`; `EditorNotice` carries code, typed arguments, and optional technical detail |

Count messages use MF2 `:integer select=plural`; plural branches contain complete sentences or complete standalone metric phrases. The project and quality summaries select on both counts. No UI-side singular/plural key selection remains. Integer display preserves the compiler's existing ungrouped integer format.

Deliberate replacements: `ui_page_recovered_drafts_one` and `_many` become `ui_count_recovered_drafts`; `ui_workspace_file` / `_files` become `ui_count_malformed_files`; `ui_project_locale` / `_locales` become `ui_count_project_locales`; `ui_page_project_wizard_language` is removed in favor of `ui_count_project_summary`. The plural-named project-wizard language key remains as the independent Languages heading.

Reviewed exemptions are product names (Runic Translations Editor), locale names in their native language, technical identifiers and grammar enum values (MF2/JSON/TOML, `auto`, `always`, plural categories, paths, locale tags, attribute/input/key names), keyboard shortcuts, numbers, punctuation, user-authored translations/notes/terminology, and compiler or OS diagnostic details. These details remain distinguishable from the localized editor-owned notice. English default strings in standalone palette/review model helpers support direct model callers; the rendered editor always passes its UI context.

Acceptance: `verify-ui-catalog.mjs` checks both locale key sets and static visible/accessibility copy, including shared UI components. `verify-localization-readiness.mjs` loads the real generated ESM manifest, runs every count message with 0/1/2/5/11/21/101/1000 in both locales through `createUiText`, checks selected exact plural outputs, and verifies stored frontend/backend notices react to locale changes. Browser/layout acceptance remains part of the parent editor verification, not established by source checks.

## Backend notice mapping

Every routine editor-owned backend notice uses the following stable resource key. Arguments remain structured through the bridge and are formatted when rendered.

| Key | English source meaning |
| --- | --- |
| `ui_backend_bundle_create` | The diagnostic bundle could not be created. |
| `ui_backend_bundle_delete` | The diagnostic bundle could not be deleted. |
| `ui_backend_bundle_missing` | That diagnostic bundle is no longer available in this user profile. |
| `ui_backend_bundle_reveal` | The diagnostic bundle location could not be opened. |
| `ui_backend_bundle_reveal_unsupported` | Revealing diagnostic bundles is not supported on this platform. |
| `ui_backend_bundle_size` | The diagnostic bundle exceeded its size limit. |
| `ui_backend_committed_reload` | The change was committed; reload the workspace to refresh it. |
| `ui_backend_config_required` | The workspace does not contain runic.json. |
| `ui_backend_confirm_change` | Preview this destructive change again and confirm the exact affected files. |
| `ui_backend_confirm_import` | Preview this import again to obtain a valid confirmation token. |
| `ui_backend_document_conflict` | '{$path}' changed on disk. Reload before saving your draft. |
| `ui_backend_document_missing` | '{$path}' no longer exists. |
| `ui_backend_document_size` | The translation document exceeds the compiler size limit. |
| `ui_backend_draft_validation` | The draft contains validation errors. |
| `ui_backend_duplicate_sample` | The review entry '{$key}' ({$locale}) lists the sample key '{$sample}' more than once. |
| `ui_backend_encoding` | The document contains text that cannot be encoded as UTF-8. |
| `ui_backend_external_error` | The operation could not be completed. Technical details: {$detail} |
| `ui_backend_history_document` | The saved document could not be changed. |
| `ui_backend_history_document_conflict` | The saved document changed after this operation; history was cleared. |
| `ui_backend_history_review` | Save workflow |
| `ui_backend_history_save` | Save {$path} |
| `ui_backend_history_sidecar` | The workflow sidecar changed after this operation; history was cleared. |
| `ui_backend_history_unsupported` | The saved history entry is unsupported. |
| `ui_backend_import_catalog_changed` | The catalog changed on disk. Preview the import again. |
| `ui_backend_import_compile` | The open catalog does not compile; fix the reported errors before importing. |
| `ui_backend_import_document_conflict` | '{$path}' changed on disk. Preview the import again. |
| `ui_backend_import_file_changed` | The imported file changed after it was previewed. Review it again. |
| `ui_backend_import_not_applied` | The import was not applied. |
| `ui_backend_import_partial` | The import could not be rolled back after a write failed. Reload the workspace and review the affected translation files. |
| `ui_backend_import_sidecar_changed` | The workflow sidecar changed on disk. Preview the import again. |
| `ui_backend_invalid_json` | The document is not valid JSON. |
| `ui_backend_no_redo` | There is no saved change to redo. |
| `ui_backend_no_undo` | There is no saved change to undo. |
| `ui_backend_not_message_document` | This document does not contain translation messages. |
| `ui_backend_path_escape` | The requested path escapes the workspace. |
| `ui_backend_path_link` | Translation sources cannot be symbolic links. |
| `ui_backend_path_relative` | Workspace paths must be relative. |
| `ui_backend_picker_start` | The native folder picker could not be started. |
| `ui_backend_picker_unavailable` | No native folder picker is available. Enter the workspace directory instead. |
| `ui_backend_preview_key_missing` | The compiled locale has no message '{$key}'. |
| `ui_backend_project_config_missing` | The Runic translation project has no runic.json file. |
| `ui_backend_project_required` | The workspace must contain translations/runic.json or runic.json and translation source files. |
| `ui_backend_recovered_reload` | Recovery completed; reload the workspace to refresh it. |
| `ui_backend_recovery_change` | Recover the interrupted transaction before making another change. |
| `ui_backend_recovery_history` | Recover the interrupted transaction before changing history. |
| `ui_backend_recovery_mode` | Recovery mode must be 'complete' or 'rollback'. |
| `ui_backend_recovery_required` | An interrupted workspace transaction requires recovery. |
| `ui_backend_recovery_review` | Recover the interrupted transaction before saving workflow data. |
| `ui_backend_recovery_save` | Recover the interrupted transaction before saving a document. |
| `ui_backend_refusal_1` | The document source locale '{$value1}' does not match the catalog default locale '{$value2}'. |
| `ui_backend_refusal_10` | The approved review entry '{$value1}' ({$value2}) was created for a different source catalog revision. |
| `ui_backend_refusal_2` | The imported document defines '{$value1}', which is not part of catalog '{$value2}'. |
| `ui_backend_refusal_3` | The review file targets catalog '{$value1}' but the open catalog is '{$value2}'. |
| `ui_backend_refusal_4` | The review file references '{$value1}', which is not part of catalog '{$value2}'. |
| `ui_backend_refusal_5` | The review file references locale '{$value1}', which the open catalog does not define. |
| `ui_backend_refusal_6` | The XLIFF source for '{$value1}' does not match the open catalog and cannot be applied. |
| `ui_backend_refusal_7` | The document targets catalog '{$value1}' but the open catalog is '{$value2}'. |
| `ui_backend_refusal_8` | The document targets locale '{$value1}', which the open catalog does not define. |
| `ui_backend_refusal_9` | The document targets default locale '{$value1}', which is canonical source text and cannot be imported through XLIFF. |
| `ui_backend_review_compile` | Review export requires a successfully compiled catalog. |
| `ui_backend_review_file_changed` | The review file changed after it was previewed. Import it again. |
| `ui_backend_saved_reload` | The document was saved; reload the workspace to refresh it. |
| `ui_backend_select_catalog_mutation` | Select a catalog before changing locales or keys. |
| `ui_backend_select_catalog_review` | Select a catalog before saving review data. |
| `ui_backend_select_catalog_review_change` | Select a catalog before changing review data. |
| `ui_backend_sidecar_history_conflict` | The editor-state sidecar changed on disk. Reload before changing history. |
| `ui_backend_source_outside_project` | '{$path}' is not a translation source in this project. |
| `ui_backend_unknown_mutation` | Unknown editor mutation '{$kind}'. |
| `ui_backend_workspace_missing` | The translation workspace '{$path}' does not exist. |
| `ui_backend_xliff_compile` | XLIFF export requires a successfully compiled catalog. |

Internal guards in `EditorLocalStateStore` (record shape, ownership, duplicate keys, storage bounds), `EditorHistory` (concurrent history invariant), and diagnostic legal-notice bounds are protocol/developer diagnostics, not normal user-facing status copy. The UI reports local-state recovery with localized frontend notices. Shared compiler, authoring sidecar, interchange-validator, filesystem, and native process errors are retained as technical detail under localized notices; user-authored paths, keys, samples, and source text remain unchanged. Native folder pickers use their operating system default localized titles.
