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
  .eyebrow { margin: 0; color: var(--primary); font-size: .64rem; font-weight: 700; letter-spacing: .15em; text-transform: uppercase; }
  .mark { width: 4rem; height: 4rem; border-radius: 1rem; background: url("/brand/icon.png") center / cover no-repeat; box-shadow: 0 1rem 3rem color-mix(in oklch, var(--foreground) 22%, transparent); }
  .mark span { display: none; }
  .primary { border: 1px solid var(--primary); border-radius: .45rem; padding: .58rem .85rem; color: var(--primary-foreground); background: var(--primary); font-weight: 750; cursor: pointer; box-shadow: 0 .35rem 1rem color-mix(in oklch, var(--foreground) 16%, transparent); }
  .primary:hover:not(:disabled) { filter: brightness(1.08); }
  .primary:disabled { cursor: not-allowed; filter: grayscale(.5); opacity: .42; }
  .recovery-shell { display: grid; place-content: center; place-items: center; height: 100vh; padding: 2rem; color: var(--muted-foreground); text-align: center; background: radial-gradient(circle at center, color-mix(in oklch, var(--primary) 10%, var(--background)), var(--background) 65%); }
  .recovery-shell { gap: .9rem; }
  .recovery-shell h1 { max-width: 42rem; margin: .4rem 0; color: var(--foreground); font-family: Georgia, serif; font-size: 2.3rem; font-weight: 500; }
  .recovery-shell > p:not(.eyebrow) { max-width: 44rem; margin: 0; line-height: 1.6; }
  .recovery-paths { display: grid; gap: .3rem; width: min(36rem, 100%); max-height: 12rem; margin: .5rem 0; overflow-y: auto; }
  .recovery-paths code { border: 1px solid var(--border); border-radius: .3rem; padding: .45rem .6rem; color: var(--foreground); text-align: left; background: var(--card); font: .6rem ui-monospace, monospace; }
  .recovery-actions { display: flex; gap: .7rem; }
  .recovery-shell > small { color: var(--muted-foreground); font-size: .58rem; }
  .secondary { border: 1px solid var(--border); border-radius: .45rem; padding: .58rem .85rem; color: var(--secondary-foreground); background: var(--secondary); cursor: pointer; }
  .secondary:hover:not(:disabled) { border-color: var(--ring); color: var(--accent-foreground); background: var(--accent); }
  .project-error { margin: 1rem 0 0; border: 1px solid color-mix(in oklch, var(--destructive) 45%, var(--border)); border-radius: .45rem; padding: .7rem .8rem; color: var(--destructive); background: color-mix(in oklch, var(--destructive) 10%, transparent); font-size: .67rem; }
</style>
