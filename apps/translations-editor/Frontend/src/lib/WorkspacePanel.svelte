<script lang="ts">
  import AlertCircleIcon from "@lucide/svelte/icons/alert-circle";
  import WrenchIcon from "@lucide/svelte/icons/wrench";
  import * as Alert from "#lib/components/ui/alert/index.js";
  import * as Sidebar from "#lib/components/ui/sidebar/index.js";
  import LibraryIcon from "@lucide/svelte/icons/library";
  import type { EditorCatalogSummary, EditorDocument, EditorLibrarySummary } from "#lib/contracts.js";
  import { getUiText } from "#lib/ui-text.js";

  let {
    malformedDocuments,
    reviewError,
    onrepair,
    catalogId,
    catalogs = [],
    libraries = [],
    onswitchcatalog,
    onopenlibrary,
  }: {
    malformedDocuments: EditorDocument[];
    reviewError?: string;
    onrepair: (document: EditorDocument) => void;
    /** The open catalog; the workspace's other catalogs and the open project's library catalogs are listed (T5d). */
    catalogId?: string;
    catalogs?: EditorCatalogSummary[];
    libraries?: EditorLibrarySummary[];
    onswitchcatalog?: (catalog: EditorCatalogSummary) => void;
    onopenlibrary?: (catalog: string) => void;
  } = $props();

  const others = $derived(catalogs.filter((catalog) => catalog.id !== catalogId));

  const ui = getUiText();
</script>

{#if malformedDocuments.length > 0 || reviewError}
  <Sidebar.Group aria-label={ui.text("ui_workspace_issues")} class="py-1">
    <Sidebar.GroupLabel>{ui.text("ui_workspace_issues")}</Sidebar.GroupLabel>
    <Sidebar.GroupContent class="grid gap-2 px-2">
      {#if malformedDocuments.length > 0}
        <Alert.Root variant="destructive" class="gap-y-1 px-2.5 py-2">
          <WrenchIcon />
          <Alert.Title class="text-xs">
            {ui.text("ui_count_malformed_files", { count: malformedDocuments.length })}
          </Alert.Title>
          <Alert.Description class="grid min-w-0 gap-1">
            {#each malformedDocuments as document (document.path)}
              <button type="button" class="truncate text-left text-xs underline-offset-4 hover:underline" onclick={() => onrepair(document)}>
                {ui.text("ui_workspace_repair")} {document.path}
              </button>
            {/each}
          </Alert.Description>
        </Alert.Root>
      {/if}

      {#if reviewError}
        <Alert.Root variant="destructive" class="px-2.5 py-2">
          <AlertCircleIcon />
          <Alert.Title class="text-xs">{ui.text("ui_workspace_review_notes_unavailable")}</Alert.Title>
          <Alert.Description class="text-xs">{reviewError}</Alert.Description>
        </Alert.Root>
      {/if}
    </Sidebar.GroupContent>
  </Sidebar.Group>
{/if}

{#if others.length > 0 || libraries.length > 0}
  <Sidebar.Group aria-label={ui.text("ui_workspace_catalogs")} class="py-1" data-testid="workspace-catalogs">
    <Sidebar.GroupLabel>{ui.text("ui_workspace_catalogs")}</Sidebar.GroupLabel>
    <Sidebar.GroupContent class="grid gap-1 px-2 text-xs">
      {#each others as catalog (catalog.manifestPaths.join("\n"))}
        <button type="button" class="truncate text-left underline-offset-4 hover:underline" data-catalog={catalog.id} onclick={() => onswitchcatalog?.(catalog)}>
          {catalog.id} <span class="text-muted-foreground">{catalog.manifestPaths[0]}</span>
        </button>
      {/each}
      {#each libraries as library (library.catalog)}
        <button type="button" class="flex items-center gap-1 truncate text-left underline-offset-4 hover:underline" data-library={library.catalog} onclick={() => onopenlibrary?.(library.catalog)}>
          <LibraryIcon class="size-3" />{library.catalog}
          <span class="text-muted-foreground">{library.package ?? ""}{library.overridden ? ` · ${ui.text("ui_library_overridden")}` : ""}</span>
        </button>
      {/each}
    </Sidebar.GroupContent>
  </Sidebar.Group>
{/if}
