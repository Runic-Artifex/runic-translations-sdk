<script lang="ts">
  import type { EditorPendingTransaction } from "#lib/contracts.js";
  import { displayNotice, getUiText, type UiMessage } from "#lib/ui-text.js";

  let {
    transaction,
    busy,
    clientError,
    onrecover,
  }: {
    /** The workspace transaction an earlier session's journal left unfinished. */
    transaction: EditorPendingTransaction;
    busy: boolean;
    clientError: UiMessage | undefined;
    onrecover(mode: "complete" | "rollback"): void;
  } = $props();
  const ui = getUiText();
</script>

<main class="recovery-shell">
  <div class="mark" aria-hidden="true"><span></span></div>
  <p class="eyebrow">{ui.text("ui_page_recovery_eyebrow")}</p>
  <h1>{ui.text("ui_page_recovery_title")}</h1>
  <p>{ui.text("ui_count_recovery_journal", { catalog: transaction.catalogId, count: transaction.paths.length })}</p>
  <div class="recovery-paths">
    {#each transaction.paths as path (path)}<code>{path}</code>{/each}
  </div>
  {#if clientError}<p class="project-error" aria-live="polite">{displayNotice(clientError, ui)}</p>{/if}
  <div class="recovery-actions">
    <button class="secondary" disabled={busy} onclick={() => onrecover("rollback")}>{ui.text("ui_page_recovery_restore_before")}</button>
    <button class="primary" disabled={busy} onclick={() => onrecover("complete")}>{busy ? ui.text("ui_page_recovery_recovering") : ui.text("ui_page_recovery_complete")}</button>
  </div>
  <small>{ui.text("ui_page_recovery_journal_note")}</small>
</main>

<style>
  .eyebrow { margin: 0; color: var(--muted-foreground); font-size: var(--ui-text-xs); font-weight: 560; }
  .mark { width: 4rem; height: 4rem; border-radius: 22%; background: url("/brand/icon.png") center / cover no-repeat; }
  .mark span { display: none; }
  .primary { border: 1px solid var(--primary); border-radius: var(--radius-control); padding: .5rem .9rem; color: var(--primary-foreground); background: var(--primary); font-weight: 560; cursor: pointer; }
  .primary:hover:not(:disabled) { background: color-mix(in srgb, var(--primary) 85%, var(--background)); }
  .primary:disabled { cursor: not-allowed; opacity: .5; }
  .recovery-shell { display: grid; place-content: center; place-items: center; height: 100vh; padding: 2rem; color: var(--muted-foreground); text-align: center; background: var(--background); }
  .recovery-shell { gap: .9rem; }
  .recovery-shell h1 { max-width: 42rem; margin: .4rem 0; color: var(--foreground); font-size: 2.25rem; font-weight: 620; line-height: 1.1; letter-spacing: -.02em; }
  .recovery-shell > p:not(.eyebrow) { max-width: 44rem; margin: 0; font-size: 1rem; line-height: 1.55; }
  .recovery-paths { display: grid; gap: .3rem; width: min(36rem, 100%); max-height: 12rem; margin: .5rem 0; overflow-y: auto; }
  .recovery-paths code { border: 1px solid var(--border); border-radius: var(--radius-control); padding: .4rem .6rem; color: var(--foreground); text-align: left; background: var(--card); font: var(--ui-text-xs) var(--font-mono); }
  .recovery-actions { display: flex; gap: .6rem; }
  .recovery-shell > small { color: var(--muted-foreground); font-size: var(--ui-text-xs); }
  .secondary { border: 1px solid var(--field-border); border-radius: var(--radius-control); padding: .5rem .9rem; color: var(--foreground); background: var(--card); cursor: pointer; }
  .secondary:hover:not(:disabled) { border-color: var(--foreground); }
  .project-error { margin: 1rem 0 0; border: 1px solid color-mix(in srgb, var(--destructive) 45%, var(--border)); border-radius: var(--radius-control); padding: .65rem .8rem; color: var(--destructive); background: var(--card); font-size: var(--ui-text); }
</style>
