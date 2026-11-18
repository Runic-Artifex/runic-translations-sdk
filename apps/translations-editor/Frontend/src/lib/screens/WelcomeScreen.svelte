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
  .eyebrow { margin: 0; color: var(--muted-foreground); font-size: var(--ui-text-xs); font-weight: 560; }
  .mark { width: 4rem; height: 4rem; border-radius: 22%; background: url("/brand/icon.png") center / cover no-repeat; }
  .mark span { display: none; }
  .mark.small { width: 2.25rem; height: 2.25rem; }

  /* A catalog's state: a filled square when it compiles, a hollow one in the error colour when it does not. The
     health text next to it names the state. */
  .status-dot { width: .5625rem; height: .5625rem; border: 1.5px solid var(--rune-done); background: var(--rune-done); }
  .status-dot.warning { border-color: var(--destructive); background: transparent; }
  .primary { border: 1px solid var(--primary); border-radius: var(--radius-control); padding: .5rem .9rem; color: var(--primary-foreground); background: var(--primary); font-weight: 560; cursor: pointer; }
  .primary:hover:not(:disabled) { background: color-mix(in srgb, var(--primary) 85%, var(--background)); }
  .primary:disabled { cursor: not-allowed; opacity: .5; }

  .welcome-shell { width: 100vw; min-height: 100vh; overflow-y: auto; background: var(--background); }
  .welcome-brand { display: grid; grid-template-columns: auto 1fr auto; align-items: center; gap: .8rem; border-bottom: 1px solid var(--border); padding: .75rem 1.5rem; background: var(--card); }
  .welcome-brand h1 { margin: .1rem 0 0; color: var(--foreground); font-size: 1.0625rem; font-weight: 620; }
  .welcome-brand select { border: 1px solid var(--field-border); border-radius: var(--radius-control); padding: .3rem .5rem; color: var(--foreground); background: var(--background); font: var(--ui-text-xs) var(--font-mono); }
  .welcome-content { width: min(880px, calc(100% - 3rem)); margin: 0 auto; padding: 4rem 0; }
  .welcome-heading { margin-bottom: 1.6rem; }
  .welcome-heading h2 { margin: .5rem 0; color: var(--foreground); font-size: clamp(1.875rem, 3.5vw, 2.25rem); font-weight: 620; line-height: 1.1; letter-spacing: -.02em; }
  .welcome-heading > p:last-child { max-width: 42rem; margin: 0; color: var(--muted-foreground); font-size: 1rem; line-height: 1.55; }
  .catalog-choices { display: grid; gap: .6rem; margin: 1.4rem 0; }
  .catalog-choice { display: grid; grid-template-columns: auto minmax(0, 1fr) auto auto; align-items: center; gap: .9rem; width: 100%; border: 1px solid var(--border); border-radius: var(--radius-pane); padding: .9rem 1rem; color: var(--card-foreground); text-align: left; background: var(--card); cursor: pointer; }
  .catalog-choice:hover { border-color: var(--foreground); }
  .catalog-choice > span:nth-child(2) { display: grid; gap: .2rem; min-width: 0; }
  .catalog-choice strong { color: var(--foreground); font-size: 1rem; font-weight: 620; }
  .catalog-choice small { overflow: hidden; color: var(--muted-foreground); font: var(--ui-text-xs) var(--font-mono); text-overflow: ellipsis; white-space: nowrap; }
  .catalog-metrics { color: var(--muted-foreground); font-size: var(--ui-text-xs); line-height: 1.55; text-align: right; }
  .health { min-width: 4.5rem; border: 1px solid var(--border); border-radius: var(--radius-control); padding: .15rem .45rem; color: var(--muted-foreground); font-size: var(--ui-text-xs); text-align: center; }
  .health.error { border-color: color-mix(in srgb, var(--destructive) 50%, var(--border)); color: var(--destructive); }
  .open-workspace-card { display: grid; gap: .45rem; margin-top: 1.2rem; border: 1px solid var(--border); border-radius: var(--radius-pane); padding: 1rem; background: var(--card); }
  .open-workspace-card label { color: var(--foreground); font-size: var(--ui-text); font-weight: 600; }
  .open-workspace-card > div { display: grid; grid-template-columns: 1fr auto auto; gap: .6rem; }
  .open-workspace-card input { min-width: 0; border: 1px solid var(--field-border); border-radius: var(--radius-control); padding: .5rem .7rem; color: var(--foreground); background: var(--background); font: var(--ui-text) var(--font-mono); }
  .open-workspace-card input:focus-visible { border-color: var(--ring); }
  .open-workspace-card small { color: var(--muted-foreground); font-size: var(--ui-text-xs); }
  .welcome-actions { display: flex; gap: .6rem; margin-top: .8rem; }
  .repair-list { margin-top: 1.5rem; border: 1px solid color-mix(in srgb, var(--destructive) 45%, var(--border)); border-radius: var(--radius-pane); background: var(--card); overflow: hidden; }
  .repair-list > header { padding: .75rem 1rem; }
  .repair-list > header > div { display: flex; justify-content: space-between; gap: 1rem; }
  .repair-list strong { color: var(--destructive); font-size: var(--ui-text); font-weight: 620; }
  .repair-list header span { color: var(--muted-foreground); font-size: var(--ui-text-xs); }
  .repair-list > button { display: grid; grid-template-columns: auto minmax(0, 1fr) auto; align-items: center; gap: .7rem; width: 100%; border: 0; border-top: 1px solid var(--border); padding: .65rem 1rem; color: var(--foreground); text-align: left; background: transparent; cursor: pointer; }
  .repair-list > button:hover { background: var(--muted); }
  .repair-list > button > span { display: grid; place-items: center; width: 1.3rem; height: 1.3rem; border: 1.5px solid var(--destructive); color: var(--destructive); font-weight: 700; }
  .repair-list code { color: var(--foreground); font: var(--ui-text-xs) var(--font-mono); }
  .repair-list small { color: var(--muted-foreground); font-size: var(--ui-text-xs); }
  .recent-projects { margin-top: 1.5rem; border: 1px solid var(--border); border-radius: var(--radius-pane); background: var(--card); overflow: hidden; }
  .recent-projects > header { display: flex; justify-content: space-between; gap: 1rem; padding: .7rem 1rem; }
  .recent-projects header strong { color: var(--foreground); font-size: var(--ui-text); font-weight: 620; }
  .recent-projects header span { color: var(--muted-foreground); font-size: var(--ui-text-xs); }
  .recent-projects > button { display: grid; grid-template-columns: 1fr auto; align-items: center; width: 100%; border: 0; border-top: 1px solid var(--border); padding: .65rem 1rem; color: var(--foreground); text-align: left; background: transparent; cursor: pointer; }
  .recent-projects > button:hover { background: var(--muted); }
  .recent-projects button > span { display: grid; gap: .2rem; min-width: 0; }
  .recent-projects code { overflow: hidden; color: var(--muted-foreground); font: var(--ui-text-xs) var(--font-mono); text-overflow: ellipsis; white-space: nowrap; }
  .recent-projects small { color: var(--muted-foreground); font-size: var(--ui-text-xs); }
  .secondary { border: 1px solid var(--field-border); border-radius: var(--radius-control); padding: .5rem .9rem; color: var(--foreground); background: var(--card); cursor: pointer; }
  .secondary:hover:not(:disabled) { border-color: var(--foreground); }
  .project-error { margin: 1rem 0 0; border: 1px solid color-mix(in srgb, var(--destructive) 45%, var(--border)); border-radius: var(--radius-control); padding: .65rem .8rem; color: var(--destructive); background: var(--card); font-size: var(--ui-text); }
</style>
