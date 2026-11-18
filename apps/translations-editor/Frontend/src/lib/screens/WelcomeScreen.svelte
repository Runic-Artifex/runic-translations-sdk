<script lang="ts">
  import type { EditorDocument, WorkspaceSnapshot } from "#lib/contracts.js";
  import type { EditorLabels } from "#lib/editor-labels.js";
  import type { WorkspaceController } from "#lib/state/workspace-controller.svelte.js";
  import { uiLocaleName } from "#lib/ui-locale.js";
  import { displayNotice, getUiText, type UiMessage } from "#lib/ui-text.js";

  let {
    snapshot,
    workspace,
    labels,
    uiLocale,
    uiLocales,
    malformedDocuments,
    clientError,
    onlocalechange,
    oncreateproject,
    onrepair,
  }: {
    /** The scanned workspace without an open catalog: the catalogs it found, if any. */
    snapshot: WorkspaceSnapshot;
    workspace: WorkspaceController;
    labels: EditorLabels;
    uiLocale: string;
    uiLocales: readonly string[];
    malformedDocuments: EditorDocument[];
    clientError: UiMessage | undefined;
    onlocalechange(locale: string): void;
    oncreateproject(): void;
    onrepair(document: EditorDocument): void;
  } = $props();
  const ui = getUiText();
</script>

