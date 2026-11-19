# Incremental catalog validation

Recorded 2026-11-19 for tr#271 (C, D077.10), before implementation. The language server, the Translations Editor and the
command line validate a whole catalog. At 20,000 messages × 10 locales one edit to one file must not recompile the
other 199 files. This plan says what a compile does per file, per message and across files, and how a validation
session reuses each part.

## Goals

- A recheck after an edit to one file costs time in proportion to that file and the messages it touches, plus a
  linear pass over cached results that is small next to a compile.
- **Exactly the full result.** The diagnostics and the compiled project are the same, value for value and in the same
  order, as `TranslationCompiler.CompileRmf2ProjectV5` of the same sources. A randomized differential test checks this
  after random edit sequences.
- **One rule implementation.** The incremental path runs the same compile code, which consults a cache. A rule added
  later needs no second, incremental version.
- **Easy to call from a cancelling worker.** Every call takes a `CancellationToken`. A cancelled call leaves the cache
  valid, and every result is an immutable snapshot that is safe to read from any thread.

## What a compile does

| Stage | Inputs | Scope |
| --- | --- | --- |
| Read runic.json: the manifest, locales, validation policies, markup contracts, source roots | runic.json, options | project |
| Read an `.rmf2` / `.mf2` file: the outline, comments, properties, the MF2 syntax of every message, the byte maps | file bytes, size limits | **file** |
| Extract the file's messages: locale, mount prefix, logical path, canonical key | file, manifest (source roots) | **file** |
| Register the paths: duplicate declarations, message/group collisions, case-only aliases, underscore-key collisions (RTR0052, RTR0054, RTR0018) | every file's paths | cross-file, global |
| Lower a base-locale message: types, inputs, selectors, variants | message syntax, size limits | **message** |
| Lower a translation | message syntax, the base message's inputs | message + its key's base message |
| Per-message checks: metadata (RTR0051), plural categories (RTR0094/95), formatted booleans (RTR0097), empty variants (RTR0021), markup links (RTR0061) | lowered message, manifest policies and markup | **message** |
| Document profile: content kind, skeletons (RTR0070, RTR0072) | linked message, the base message's kind | message + its key's base message |
| Missing (RTR0010) and extra (RTR0011, with a suggestion from every base key) translations | the keys of every locale | cross-file, per key, with a global suggestion list |
| Pair checks: caller contract (RTR0016), slots (RTR0062), structure (RTR0074, RTR0071), input parity (RTR0096), maximum length (RTR0051, RTR0098) | base entry, translated entry, the key's slot requirements | **pair** of messages of one key |
| Contracts: slot requirements, markup names, readable C# names (RTR0069 warnings) | every locale's entry of one key | **key** |
| Readable reference names, `markup.slots` constraints | every contract | cross-file, global |
| Locales: fallback resolution, the content locale of each resolved resource (RTR0031, RTR0102) | every locale's entries | cross-file, per locale |
| Caller contract, markup contract export, source hash | every contract, every source | global |

## Design

A `Rmf2CompileCache` (compiler, internal) holds the reusable results. The compile takes it as an optional argument and
uses it at each stage:

1. **Files.** A source unit (`Rmf2SourceUnitV5`) per path holds the parse of the file. It is reused while the path, the
   bytes and the size limits are the same; otherwise it is replaced. Its byte comparison is a span comparison, without
   copies.
2. **Extraction.** The unit keeps its extracted messages (`Rmf2ProjectSourceV5`) per *link context* (below), so the
   objects are the same from one compile to the next.
3. **Messages.** The unit memoizes, per extracted message and context, the result of lowering and of the per-message
   checks: the entry and the diagnostics it reported. A translation's memo key adds the signature of the caller
   inputs it was lowered against, and its document profile adds the expected content kind. A replayed memo adds its
   diagnostics at the point of the compile where the computation would have added them, so the order of the
   diagnostics is unchanged.
4. **Pairs.** The pair checks of a base entry and a translated entry are memoized by the identity of the two entries,
   which (2) and (3) keep stable while neither message changes.
