<script lang="ts">
  import type { EditorLibraryMessage, EditorLibrarySummary, EditorLibraryView } from "#lib/contracts.js";
  import AppDialog from "#lib/AppDialog.svelte";
  import * as Alert from "#lib/components/ui/alert/index.js";
  import { Badge } from "#lib/components/ui/badge/index.js";
  import { Button } from "#lib/components/ui/button/index.js";
  import { Input } from "#lib/components/ui/input/index.js";
  import { Textarea } from "#lib/components/ui/textarea/index.js";
  import { Spinner } from "#lib/components/ui/spinner/index.js";
  import { getUiText, displayNotice, type UiNotice } from "#lib/ui-text.js";

  // A library catalog of an installed package (T5d): layer 0 is the library's text, read-only; layer 1 the application's
  // overrides in translations/overrides/<catalog>/<locale>.rmf2. Everything the dialog shows and writes goes through the
  // editor session and Tooling, as the build, the language server and runic-translations overrides see it.
  let {
    open = $bindable(false),
    busy = false,
    project = "",
    libraries,
    library,
    notice,
    conflict = false,
    locale = $bindable(""),
    onselect,
    onoverride,
    onsave,
    onrevert,
    onkeep,
    onaddlocale,
    onreload,
  }: {
    open?: boolean;
    busy?: boolean;
    /** The open project's runic.json: drafts are kept per project, so two projects that override one library do not share them. */
    project?: string;
    libraries: EditorLibrarySummary[];
    library?: EditorLibraryView;
    notice?: UiNotice;
    /** The last write was refused because a file changed since the dialog loaded it; the drafts are kept. */
    conflict?: boolean;
    locale?: string;
    onselect(catalog: string): void;
    /** Creates the override with the text the user saved, from the locale's override source at `revision` ("" when it has none). Resolves whether it was written. */
    onoverride(key: string, locale: string, text: string, revision: string): Promise<boolean>;
    /** Resolves whether the text was saved; the draft stays until then and after a failure. */
    onsave(key: string, locale: string, text: string, revision: string): Promise<boolean>;
    onrevert(key: string, locale: string, revision: string): void;
    onkeep(key: string, locale: string, revision: string): void;
    onaddlocale(locale: string): void;
    onreload(): void;
  } = $props();

  const ui = getUiText();
  let newLocale = $state("");
  let edits = $state<Record<string, string>>({});

  const current = $derived(library?.locales.find((item) => item.tag === locale) ?? library?.locales[0]);
  const editKey = (message: EditorLibraryMessage) => `${project}\n${library?.catalog}\n${current?.tag}\n${message.key}`;

  // Override opens the text pre-filled with the library's text; the override is written on the first Save, so a click
  // alone does not pin the library's current translation.
  function beginOverride(message: EditorLibraryMessage): void {
    edits[editKey(message)] = libraryText(message).text;
  }

  async function save(message: EditorLibraryMessage, create: boolean): Promise<void> {
    const key = editKey(message);
    const text = edits[key];
    if (text === undefined || current === undefined) return;
    const tag = current.tag;
    const override = message.overrides.find((item) => item.locale === tag);
    const ok = create || !override
      ? await onoverride(message.key, tag, text, current.revision ?? "")
      : await onsave(message.key, tag, text, override.revision);
    if (ok && edits[key] === text) delete edits[key];
  }

  // The library text the locale shows: its own, else the first on its fallback chain the library translates.
  function libraryText(message: EditorLibraryMessage): { text: string; from: string } {
    let tag: string | undefined = current?.tag;
    for (let hops = 0; tag !== undefined && hops <= (library?.locales.length ?? 0); hops++) {
      if (message.texts[tag] !== undefined) return { text: message.texts[tag], from: tag };
      tag = library?.locales.find((item) => item.tag === tag)?.fallback;
    }
    return { text: message.baseText, from: library?.baseLocale ?? "" };
  }

  function addLocale(): void {
    const value = newLocale.trim();
    if (value === "") return;
    onaddlocale(value);
    newLocale = "";
  }
</script>

