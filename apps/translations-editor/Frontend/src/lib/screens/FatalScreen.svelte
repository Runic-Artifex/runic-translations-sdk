<script lang="ts">
  import { displayNotice, getUiText, notice, type UiMessage } from "#lib/ui-text.js";

  let {
    eyebrow,
    reloadLabel,
    recoveryReloadRequired,
    recoveryReloadMessage,
    clientError,
    onreload,
    onopenother,
  }: {
    eyebrow: string;
    reloadLabel: string;
    /** A recovered transaction left the workspace to be reloaded. */
    recoveryReloadRequired: boolean;
    recoveryReloadMessage: UiMessage | undefined;
    clientError: UiMessage | undefined;
    onreload(): void;
    onopenother(): void;
  } = $props();
  const ui = getUiText();
</script>

<main class="fatal-shell">
  <div class="mark" aria-hidden="true"><span></span></div>
  <p class="eyebrow">{eyebrow}</p>
  <h1>{recoveryReloadRequired ? ui.text("ui_page_fatal_recovery_reload_required") : ui.text("ui_page_fatal_could_not_open")}</h1>
  <p>{displayNotice(recoveryReloadRequired ? recoveryReloadMessage ?? notice("ui_page_fatal_recovery_refresh") : clientError ?? notice("ui_page_fatal_no_catalog"), ui)}</p>
  <div class="recovery-actions">
    <button class="primary" onclick={onreload}>{reloadLabel}</button>
    {#if recoveryReloadRequired}<button class="secondary" onclick={onopenother}>{ui.text("ui_page_fatal_open_another_workspace")}</button>{/if}
  </div>
</main>

<style>
  .eyebrow { margin: 0; color: var(--muted-foreground); font-size: var(--ui-text-xs); font-weight: 560; }
  .mark { width: 4rem; height: 4rem; border-radius: 22%; background: url("/brand/icon.png") center / cover no-repeat; }
  .mark span { display: none; }
  .primary { border: 1px solid var(--primary); border-radius: var(--radius-control); padding: .5rem .9rem; color: var(--primary-foreground); background: var(--primary); font-weight: 560; cursor: pointer; }
  .primary:hover:not(:disabled) { background: color-mix(in srgb, var(--primary) 85%, var(--background)); }
  .primary:disabled { cursor: not-allowed; opacity: .5; }
  .fatal-shell { display: grid; place-content: center; place-items: center; height: 100vh; padding: 2rem; color: var(--muted-foreground); text-align: center; background: var(--background); }
  .fatal-shell { max-width: none; }
  .fatal-shell .mark { margin-bottom: 2rem; }
  .fatal-shell h1 { max-width: 36rem; margin: .8rem 0; color: var(--foreground); font-size: 2.25rem; font-weight: 620; line-height: 1.1; letter-spacing: -.02em; }
  .fatal-shell > p:not(.eyebrow) { max-width: 44rem; margin: 0 0 1.5rem; font-size: 1rem; line-height: 1.55; }
  .recovery-actions { display: flex; gap: .6rem; }
  .secondary { border: 1px solid var(--field-border); border-radius: var(--radius-control); padding: .5rem .9rem; color: var(--foreground); background: var(--card); cursor: pointer; }
  .secondary:hover:not(:disabled) { border-color: var(--foreground); }
</style>
