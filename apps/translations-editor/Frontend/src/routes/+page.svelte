<script lang="ts">
  import { onMount } from "svelte";
  import {
    m$App$Advanced,
    m$App$All,
    m$App$DefaultLocale,
    m$App$Diagnostics,
    m$App$Eyebrow,
    m$App$Invalid,
    m$App$Missing,
    m$App$NoResults,
    m$App$NoSelection,
    m$App$Raw,
    m$App$Reload,
    m$App$Save,
    m$App$Saved,
    m$App$Saving,
    m$App$Search,
    m$App$Simple,
    m$App$Structured,
    m$App$Title,
    m$App$Unsaved,
    m$App$Valid,
    m$App$Workspace,
  } from "virtual:runic-text-resources/editor";
  import type {
    EditorDiagnostic,
    EditorDocument,
    ValidationResult,
    WorkspaceSnapshot,
  } from "$lib/contracts";
  import { createEditorBridge } from "$lib/editor-bridge";
  import {
    buildRows,
    coverage,
    formatJson,
    preview,
    updateResourceValue,
    type ResourceValue,
    type TranslationRow,
  } from "$lib/resource-model";

  type Filter = "all" | "missing" | "structured";
  type EditorMode = "simple" | "advanced" | "raw";

  const bridge = createEditorBridge();
  let snapshot = $state.raw<WorkspaceSnapshot>();
  let drafts = $state<Record<string, string>>({});
  let selectedKey = $state("");
  let selectedLocale = $state("");
  let selectedDocumentPath = $state("");
  let filter = $state<Filter>("all");
  let query = $state("");
  let mode = $state<EditorMode>("simple");
  let editorText = $state("");
  let uiLocale = $state("en");
  let loading = $state(true);
  let saving = $state(false);
  let validationBusy = $state(false);
  let validation = $state.raw<ValidationResult>();
  let clientError = $state<string>();
  let operationMessage = $state<string>();
  let searchInput = $state<HTMLInputElement>();
  let validationTimer: number | undefined;
  let validationEpoch = 0;

  let labels = $derived(labelsFor(uiLocale));
  let rows = $derived(buildRows(snapshot, drafts));
  let visibleRows = $derived.by(() => {
    const normalized = query.trim().toLocaleLowerCase();
    return rows.filter((row) => {
      const cell = row.cells[selectedLocale];
      if (filter === "missing" && cell?.entry !== undefined) return false;
      if (filter === "structured" && !row.structured) return false;
      if (normalized.length === 0) return true;
      const searchable = [
        row.key,
        row.description ?? "",
        ...row.tags,
        ...Object.values(row.cells).map((candidate) => preview(candidate.entry)),
      ].join("\n").toLocaleLowerCase();
      return searchable.includes(normalized);
    });
  });
  let selectedRow = $derived(rows.find((row) => row.key === selectedKey));
  let currentCell = $derived(selectedRow?.cells[selectedLocale]);
  let currentDocument = $derived(
    snapshot?.documents.find((document) => document.path === selectedDocumentPath),
  );
  let currentContent = $derived(
    currentDocument === undefined
      ? undefined
      : (drafts[currentDocument.path] ?? currentDocument.content),
  );
  let isDirty = $derived(
    currentDocument !== undefined && drafts[currentDocument.path] !== undefined,
  );
  let diagnostics = $derived(validation?.diagnostics ?? snapshot?.diagnostics ?? []);
  let errorCount = $derived(diagnostics.filter((item) => item.severity === "error").length);
  let warningCount = $derived(diagnostics.filter((item) => item.severity === "warning").length);

  onMount(() => {
    void loadWorkspace(false);
  });

  async function loadWorkspace(confirmDiscard: boolean): Promise<void> {
    if (confirmDiscard && Object.keys(drafts).length > 0 && !confirm("Discard all unsaved changes?")) return;
    loading = true;
    operationMessage = undefined;
    clientError = undefined;
    try {
      const next = await bridge.load();
      installSnapshot(next, true);
    } catch (error) {
      clientError = errorMessage(error);
    } finally {
      loading = false;
    }
  }

  function installSnapshot(next: WorkspaceSnapshot, resetSelection: boolean): void {
    snapshot = next;
    drafts = {};
    validation = undefined;
    const nextRows = buildRows(next, {});
    if (resetSelection || !nextRows.some((row) => row.key === selectedKey)) {
      selectedKey = nextRows[0]?.key ?? "";
    }
    if (resetSelection || !next.catalog?.locales.some((locale) => locale.tag === selectedLocale)) {
      selectedLocale = next.catalog?.defaultLocale ?? next.catalog?.locales[0]?.tag ?? "";
    }
    configureEditor();
  }

  function selectRow(row: TranslationRow): void {
    selectedKey = row.key;
    validation = undefined;
    clientError = undefined;
    operationMessage = undefined;
    configureEditor();
  }

  function selectLocale(locale: string): void {
    selectedLocale = locale;
    validation = undefined;
    clientError = undefined;
    operationMessage = undefined;
    configureEditor();
  }

  function chooseMode(nextMode: EditorMode): void {
    mode = nextMode;
    clientError = undefined;
    configureEditor(nextMode);
  }

  function configureEditor(preferredMode?: EditorMode): void {
    const row = buildRows(snapshot, drafts).find((candidate) => candidate.key === selectedKey);
    const cell = row?.cells[selectedLocale];
    const document = cell?.document;
    selectedDocumentPath = document?.path ?? "";
    const sourceEntry = row?.cells[snapshot?.catalog?.defaultLocale ?? ""]?.entry;
    const nextMode = preferredMode ?? (cell?.entry?.structured || row?.structured ? "advanced" : "simple");
    mode = nextMode;
    if (nextMode === "raw") {
      editorText = document === undefined ? "" : (drafts[document.path] ?? document.content);
    } else if (nextMode === "advanced") {
      editorText = JSON.stringify(cell?.entry?.value ?? sourceEntry?.value ?? "", null, 2);
    } else {
      editorText = typeof cell?.entry?.value === "string" ? cell.entry.value : "";
    }
  }

  function edit(value: string): void {
    editorText = value;
    clientError = undefined;
    operationMessage = undefined;
    const document = currentDocument;
    if (document === undefined) {
      clientError = "This locale has no resource document to edit.";
      return;
    }
    try {
      let content: string;
      if (mode === "raw") {
        content = value;
      } else {
        const resourceValue: ResourceValue = mode === "advanced"
          ? parseResourceValue(value)
          : value;
        const sourceEntry = selectedRow?.cells[snapshot?.catalog?.defaultLocale ?? ""]?.entry;
        content = updateResourceValue(
          drafts[document.path] ?? document.content,
          selectedKey,
          resourceValue,
          sourceEntry,
        );
      }
      drafts[document.path] = content;
      scheduleValidation(document.path, content);
    } catch (error) {
      clientError = errorMessage(error);
      validation = { success: false, diagnostics: [] };
    }
  }

  function formatRaw(): void {
    if (mode !== "raw") return;
    try {
      const formatted = formatJson(editorText);
      edit(formatted);
    } catch (error) {
      clientError = errorMessage(error);
    }
  }

  function scheduleValidation(path: string, content: string): void {
    if (validationTimer !== undefined) window.clearTimeout(validationTimer);
    const epoch = ++validationEpoch;
    validationBusy = true;
    validationTimer = window.setTimeout(() => {
      void bridge.validate(path, content).then((result) => {
        if (epoch !== validationEpoch) return;
        validation = result;
        validationBusy = false;
      }).catch((error) => {
        if (epoch !== validationEpoch) return;
        clientError = errorMessage(error);
        validationBusy = false;
      });
    }, 350);
  }

  async function save(): Promise<void> {
    const document = currentDocument;
    const content = currentContent;
    if (document === undefined || content === undefined || !isDirty || saving) return;
    saving = true;
    operationMessage = undefined;
    clientError = undefined;
    try {
      const checked = await bridge.validate(document.path, content);
      validation = checked;
      if (!checked.success) return;
      const result = await bridge.save(document.path, content, document.revision);
      if (!result.ok || result.snapshot === undefined) {
        if (result.validation !== undefined) validation = result.validation;
        clientError = result.message ?? `Save failed (${result.kind}).`;
        return;
      }
      const key = selectedKey;
      const locale = selectedLocale;
      installSnapshot(result.snapshot, false);
      selectedKey = key;
      selectedLocale = locale;
      configureEditor();
      operationMessage = labels.saved;
    } catch (error) {
      clientError = errorMessage(error);
    } finally {
      saving = false;
    }
  }

  function handleKeyboard(event: KeyboardEvent): void {
    if ((event.ctrlKey || event.metaKey) && event.key.toLocaleLowerCase() === "s") {
      event.preventDefault();
      void save();
    }
    if ((event.ctrlKey || event.metaKey) && event.key.toLocaleLowerCase() === "k") {
      event.preventDefault();
      searchInput?.focus();
    }
  }

  function selectDiagnostic(diagnostic: EditorDiagnostic): void {
    const document = snapshot?.documents.find((candidate) => candidate.path === diagnostic.path);
    if (document?.locale !== undefined) selectedLocale = document.locale;
    if (document !== undefined) {
      selectedDocumentPath = document.path;
      mode = "raw";
      editorText = drafts[document.path] ?? document.content;
    }
  }

  function protectDraft(event: BeforeUnloadEvent): void {
    if (Object.keys(drafts).length === 0) return;
    event.preventDefault();
  }

  function localeName(tag: string): string {
    try {
      return new Intl.DisplayNames([uiLocale], { type: "language" }).of(tag) ?? tag;
    } catch {
      return tag;
    }
  }

  function parseResourceValue(value: string): ResourceValue {
    const parsed: unknown = JSON.parse(value);
    if (typeof parsed === "string") return parsed;
    if (typeof parsed === "object" && parsed !== null && !Array.isArray(parsed)) {
      return parsed as Record<string, unknown>;
    }
    throw new TypeError("A resource value must be a string or structured message object.");
  }

  function errorMessage(error: unknown): string {
    return error instanceof Error ? error.message : String(error);
  }

  function labelsFor(locale: string) {
    const options = { locale };
    return {
      title: m$App$Title(options),
      eyebrow: m$App$Eyebrow(options),
      search: m$App$Search(options),
      all: m$App$All(options),
      missing: m$App$Missing(options),
      structured: m$App$Structured(options),
      save: m$App$Save(options),
      saving: m$App$Saving(options),
      reload: m$App$Reload(options),
      simple: m$App$Simple(options),
      advanced: m$App$Advanced(options),
      raw: m$App$Raw(options),
      noSelection: m$App$NoSelection(options),
      noResults: m$App$NoResults(options),
      valid: m$App$Valid(options),
      invalid: m$App$Invalid(options),
      unsaved: m$App$Unsaved(options),
      saved: m$App$Saved(options),
      defaultLocale: m$App$DefaultLocale(options),
      workspace: m$App$Workspace(options),
      diagnostics: m$App$Diagnostics(options),
    };
  }