<main class="welcome-shell">
  <header class="welcome-brand">
    <div class="mark small" aria-hidden="true"><span></span></div>
    <div><p class="eyebrow">{labels.eyebrow}</p><h1>{labels.title}</h1></div>
    <select aria-label={ui.text("ui_page_interface_language")} value={uiLocale} onchange={(event) => onlocalechange(event.currentTarget.value)}>
      {#each uiLocales as locale (locale)}<option value={locale} title={uiLocaleName(locale)}>{locale.toUpperCase()}</option>{/each}
    </select>
  </header>
  <section class="welcome-content">
    <div class="welcome-heading">
      <p class="eyebrow">{ui.text("ui_page_welcome_eyebrow")}</p>
      <h2>{snapshot.catalogs.length > 1 ? ui.text("ui_page_welcome_choose_catalog") : ui.text("ui_page_welcome_open_project")}</h2>
      <p>{snapshot.catalogs.length > 1
        ? ui.text("ui_count_catalogs_found", { count: snapshot.catalogs.length })
        : ui.text("ui_page_welcome_open_or_create")}</p>
    </div>

    {#if snapshot.catalogs.length > 0}
      <div class="catalog-choices">
        {#each snapshot.catalogs as catalog (catalog.id)}
          <button class="catalog-choice" onclick={() => void workspace.open(catalog.id)} disabled={workspace.openingWorkspace}>
            <span class={{ "status-dot": true, warning: !catalog.success }}></span>
            <span><strong>{catalog.id}</strong><small>{catalog.manifestPaths.join(", ")}</small></span>
            <span class="catalog-metrics">{ui.text("ui_count_locales", { count: catalog.localeCount })}<br />{ui.text("ui_count_messages", { count: catalog.messageCount })}</span>
            <span class={catalog.errorCount > 0 ? "health error" : "health"}>{catalog.errorCount > 0 ? ui.text("ui_count_errors", { count: catalog.errorCount }) : ui.text("ui_page_healthy")}</span>
          </button>
        {/each}
      </div>
    {/if}

    <div class="open-workspace-card">
      <label for="open-directory">{ui.text("ui_page_workspace_directory")}</label>
      <div><input id="open-directory" bind:value={workspace.openDirectory} placeholder="/projects/customer-app" autocomplete="off" />
        <button class="secondary" disabled={workspace.pickingWorkspace || workspace.openingWorkspace} onclick={() => void workspace.pick()}>{workspace.pickingWorkspace ? ui.text("ui_page_choosing") : ui.text("ui_page_browse")}</button>
        <button class="primary" disabled={workspace.openingWorkspace} onclick={() => void workspace.open()}>{workspace.openingWorkspace ? ui.text("ui_page_opening") : ui.text("ui_page_open")}</button></div>
      <small>{ui.text("ui_page_welcome_traversal_note")}</small>
    </div>

    <div class="welcome-actions">
      <button class="secondary" onclick={oncreateproject}>＋ {ui.text("ui_page_welcome_create_project")}</button>
      <button class="secondary" onclick={() => void workspace.load(true)}>↻ {ui.text("ui_page_welcome_scan")} {snapshot.root}</button>
    </div>

    {#if workspace.recentProjects.length > 0}
      <section class="recent-projects">
        <header><strong>{ui.text("ui_page_welcome_recent_projects")}</strong><span>{ui.text("ui_page_welcome_stored_local_profile")}</span></header>
        {#each workspace.recentProjects as project (`${project.root}\n${project.catalogId}`)}
          <button onclick={() => void workspace.open(project.catalogId, project.root)} disabled={workspace.openingWorkspace}>
            <span><strong>{project.catalogId}</strong><code>{project.root}</code></span>
            <small>{new Date(project.openedAt).toLocaleDateString(uiLocale)}</small>
          </button>
        {/each}
      </section>
    {/if}

    {#if malformedDocuments.length > 0}
      <section class="repair-list">
        <header><div><strong>{ui.text("ui_page_welcome_repair_malformed_json")}</strong><span>{ui.text("ui_count_files_attention", { count: malformedDocuments.length })}</span></div></header>
        {#each malformedDocuments as document (document.path)}
          <button onclick={() => onrepair(document)}><span>!</span><code>{document.path}</code><small>{ui.text("ui_page_welcome_open_repair_editor")} →</small></button>
        {/each}
      </section>
    {/if}
    {#if clientError}<p class="project-error" aria-live="polite">{displayNotice(clientError, ui)}</p>{/if}
  </section>
</main>

<style>
  .eyebrow { margin: 0; color: var(--primary); font-size: .64rem; font-weight: 700; letter-spacing: .15em; text-transform: uppercase; }
  .mark { width: 4rem; height: 4rem; border-radius: 1rem; background: url("/brand/icon.png") center / cover no-repeat; box-shadow: 0 1rem 3rem color-mix(in oklch, var(--foreground) 22%, transparent); }
  .mark span { display: none; }
  .mark.small { width: 2.25rem; height: 2.25rem; border-radius: .65rem; box-shadow: 0 .35rem 1rem color-mix(in oklch, var(--foreground) 16%, transparent); }

  .status-dot { width: .5rem; height: .5rem; border-radius: 50%; background: var(--chart-2); box-shadow: 0 0 .6rem color-mix(in oklch, var(--chart-2) 55%, transparent); }
  .status-dot.warning { background: var(--primary); box-shadow: 0 0 .6rem color-mix(in oklch, var(--primary) 55%, transparent); }
  .primary { border: 1px solid var(--primary); border-radius: .45rem; padding: .58rem .85rem; color: var(--primary-foreground); background: var(--primary); font-weight: 750; cursor: pointer; box-shadow: 0 .35rem 1rem color-mix(in oklch, var(--foreground) 16%, transparent); }
  .primary:hover:not(:disabled) { filter: brightness(1.08); }
  .primary:disabled { cursor: not-allowed; filter: grayscale(.5); opacity: .42; }

  .welcome-shell { width: 100vw; min-height: 100vh; overflow-y: auto; background: radial-gradient(circle at 70% -10%, color-mix(in oklch, var(--primary) 12%, transparent), transparent 42%), var(--background); }
  .welcome-brand { display: grid; grid-template-columns: auto 1fr auto; align-items: center; gap: .8rem; border-bottom: 1px solid var(--border); padding: 1rem 1.5rem; background: color-mix(in oklch, var(--card) 94%, transparent); }
  .welcome-brand h1 { margin: .1rem 0 0; color: var(--foreground); font-family: Georgia, serif; font-size: 1.35rem; font-weight: 500; }
  .welcome-brand select { border: 1px solid var(--input); border-radius: .4rem; padding: .35rem .5rem; background: var(--secondary); }
  .welcome-content { width: min(880px, calc(100% - 3rem)); margin: 0 auto; padding: 4rem 0; }
  .welcome-heading { margin-bottom: 1.6rem; }
  .welcome-heading h2 { margin: .55rem 0; color: var(--foreground); font-family: Georgia, serif; font-size: clamp(2rem, 4vw, 3.2rem); font-weight: 500; letter-spacing: -.035em; }
  .welcome-heading > p:last-child { max-width: 42rem; margin: 0; color: var(--muted-foreground); font-size: .78rem; line-height: 1.6; }
  .catalog-choices { display: grid; gap: .65rem; margin: 1.4rem 0; }
  .catalog-choice { display: grid; grid-template-columns: auto minmax(0, 1fr) auto auto; align-items: center; gap: .9rem; width: 100%; border: 1px solid var(--border); border-radius: .65rem; padding: 1rem; color: var(--card-foreground); text-align: left; background: var(--card); cursor: pointer; }
  .catalog-choice:hover { border-color: var(--ring); background: var(--accent); }
  .catalog-choice > span:nth-child(2) { display: grid; gap: .2rem; min-width: 0; }
  .catalog-choice strong { color: var(--foreground); font-size: .82rem; }
  .catalog-choice small { overflow: hidden; color: var(--muted-foreground); font: .58rem ui-monospace, monospace; text-overflow: ellipsis; white-space: nowrap; }
  .catalog-metrics { color: var(--muted-foreground); font-size: .6rem; line-height: 1.55; text-align: right; }
  .health { min-width: 4.5rem; border-radius: 1rem; padding: .25rem .45rem; color: var(--chart-2); background: color-mix(in oklch, var(--chart-2) 13%, transparent); font-size: .56rem; text-align: center; }
  .health.error { color: var(--destructive); background: color-mix(in oklch, var(--destructive) 13%, transparent); }
  .open-workspace-card { display: grid; gap: .45rem; margin-top: 1.2rem; border: 1px solid var(--border); border-radius: .65rem; padding: 1rem; background: var(--card); }
  .open-workspace-card label { color: var(--foreground); font-size: .66rem; font-weight: 650; }
  .open-workspace-card > div { display: grid; grid-template-columns: 1fr auto auto; gap: .6rem; }
  .open-workspace-card input { min-width: 0; border: 1px solid var(--input); border-radius: .45rem; outline: 0; padding: .68rem .75rem; color: var(--foreground); background: var(--background); font-size: .75rem; }
  .open-workspace-card input:focus { border-color: var(--ring); }
  .open-workspace-card small { color: var(--muted-foreground); font-size: .58rem; }
  .welcome-actions { display: flex; gap: .65rem; margin-top: .8rem; }
  .repair-list { margin-top: 1.5rem; border: 1px solid color-mix(in oklch, var(--destructive) 42%, var(--border)); border-radius: .65rem; background: color-mix(in oklch, var(--destructive) 5%, var(--card)); overflow: hidden; }
  .repair-list > header { padding: .8rem 1rem; background: color-mix(in oklch, var(--destructive) 10%, var(--card)); }
  .repair-list > header > div { display: flex; justify-content: space-between; }
  .repair-list strong { color: var(--destructive); font-size: .7rem; }
  .repair-list header span { color: color-mix(in oklch, var(--destructive) 70%, var(--muted-foreground)); font-size: .6rem; }
  .repair-list > button { display: grid; grid-template-columns: auto minmax(0, 1fr) auto; align-items: center; gap: .7rem; width: 100%; border: 0; border-top: 1px solid color-mix(in oklch, var(--destructive) 28%, var(--border)); padding: .7rem 1rem; text-align: left; background: transparent; cursor: pointer; }
  .repair-list > button:hover { background: color-mix(in oklch, var(--destructive) 9%, var(--card)); }
  .repair-list > button > span { display: grid; place-items: center; width: 1.3rem; height: 1.3rem; border-radius: 50%; color: var(--destructive); background: color-mix(in oklch, var(--destructive) 16%, transparent); font-weight: 800; }
  .repair-list code { color: var(--foreground); font: .62rem ui-monospace, monospace; }
  .repair-list small { color: var(--muted-foreground); font-size: .58rem; }
  .recent-projects { margin-top: 1.5rem; border: 1px solid var(--border); border-radius: .65rem; background: var(--card); overflow: hidden; }
  .recent-projects > header { display: flex; justify-content: space-between; padding: .75rem 1rem; background: var(--muted); }
  .recent-projects header strong { color: var(--foreground); font-size: .68rem; }
  .recent-projects header span { color: var(--muted-foreground); font-size: .56rem; }
  .recent-projects > button { display: grid; grid-template-columns: 1fr auto; align-items: center; width: 100%; border: 0; border-top: 1px solid var(--border); padding: .7rem 1rem; color: var(--foreground); text-align: left; background: transparent; cursor: pointer; }
  .recent-projects > button:hover { background: var(--accent); }
  .recent-projects button > span { display: grid; gap: .2rem; min-width: 0; }
  .recent-projects code { overflow: hidden; color: var(--muted-foreground); font: .57rem ui-monospace, monospace; text-overflow: ellipsis; white-space: nowrap; }
  .recent-projects small { color: var(--muted-foreground); font-size: .55rem; }
  .secondary { border: 1px solid var(--border); border-radius: .45rem; padding: .58rem .85rem; color: var(--secondary-foreground); background: var(--secondary); cursor: pointer; }
  .secondary:hover:not(:disabled) { border-color: var(--ring); color: var(--accent-foreground); background: var(--accent); }
  .project-error { margin: 1rem 0 0; border: 1px solid color-mix(in oklch, var(--destructive) 45%, var(--border)); border-radius: .45rem; padding: .7rem .8rem; color: var(--destructive); background: color-mix(in oklch, var(--destructive) 10%, transparent); font-size: .67rem; }
</style>
