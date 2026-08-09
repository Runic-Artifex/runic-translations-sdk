<script lang="ts">
  import AlertCircleIcon from "@lucide/svelte/icons/alert-circle";
  import WrenchIcon from "@lucide/svelte/icons/wrench";
  import * as Alert from "$lib/components/ui/alert/index.js";
  import * as Sidebar from "$lib/components/ui/sidebar/index.js";
  import type { EditorDocument } from "$lib/contracts";

  let {
    malformedDocuments,
    reviewError,
    onrepair,
  }: {
    malformedDocuments: EditorDocument[];
    reviewError?: string;
    onrepair: (document: EditorDocument) => void;
  } = $props();
</script>

{#if malformedDocuments.length > 0 || reviewError}
  <Sidebar.Group aria-label="Workspace issues" class="py-1">
    <Sidebar.GroupLabel>Workspace issues</Sidebar.GroupLabel>
    <Sidebar.GroupContent class="grid gap-2 px-2">
      {#if malformedDocuments.length > 0}
        <Alert.Root variant="destructive" class="gap-y-1 px-2.5 py-2">
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
        <Alert.Root variant="destructive" class="px-2.5 py-2">
          <AlertCircleIcon />
          <Alert.Title class="text-xs">Review notes unavailable</Alert.Title>
          <Alert.Description class="text-xs">{reviewError}</Alert.Description>
        </Alert.Root>
      {/if}
    </Sidebar.GroupContent>
  </Sidebar.Group>
{/if}
