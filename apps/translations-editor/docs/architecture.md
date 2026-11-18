# Translations Editor architecture

Runic Translations Editor is a workspace application for the same translation
sources that application builds consume. It does not define a second message
language, schema, or validation path.

## Source and semantic authority

Each workspace has one `runic.json` declaration. The editor infers direct `.mf2`
or grouped `.rmf2` sources and loads, validates, previews, and saves through the
compiler-backed authoring model. A normal text editor remains a valid way to edit
those sources.

```text
translations/                 # direct MF2 messages
  runic.json
  en/
    application_title.mf2
  de/
    application_title.mf2

translations/                 # grouped RMF2 resources
  runic.json
  en.rmf2
  de.rmf2

translations/                 # grouped RMF2 resources
  runic.json
  en.rmf2
  checkout/
    de.rmf2
```

RMF2 files are recursively discovered as `{locale}.rmf2`; directory segments and
explicit groups form their logical namespace. Explicit `sourceRoots` can mount
feature-local resources. The editor uses the compiler's RMF2 source model and
diagnostics, including the resource/markup contracts that apply to that layout.
The editor uses the compiler's v5 project carrier. Its internal interchange
projection normalizes the typed result only for closed text interchange. It
never converts a project into a retired catalog carrier, which would lose direct-resource
identity, raw syntax, caller contracts, and freshness information.

Discovery is reconciled from the configured roots on every load/check, so
mounted add, change, delete, and rename events are handled as membership
changes. Desktop and IDE hosts must open a common containing workspace when a
mount is outside the configuration directory; no host silently follows a
symlink or reparse point into an unconfigured tree. CLI and MSBuild perform the
same bounded rescan on each invocation/build.

The editor owns workspace navigation, drafts, review state, interchange, and
atomic workspace mutations. The compiler owns message semantics, diagnostics,
generated APIs, and build-time validation. Review state is stored separately from
authored translation sources.

The editor checks revisions before replacing files, watches for external changes,
and can recover interrupted workspace transactions. Direct saves preserve a
translator's MF2 formatting: determinism means that an identical operation on
identical starting bytes has identical output, not that every message is
reformatted.

## User interfaces

The Svelte frontend renders the editor workflow. A generated Runic Views
Window connects typed TypeScript calls to an application-scoped `EditorViewModel`
and `EditorSession` through the CS-WebUI host. The host serves the frontend and owns local application state, diagnostics, and filesystem-facing
workspace operations. The browser profile does not hold durable editor state;
preferences, recent projects, and recovery copies of unsaved text and review
changes live in one native per-user
record. Clearing that record does not change workspace files or in-memory work.

The root ViewModel publishes stable routed ViewModels for workspace, document
tools, review, interchange, diagnostics, local state, and project setup, plus a
collection of `EditorDocumentViewModel` instances. Each feature owns named
ReactiveUI commands and its latest typed result. The generated web routes
carry correlated command responses to the typed TypeScript editor bridge.
Selecting a document reads its path, content, and file revision. Validation
and save run on that document's generated commands; `EditorSession` checks the
file revision before atomic replacement. The active input draft and unsaved
review edits stay in the browser UI so they can be discarded or recovered
without changing the shared workspace. Persisted review state and workspace
mutations remain in the compiler-backed session.

The page (`Frontend/src/routes/+page.svelte`) is the composition root. It
creates the stores and controllers of `Frontend/src/lib/state/`, wires them to
each other and to the bridge, derives what blocks the history and the editor
while an operation runs, binds the keyboard shortcuts and the command palette,
and lays out the screens and dialogs. Behaviour lives in the state modules;
layout lives in the components, which take stores and controllers as props
instead of deriving state again.

The stores own the unsaved work and the host's answers:

- `DraftStore` holds each document's draft, its generation, the compiler's
  parsed entries and the recovery copy. A draft is kept only while it differs
  from the saved text. Composer edits, authoring operations, raw-source
  validation and quick fixes run through its one queue per document; an answer
  is accepted only for the current workspace, draft generation and latest
  request of its kind. Its `DraftHistory` keeps the undo and redo steps of the
  unsaved edits per document: each committed draft change records the
  document's state before and after it (draft, the compiler's entries, starter
  placements and the starters of `MessageDraftStore`); typing in one field
  extends the latest step until a pause, the stacks are capped by steps and
  bytes, and a change of the saved text (a save, an undone saved change)
  clears them. Undo restores the earlier state, so a draft equal to the saved
  text is dropped and the document is clean again.
