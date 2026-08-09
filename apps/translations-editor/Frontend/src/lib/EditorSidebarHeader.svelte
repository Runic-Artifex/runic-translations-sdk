<script lang="ts" module>
  export interface RecentProjectItem {
    root: string;
    catalogId: string;
    openedAt: string;
  }
</script>

<script lang="ts">
  import AlertCircleIcon from "@lucide/svelte/icons/alert-circle";
  import ChevronsUpDownIcon from "@lucide/svelte/icons/chevrons-up-down";
  import FolderOpenIcon from "@lucide/svelte/icons/folder-open";
  import LanguagesIcon from "@lucide/svelte/icons/languages";
  import PlusIcon from "@lucide/svelte/icons/plus";
  import RefreshCwIcon from "@lucide/svelte/icons/refresh-cw";
  import { Badge } from "$lib/components/ui/badge/index.js";
  import * as DropdownMenu from "$lib/components/ui/dropdown-menu/index.js";
  import * as Sidebar from "$lib/components/ui/sidebar/index.js";

  let {
    catalogId,
    localeCount,
    schemaVersion,
    root,
    success,
    reloadLabel,
    recentProjects,
    onreload,
    onopenworkspace,
    onnewproject,
    onopenrecent,
  }: {
    catalogId: string;
    localeCount: number;
    schemaVersion: number;
    root: string;
    success: boolean;
    reloadLabel: string;
    recentProjects: RecentProjectItem[];
    onreload: () => void;
    onopenworkspace: () => void;
    onnewproject: () => void;
    onopenrecent: (project: RecentProjectItem) => void;
  } = $props();
</script>

<Sidebar.Header class="border-b border-sidebar-border p-2 pr-12 md:pr-2">
  <Sidebar.Menu>
    <Sidebar.MenuItem>
      <DropdownMenu.Root>
        <DropdownMenu.Trigger>
          {#snippet child({ props })}
            <Sidebar.MenuButton
              {...props}
              size="lg"
              class="h-auto min-h-16 py-2"
              aria-label={`Project ${catalogId}`}
              tooltipContent={catalogId}
            >
              <Badge
                variant={success ? "default" : "destructive"}
                class="size-10 shrink-0 justify-center rounded-xl p-0"
              >
                {#if success}
                  <LanguagesIcon aria-hidden="true" />
                {:else}
                  <AlertCircleIcon aria-hidden="true" />
                {/if}
              </Badge>
              <span class="grid min-w-0 flex-1 text-left leading-tight">
                <span class="truncate font-semibold">{catalogId}</span>
                <span class="truncate text-xs text-muted-foreground">
                  {localeCount} {localeCount === 1 ? "locale" : "locales"} · schema v{schemaVersion}
                </span>
              </span>
              <ChevronsUpDownIcon class="ml-auto" aria-hidden="true" />
            </Sidebar.MenuButton>
          {/snippet}
        </DropdownMenu.Trigger>
        <DropdownMenu.Content class="w-(--bits-dropdown-menu-anchor-width) min-w-72" align="start">
          <DropdownMenu.Label class="grid gap-1">
            <span>Current project</span>
            <span class="truncate font-mono text-xs font-normal text-muted-foreground" title={root}>{root}</span>
          </DropdownMenu.Label>

          {#if recentProjects.length > 0}
            <DropdownMenu.Separator />
            <DropdownMenu.Group>
              <DropdownMenu.Label>Recent projects</DropdownMenu.Label>
              {#each recentProjects.slice(0, 5) as project (`${project.root}\n${project.catalogId}`)}
                <DropdownMenu.Item onclick={() => onopenrecent(project)}>
                  <LanguagesIcon />
                  <span class="grid min-w-0">
                    <span class="truncate">{project.catalogId}</span>
                    <span class="truncate text-xs text-muted-foreground">{project.root}</span>
                  </span>
                </DropdownMenu.Item>
              {/each}
            </DropdownMenu.Group>
          {/if}

          <DropdownMenu.Separator />
          <DropdownMenu.Group>
            <DropdownMenu.Item onclick={onreload}>
              <RefreshCwIcon />
              {reloadLabel}
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
        </DropdownMenu.Content>
      </DropdownMenu.Root>
    </Sidebar.MenuItem>
  </Sidebar.Menu>
</Sidebar.Header>
