<script lang="ts">
  import PlusIcon from "@lucide/svelte/icons/plus";
  import Trash2Icon from "@lucide/svelte/icons/trash-2";
  import AppDialog from "#lib/AppDialog.svelte";
  import * as Alert from "#lib/components/ui/alert/index.js";
  import { Badge } from "#lib/components/ui/badge/index.js";
  import { Button } from "#lib/components/ui/button/index.js";
  import { Checkbox } from "#lib/components/ui/checkbox/index.js";
  import * as Field from "#lib/components/ui/field/index.js";
  import { Input } from "#lib/components/ui/input/index.js";
  import * as Select from "#lib/components/ui/select/index.js";
  import { Spinner } from "#lib/components/ui/spinner/index.js";
  import type { EditorLocaleCatalog } from "#lib/contracts.js";
  import { projectWizardSteps, type ProjectWizardController } from "#lib/state/project-wizard.svelte.js";
  import { displayNotice, getUiText } from "#lib/ui-text.js";

  let { wizard, localeCatalog }: {
    wizard: ProjectWizardController;
    /** The SDK's CLDR locales, suggested for the project's languages. */
    localeCatalog: EditorLocaleCatalog | undefined;
  } = $props();
  const ui = getUiText();
</script>

{#if wizard.open}
  <AppDialog
    open
    title={ui.text("ui_page_project_wizard_title")}
    description={ui.text("ui_page_project_wizard_description")}
    class="sm:max-w-3xl"
    showCloseButton={!wizard.busy}
    onopenchange={(open) => { if (!open && !wizard.busy) wizard.close(); }}
  >
    <ol class="mb-6 grid grid-cols-2 gap-2 sm:grid-cols-5" aria-label={ui.text("ui_page_project_wizard_steps_aria_label")}>
      {#each [ui.text("ui_page_project_wizard_step_project"), ui.text("ui_page_project_wizard_step_languages"), ui.text("ui_page_project_wizard_step_settings"), ui.text("ui_page_project_wizard_step_import"), ui.text("ui_page_project_wizard_step_review")] as title, index (title)}
        <li class="flex items-center gap-2 text-sm" aria-current={wizard.step === index + 1 ? "step" : undefined}>
          <Badge variant={wizard.step === index + 1 ? "default" : wizard.step > index + 1 ? "secondary" : "outline"}>{wizard.step > index + 1 ? "✓" : index + 1}</Badge>
          <span class={wizard.step === index + 1 ? "font-medium" : "text-muted-foreground"}>{title}</span>
        </li>
      {/each}
    </ol>

    {#if wizard.step === 1}
      <div class="mb-5"><h3 class="font-medium">{ui.text("ui_page_project_wizard_where_live")}</h3><p class="text-sm text-muted-foreground">{ui.text("ui_page_project_wizard_no_overwrite")}</p></div>
      <Field.FieldGroup>
        <Field.Field><Field.Label for="project-directory">{ui.text("ui_page_project_wizard_new_directory")}</Field.Label><Input id="project-directory" bind:value={wizard.directory} placeholder="/projects/customer-app/Resources" autocomplete="off" /><Field.Description>{ui.text("ui_page_project_wizard_directory_description")}</Field.Description></Field.Field>
        <Field.Field><Field.Label for="project-catalog">{ui.text("ui_page_project_wizard_catalog_id")}</Field.Label><Input id="project-catalog" bind:value={wizard.catalog} placeholder="product" autocomplete="off" /><Field.Description>{ui.text("ui_page_project_wizard_catalog_id_description")}</Field.Description></Field.Field>
      </Field.FieldGroup>
    {:else if wizard.step === 2}
      <div class="mb-5"><h3 class="font-medium">{ui.text("ui_page_project_wizard_which_languages")}</h3><p class="text-sm text-muted-foreground">{ui.text("ui_page_project_wizard_language_description")}</p></div>
      <div class="grid gap-3">
        <div class="grid items-end gap-3 rounded-xl border p-4 sm:grid-cols-[1fr_auto]">
          <Field.Field><Field.Label for="project-default-locale">{ui.text("ui_page_project_wizard_source_default")}</Field.Label><Input id="project-default-locale" bind:value={wizard.defaultLocale} placeholder="de" list="cldr-locales-wizard" autocomplete="off" /></Field.Field>
          <Badge variant="secondary" class="mb-2">{ui.text("ui_page_project_wizard_canonical_source")}</Badge>
        </div>
        {#each wizard.locales as locale (locale.id)}
          <div class="grid items-end gap-3 rounded-xl border p-4 sm:grid-cols-[1fr_1fr_auto]">
            <Field.Field><Field.Label for={`project-locale-${locale.id}`}>{ui.text("ui_page_project_wizard_additional_language")}</Field.Label><Input id={`project-locale-${locale.id}`} bind:value={locale.tag} placeholder="en" list="cldr-locales-wizard" autocomplete="off" /></Field.Field>
            <Field.Field><Field.Label for={`project-fallback-${locale.id}`}>{ui.text("ui_page_mutation_fallback")}</Field.Label>
              <Select.Root type="single" value={locale.fallback} onValueChange={(value) => locale.fallback = value}>
                <Select.Trigger id={`project-fallback-${locale.id}`} class="w-full">{locale.fallback || `${ui.text("ui_page_project_wizard_default")} (${wizard.defaultLocale || ui.text("ui_page_project_wizard_source")})`}</Select.Trigger>
                <Select.Content><Select.Group><Select.Item value="" label={`${ui.text("ui_page_project_wizard_default")} (${wizard.defaultLocale || ui.text("ui_page_project_wizard_source")})`}>{ui.text("ui_page_project_wizard_default")} ({wizard.defaultLocale || ui.text("ui_page_project_wizard_source")})</Select.Item>{#each wizard.locales.filter((candidate) => candidate.id !== locale.id && candidate.tag.trim() !== "") as candidate (candidate.id)}<Select.Item value={candidate.tag} label={candidate.tag}>{candidate.tag}</Select.Item>{/each}</Select.Group></Select.Content>
              </Select.Root>
            </Field.Field>
            <Button variant="ghost" size="icon-sm" aria-label={ui.text("ui_project_remove_locale", { locale: locale.tag || ui.text("ui_page_project_wizard_row") })} onclick={() => wizard.removeLocale(locale.id)}><Trash2Icon /></Button>
          </div>
        {/each}
        <datalist id="cldr-locales-wizard">{#each localeCatalog?.locales ?? [] as tag (tag)}<option value={tag}></option>{/each}</datalist>
        <Button variant="outline" class="justify-self-start" onclick={() => wizard.addLocale()}><PlusIcon data-icon="inline-start" />{ui.text("ui_page_project_wizard_add_language")}</Button>
      </div>
    {:else if wizard.step === 3}
      <div class="mb-5"><h3 class="font-medium">{ui.text("ui_page_project_wizard_generated_api")}</h3><p class="text-sm text-muted-foreground">{ui.text("ui_page_project_wizard_generated_api_description")}</p></div>
      <Field.FieldGroup>
        <div class="grid gap-4 sm:grid-cols-2">
          <Field.Field><Field.Label for="project-namespace">{ui.text("ui_page_project_wizard_code_namespace")}</Field.Label><Input id="project-namespace" bind:value={wizard.namespace} autocomplete="off" /></Field.Field>
          <Field.Field><Field.Label for="project-class">{ui.text("ui_page_project_wizard_generated_class")}</Field.Label><Input id="project-class" bind:value={wizard.className} autocomplete="off" /></Field.Field>
        </div>
        <Field.Field orientation="horizontal"><Checkbox id="project-starter" bind:checked={wizard.includeStarter} /><Field.Content><Field.Label for="project-starter">{ui.text("ui_page_project_wizard_add_starter")}</Field.Label><Field.Description>{ui.text("ui_page_project_wizard_add_starter_description")}</Field.Description></Field.Content></Field.Field>
      </Field.FieldGroup>
    {:else if wizard.step === 4}
      <div class="mb-5"><h3 class="font-medium">{ui.text("ui_catalog_import_title")}</h3><p class="text-sm text-muted-foreground">{ui.text("ui_page_project_wizard_import_description")}</p></div>
      <Field.FieldGroup>
        <Field.Field><Field.Label for="wizard-import-format">{ui.text("ui_catalog_import_format")}</Field.Label>
          <select id="wizard-import-format" class="h-9 rounded-md border bg-background px-2 text-sm" bind:value={wizard.importFrom}>
            <option value="">{ui.text("ui_page_project_wizard_import_none")}</option>
            <option value="resx">.resx</option>
            <option value="i18next">i18next</option>
          </select>
        </Field.Field>
        {#if wizard.importFrom !== ""}
          <Field.Field><Field.Label for="wizard-import-source">{ui.text("ui_catalog_import_source")}</Field.Label>
            <Input id="wizard-import-source" bind:value={wizard.importSource} placeholder={wizard.importFrom === "resx" ? "/projects/customer-app/Properties/Strings.resx" : "/projects/customer-web/public/locales"} autocomplete="off" />
            <Field.Description>{wizard.importFrom === "resx" ? ui.text("ui_catalog_import_format_resx") : ui.text("ui_catalog_import_format_i18next")}</Field.Description>
          </Field.Field>
        {/if}
      </Field.FieldGroup>
    {:else if wizard.step === projectWizardSteps && wizard.plan !== undefined}
      <Alert.Root><Alert.Title>{ui.text("ui_page_project_wizard_ready_create")} {wizard.plan.catalogId}</Alert.Title><Alert.Description>{ui.text("ui_count_project_summary", { languages: wizard.plan.locales.length, files: wizard.plan.files.length })}</Alert.Description></Alert.Root>
      <dl class="mt-4 grid gap-3 rounded-xl border p-4"><div class="grid gap-1 sm:grid-cols-[7rem_1fr]"><dt class="text-muted-foreground">{ui.text("ui_page_project_wizard_directory")}</dt><dd class="m-0 truncate font-mono text-xs">{wizard.plan.directory}</dd></div><div class="grid gap-1 sm:grid-cols-[7rem_1fr]"><dt class="text-muted-foreground">{ui.text("ui_page_project_wizard_languages")}</dt><dd class="m-0 font-medium">{wizard.plan.locales.map((locale) => locale.tag).join(", ")}</dd></div></dl>
      {#if wizard.importFrom !== ""}<p class="mt-4 text-sm">{ui.text("ui_catalog_import_title")}: <code>{wizard.importSource}</code></p>{/if}
      <section class="mt-4 overflow-hidden rounded-xl border" aria-label={ui.text("ui_page_project_wizard_files_to_create")}><h4 class="border-b px-4 py-3 font-medium">{ui.text("ui_page_project_wizard_files_to_create")}</h4>{#each wizard.plan.files as file (file)}<div class="border-b px-4 py-3 last:border-b-0"><code class="text-xs">{file}</code></div>{/each}</section>
    {/if}

    {#if wizard.error}<Alert.Root variant="destructive" class="mt-4" aria-live="polite"><Alert.Title>{ui.text("ui_page_project_wizard_invalid")}</Alert.Title><Alert.Description>{displayNotice(wizard.error, ui)}</Alert.Description></Alert.Root>{/if}
    {#snippet footer()}
      <Button variant="outline" disabled={wizard.busy} onclick={() => wizard.close()}>{ui.text("ui_page_cancel")}</Button>
      {#if wizard.step > 1}<Button variant="ghost" disabled={wizard.busy} onclick={() => wizard.back()}>{ui.text("ui_page_back")}</Button>{/if}
      {#if wizard.step < projectWizardSteps}
        <Button disabled={wizard.busy} onclick={() => void wizard.advance()}>{#if wizard.busy}<Spinner data-icon="inline-start" />{/if}{wizard.busy ? ui.text("ui_page_validating") : ui.text("ui_page_continue")}</Button>
      {:else}
        <Button disabled={wizard.busy || wizard.plan?.ok !== true} onclick={() => void wizard.create()}>{#if wizard.busy}<Spinner data-icon="inline-start" />{/if}{wizard.busy ? ui.text("ui_page_creating") : ui.text("ui_page_project_wizard_create_project")}</Button>
      {/if}
    {/snippet}
  </AppDialog>
{/if}
