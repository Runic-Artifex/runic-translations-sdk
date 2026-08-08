<script lang="ts">
  import AlertCircleIcon from "@lucide/svelte/icons/alert-circle";
  import FolderOpenIcon from "@lucide/svelte/icons/folder-open";
  import InfoIcon from "@lucide/svelte/icons/info";
  import LanguagesIcon from "@lucide/svelte/icons/languages";
  import PlusIcon from "@lucide/svelte/icons/plus";
  import RefreshCwIcon from "@lucide/svelte/icons/refresh-cw";
  import WrenchIcon from "@lucide/svelte/icons/wrench";
  import * as Alert from "$lib/components/ui/alert/index.js";
  import { Badge } from "$lib/components/ui/badge/index.js";
  import { Button } from "$lib/components/ui/button/index.js";
  import * as Card from "$lib/components/ui/card/index.js";
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
</script>

<Card.Root
  size="sm"
  class="mx-4 mt-1 mb-4 gap-3 border border-border/80 bg-card/80 py-3 shadow-none"
  aria-label={workspaceLabel}
>
  <Card.Header class="grid-cols-[minmax(0,1fr)_auto] gap-x-2 px-3">
    <Card.Title class="flex min-w-0 items-center gap-2 text-sm">
      <Badge
        variant={success ? "secondary" : "destructive"}
        class="size-2 shrink-0 p-0"
        aria-label={success ? "Workspace ready" : "Workspace has issues"}
      ></Badge>
      <span class="truncate">{catalogId}</span>
    </Card.Title>
    <Card.Description class="truncate text-xs">
      {localeCount} {localeCount === 1 ? "locale" : "locales"} · schema v{schemaVersion}
    </Card.Description>
    <Card.Action>
      <Button variant="ghost" size="icon-xs" title={reloadLabel} aria-label={reloadLabel} onclick={onreload}>
        <RefreshCwIcon />
      </Button>
    </Card.Action>
  </Card.Header>

  <Card.Content class="grid gap-2 px-3">
    <p class="truncate font-mono text-xs text-muted-foreground" title={root}>{root}</p>

    {#if malformedDocuments.length > 0}
      <Alert.Root variant="destructive" class="gap-y-1 px-2.5 py-2">
        <WrenchIcon />
        <Alert.Title class="text-xs">
          {malformedDocuments.length} malformed {malformedDocuments.length === 1 ? "file" : "files"}
        </Alert.Title>
        <Alert.Description class="grid min-w-0 gap-1">
          {#each malformedDocuments as document (document.path)}
            <Button
              variant="ghost"
              size="xs"
              class="h-auto min-w-0 justify-start px-0 text-destructive"
              title={document.path}
              onclick={() => onrepair(document)}
            >
              <span class="truncate">Repair {document.path}</span>
            </Button>
          {/each}
        </Alert.Description>
      </Alert.Root>
    {/if}

    {#if reviewError}
      <Alert.Root variant="destructive" class="px-2.5 py-2">
        <AlertCircleIcon />
        <Alert.Title class="text-xs">Review sidecar disabled</Alert.Title>
        <Alert.Description class="text-xs">{reviewError}</Alert.Description>
      </Alert.Root>
    {/if}
  </Card.Content>

  <Card.Footer class="grid grid-cols-2 gap-1.5 px-3">
    <Button variant="outline" size="xs" class="col-span-2 justify-start" onclick={onmanagelanguages}>
      <LanguagesIcon data-icon="inline-start" />
      Manage languages
    </Button>
    <Button variant="outline" size="xs" class="justify-start" onclick={onopenworkspace}>
      <FolderOpenIcon data-icon="inline-start" />
      Open workspace
    </Button>
    <Button variant="outline" size="xs" class="justify-start" onclick={onnewproject}>
      <PlusIcon data-icon="inline-start" />
      New project
    </Button>
    <Button variant="ghost" size="xs" class="col-span-2 justify-start text-muted-foreground" onclick={onabout}>
      <InfoIcon data-icon="inline-start" />
      About &amp; diagnostics
    </Button>
  </Card.Footer>
</Card.Root>
