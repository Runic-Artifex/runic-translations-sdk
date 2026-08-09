<script lang="ts">
  import AlertCircleIcon from "@lucide/svelte/icons/alert-circle";
  import ChevronDownIcon from "@lucide/svelte/icons/chevron-down";
  import ChevronsUpDownIcon from "@lucide/svelte/icons/chevrons-up-down";
  import FolderOpenIcon from "@lucide/svelte/icons/folder-open";
  import InfoIcon from "@lucide/svelte/icons/info";
  import LanguagesIcon from "@lucide/svelte/icons/languages";
  import PlusIcon from "@lucide/svelte/icons/plus";
  import RefreshCwIcon from "@lucide/svelte/icons/refresh-cw";
  import WrenchIcon from "@lucide/svelte/icons/wrench";
  import * as Alert from "$lib/components/ui/alert/index.js";
  import { Badge } from "$lib/components/ui/badge/index.js";
  import * as Collapsible from "$lib/components/ui/collapsible/index.js";
  import * as DropdownMenu from "$lib/components/ui/dropdown-menu/index.js";
  import * as Sidebar from "$lib/components/ui/sidebar/index.js";
  import type { EditorDocument } from "$lib/contracts";

  let {
    workspaceLabel,
    catalogId,
    localeCount,
    schemaVersion,
    root,
    success,
    reloadLabel,
    malformedDocuments,
    reviewError,
    onreload,
    onrepair,
    onmanagelanguages,
    onopenworkspace,
    onnewproject,
    onabout,
  }: {
    workspaceLabel: string;
    catalogId: string;
    localeCount: number;
    schemaVersion: number;
    root: string;
    success: boolean;
    reloadLabel: string;
    malformedDocuments: EditorDocument[];
    reviewError?: string;
    onreload: () => void;
    onrepair: (document: EditorDocument) => void;
    onmanagelanguages: () => void;
    onopenworkspace: () => void;
    onnewproject: () => void;
    onabout: () => void;
  } = $props();

  let open = $state(false);
</script>

<Collapsible.Root bind:open class="group/workspace">
  <Sidebar.Group class="py-1" aria-label={workspaceLabel}>
    <Sidebar.GroupLabel>
      {#snippet child({ props })}
        <Collapsible.Trigger {...props}>
          <span>{workspaceLabel}</span>
          <span class="ml-auto max-w-40 truncate font-mono">{catalogId}</span>
          <ChevronDownIcon class="transition-transform group-data-[state=open]/workspace:rotate-180" />
        </Collapsible.Trigger>
      {/snippet}
    </Sidebar.GroupLabel>
    <Collapsible.Content>
      <Sidebar.GroupContent>
    <Sidebar.Menu>
      <Sidebar.MenuItem>
        <DropdownMenu.Root>
          <DropdownMenu.Trigger>
            {#snippet child({ props })}
              <Sidebar.MenuButton {...props} size="lg" class="h-auto py-2">
                <Badge
                  variant={success ? "secondary" : "destructive"}
                  class="size-2 shrink-0 p-0"
                  aria-label={success ? "Workspace ready" : "Workspace has issues"}
                ></Badge>
                <span class="grid min-w-0 flex-1 text-left text-sm leading-tight">
                  <span class="truncate font-medium">{catalogId}</span>
                  <span class="truncate text-xs text-muted-foreground">
                    {localeCount} {localeCount === 1 ? "locale" : "locales"} · schema v{schemaVersion}
                  </span>
                </span>
                <ChevronsUpDownIcon class="ml-auto" />
              </Sidebar.MenuButton>
            {/snippet}
          </DropdownMenu.Trigger>
          <DropdownMenu.Content class="w-(--bits-dropdown-menu-anchor-width)" align="start">
            <DropdownMenu.Group>
              <DropdownMenu.Label class="truncate font-mono text-xs" title={root}>{root}</DropdownMenu.Label>
              <DropdownMenu.Item onclick={onreload}>
                <RefreshCwIcon />
                {reloadLabel}
              </DropdownMenu.Item>
              <DropdownMenu.Item onclick={onmanagelanguages}>
                <LanguagesIcon />
                Manage languages
              </DropdownMenu.Item>
              <DropdownMenu.Item onclick={onopenworkspace}>
                <FolderOpenIcon />
                Open workspace
              </DropdownMenu.Item>
              <DropdownMenu.Item onclick={onnewproject}>
                <PlusIcon />
                New project
              </DropdownMenu.Item>
            </DropdownMenu.Group>
            <DropdownMenu.Separator />
            <DropdownMenu.Group>
              <DropdownMenu.Item onclick={onabout}>
                <InfoIcon />
                About &amp; diagnostics
              </DropdownMenu.Item>
            </DropdownMenu.Group>
          </DropdownMenu.Content>
        </DropdownMenu.Root>
      </Sidebar.MenuItem>
    </Sidebar.Menu>

    <p class="truncate px-2 pt-1 font-mono text-xs text-muted-foreground" title={root}>{root}</p>

    {#if malformedDocuments.length > 0}
      <Alert.Root variant="destructive" class="mt-2 gap-y-1 px-2.5 py-2">
        <WrenchIcon />
        <Alert.Title class="text-xs">
          {malformedDocuments.length} malformed {malformedDocuments.length === 1 ? "file" : "files"}
        </Alert.Title>
        <Alert.Description class="grid min-w-0 gap-1">
          {#each malformedDocuments as document (document.path)}
            <button type="button" class="truncate text-left text-xs underline-offset-4 hover:underline" onclick={() => onrepair(document)}>
              Repair {document.path}
            </button>
          {/each}
        </Alert.Description>
      </Alert.Root>
    {/if}

    {#if reviewError}
      <Alert.Root variant="destructive" class="mt-2 px-2.5 py-2">
        <AlertCircleIcon />
        <Alert.Title class="text-xs">Review sidecar disabled</Alert.Title>
        <Alert.Description class="text-xs">{reviewError}</Alert.Description>
      </Alert.Root>
    {/if}
      </Sidebar.GroupContent>
    </Collapsible.Content>
  </Sidebar.Group>
</Collapsible.Root>
