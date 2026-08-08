<script lang="ts">
  import AlertCircleIcon from "@lucide/svelte/icons/circle-alert";
  import CheckCircle2Icon from "@lucide/svelte/icons/circle-check-big";
  import TriangleAlertIcon from "@lucide/svelte/icons/triangle-alert";
  import * as Alert from "$lib/components/ui/alert/index.js";
  import { Badge } from "$lib/components/ui/badge/index.js";
  import { Button } from "$lib/components/ui/button/index.js";
  import { Spinner } from "$lib/components/ui/spinner/index.js";
  import type { EditorDiagnostic } from "$lib/contracts";

  interface Props {
    busy: boolean;
    diagnostics: EditorDiagnostic[];
    clientError: string | undefined;
    errorCount: number;
    warningCount: number;
    validLabel: string;
    invalidLabel: string;
    diagnosticsLabel: string;
    schemaVersion: number;
    onselect: (diagnostic: EditorDiagnostic) => void;
  }

  let {
    busy,
    diagnostics,
    clientError,
    errorCount,
    warningCount,
    validLabel,
    invalidLabel,
    diagnosticsLabel,
    schemaVersion,
    onselect,
  }: Props = $props();

  let invalid = $derived(errorCount > 0 || clientError !== undefined);
</script>

<Alert.Root
  variant={invalid ? "destructive" : "default"}
  class="mx-auto mt-7 max-w-[1000px] gap-0 overflow-hidden p-0"
  aria-live="polite"
>
  <header class="flex flex-wrap items-center justify-between gap-3 px-4 py-3">
    <div class="flex min-w-0 items-center gap-3">
      {#if busy}
        <Spinner class="size-5 shrink-0 text-primary" aria-label="Validating" />
      {:else if invalid}
        <AlertCircleIcon class="size-5 shrink-0" aria-hidden="true" />
      {:else}
        <CheckCircle2Icon class="size-5 shrink-0 text-primary" aria-hidden="true" />
      {/if}
      <div class="min-w-0">
        <Alert.Title class="text-xs font-semibold">
          {busy ? "Validating with the Runic compiler…" : invalid ? invalidLabel : validLabel}
        </Alert.Title>
        <Alert.Description class="text-xs">
          {diagnosticsLabel} · {errorCount} errors · {warningCount} warnings
        </Alert.Description>
      </div>
    </div>
    <Badge variant="outline" class="font-mono text-[0.65rem]">compiler · schema v{schemaVersion}</Badge>
  </header>

  {#if clientError}
    <div class="border-t border-destructive/30 bg-destructive/10 px-4 py-3 text-xs text-destructive">
      {clientError}
    </div>
  {/if}

  {#if diagnostics.length > 0}
    <div class="divide-y border-t">
      {#each diagnostics as diagnostic (`${diagnostic.path}-${diagnostic.id}-${diagnostic.line}-${diagnostic.column}`)}
        <Button
          variant="ghost"
          class="grid h-auto w-full grid-cols-[auto_minmax(0,1fr)] items-start justify-start gap-3 px-4 py-3 text-left whitespace-normal md:grid-cols-[auto_minmax(0,1fr)_auto]"
          onclick={() => onselect(diagnostic)}
        >
          {#if diagnostic.severity === "error"}
            <AlertCircleIcon class="mt-0.5 size-4 text-destructive" aria-hidden="true" />
          {:else}
            <TriangleAlertIcon class="mt-0.5 size-4 text-primary" aria-hidden="true" />
          {/if}
          <span class="min-w-0 text-xs leading-5 text-muted-foreground">
            <strong class="mr-2 font-mono text-foreground">{diagnostic.id}</strong>{diagnostic.message}
          </span>
          <code class="hidden whitespace-nowrap text-[0.65rem] text-muted-foreground md:block">
            {diagnostic.path}:{diagnostic.line}:{diagnostic.column}
          </code>
        </Button>
      {/each}
    </div>
  {/if}
</Alert.Root>
