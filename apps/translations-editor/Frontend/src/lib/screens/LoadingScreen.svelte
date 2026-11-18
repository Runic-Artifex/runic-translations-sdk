<script lang="ts">
  let { eyebrow }: { eyebrow: string } = $props();
</script>

<main class="loading-shell" aria-live="polite">
  <div class="mark" aria-hidden="true"><span></span></div>
  <p>{eyebrow}</p>
  <div class="loading-line"></div>
</main>

<style>
  .mark { width: 4rem; height: 4rem; border-radius: 1rem; background: url("/brand/icon.png") center / cover no-repeat; box-shadow: 0 1rem 3rem color-mix(in oklch, var(--foreground) 22%, transparent); }
  .mark span { display: none; }
  .loading-shell { display: grid; place-content: center; place-items: center; height: 100vh; padding: 2rem; color: var(--muted-foreground); text-align: center; background: radial-gradient(circle at center, color-mix(in oklch, var(--primary) 10%, var(--background)), var(--background) 65%); }
  .loading-shell { gap: 1.5rem; }
  .loading-shell p { margin: 0; color: var(--primary); font-size: .65rem; font-weight: 700; letter-spacing: .18em; text-transform: uppercase; }
  .loading-line { width: 12rem; height: 1px; background: linear-gradient(90deg, transparent, var(--primary), transparent); animation: pulse .8s infinite alternate; }
  @keyframes pulse { to { opacity: .35; } }
</style>
