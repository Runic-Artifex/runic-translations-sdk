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
  .eyebrow { margin: 0; color: var(--primary); font-size: .64rem; font-weight: 700; letter-spacing: .15em; text-transform: uppercase; }
  .mark { width: 4rem; height: 4rem; border-radius: 1rem; background: url("/brand/icon.png") center / cover no-repeat; box-shadow: 0 1rem 3rem color-mix(in oklch, var(--foreground) 22%, transparent); }
  .mark span { display: none; }
  .primary { border: 1px solid var(--primary); border-radius: .45rem; padding: .58rem .85rem; color: var(--primary-foreground); background: var(--primary); font-weight: 750; cursor: pointer; box-shadow: 0 .35rem 1rem color-mix(in oklch, var(--foreground) 16%, transparent); }
  .primary:hover:not(:disabled) { filter: brightness(1.08); }
  .primary:disabled { cursor: not-allowed; filter: grayscale(.5); opacity: .42; }
  .fatal-shell { display: grid; place-content: center; place-items: center; height: 100vh; padding: 2rem; color: var(--muted-foreground); text-align: center; background: radial-gradient(circle at center, color-mix(in oklch, var(--primary) 10%, var(--background)), var(--background) 65%); }
  .fatal-shell { max-width: none; }
  .fatal-shell .mark { margin-bottom: 2rem; }
  .fatal-shell h1 { max-width: 36rem; margin: .8rem 0; color: var(--foreground); font-family: Georgia, serif; font-size: 2.6rem; font-weight: 500; }
  .fatal-shell > p:not(.eyebrow) { max-width: 44rem; margin: 0 0 1.5rem; }
  .recovery-actions { display: flex; gap: .7rem; }
  .secondary { border: 1px solid var(--border); border-radius: .45rem; padding: .58rem .85rem; color: var(--secondary-foreground); background: var(--secondary); cursor: pointer; }
  .secondary:hover:not(:disabled) { border-color: var(--ring); color: var(--accent-foreground); background: var(--accent); }
</style>