- `MessageDraftStore` holds the in-memory starters of missing translations
  and places them into their document's draft once they have text.
- `ReviewStore` holds review states, notes and terminology with the saved data
  the edits started from; it is dirty only while they differ from that data. It
  saves through the bridge against that revision, rebases the edits when
  another writer changes the review file, and keeps the changed entries with
  their starting point as a recovery copy, which a later session rebases onto
  the review file in the same way.
- `SaveCoordinator` is the one save behind Ctrl+S and the toolbar: it owns the
  unsaved summary (documents with unsaved text, unsaved review changes) and
  `saveAll()`, which validates every unsaved document, writes them, reloads the
  workspace once and then saves the review changes.
- `DiagnosticsStore` keeps validation results per request and document. The
  panel lists the latest one, or the workspace's diagnostics before any; it also
  offers per-message and errors-first selectors.
- `PreviewController` owns the preview samples, the requested formatting locale
  and time zone, the debounced compiler requests and the pseudo-locale result.

The controllers own the page's logic, each with explicit inputs (the bridge
methods it calls, the stores it changes, and getters for the snapshot and the
blocking flags):

- `WorkspaceController` holds the open snapshot, loads and opens workspaces
  (with the unsaved-work confirmation), installs a snapshot into the stores,
  keeps the recent projects and runs the one save's notices.
  `RecoveryController` completes or rolls back a pending transaction and
  restores or discards the work an earlier session left;
  `ExternalChangesController` polls for changed files and merges one;
  `RepairController` edits, validates and saves a malformed file;
  `UnsavedWork` says what is unsaved and confirms dropping it.
- `SelectionController` holds the selected message and locale, the editor mode
  and text, and the list's filter and search, with the per-catalog views and
  the selection stored for the next launch. `configureEditor` shows a
  selection: it opens the document, sets the samples, starts the preview and
  opens a missing translation's starter, in that order. Every navigation
  (selecting, an undone step, a diagnostic, a reload) goes through it.
- `MessageEditingController` routes the edits of the selected message (file
  view, composer, authoring operations, "Copy source", quick fixes) to the
  starter or the document's draft, and navigates from a diagnostic to its span.
- `HistoryController` runs Undo and Redo one after another: the selected
  document's unsaved step first, else the host's saved-change history.
