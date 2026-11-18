<script lang="ts">
  import AppDialog from "#lib/AppDialog.svelte";
  import * as Alert from "#lib/components/ui/alert/index.js";
  import { Badge } from "#lib/components/ui/badge/index.js";
  import { Button } from "#lib/components/ui/button/index.js";
  import { Checkbox } from "#lib/components/ui/checkbox/index.js";
  import * as Field from "#lib/components/ui/field/index.js";
  import { Input } from "#lib/components/ui/input/index.js";
  import * as Select from "#lib/components/ui/select/index.js";
  import { Spinner } from "#lib/components/ui/spinner/index.js";
  import { Textarea } from "#lib/components/ui/textarea/index.js";
  import type { EditorCatalog, EditorLocaleCatalog, EditorLocaleMatches } from "#lib/contracts.js";
  import type { MutationController, MutationKind } from "#lib/state/mutation-controller.svelte.js";
  import { displayNotice, getUiText } from "#lib/ui-text.js";

  let {
    mutation,
    catalog,
    localeCatalog,
    addMessageLabel,
    localeName,
  }: {
    mutation: MutationController;
    /** The open catalog; the dialog is shown only with one. */
    catalog: EditorCatalog | undefined;
    /** The SDK's CLDR locales, suggested for a new language. */
    localeCatalog: EditorLocaleCatalog | undefined;
    addMessageLabel: string;
    localeName(tag: string): string;
  } = $props();
  const ui = getUiText();

  function mutationTitle(kind: MutationKind): string {
    return {
      "add-locale": ui.text("ui_page_mutation_add_language"),
      "remove-locale": ui.text("ui_page_mutation_remove_language"),
      "set-fallback": ui.text("ui_page_mutation_change_fallback_relationships"),
      "create-key": addMessageLabel,
      "rename-key": ui.text("ui_page_mutation_rename_move_message"),
      "duplicate-key": ui.text("ui_page_mutation_duplicate_message"),
      "delete-key": ui.text("ui_page_mutation_delete_message"),
    }[kind];
  }

  // "With unsupportedLocale "exact", zh-Hant serves requests for: zh-Hant." (the tags the runtime resolves to it).
  function localeMatchesText(locale: string, matches: EditorLocaleMatches, policy: string): string {
    const more = matches.count - matches.tags.length;
    const tags = matches.tags.join(", ") + (more > 0 ? " " + ui.text("ui_locale_matches_more", { count: more }) : "");
    return ui.text("ui_locale_matches", { locale, policy, tags });
  }
</script>