<AppDialog
  {open}
  title={library ? `${library.catalog} · ${library.package ?? ""} ${library.version ?? ""}`.trim() : ui.text("ui_library_title")}
  description={ui.text("ui_library_description")}
  class="sm:max-w-5xl"
  onopenchange={(value) => open = value}
>
  <div class="grid gap-4" data-testid="library-dialog">
    {#if libraries.length === 0}
      <Alert.Root><Alert.Description>{ui.text("ui_library_empty")}</Alert.Description></Alert.Root>
    {:else}
      <div class="flex flex-wrap items-center gap-2">
        <span class="text-sm text-muted-foreground">{ui.text("ui_library_choose")}</span>
        {#each libraries as item (item.catalog)}
          <Button size="sm" variant={library?.catalog === item.catalog ? "default" : "outline"} disabled={busy} onclick={() => onselect(item.catalog)}
            data-library={item.catalog}>
            {item.catalog}{#if item.overridden}<Badge variant="secondary" class="ml-1">{ui.text("ui_library_overridden")}</Badge>{/if}
          </Button>
        {/each}
        {#if busy}<Spinner />{/if}
      </div>
    {/if}

    {#if notice}
      <Alert.Root variant="destructive" data-testid="library-notice">
        <Alert.Description>
          {displayNotice(notice, ui)}
          {#if conflict}<Button size="sm" variant="outline" class="mt-2" disabled={busy} data-action="reload-library" onclick={onreload}>{ui.text("ui_library_reload")}</Button>{/if}
        </Alert.Description>
      </Alert.Root>
    {/if}

    {#if library}
      {#if !library.listed}<Alert.Root><Alert.Description>{ui.text("ui_library_not_listed")}</Alert.Description></Alert.Root>{/if}
      {#if library.libraryError}<Alert.Root variant="destructive"><Alert.Description>{library.libraryError}</Alert.Description></Alert.Root>{/if}

      <div class="flex flex-wrap gap-2 text-xs" aria-label={ui.text("ui_library_layers")}>
        {#each library.layers as layer (layer.name)}
          <Badge variant={layer.name === "library" ? "outline" : "secondary"} data-layer={layer.name}>
            {layer.priority} · {layer.name === "library" ? ui.text("ui_library_layer_library") : ui.text("ui_library_layer_override")}
          </Badge>
        {/each}
      </div>

      <section class="grid gap-2" aria-label={ui.text("ui_library_locales")}>
        <div class="flex flex-wrap gap-2">
          {#each library.locales as item (item.tag)}
            <button type="button" data-locale={item.tag}
              class={["rounded-md border px-3 py-2 text-left text-xs", item.tag === current?.tag ? "border-primary bg-accent" : "border-border"]}
              onclick={() => locale = item.tag}>
              <strong class="block text-sm">{item.tag}</strong>
              <span class="block">{item.origin === "library" ? ui.text("ui_library_locale_library") : ui.text("ui_library_locale_added", { fallback: item.fallback ?? library.baseLocale })}</span>
              <span class="block text-muted-foreground" data-coverage={item.tag}>{ui.text("ui_library_coverage", { covered: item.covered, messages: item.messages })}{item.pending ? ` · ${ui.text("ui_library_locale_pending")}` : ""}</span>
            </button>
          {/each}
        </div>
        <form class="flex max-w-sm gap-2" onsubmit={(event) => { event.preventDefault(); addLocale(); }}>
          <Input aria-label={ui.text("ui_library_add_locale")} placeholder="fr-CA" bind:value={newLocale} data-testid="library-new-locale" />
          <Button type="submit" variant="outline" size="sm" disabled={busy || newLocale.trim() === ""}>{ui.text("ui_library_add_locale")}</Button>
        </form>
      </section>

      {#if current}
        <table class="w-full table-fixed border-collapse text-sm" aria-label={ui.text("ui_library_messages")}>
          <thead>
            <tr class="border-b text-left text-xs text-muted-foreground">
              <th class="w-1/5 py-2">{ui.text("ui_library_column_message")}</th>
              <th class="w-2/5 py-2">{ui.text("ui_library_column_library")}</th>
              <th class="w-2/5 py-2">{ui.text("ui_library_column_override")}</th>
            </tr>
          </thead>
          <tbody>
            {#each library.messages as message (message.key)}
              {@const shown = libraryText(message)}
              {@const override = message.overrides.find((item) => item.locale === current.tag)}
              <tr class="border-b align-top" data-message={message.key}>
                <td class="py-2 pr-2"><code class="break-all text-xs">{message.path}</code>
                  {#if message.inputs.length > 0}<small class="block text-muted-foreground">{message.inputs.join(", ")}</small>{/if}</td>
                <td class="py-2 pr-2">
                  <pre class="whitespace-pre-wrap font-sans text-sm" data-layer="library">{shown.text}</pre>
                  {#if shown.from !== current.tag}<small class="text-muted-foreground">{ui.text("ui_library_falls_back", { locale: shown.from })}</small>{/if}
                  {#if current.tag !== library.baseLocale}<small class="block text-muted-foreground">{ui.text("ui_library_base_text", { locale: library.baseLocale })}: {message.baseText}</small>{/if}
                </td>
                <td class="py-2">
                  {#if override || edits[editKey(message)] !== undefined}
                    <div class="mb-1 flex flex-wrap gap-1">
                      {#if override}
                        <Badge variant="secondary" data-badge="overridden">{ui.text("ui_library_overridden")}</Badge>
                        {#if override.changed}<Badge variant="destructive" data-badge="changed">{ui.text("ui_library_changed")}</Badge>{/if}
                      {:else}
                        <Badge variant="outline" data-badge="new-override">{ui.text("ui_library_new_override")}</Badge>
                      {/if}
                    </div>
                    <Textarea class="min-h-16 font-mono text-xs" data-layer="override" aria-label={`${message.path} ${current.tag}`}
                      value={edits[editKey(message)] ?? override?.text ?? ""}
                      oninput={(event) => edits[editKey(message)] = event.currentTarget.value} />
                    <div class="mt-1 flex flex-wrap gap-1">
                      {#if override}
                        <Button size="sm" disabled={busy || (edits[editKey(message)] ?? override.text) === override.text}
                          onclick={() => void save(message, false)}>{ui.text("ui_library_save")}</Button>
                        <Button size="sm" variant="outline" disabled={busy} onclick={() => { delete edits[editKey(message)]; onrevert(message.key, current.tag, override.revision); }}>{ui.text("ui_library_revert")}</Button>
                        {#if override.changed}<Button size="sm" variant="outline" disabled={busy} onclick={() => onkeep(message.key, current.tag, override.revision)}>{ui.text("ui_library_keep")}</Button>{/if}
                      {:else}
                        <Button size="sm" disabled={busy} data-action="create-override" onclick={() => void save(message, true)}>{ui.text("ui_library_save")}</Button>
                        <Button size="sm" variant="outline" disabled={busy} onclick={() => delete edits[editKey(message)]}>{ui.text("ui_library_cancel")}</Button>
                      {/if}
                    </div>
                  {:else}
                    <Button size="sm" variant="outline" disabled={busy} onclick={() => beginOverride(message)}>{ui.text("ui_library_override")}</Button>
                  {/if}
                </td>
              </tr>
            {/each}
          </tbody>
        </table>
      {/if}

      {#if library.diagnostics.length > 0}
        <section class="grid gap-1" aria-label={ui.text("ui_library_diagnostics")}>
          {#each library.diagnostics as diagnostic, index (`${diagnostic.path}:${diagnostic.line}:${index}`)}
            <p class={diagnostic.severity === "error" ? "text-destructive text-xs" : "text-xs"} data-diagnostic={diagnostic.id}>
              <code>{diagnostic.id}</code> {diagnostic.path}:{diagnostic.line} {diagnostic.message}
            </p>
          {/each}
        </section>
      {/if}
    {/if}
  </div>
  {#snippet footer()}<Button variant="outline" onclick={() => open = false}>{ui.text("ui_page_close")}</Button>{/snippet}
</AppDialog>