- `WorkspaceView` derives what the catalog shows for the selected locale
  (coverage, quality findings, the message list and its filters, the selected
  message's review) and applies the review actions.
- `PreferencesController` keeps the interface locale, theme and simulations;
  `ProjectWizardController`, `MutationController`, `InterchangeController`,
  `LibraryController` and `AboutController` own their dialogs' state and host
  calls; `EditorFeedback` holds the error and operation notices.

The stores and controllers take the bridge and the current snapshot as
arguments, so the frontend checks exercise them without a host.

The components are split by screen and area. `Frontend/src/lib/screens/` holds
the start screens (`LoadingScreen`, `FatalScreen`,
`TransactionRecoveryScreen`, `WelcomeScreen`), the `WorkspaceNotices` about
files on disk, and the workspace: `WorkspaceShell` lays out the sidebar
(`WorkspaceSidebar`) and the inset with the toolbar, the
`MessageEditorPane` (heading, review workflow, mode switch, translation
editor), the `PreviewSection` (the rendered preview and the artifact panel)
and the `ValidationArea`. `Frontend/src/lib/dialogs/` holds one component per
dialog: project wizard, structural changes (`MutationDialog`), repair, open
project, external change, About and diagnostics, terminology and quality
report; the interchange and library dialogs are `InterchangeDialog` and
`LibraryCatalogDialog` in `Frontend/src/lib/`. A layout change stays in these
components.

On the host, `EditorSession` is the façade behind the bridge: it adds the undo
history of saved changes (which Undo and Redo use once no unsaved step is
left), confirmation tokens for destructive and import operations, and
workspace transactions. It calls one `EditorWorkspace`, a thin coordinator that
owns the workspace gate and composes the services in `Workspace/`:

- `WorkspaceGate` is the one lock and the disposed flag. Every service takes
  this same gate around each of its operations, so workspace operations run one
  at a time. A service holding the gate calls only ungated members of the
  others (for example the interchange commit reloads through
  `WorkspaceLoader.LoadCoreAsync`), never another gated operation.
- `WorkspaceFiles` holds the root and the path rules: containment and link
  checks, the selected `runic.json` and project discovery, source enumeration,
  atomic writes, and the byte, revision and path helpers the others share.
- `WorkspaceChangeTracker` owns the file system watcher and the external-change
  baseline (the revision of each source the editor last read or wrote) and
  reconciles them with the disk in `CheckExternalChangesAsync`.
- `WorkspaceStateReader` reads the project's files, optionally with one unsaved
  document in place of its file, and compiles them. It caches the last
  compilation and its pseudo-locale compilations by sources revision, records
  the open catalog's ID and resets the change baseline on a full read.
- `WorkspaceLoader` builds the `WorkspaceSnapshot` (catalog, documents,
  diagnostics, review data, status, library catalogs) and the locale context.
- `DiagnosticProjector` turns compiler and review-file diagnostics into the
  contract's diagnostics with spans, quick fixes and their message row;
  `EntryProjector` turns a document into message entries with the composer
  projection, context and length limits.
- `DocumentEditService` runs the draft transforms, which write nothing: text
  and authoring operations, starter drafts of missing messages, quick fixes and
  validation. `PreviewService` renders one message, also in a generated
  pseudo-locale, and picks its primary diagnostic. `DocumentSaveService` writes
  one document atomically against its expected revision.
- `ReviewService` owns the review file: saving and deleting review data, the
  revision the frontend has, and the entries other operations update (starter
  drafts of an added locale, drafts a save promotes to translated, a removed
  locale's entries).
- `InterchangeService` exports and imports XLIFF and review JSON; an import is a
  preview the session confirms and a commit that checks nothing changed since.
- `MutationPlanner` plans structural changes as workspace transactions: RMF2
  key creation, rename, duplicate and delete, locale and fallback changes, and
  `.resx`/i18next catalog imports.

Headless commands use the same workspace and compiler path as the interface:

- `validate` reports compiler-backed workspace diagnostics;
- `export`, `report`, and `import --apply` support XLIFF and review JSON
  interchange, with `report` providing the read-only import preview; and
- `diagnostics` creates a local diagnostic bundle and never uploads it.

The XLIFF 2.1 implementation is a deterministic closed plain-text profile.
Direct plain resources round-trip; declarations, expressions, selectors, and
markup appear as explicit semantic-loss entries and are refused on text-profile
import. A review approval uses the closed-text-profile fingerprint, separately
from source freshness, so a compatible review state cannot hide a stale catalog.
Imports and structural transactions write only declared translation resources
and editor review state. They never edit application call sites or convert an
RMF2 project into direct `.mf2` files.

For rich preview, the editor host renders typed v5 requests through the
verified .NET artifact/pack path. The Views command returns validated semantic runs;
the Svelte frontend treats links, actions, icons, and custom markup as inert
display data. It does not load application renderers, execute callbacks, or
coerce AST 5 into a JavaScript evaluator.

## Formats and language tooling

Grouped RMF2 resources implement a bounded execution profile; that is not a
claim of full Unicode MF2 conformance. The [RMF2 implementation guide](../../../docs/guides/translations/rmf2.md)
documents its grammar, resource discovery, inline-markup contracts, and current
limits. Direct MF2 sources remain supported as the alternate source
representation.

The editor does not maintain a separate translation language or semantic model.
Its RMF2 behavior follows the shared [compiler](../../../tools/Runic.Translations.Compiler/README.md)
and [authoring tools](../../../packages/dotnet/Runic.Translations.Authoring/README.md),
which also serve the CLI and IDE integrations.

General IDE language tooling likewise belongs on that shared semantic authority;
the editor is not an alternative language-server implementation.