</script>

<svelte:head>
  <title>{labels.title} · Runic Artifex</title>
  <meta name="description" content="A focused editor for Runic Text Resources" />
</svelte:head>
<svelte:window onkeydown={handleKeyboard} onbeforeunload={protectDraft} />

{#if loading}
  <main class="loading-shell" aria-live="polite">
    <div class="mark" aria-hidden="true"><span></span></div>
    <p>{labels.eyebrow}</p>
    <div class="loading-line"></div>
  </main>
{:else if snapshot === undefined || snapshot.catalog === undefined}
  <main class="fatal-shell">
    <div class="mark" aria-hidden="true"><span></span></div>
    <p class="eyebrow">{labels.eyebrow}</p>
    <h1>Could not open this translation workspace</h1>
    <p>{clientError ?? "No Runic Text Resources catalog was found."}</p>
    <button class="primary" onclick={() => void loadWorkspace(false)}>{labels.reload}</button>
  </main>
{:else}
  <main class="app-shell">
    <aside class="sidebar">
      <header class="brand">
        <div class="mark small" aria-hidden="true"><span></span></div>
        <div>
          <p class="eyebrow">{labels.eyebrow}</p>
          <h1>{labels.title}</h1>
        </div>
        <select aria-label="Editor language" value={uiLocale} onchange={(event) => uiLocale = event.currentTarget.value}>
          <option value="en">EN</option>
          <option value="de">DE</option>
        </select>
      </header>

      <section class="workspace-card" aria-label={labels.workspace}>
        <div class="workspace-title">
          <span class={{ "status-dot": true, warning: !snapshot.success }}></span>
          <div>
            <strong>{snapshot.catalog.id}</strong>
            <span>{snapshot.catalog.locales.length} {snapshot.catalog.locales.length === 1 ? "locale" : "locales"} · schema v{snapshot.catalog.schemaVersion}</span>
          </div>
          <button class="icon-button" title={labels.reload} aria-label={labels.reload} onclick={() => void loadWorkspace(true)}>
            ↻
          </button>
        </div>
        <p title={snapshot.root}>{snapshot.root}</p>
      </section>

      <section class="locale-overview" aria-label="Locale coverage">
        {#each snapshot.catalog.locales as locale (locale.tag)}
          {@const state = coverage(rows, locale.tag)}
          {@const percent = state.total === 0 ? 100 : Math.round((state.translated / state.total) * 100)}
          <button
            class={selectedLocale === locale.tag ? "locale-card active" : "locale-card"}
            aria-pressed={selectedLocale === locale.tag}
            onclick={() => selectLocale(locale.tag)}
          >
            <span class="locale-code">{locale.tag}</span>
            <span class="locale-copy">
              <strong>{localeName(locale.tag)}</strong>
              <span>{state.translated}/{state.total} translated</span>
            </span>
            <span class="locale-percent">{percent}%</span>
            <span class="progress"><span style:width={`${percent}%`}></span></span>
          </button>
        {/each}
      </section>

      <div class="search-box">
        <span aria-hidden="true">⌕</span>
        <input bind:this={searchInput} type="search" placeholder={labels.search} bind:value={query} />
        <kbd>⌘ K</kbd>
      </div>

      <div class="filters" aria-label="Message filters">
        {#each [
          { value: "all" as const, label: labels.all, count: rows.length },
          { value: "missing" as const, label: labels.missing, count: rows.filter((row) => row.cells[selectedLocale]?.entry === undefined).length },
          { value: "structured" as const, label: labels.structured, count: rows.filter((row) => row.structured).length },
        ] as option (option.value)}
          <button
            class={filter === option.value ? "active" : ""}
            aria-pressed={filter === option.value}
            onclick={() => filter = option.value}
          >{option.label}<span>{option.count}</span></button>
        {/each}
      </div>

      <nav class="message-list" aria-label="Translation messages">
        {#each visibleRows as row (row.key)}
          {@const cell = row.cells[selectedLocale]}
          <button
            class={selectedKey === row.key ? "message active" : "message"}
            aria-current={selectedKey === row.key ? "true" : undefined}
            onclick={() => selectRow(row)}
          >
            <span class={cell?.entry === undefined ? "translation-state missing" : row.structured ? "translation-state structured" : "translation-state"}></span>
            <span class="message-copy">
              <strong>{row.key}</strong>
              <span>{preview(cell?.entry)}</span>
            </span>
            {#if row.structured}<span class="structure-badge">AST</span>{/if}
          </button>
        {:else}
          <div class="empty-list">
            <span>◇</span>
            <p>{labels.noResults}</p>
          </div>
        {/each}
      </nav>
    </aside>

    <section class="editor-shell">
      <header class="editor-toolbar">
        <div class="locale-tabs" role="tablist" aria-label="Editing locale">
          {#each snapshot.catalog.locales as locale (locale.tag)}
            <button
              role="tab"
              aria-selected={selectedLocale === locale.tag}
              class={selectedLocale === locale.tag ? "active" : ""}
              onclick={() => selectLocale(locale.tag)}
            >
              <span>{locale.tag.toLocaleUpperCase()}</span>
              {localeName(locale.tag)}
              {#if locale.tag === snapshot.catalog.defaultLocale}<i title={labels.defaultLocale}>source</i>{/if}
            </button>
          {/each}
        </div>
        <div class="toolbar-actions">
          <span class={isDirty ? "save-state dirty" : "save-state"}>
            <span></span>{isDirty ? labels.unsaved : operationMessage ?? labels.saved}
          </span>
          <button
            class="primary save-button"
            disabled={!isDirty || saving || validationBusy || validation?.success === false || clientError !== undefined}
            onclick={() => void save()}
          >
            <span aria-hidden="true">↓</span>
            {saving ? labels.saving : labels.save}
            <kbd>⌘ S</kbd>
          </button>
        </div>
      </header>

      {#if selectedRow === undefined}
        <div class="no-selection">
          <span>◇</span>
          <h2>{labels.noSelection}</h2>
        </div>
      {:else}
        <div class="editor-content">
          <header class="message-heading">
            <div>
              <div class="breadcrumb">
                {#each selectedRow.key.split(".") as segment, index (`${segment}-${index}`)}
                  {#if index > 0}<span>/</span>{/if}<span>{segment}</span>
                {/each}
              </div>
              <h2>{selectedRow.key.split(".").at(-1)}</h2>
              {#if selectedRow.description}<p>{selectedRow.description}</p>{/if}
              {#if selectedRow.tags.length > 0}
                <div class="tags">{#each selectedRow.tags as tag (tag)}<span>{tag}</span>{/each}</div>
              {/if}
            </div>
            <div class="message-facts">
              <span>{selectedLocale}</span>
              <span>{currentDocument?.layer ?? "no document"}</span>
              {#if currentCell?.inheritedFrom}<span class="fallback">falls back to {currentCell.inheritedFrom}</span>{/if}
            </div>
          </header>

          <div class="mode-tabs" role="tablist" aria-label="Editing mode">
            <button role="tab" aria-selected={mode === "simple"} class={mode === "simple" ? "active" : ""} onclick={() => chooseMode("simple")}>{labels.simple}</button>
            <button role="tab" aria-selected={mode === "advanced"} class={mode === "advanced" ? "active" : ""} onclick={() => chooseMode("advanced")}>{labels.advanced}</button>
            <button role="tab" aria-selected={mode === "raw"} class={mode === "raw" ? "active" : ""} onclick={() => chooseMode("raw")}>{labels.raw}</button>
          </div>

          <section class="writing-area">
            <div class="field-label">
              <label for="translation-value">{mode === "simple" ? localeName(selectedLocale) : mode === "advanced" ? labels.advanced : currentDocument?.path}</label>
              <span>{editorText.length.toLocaleString()} characters</span>
            </div>
            <textarea
              id="translation-value"
              class={{ code: mode !== "simple", invalid: clientError !== undefined || validation?.success === false }}
              value={editorText}
              placeholder={currentCell?.entry === undefined ? "Add this translation…" : undefined}
              spellcheck={mode === "simple"}
              oninput={(event) => edit(event.currentTarget.value)}
            ></textarea>
            <div class="editor-hints">
              <div>
                {#if mode === "simple"}
                  <span>Use <code>{"{name}"}</code> for declared inputs. Literal braces use <code>{"{{braces}}"}</code>.</span>
                {:else if mode === "advanced"}
                  <span>Edit selectors, variants, formats, and semantic markup as a schema-v2 value.</span>
                {:else}
                  <span>Changes here affect the complete resource document.</span>
                {/if}
              </div>
              {#if mode === "raw"}<button onclick={formatRaw}>Format JSON</button>{/if}
            </div>
          </section>

          <section class="validation-panel" aria-live="polite">
            <header>
              <div>
                <span class={validationBusy ? "validation-icon busy" : errorCount > 0 || clientError ? "validation-icon error" : "validation-icon valid"}>
                  {validationBusy ? "…" : errorCount > 0 || clientError ? "!" : "✓"}
                </span>
                <div>
                  <strong>{validationBusy ? "Validating with the Runic compiler…" : errorCount > 0 || clientError ? labels.invalid : labels.valid}</strong>
                  <span>{labels.diagnostics} · {errorCount} errors · {warningCount} warnings</span>
                </div>
              </div>
              <span class="compiler-badge">compiler · schema v{snapshot.catalog.schemaVersion}</span>
            </header>
            {#if clientError}<p class="client-error">{clientError}</p>{/if}
            {#if diagnostics.length > 0}
              <div class="diagnostics">
                {#each diagnostics as diagnostic (`${diagnostic.path}-${diagnostic.id}-${diagnostic.line}-${diagnostic.column}`)}
                  <button onclick={() => selectDiagnostic(diagnostic)}>
                    <span class={diagnostic.severity}>{diagnostic.severity === "error" ? "×" : "△"}</span>
                    <span><strong>{diagnostic.id}</strong>{diagnostic.message}</span>
                    <code>{diagnostic.path}:{diagnostic.line}:{diagnostic.column}</code>
                  </button>
                {/each}
              </div>
            {/if}
          </section>
        </div>
      {/if}
    </section>
  </main>
{/if}

<style>
  :global(*) { box-sizing: border-box; }
  :global(:root) {
    font-family: Inter, "Segoe UI", system-ui, sans-serif;
    color: #edf1ec;
    background: #0b0e0d;
    font-synthesis: none;
  }
  :global(body) { margin: 0; min-width: 980px; min-height: 100vh; overflow: hidden; }
  :global(button), :global(input), :global(textarea), :global(select) { font: inherit; }
  :global(button), :global(select) { color: inherit; }
  :global(button:focus-visible), :global(input:focus-visible), :global(textarea:focus-visible), :global(select:focus-visible) { outline: 2px solid #c8a65a; outline-offset: 2px; }
  :global(::selection) { color: #18150d; background: #d5b566; }

  .app-shell { display: grid; grid-template-columns: 390px minmax(0, 1fr); width: 100vw; height: 100vh; background: radial-gradient(circle at 80% -20%, #26332d 0, transparent 40%), #0c100e; }
  .sidebar { display: flex; flex-direction: column; min-height: 0; border-right: 1px solid #2c332e; background: #101412f2; box-shadow: 1rem 0 4rem #0004; }
  .brand { display: grid; grid-template-columns: auto 1fr auto; align-items: center; gap: .8rem; padding: 1.25rem 1.25rem 1rem; }
  .brand h1 { margin: .1rem 0 0; font-family: Georgia, serif; font-size: 1.6rem; font-weight: 500; letter-spacing: -.025em; }
  .eyebrow { margin: 0; color: #bda35f; font-size: .64rem; font-weight: 700; letter-spacing: .15em; text-transform: uppercase; }
  .brand select { align-self: start; border: 1px solid #3c443e; border-radius: .4rem; padding: .32rem .45rem; color: #bac2bc; background: #171c19; font-size: .7rem; }
  .mark { display: grid; place-items: center; width: 4rem; height: 4rem; transform: rotate(45deg); border: 1px solid #9b8144; border-radius: .35rem; background: linear-gradient(145deg, #2e3127, #151b17); box-shadow: inset 0 0 0 .28rem #0e120f, 0 1rem 3rem #0007; }
  .mark span { width: 1.15rem; height: 1.15rem; border: 2px solid #d2b96f; transform: rotate(45deg); }
  .mark.small { width: 2rem; height: 2rem; border-radius: .2rem; box-shadow: inset 0 0 0 .18rem #0e120f; }
  .mark.small span { width: .55rem; height: .55rem; border-width: 1px; }

  .workspace-card { margin: .25rem 1rem 1rem; border: 1px solid #303832; border-radius: .7rem; padding: .8rem; background: linear-gradient(135deg, #1a201c, #141915); }
  .workspace-title { display: grid; grid-template-columns: auto 1fr auto; align-items: center; gap: .65rem; }
  .workspace-title div { display: grid; gap: .08rem; min-width: 0; }
  .workspace-title strong { overflow: hidden; color: #f4f1e8; font-size: .82rem; text-overflow: ellipsis; white-space: nowrap; }
  .workspace-title span, .workspace-card > p { color: #818b84; font-size: .68rem; }
  .workspace-card > p { overflow: hidden; margin: .6rem 0 0 1.15rem; text-overflow: ellipsis; white-space: nowrap; }
  .status-dot { width: .5rem; height: .5rem; border-radius: 50%; background: #65b886; box-shadow: 0 0 .6rem #65b88688; }
  .status-dot.warning { background: #d4a95a; box-shadow: 0 0 .6rem #d4a95a88; }
  .icon-button { display: grid; place-items: center; width: 1.8rem; height: 1.8rem; border: 0; border-radius: .35rem; color: #9da69f; background: transparent; cursor: pointer; }
  .icon-button:hover { color: #f2e5be; background: #2a312c; }

  .locale-overview { display: flex; gap: .5rem; padding: 0 1rem 1rem; overflow-x: auto; scrollbar-width: thin; }
  .locale-card { position: relative; display: grid; flex: 1 0 108px; grid-template-columns: auto 1fr auto; gap: .45rem; min-width: 108px; border: 1px solid #2d3530; border-radius: .55rem; padding: .6rem .6rem .7rem; text-align: left; background: #141916; cursor: pointer; overflow: hidden; }
  .locale-card:hover, .locale-card.active { border-color: #786a43; background: #1b211c; }
  .locale-code { display: grid; place-items: center; width: 1.5rem; height: 1.5rem; border-radius: 50%; color: #d8c68d; background: #313127; font-size: .57rem; font-weight: 800; text-transform: uppercase; }
  .locale-copy { display: grid; min-width: 0; }
  .locale-copy strong { overflow: hidden; font-size: .66rem; text-overflow: ellipsis; white-space: nowrap; }
  .locale-copy span { color: #727c75; font-size: .54rem; white-space: nowrap; }
  .locale-percent { color: #929c95; font-family: ui-monospace, monospace; font-size: .56rem; }
  .progress { position: absolute; right: 0; bottom: 0; left: 0; height: 2px; background: #29302b; }
  .progress span { display: block; height: 100%; background: #ba9d56; }

  .search-box { display: grid; grid-template-columns: auto 1fr auto; align-items: center; gap: .5rem; margin: 0 1rem .65rem; border: 1px solid #313a34; border-radius: .55rem; padding: .55rem .65rem; background: #0e1210; }
  .search-box:focus-within { border-color: #8e7845; box-shadow: 0 0 0 2px #9a81451c; }
  .search-box > span { color: #778079; font-size: 1.1rem; }
  .search-box input { min-width: 0; border: 0; outline: 0; color: #e5eae6; background: transparent; font-size: .76rem; }
  .search-box input::placeholder { color: #626b65; }
  kbd { border: 1px solid #39413c; border-bottom-color: #4c5650; border-radius: .25rem; padding: .12rem .3rem; color: #778079; background: #191f1b; font: .55rem ui-monospace, monospace; }
  .filters { display: flex; gap: .25rem; padding: 0 1rem .7rem; border-bottom: 1px solid #262d29; }
  .filters button { display: flex; align-items: center; gap: .35rem; border: 0; border-radius: .4rem; padding: .38rem .5rem; color: #79837c; background: transparent; font-size: .66rem; cursor: pointer; }
  .filters button:hover, .filters button.active { color: #e3d8b9; background: #292b22; }
  .filters button span { min-width: 1.1rem; border-radius: .5rem; padding: .05rem .25rem; color: #8d968f; background: #222824; font-size: .54rem; text-align: center; }
  .message-list { flex: 1; min-height: 0; padding: .4rem .55rem 1rem; overflow-y: auto; scrollbar-color: #3a433d transparent; }
  .message { display: grid; grid-template-columns: auto minmax(0, 1fr) auto; align-items: center; gap: .65rem; width: 100%; border: 1px solid transparent; border-radius: .5rem; padding: .62rem .7rem; text-align: left; background: transparent; cursor: pointer; }
  .message:hover { background: #171d19; }
  .message.active { border-color: #4c4934; background: linear-gradient(90deg, #25261d, #1b211c); box-shadow: inset 2px 0 #c0a45e; }
  .translation-state { width: .42rem; height: .42rem; border-radius: 50%; background: #629c74; }
  .translation-state.missing { border: 1px solid #a06055; background: transparent; }
  .translation-state.structured { background: #b99b52; }
  .message-copy { display: grid; gap: .16rem; min-width: 0; }
  .message-copy strong { overflow: hidden; color: #cbd2cd; font: .68rem ui-monospace, "SFMono-Regular", monospace; text-overflow: ellipsis; white-space: nowrap; }
  .message-copy span { overflow: hidden; color: #68726c; font-size: .64rem; text-overflow: ellipsis; white-space: nowrap; }
  .message.active .message-copy strong { color: #f0e6ca; }
  .structure-badge { border: 1px solid #5d5436; border-radius: .25rem; padding: .12rem .22rem; color: #bfa867; font: .48rem ui-monospace, monospace; }
  .empty-list { display: grid; place-items: center; gap: .4rem; padding: 3rem 1rem; color: #5f6862; text-align: center; }
  .empty-list span { font-size: 2rem; color: #766a46; }
  .empty-list p { margin: 0; font-size: .72rem; }

  .editor-shell { display: flex; flex-direction: column; min-width: 0; min-height: 0; }
  .editor-toolbar { display: flex; align-items: center; justify-content: space-between; gap: 1rem; min-height: 4.65rem; border-bottom: 1px solid #2c332e; padding: 0 1.6rem; background: #111613d9; backdrop-filter: blur(16px); }
  .locale-tabs { display: flex; align-self: stretch; gap: .25rem; overflow-x: auto; }
  .locale-tabs button { position: relative; display: flex; align-items: center; gap: .5rem; border: 0; padding: 0 .8rem; color: #7f8982; background: transparent; font-size: .7rem; cursor: pointer; }
  .locale-tabs button::after { position: absolute; right: .8rem; bottom: 0; left: .8rem; height: 2px; background: transparent; content: ""; }
  .locale-tabs button:hover, .locale-tabs button.active { color: #ede6d2; }
  .locale-tabs button.active::after { background: #bea157; box-shadow: 0 -2px .65rem #bea15766; }
  .locale-tabs button > span { display: grid; place-items: center; width: 1.75rem; height: 1.3rem; border-radius: .25rem; color: #c6b577; background: #2e3026; font-size: .54rem; font-weight: 800; }
  .locale-tabs i { border-radius: .25rem; padding: .12rem .25rem; color: #789782; background: #1d2a21; font-size: .48rem; font-style: normal; text-transform: uppercase; }
  .toolbar-actions { display: flex; align-items: center; gap: 1rem; }
  .save-state { display: flex; align-items: center; gap: .42rem; color: #6f7972; font-size: .65rem; white-space: nowrap; }
  .save-state > span { width: .42rem; height: .42rem; border-radius: 50%; background: #5b7e66; }
  .save-state.dirty { color: #b8a66f; }
  .save-state.dirty > span { background: #ca9f4e; box-shadow: 0 0 .5rem #ca9f4e77; }
  .primary { border: 1px solid #d0b460; border-radius: .45rem; padding: .58rem .85rem; color: #1a170e; background: linear-gradient(#d6bd70, #b9984f); font-weight: 750; cursor: pointer; box-shadow: 0 .35rem 1rem #0005; }
  .primary:hover:not(:disabled) { filter: brightness(1.08); }
  .primary:disabled { cursor: not-allowed; filter: grayscale(.5); opacity: .42; }
  .save-button { display: flex; align-items: center; gap: .5rem; font-size: .68rem; }
  .save-button > span { font-size: .85rem; font-weight: 900; }
  .save-button kbd { border-color: #8f783e; color: #5e4e28; background: #d9c47f; }

  .editor-content { flex: 1; min-height: 0; padding: 2rem clamp(2rem, 5vw, 5rem) 3rem; overflow-y: auto; scrollbar-color: #3b443e transparent; }
  .message-heading { display: flex; align-items: flex-start; justify-content: space-between; gap: 2rem; max-width: 1000px; margin: 0 auto 1.6rem; }
  .breadcrumb { display: flex; gap: .4rem; margin-bottom: .55rem; color: #7f8b83; font: .62rem ui-monospace, monospace; }
  .breadcrumb span:nth-child(even) { color: #454e48; }
  .message-heading h2 { margin: 0; color: #f0eee5; font-family: Georgia, serif; font-size: clamp(2rem, 3.2vw, 3rem); font-weight: 500; letter-spacing: -.035em; }
  .message-heading p { max-width: 46rem; margin: .55rem 0 0; color: #8d978f; font-size: .75rem; line-height: 1.55; }
  .tags { display: flex; flex-wrap: wrap; gap: .35rem; margin-top: .7rem; }
  .tags span { border: 1px solid #3d453f; border-radius: 1rem; padding: .18rem .45rem; color: #89948c; background: #171c19; font-size: .56rem; }
  .message-facts { display: flex; flex-wrap: wrap; justify-content: flex-end; gap: .35rem; padding-top: .4rem; }
  .message-facts span { border: 1px solid #333b36; border-radius: .3rem; padding: .25rem .45rem; color: #879188; background: #151a17; font: .56rem ui-monospace, monospace; }
  .message-facts span.fallback { border-color: #5b4d35; color: #bd9d61; }
  .mode-tabs { display: flex; gap: .2rem; max-width: 1000px; margin: 0 auto; border-bottom: 1px solid #303732; }
  .mode-tabs button { position: relative; border: 0; padding: .65rem .8rem; color: #747f77; background: transparent; font-size: .68rem; cursor: pointer; }
  .mode-tabs button::after { position: absolute; right: .7rem; bottom: -1px; left: .7rem; height: 2px; background: transparent; content: ""; }
  .mode-tabs button:hover, .mode-tabs button.active { color: #e8dec1; }
  .mode-tabs button.active::after { background: #b99d57; }
  .writing-area { max-width: 1000px; margin: 1.1rem auto 0; }
  .field-label { display: flex; justify-content: space-between; margin-bottom: .45rem; }
  .field-label label { color: #aeb7b0; font-size: .68rem; font-weight: 650; }
  .field-label span { color: #5e6861; font-size: .58rem; }
  textarea { display: block; width: 100%; min-height: 240px; resize: vertical; border: 1px solid #38413b; border-radius: .65rem; outline: 0; padding: 1.2rem 1.3rem; color: #eef1ed; background: #111613; font-size: 1rem; line-height: 1.7; caret-color: #d4b760; box-shadow: inset 0 1px .25rem #0006, 0 1rem 4rem #0002; }
  textarea:focus { border-color: #8c7847; box-shadow: 0 0 0 3px #ad914426, inset 0 1px .25rem #0006; }
  textarea.code { min-height: 380px; tab-size: 2; color: #d8dfda; font: .78rem/1.65 "SFMono-Regular", Consolas, monospace; white-space: pre; }
  textarea.invalid { border-color: #955b52; box-shadow: 0 0 0 2px #955b5222; }
  .editor-hints { display: flex; justify-content: space-between; gap: 1rem; padding: .65rem .2rem; color: #66716a; font-size: .62rem; }
  .editor-hints code { border-radius: .2rem; padding: .08rem .25rem; color: #a99d7a; background: #1b211d; font: .58rem ui-monospace, monospace; }
  .editor-hints button { border: 0; padding: 0; color: #b69d61; background: transparent; font-size: .62rem; cursor: pointer; }
  .validation-panel { max-width: 1000px; margin: 1.7rem auto 0; border: 1px solid #303833; border-radius: .65rem; background: #111613; overflow: hidden; }
  .validation-panel > header { display: flex; align-items: center; justify-content: space-between; gap: 1rem; padding: .9rem 1rem; }
  .validation-panel > header > div { display: flex; align-items: center; gap: .7rem; }
  .validation-panel > header > div > div { display: grid; gap: .13rem; }
  .validation-panel strong { color: #cfd6d1; font-size: .68rem; }
  .validation-panel header span { color: #707b73; font-size: .57rem; }
  .validation-icon { display: grid; place-items: center; width: 1.65rem; height: 1.65rem; border-radius: 50%; font-size: .7rem; font-weight: 800; }
  .validation-icon.valid { color: #81c394; background: #203528; }
  .validation-icon.error { color: #e18d80; background: #3b2421; }
  .validation-icon.busy { color: #d7ba6c; background: #3a3321; animation: pulse 1s infinite alternate; }
  .compiler-badge { border: 1px solid #38413b; border-radius: .3rem; padding: .25rem .4rem; font-family: ui-monospace, monospace; }
  .client-error { margin: 0; border-top: 1px solid #4b2f2b; padding: .75rem 1rem; color: #e7a097; background: #2c1d1a; font-size: .68rem; }
  .diagnostics { border-top: 1px solid #2b322e; }
  .diagnostics button { display: grid; grid-template-columns: auto minmax(0, 1fr) auto; align-items: start; gap: .7rem; width: 100%; border: 0; border-bottom: 1px solid #252c28; padding: .7rem 1rem; text-align: left; background: #0e1210; cursor: pointer; }
  .diagnostics button:last-child { border-bottom: 0; }
  .diagnostics button:hover { background: #161b18; }
  .diagnostics button > span:first-child { display: grid; place-items: center; width: 1.15rem; height: 1.15rem; border-radius: 50%; font-size: .65rem; }
  .diagnostics .error { color: #e28b7f; background: #3b2521; }
  .diagnostics .warning { color: #d8b35d; background: #3b3320; }
  .diagnostics button > span:nth-child(2) { color: #9ca69f; font-size: .63rem; line-height: 1.45; }
  .diagnostics button strong { margin-right: .45rem; color: #d0d7d2; font-family: ui-monospace, monospace; }
  .diagnostics code { color: #606a63; font: .55rem ui-monospace, monospace; white-space: nowrap; }
  .no-selection, .loading-shell, .fatal-shell { display: grid; place-content: center; place-items: center; height: 100vh; padding: 2rem; color: #8d978f; text-align: center; background: radial-gradient(circle at center, #1d2822, #0b0e0d 65%); }
  .no-selection { height: auto; flex: 1; background: transparent; }
  .no-selection > span { color: #837347; font-size: 3rem; }
  .no-selection h2 { font-family: Georgia, serif; font-weight: 500; }
  .loading-shell { gap: 1.5rem; }
  .loading-shell p { margin: 0; color: #a99150; font-size: .65rem; font-weight: 700; letter-spacing: .18em; text-transform: uppercase; }
  .loading-line { width: 12rem; height: 1px; background: linear-gradient(90deg, transparent, #b89b51, transparent); animation: pulse .8s infinite alternate; }
  .fatal-shell { max-width: none; }
  .fatal-shell .mark { margin-bottom: 2rem; }
  .fatal-shell h1 { max-width: 36rem; margin: .8rem 0; color: #eee9da; font-family: Georgia, serif; font-size: 2.6rem; font-weight: 500; }
  .fatal-shell > p:not(.eyebrow) { max-width: 44rem; margin: 0 0 1.5rem; }
  @keyframes pulse { to { opacity: .35; } }
  @media (max-width: 1100px) {
    .app-shell { grid-template-columns: 335px minmax(0, 1fr); }
    .editor-content { padding-inline: 2rem; }
    .locale-copy { display: none; }
    .locale-card { flex-basis: 70px; grid-template-columns: auto 1fr; }
  }
</style>
