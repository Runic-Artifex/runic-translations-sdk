<script lang="ts">
  let { eyebrow }: { eyebrow: string } = $props();
</script>

<main class="loading-shell" aria-live="polite">
  <div class="mark" aria-hidden="true"><span></span></div>
  <p>{eyebrow}</p>
  <div class="loading-line"></div>
</main>

<style>
  .mark { width: 4rem; height: 4rem; border-radius: 22%; background: url("/brand/icon.png") center / cover no-repeat; }
  .mark span { display: none; }
  .loading-shell { display: grid; place-content: center; place-items: center; height: 100vh; padding: 2rem; color: var(--muted-foreground); text-align: center; background: var(--background); }
  .loading-shell { gap: 1.5rem; }
  .loading-shell p { margin: 0; color: var(--muted-foreground); font-size: var(--ui-text); font-weight: 560; }
  .loading-line { width: 12rem; height: 1px; background: linear-gradient(90deg, transparent, var(--foreground), transparent); animation: pulse .8s infinite alternate; }
  @keyframes pulse { to { opacity: .35; } }
</style>