{#if mutation.open && catalog !== undefined}
  <AppDialog
    open
    title={mutationTitle(mutation.kind)}
    description={ui.text("ui_page_mutation_description")}
    class="sm:max-w-3xl"
    showCloseButton={!mutation.busy}
    onopenchange={(open) => { if (!mutation.busy) mutation.open = open; }}
  >
    <Field.FieldGroup class="gap-4">
      {#if mutation.kind === "add-locale" || mutation.kind === "remove-locale" || mutation.kind === "set-fallback"}
        <Field.Field>
          <Field.Label for="language-operation">{ui.text("ui_page_mutation_language_operation")}</Field.Label>
          <Select.Root type="single" value={mutation.kind} onValueChange={(value) => mutation.changeKind(value)}>
            <Select.Trigger id="language-operation" class="w-full">{mutationTitle(mutation.kind)}</Select.Trigger>
            <Select.Content><Select.Group><Select.Label>{ui.text("ui_page_mutation_language_operation")}</Select.Label>
              <Select.Item value="add-locale" label={ui.text("ui_page_mutation_add_language")}>{ui.text("ui_page_mutation_add_language")}</Select.Item>
              <Select.Item value="remove-locale" label={ui.text("ui_page_mutation_remove_language")}>{ui.text("ui_page_mutation_remove_language")}</Select.Item>
              <Select.Item value="set-fallback" label={ui.text("ui_page_mutation_change_fallback")}>{ui.text("ui_page_mutation_change_fallback")}</Select.Item>
            </Select.Group></Select.Content>
          </Select.Root>
        </Field.Field>
      {/if}

      <datalist id="cldr-locales">{#each localeCatalog?.locales ?? [] as tag (tag)}<option value={tag}></option>{/each}</datalist>
      {#if mutation.kind === "add-locale"}
        <div class="grid gap-4 sm:grid-cols-2">
          <Field.Field><Field.Label for="mutation-locale">{ui.text("ui_page_mutation_new_locale_tag")}</Field.Label><Input id="mutation-locale" bind:value={mutation.locale} oninput={() => { mutation.invalidate(); mutation.describeLocale(); }} placeholder="fr-FR" list="cldr-locales" autocomplete="off" aria-describedby="mutation-locale-cldr" /></Field.Field>
          <Field.Field><Field.Label for="mutation-fallback">{ui.text("ui_page_mutation_fallback")}</Field.Label>
            <Select.Root type="single" value={mutation.fallback} onValueChange={(value) => { mutation.fallback = value; mutation.invalidate(); }}>
              <Select.Trigger id="mutation-fallback" class="w-full">{mutation.fallback} · {localeName(mutation.fallback)}</Select.Trigger>
              <Select.Content><Select.Group>{#each catalog.locales as locale (locale.tag)}<Select.Item value={locale.tag} label={`${locale.tag} · ${localeName(locale.tag)}`}>{locale.tag} · {localeName(locale.tag)}</Select.Item>{/each}</Select.Group></Select.Content>
            </Select.Root>
          </Field.Field>
          <Field.Field><Field.Label for="mutation-copy-from">{ui.text("ui_page_mutation_copy_starter_values")}</Field.Label>
            <Select.Root type="single" value={mutation.copyFrom} onValueChange={(value) => { mutation.copyFrom = value; mutation.invalidate(); }}>
              <Select.Trigger id="mutation-copy-from" class="w-full">{mutation.copyFrom} · {localeName(mutation.copyFrom)}</Select.Trigger>
              <Select.Content><Select.Group>{#each catalog.locales as locale (locale.tag)}<Select.Item value={locale.tag} label={`${locale.tag} · ${localeName(locale.tag)}`}>{locale.tag} · {localeName(locale.tag)}</Select.Item>{/each}</Select.Group></Select.Content>
            </Select.Root>
            <Field.Description>{ui.text("ui_page_mutation_copy_starter_values_description")}</Field.Description>
          </Field.Field>
        </div>
        <div id="mutation-locale-cldr" class="mt-3 grid gap-1 text-sm" aria-live="polite">
          {#if mutation.localeDescription?.valid && mutation.localeDescription.canonical}
            <p class="text-muted-foreground">{ui.text("ui_locale_cldr_summary", { locale: mutation.localeDescription.canonical, data: mutation.localeDescription.cldrLocale ?? "root", direction: mutation.localeDescription.direction === "rtl" ? ui.text("ui_locale_direction_rtl") : ui.text("ui_locale_direction_ltr") })}</p>
            {#if mutation.localeDescription.dataId && mutation.localeDescription.dataId !== mutation.localeDescription.canonical}
              <p class="text-muted-foreground">{ui.text("ui_locale_cldr_canonical", { locale: mutation.localeDescription.dataId })}</p>
            {/if}
            {#if mutation.localeDescription.matches && mutation.localeDescription.unsupportedLocale}
              <p data-locale-matches={mutation.localeDescription.canonical}>{localeMatchesText(mutation.localeDescription.canonical, mutation.localeDescription.matches, mutation.localeDescription.unsupportedLocale)}</p>
            {/if}
            {#if mutation.localeDescription.suggested}
              <p class="flex flex-wrap items-center gap-2"><span>{ui.text("ui_locale_script_suggestion", { locale: mutation.localeDescription.suggested, typed: mutation.localeDescription.canonical })}</span>
                <Button variant="outline" size="xs" onclick={() => mutation.useSuggestedLocale()}>{ui.text("ui_locale_cldr_use", { locale: mutation.localeDescription.suggested })}</Button></p>
              {#if mutation.localeDescription.suggestedMatches && mutation.localeDescription.unsupportedLocale}
                <p class="text-muted-foreground" data-locale-matches={mutation.localeDescription.suggested}>{localeMatchesText(mutation.localeDescription.suggested, mutation.localeDescription.suggestedMatches, mutation.localeDescription.unsupportedLocale)}</p>
              {/if}
            {/if}
            <p>{ui.text("ui_locale_plural_categories", { categories: mutation.localeDescription.cardinal.join(", ") || "other" })} · {ui.text("ui_locale_ordinal_categories", { categories: mutation.localeDescription.ordinal.join(", ") || "other" })}</p>
            <p class="text-muted-foreground">{ui.text("ui_locale_categories_seeded")}</p>
          {/if}
          {#if mutation.localeDescription?.message}<p class="text-muted-foreground">{displayNotice(mutation.localeDescription.message, ui)}</p>{/if}
        </div>
      {:else if mutation.kind === "remove-locale"}
        <div class="grid gap-4 sm:grid-cols-2">
          <Field.Field><Field.Label for="remove-locale">{ui.text("ui_page_mutation_language_to_remove")}</Field.Label>
            <Select.Root type="single" value={mutation.locale} onValueChange={(value) => { mutation.locale = value; mutation.invalidate(); }}>
              <Select.Trigger id="remove-locale" class="w-full">{mutation.locale} · {localeName(mutation.locale)}</Select.Trigger>
              <Select.Content><Select.Group>{#each catalog.locales.filter((locale) => locale.tag !== catalog?.defaultLocale) as locale (locale.tag)}<Select.Item value={locale.tag} label={`${locale.tag} · ${localeName(locale.tag)}`}>{locale.tag} · {localeName(locale.tag)}</Select.Item>{/each}</Select.Group></Select.Content>
            </Select.Root>
          </Field.Field>
          <Field.Field><Field.Label for="replacement-fallback">{ui.text("ui_page_mutation_redirect_fallbacks")}</Field.Label>
            <Select.Root type="single" value={mutation.replacementFallback} onValueChange={(value) => { mutation.replacementFallback = value; mutation.invalidate(); }}>
              <Select.Trigger id="replacement-fallback" class="w-full">{mutation.replacementFallback}</Select.Trigger>
              <Select.Content><Select.Group>{#each catalog.locales.filter((locale) => locale.tag !== mutation.locale) as locale (locale.tag)}<Select.Item value={locale.tag} label={locale.tag}>{locale.tag}</Select.Item>{/each}</Select.Group></Select.Content>
            </Select.Root>
          </Field.Field>
        </div>
        <Alert.Root variant="destructive"><Alert.Title>{ui.text("ui_page_mutation_files_deleted")}</Alert.Title><Alert.Description>{ui.text("ui_page_mutation_files_deleted_description")}</Alert.Description></Alert.Root>
      {:else if mutation.kind === "set-fallback"}
        <div class="grid gap-4 sm:grid-cols-2">
          <Field.Field><Field.Label for="fallback-locale">{ui.text("ui_page_mutation_language")}</Field.Label>
            <Select.Root type="single" value={mutation.locale} onValueChange={(value) => { mutation.locale = value; mutation.invalidate(); }}>
              <Select.Trigger id="fallback-locale" class="w-full">{mutation.locale} · {localeName(mutation.locale)}</Select.Trigger>
              <Select.Content><Select.Group>{#each catalog.locales.filter((locale) => locale.tag !== catalog?.defaultLocale) as locale (locale.tag)}<Select.Item value={locale.tag} label={`${locale.tag} · ${localeName(locale.tag)}`}>{locale.tag} · {localeName(locale.tag)}</Select.Item>{/each}</Select.Group></Select.Content>
            </Select.Root>
          </Field.Field>
          <Field.Field><Field.Label for="fallback-target">{ui.text("ui_page_mutation_fallback")}</Field.Label>
            <Select.Root type="single" value={mutation.fallback} onValueChange={(value) => { mutation.fallback = value; mutation.invalidate(); }}>
              <Select.Trigger id="fallback-target" class="w-full">{mutation.fallback} · {localeName(mutation.fallback)}</Select.Trigger>
              <Select.Content><Select.Group>{#each catalog.locales.filter((locale) => locale.tag !== mutation.locale) as locale (locale.tag)}<Select.Item value={locale.tag} label={`${locale.tag} · ${localeName(locale.tag)}`}>{locale.tag} · {localeName(locale.tag)}</Select.Item>{/each}</Select.Group></Select.Content>
            </Select.Root>
          </Field.Field>
        </div>
        <div class="flex flex-wrap gap-2">{#each catalog.locales as locale (locale.tag)}<Badge variant="outline"><strong>{locale.tag}</strong>{locale.tag === mutation.locale ? ` → ${mutation.fallback}` : locale.fallback ? ` → ${locale.fallback}` : ` · ${ui.text("ui_page_project_wizard_source")}`}</Badge>{/each}</div>
      {:else if mutation.kind === "create-key"}
        <Field.Field><Field.Label for="mutation-target-key">{ui.text("ui_page_mutation_message_key")}</Field.Label><Input id="mutation-target-key" bind:value={mutation.targetKey} oninput={() => mutation.invalidate()} placeholder="checkout_actions_pay" autocomplete="off" /><Field.Description>{ui.text("ui_page_mutation_message_key_description")}</Field.Description></Field.Field>
        <Field.Field><Field.Label for="mutation-initial-value">{ui.text("ui_page_mutation_initial_text")}</Field.Label><Textarea id="mutation-initial-value" class="min-h-28" bind:value={mutation.initialValue} oninput={() => mutation.invalidate()} placeholder={ui.text("ui_page_mutation_initial_text_placeholder")} /><Field.Description>{ui.text("ui_page_mutation_initial_text_description")}</Field.Description></Field.Field>
      {:else if mutation.kind === "rename-key" || mutation.kind === "duplicate-key"}
        <Field.Field><Field.Label for="mutation-source-key">{ui.text("ui_page_mutation_existing_key")}</Field.Label><Input id="mutation-source-key" value={mutation.sourceKey} readonly /></Field.Field>
        <Field.Field><Field.Label for="mutation-new-key">{mutation.kind === "rename-key" ? ui.text("ui_page_mutation_new_key_group_path") : ui.text("ui_page_mutation_duplicate_key")}</Field.Label><Input id="mutation-new-key" bind:value={mutation.targetKey} oninput={() => mutation.invalidate()} autocomplete="off" /><Field.Description>{ui.text("ui_page_mutation_rename_description")}</Field.Description></Field.Field>
      {:else}
        <Alert.Root variant="destructive"><Alert.Title>{ui.text("ui_page_mutation_delete")} {mutation.sourceKey}?</Alert.Title><Alert.Description>{ui.text("ui_page_mutation_delete_description")}</Alert.Description></Alert.Root>
      {/if}
    </Field.FieldGroup>

    {#if mutation.error}<Alert.Root variant="destructive" class="mt-4" aria-live="polite"><Alert.Title>{ui.text("ui_page_mutation_invalid")}</Alert.Title><Alert.Description>{displayNotice(mutation.error, ui)}</Alert.Description></Alert.Root>{/if}
    {#if mutation.preview?.ok}
      <section class="mt-5 overflow-hidden rounded-xl border" aria-label={ui.text("ui_page_mutation_operation_preview")}>
        <header class="flex items-center justify-between gap-3 border-b px-4 py-3"><strong>{ui.text("ui_page_mutation_operation_preview")}</strong><Badge variant="secondary">{ui.text("ui_count_affected_files", { count: mutation.preview.files.length })}</Badge></header>
        {#each mutation.preview.files as file (file.path)}
          <div class="grid grid-cols-[auto_minmax(0,1fr)] items-center gap-3 border-b px-4 py-3 last:border-b-0 sm:grid-cols-[auto_minmax(0,1fr)_auto]"><Badge variant={file.kind === "delete" ? "destructive" : file.kind === "create" ? "default" : "secondary"}>{file.kind}</Badge><code class="truncate text-xs">{file.path}</code><small class="col-start-2 text-muted-foreground sm:col-start-auto">{file.beforeBytes.toLocaleString()} → {file.afterBytes.toLocaleString()} bytes</small></div>
        {/each}
      </section>
      {#if mutation.preview.requiresIrreversibleConfirmation}
        <Field.Field class="mt-4 rounded-lg border border-destructive/40 bg-destructive/5 p-4">
          <div class="flex items-start gap-3">
            <Checkbox id="confirm-irreversible" bind:checked={mutation.irreversibleConfirmed} />
            <div class="grid gap-1.5 leading-none">
              <Field.Label for="confirm-irreversible">{ui.text("ui_page_mutation_irreversible_confirm")}</Field.Label>
              <Field.Description>{ui.text("ui_page_mutation_irreversible_description")}</Field.Description>
            </div>
          </div>
        </Field.Field>
      {/if}
    {/if}
    {#snippet footer()}
      <Button variant="outline" disabled={mutation.busy} onclick={() => mutation.open = false}>{ui.text("ui_page_cancel")}</Button>
      {#if mutation.preview?.ok}
        <Button variant={mutation.kind === "remove-locale" || mutation.kind === "delete-key" ? "destructive" : "default"} disabled={mutation.busy || (mutation.preview.requiresIrreversibleConfirmation && !mutation.irreversibleConfirmed)} onclick={() => void mutation.apply()}>{#if mutation.busy}<Spinner data-icon="inline-start" />{/if}{mutation.busy ? ui.text("ui_page_mutation_committing") : ui.text("ui_page_mutation_commit")}</Button>
      {:else}
        <Button disabled={mutation.busy} onclick={() => void mutation.previewChange()}>{#if mutation.busy}<Spinner data-icon="inline-start" />{/if}{mutation.busy ? ui.text("ui_page_mutation_checking") : ui.text("ui_page_mutation_preview")}</Button>
      {/if}
    {/snippet}
  </AppDialog>
{/if}