5. **Keys.** The contract of a key (its slot requirements, markup names, readable-name check and the RTR0010 reports of
   locales that lack it) is memoized by the identities of the key's entries in every locale.
6. **Global work** runs every time, over the cached objects: path registration, the key lists, locale resolution, the
   contract exports and the hashes. It has no parsing or lowering. Per link context, the session also keeps what the
   last compile built (`Rmf2LinkState`), so this pass is proportional to the edit wherever it can be:
   - a compile whose units and context are those of the last compile returns the last result;
   - a locale's entries are a hash table enumerated in the last compile's key order while its keys are the same,
     and fallback resolution merges sorted key lists (nothing for a complete translation);
   - the caller and markup contract exports are written from each message's JSON, which is reused while the message's
     contract (memoized per key) and content locales are unchanged; the readable reference names are reused while
     the contracts are the same.

**Link context.** Everything that runic.json and the options decide: the bytes of runic.json, the size limits, relaxed
and base-locale-only validation, and pseudo-locale generation. A memo of another context is never used. A change to
runic.json therefore relinks every message, but it reuses every parse.

**Review compiles.** The relaxed and base-locale-only compiles (`TranslationReviewFile.ForReview`, after a full compile
that failed) differ from a full compile only where a validation policy is read: every policy reads as `allow`, and the
base-locale-only one leaves out the translation files. They keep their own link state (2 to 6 above), but share the full
compile's extracted messages and memos (the link context without relaxed validation), so they neither extract the
catalog again nor keep a second copy of it:
- the memos whose checks read a policy (a message's compile, a pair's checks) record whether their compile was relaxed,
  and their diagnostics without the policies' (`AddPolicyDiagnostic` marks them). A relaxed compile replays a full
  compile's memo without those; a full compile does not reuse a relaxed one. The other memos read no policy.
- a relaxed compile of the files of a full compile that failed without any policy diagnostic has that compile's
  result: each policy read reports a diagnostic or skips it, so with none reported, both compiles report the same
  diagnostics and fail at the same point.

**Dependencies** are not tracked by hand. Each memo is keyed by the identities of the objects it was computed from. A
changed file gets a new unit, so its messages get new entries. Every pair and key memo that involved them misses, and
every other memo hits. A changed base message changes its inputs, so its translations' memo keys miss. Their files
keep their parses.

**Invalidation and memory.** A unit is dropped when its path leaves the source set or its bytes change. Its memos and
the pair memos keyed by its entries go with it: they are held by the unit, or weakly by the entry. Key memos are kept
for the latest compile only. A context change clears the message, pair and key memos.

## API (Authoring, internal; visible to the language server and the editor)

```csharp
var session = new TranslationValidationSession();               // one per project in a host
TranslationValidationSnapshot snapshot = session.Validate(project, sources, cancellationToken);
snapshot.Compilation;     // Rmf2ProjectCompilation, equal to a full compile of the same sources
snapshot.Diagnostics;     // = Compilation.Diagnostics
snapshot.DiagnosticsByPath;
snapshot.ChangedPaths;    // sources added, removed or changed since the session's previous snapshot
snapshot.Statistics;      // files read, messages lowered and reused, for tests and telemetry
```

- `Validate` may run on any thread, and two calls may overlap. The cache is thread-safe, and each call builds its own
  snapshot.
- The `CancellationToken` is checked between files and between messages. When it is cancelled, `Validate` throws
  `OperationCanceledException`, and only completed memos remain in the cache.
- `Rmf2Workspace.Validate()` uses the session of its `Rmf2WorkspaceCache`. The language server and the editor gain the
  reuse without code changes, and can call the session directly for the review compilations (relaxed and base-locale
  only), which reuse the same file units.

## Proof

- **Differential test.** Random catalogs (several locales, nested groups, plurals, markup, metadata, comments, missing
  and extra keys, errors), then random edit sequences: change, insert or delete messages, break and repair syntax,
  add or remove files, edit runic.json. After every step it asserts that the session's diagnostics, their order and
  the compiled project are equal to a fresh full compile of the same sources.
- The generated-catalog comparison of #271 (`validate` diagnostics and `generate` output before and after) still holds.
