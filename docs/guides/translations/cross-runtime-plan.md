# Runic Text Resources cross-runtime plan

Status: implemented core; production hardening and expanded formatter/markup registries remain  
Last updated: 7 August 2026

## Implementation record

The repository now contains the shared compiler/AST boundary, deterministic v1
lowering, typed per-message ESM generation, a strict hashed web-module manifest,
CLI/MSBuild selection, an optional Vite adapter, schema v2 structured selectors
and variants, .NET/ESM v2 emission, a bounded transport contract and generated
decoder, and an opt-in C++20 feasibility backend. Cross-runtime execution tests run
generated ESM under Node and generated C++ under Clang; the original .NET, package,
and Native AOT verification remains in place.

The normalized AST v2 schema already reserves formatter-expression, local,
source-metadata, directionality, and structured-markup nodes. Authoring and runtime
execution currently cover strings, typed inputs, literal/cardinal/ordinal
selectors, ordered variants, and existing structured scalar formats. Relative-time
execution and structured markup results stay deliberately unimplemented until
their exact cross-runtime semantics and safe host rendering API are frozen; they
must not silently degrade to strings or trusted HTML.

## Decision

Runic Text Resources will be a Runic-owned, deterministic localization compiler
and portable message model with generated backends for .NET and TypeScript/ESM.
A C++ backend may follow after the portable message model is stable.

Runic Text Resources will not implement compatibility with Paraglide JS, inlang,
or their source formats, generated APIs, project models, or runtimes. Their useful
architectural ideas may inform this design, but Runic contracts and versioning
remain independent.

The product direction is:

> One canonical compiler and portable message AST, with target-specific generated
> code and small target runtimes.

The compiler remains implemented once in .NET. A future C++ backend means emitting
C++ headers and sources from the same canonical compiler IR; it does not require a
second compiler implementation in C++.

## Goals

- Preserve the existing deterministic source compiler, diagnostics, explicit
  fallback graph, fingerprints, Native AOT support, and immutable .NET runtime.
- Generate typed, framework-independent ESM message functions for browser and
  server-side JavaScript applications.
- Make unused browser messages removable through ordinary ESM tree-shaking.
- Keep compiled messages free of runtime JSON parsing and arbitrary format-string
  evaluation.
- Use one normalized message AST and one conformance corpus across all backends.
- Add selectors, variants, pluralization, structured formatting, and safe markup
  through a versioned schema and AST rather than backend-specific features.
- Support both build-time compiled localization and explicitly selected
  runtime-loaded locale catalogs.
- Provide a stable key-and-arguments transport contract when .NET sends a
  localizable result to a web frontend.
- Keep source, AST, artifact, and generated-code compatibility independently
  versioned.

## Non-goals

- Paraglide JS or inlang API, file-format, project, or output compatibility.
- A translation-management service or editor.
- Framework-specific message semantics.
- Automatic URL routing, cookie policy, navigation, or locale persistence in the
  core compiler/runtime.
- Runtime compilation of authoring JSON in production applications.
- Treating translated content as trusted HTML, JavaScript, CSS, URLs, or shell
  input.
- Reimplementing the canonical compiler separately in TypeScript or C++.

## Existing foundation

The current implementation already provides most of the required lower layers:

- strict catalog and resource schemas;
- a deterministic, UI-independent compiler kernel;
- canonical keys, locales, layers, fallback, and fingerprints;
- generated C# keys, typed accessors, catalog data, and registration;
- immutable .NET snapshots and transactional locale switching;
- resolved locale JSON artifacts;
- TypeScript key and argument declarations;
- template and asset manifests;
- compiler, runtime, build, package, and Native AOT conformance tests.

The cross-runtime work extends this architecture. It does not replace the existing
compiler or make the browser consume the .NET runtime model directly.

## Architecture

```text
Catalog manifest + resource documents
                  |
                  v
       Runic compiler and validation
                  |
                  v
       Canonical catalog/message IR
          /       |        |        \
         /        |        |         \
        v         v        v          v
 Generated C#  Generated  Resolved   Template and
 and .NET ABI  ESM ABI    locale     tooling metadata
                          artifacts
                              |
                              v
                       Dynamic catalog mode

Future:
Canonical catalog/message IR -> generated C++ headers and sources
```

All generators consume the same canonical IR. Source parsing, merge precedence,
fallback resolution, placeholder validation, selector validation, and diagnostic
behavior remain compiler-owned.

## Independent version selectors

The following versions evolve independently from package SemVer and from each
other:

| Selector | Purpose |
|---|---|
| `sourceSchemaVersion` | Human-authored catalog and resource JSON shape |
| `messageGrammarVersion` | Meaning of authored message expressions |
| `messageAstVersion` | Normalized portable AST contract |
| `localeArtifactVersion` | Runtime-loaded resolved locale payload |
| `templateManifestVersion` | Template/tooling metadata contract |
| `assetManifestVersion` | Generated asset inventory contract |
| `runtimeAbiVersion` | Generated C# and .NET runtime compatibility |
| `esmAbiVersion` | Generated ESM API and runtime compatibility |
| `cppAbiVersion` | Future generated C++ API/runtime compatibility |
| transport contract version | Cross-process key-and-arguments envelope |

An unsupported version fails explicitly. Writers emit exactly one documented
version. Existing version 1 schemas and artifacts are never extended in place
with incompatible meaning.

Source schema version 2 and message AST version 2 are related but not identical.
The source schema is optimized for human authoring. The AST is normalized for
validation, deterministic serialization, code generation, and cross-language
conformance. Multiple source schema versions may eventually lower to the same AST
version.

## Portable value model

The portable model must not contain .NET, JavaScript, or C++ type names. Its closed
input types are initially:

| Portable type | .NET API | ESM API | Transport representation |
|---|---|---|---|
| `string` | `string` | `string` | JSON string |
| `bool` | `bool` | `boolean` | JSON boolean |
| `int64` | `long` | validated `number` or `bigint` | canonical decimal string |
| `decimal` | `decimal` | validated `number` initially | canonical decimal string |
| `date` | `DateOnly` | ISO date string | `YYYY-MM-DD` string |
| `time` | `TimeOnly` | ISO time string | ISO time string |
| `instant` | `DateTimeOffset` | ISO instant string or `Date` adapter | UTC ISO string |
| `uuid` | `Guid` | canonical UUID string | canonical UUID string |

The ESM backend must document and validate numeric ranges. It must not silently
claim the full .NET `decimal` or `long` domain when JavaScript cannot represent a
value exactly. Exact cross-runtime decimal formatting may later require a bounded
decimal representation or an optional decimal adapter.

## Message AST

### Version 1 normalization

Before introducing new authoring features, existing grammar version 1 patterns
will lower to a small AST:

- `Message`
- `Pattern`
- `TextNode`
- `InputNode`
- typed input declarations derived from placeholder descriptors

Escaped braces are resolved during parsing. Backends never parse the original
pattern string.

This step creates a stable backend boundary and lets the first ESM backend ship
without waiting for schema version 2.

### Version 2 capabilities

Message AST version 2 adds:

- external input declarations;
- local declarations;
- formatter expressions;
- one or more selectors;
- ordered variants;
- literal and catch-all matches;
- cardinal and ordinal plural selection;
- structured number, date, time, and relative-time formatting;
- structured markup nodes and attributes;
- source metadata required for diagnostics and tooling;
- explicit directionality metadata where required;
- closed formatter and selector registries.

The compiler validates selector types, variant shapes, catch-all coverage,
formatter options, input use, and backend support before generation.

An illustrative normalized message is:

```json
{
  "astVersion": 2,
  "kind": "message",
  "inputs": {
    "count": { "type": "int64" },
    "folder": { "type": "string" }
  },
  "declarations": [
    {
      "name": "countPlural",
      "kind": "format",
      "function": "plural",
      "operand": { "kind": "input", "name": "count" },
      "options": { "type": "cardinal" }
    }
  ],
  "selectors": ["countPlural"],
  "variants": [
    {
      "matches": { "countPlural": "one" },
      "pattern": [
        { "kind": "text", "value": "One file was deleted from " },
        { "kind": "input", "name": "folder" },
        { "kind": "text", "value": "." }
      ]
    },
    {
      "matches": { "countPlural": "*" },
      "pattern": [
        {
          "kind": "format",
          "function": "number",
          "operand": { "kind": "input", "name": "count" },
          "options": { "useGrouping": true }
        },
        { "kind": "text", "value": " files were deleted from " },
        { "kind": "input", "name": "folder" },
        { "kind": "text", "value": "." }
      ]
    }
  ]
}
```

This is an AST example, not the final authoring syntax. Simple authored messages
remain strings in source schema version 2. Only messages needing variants,
formatters, or markup require a structured source form.

## Formatter and selector registry

The AST refers to stable semantic functions rather than arbitrary runtime format
strings. The initial registry should include:

- `string`;
- `integer`;
- `number`;
- `date`;
- `time`;
- `datetime`;
- `plural` with cardinal and ordinal modes;
- `relativeTime` after cross-runtime behavior is specified.

Every function defines:

- admitted operand types;
- allowed literal and variable options;
- normalized default options;
- whether it can format, select, or do both;
- result type;
- exact or semantic cross-runtime guarantee;
- backend support requirements.

Custom application functions are deferred. Adding them without a portable
registry would make messages non-portable and prevent complete validation.

## Generated ESM backend

### Compiled mode

Compiled ESM is the default web deployment mode. The compiler emits one
independently tree-shakable module per message plus a small shared runtime:

```text
generated/
  messages.js
  messages.d.ts
  runtime.js
  runtime.d.ts
  web-module-manifest-v1.json
  messages/
    Common_Save.js
    Files_Deleted.js
```

Each generated message function:

- has a typed argument object;
- supports an optional explicit locale override;
- contains or imports only that message's bundled locale implementations;
- consumes compiler-resolved fallback values;
- calls small generated/shared formatting helpers;
- returns plain text or, for future structured markup, a distinct structured
  result type;
- performs no authoring-pattern parsing.

An illustrative API is:

```ts
import { messages } from "virtual:runic-text-resources/app";

const text = messages.Files_Deleted(
  { count: 3, folder: "Archive" },
  { locale: "de" },
);
```

The final export layout must be selected by a tree-shaking spike. Static property
access, dotted source keys, generated identifier collisions, and dynamic lookup
must not accidentally retain the entire catalog.

### Runtime ownership

The generated ESM runtime owns only:

- declared and default locales;
- locale canonicalization and support checks;
- a configurable synchronous locale resolver;
- explicit per-call locale override;
- portable formatting helpers;
- optional branded `LocalizedString` typing.

URL localization, cookies, local storage, navigation, framework reactivity, and
request lifecycle are host concerns.

SSR integrations must provide request isolation. Explicit locale arguments are
always valid. A separate server adapter may provide `withLocale(locale, action)`
using the host runtime's asynchronous context support. A process-global mutable
locale is not an acceptable SSR default.

### Development and production layouts

The ESM backend may support two output layouts:

- message modules for production tree-shaking;
- locale modules for faster development with very large catalogs.

Both layouts implement the same public generated API. The build adapter may choose
a development layout, but production output defaults to message modules.

## Dynamic catalog mode

The existing resolved locale artifact remains useful and is retained as an
explicit alternative for:

- external or customer-supplied locale packs;
- translation updates without rebuilding the frontend;
- large locale counts;
- CDN-hosted resources;
- applications requiring unrestricted dynamic-key lookup.

Dynamic mode loads a versioned locale artifact and uses a small browser formatter.
It inherently gives up some tree-shaking and adds loading/parsing work. It must not
be silently combined with compiled mode. Consumers choose the topology in build
configuration.

## Cross-process text references

Static UI text is compiled into the frontend and is not sent by .NET. When a
backend-originated validation error, notification, command result, or domain event
needs localization in the frontend, the application transports a versioned text
reference:

```json
{
  "version": 1,
  "catalog": "app",
  "contractFingerprint": "sha256:...",
  "key": "Files.Deleted",
  "arguments": {
    "count": "3",
    "folder": "Archive"
  },
  "fallbackText": "3 files were deleted from Archive."
}
```

Rules:

- Persist and transport catalog ID plus stable key name, never integer key ID.
- Validate the catalog fingerprint and complete argument contract before
  formatting.
- Bound key, argument count, argument size, and fallback text.
- Keep resolved text available for logs, email, CLI output, inaccessible clients,
  and version-skew fallback.
- Treat every value and result as plain text.
- Version the transport independently from locale artifacts and generated ABIs.

## C++ direction

C++ work begins only after AST version 2 and the .NET/ESM conformance corpus are
stable. The first backend should generate:

- typed argument structs;
- stable key declarations;
- message functions;
- static compiled locale tables;
- a small formatter and locale-provider interface;
- headers and implementation files with deterministic content.

Locale-sensitive formatting should be an adapter over an explicitly selected
provider such as ICU. The core C++ output must not pretend that standard-library
locale facilities provide behavior equivalent to .NET globalization or
JavaScript `Intl` without evidence.

## Build and package integration

Add exact output selections rather than a single ambiguous web switch:

```text
--emit-esm
--emit-locale-json
--emit-typescript-contract
--emit-template-manifest
```

Corresponding MSBuild properties should include:

```xml
<TextResourcesEmitEsm>true</TextResourcesEmitEsm>
<TextResourcesEmitJson>false</TextResourcesEmitJson>
```

Requirements:

- Generated build outputs remain beneath the intermediate directory.
- Normal builds never modify tracked frontend source files.
- The generated module manifest owns every generated file and hash.
- Nested module outputs require a new manifest/build contract; frozen flat-output
  assumptions are not extended silently.
- Clean removes only validated, inventoried outputs.
- Verify compares the complete expected output byte-for-byte.
- A Vite adapter exposes stable virtual modules, watches declared source inputs,
  and performs HMR during development.
- The ESM output itself has no Vite or framework dependency.

## Delivery plan

### Phase X0: architecture ADR and ESM spike

Deliver:

- an ADR freezing compiler/backend boundaries and version selectors;
- a normalized grammar version 1 `TextNode`/`InputNode` AST spike;
- a direct IR-to-ESM prototype;
- a Vite production bundle fixture;
- measurements at 100 and 10,000 messages and at 2, 20, and 50 locales;
- an SSR locale-isolation fixture;
- a decision on generated export and module layout.

Gate:

- one imported message does not retain unrelated messages;
- generated functions require no runtime pattern parser;
- explicit locale override works;
- the design does not require a JavaScript implementation of the Runic compiler.

Indicative effort: 3-5 engineering days.

### Phase X1: portable grammar version 1 AST

Deliver:

- public or internal canonical AST contracts as decided by the ADR;
- compiler lowering from existing version 1 patterns;
- deterministic AST ordering and serialization tests;
- C# generation/runtime adapted to consume the normalized representation;
- unchanged version 1 observable behavior and diagnostics.

Gate:

- the existing corpus and public API checks remain green;
- no backend parses authored pattern strings independently;
- canonical AST output is byte-stable across operating systems and cultures.

Indicative effort: 1 week.

### Phase X2: production ESM backend

Deliver:

- message modules;
- aggregate typed exports;
- shared ESM runtime;
- declaration files;
- web-module manifest and hashes;
- compiler, CLI, and build output selection;
- identifier collision diagnostics;
- compiled-mode documentation.

Gate:

- C# and ESM pass the shared grammar version 1 corpus;
- unused messages are absent from production bundles;
- compiled mode performs no JSON fetch or message parsing;
- invariant formats are byte-equivalent where promised;
- locale-sensitive formats meet documented semantic expectations.

Indicative effort: 1-2 weeks.

### Phase X3: Vite and host integration

Deliver:

- a framework-independent Vite adapter;
- virtual catalog/message/runtime modules;
- watch and HMR support;
- production and development output-layout handling;
- package and clean-build fixtures;
- vanilla TypeScript plus representative framework examples.

Gate:

- editing a declared resource updates the running UI;
- production builds use message modules by default;
- generated files stay outside tracked source directories;
- no framework runtime is required by generated messages.

Indicative effort: 1 week.

### Phase X4: source schema and message AST version 2

Deliver:

- source schema version 2;
- message AST version 2;
- selector and formatter registries;
- declarations, selectors, variants, and catch-all validation;
- plural and ordinal behavior;
- structured markup contract;
- new diagnostics and versioned corpus;
- lowering of simple source strings and complex structured messages.

Gate:

- invalid or incomplete variants fail deterministically with precise locations;
- simple messages stay simple to author;
- the AST contains no target-language types or executable code;
- every AST node and registry option has defined backend semantics.

Indicative effort: 2-3 weeks for the core contract and compiler.

### Phase X5: AST version 2 .NET and ESM backends

Deliver:

- variant selection in both backends;
- cardinal and ordinal plural rules;
- structured formatting;
- markup-node output through a separate result type;
- multi-selector conformance fixtures;
- backend capability diagnostics.

Gate:

- both backends pass the same version 2 semantic corpus;
- concurrent SSR requests cannot leak locales;
- markup can never become implicitly trusted HTML;
- unsupported functions fail at build time rather than degrading at runtime.

Indicative effort: 2-3 weeks.

### Phase X6: transport contract

Deliver:

- .NET transport types and source-generated JSON metadata;
- generated TypeScript types and decoder;
- fingerprint and argument validation;
- fallback policy;
- bounds, hostile-input, and version-skew tests;
- application-bridge integration examples.

Gate:

- a backend result can be localized by the browser without sending static UI
  wording;
- incompatible catalogs are detected deterministically;
- malformed references cannot bypass key or argument validation.

Indicative effort: 1 week.

### Phase X7: C++ feasibility spike

Deliver only after version 2 is stable:

- generated header/source prototype;
- formatter-provider evaluation;
- Native build fixture on supported CI platforms;
- size, startup, and conformance measurements;
- ADR selecting or rejecting the initial C++ runtime dependency.

Gate:

- the spike consumes the unchanged canonical AST and corpus;
- no second source compiler is introduced;
- locale-sensitive behavior is documented honestly and tested.

Indicative effort: 1-2 weeks for evaluation, excluding a production backend.

## Quality gates

Every release containing cross-runtime work must cover:

- deterministic source-to-IR and IR-to-output generation;
- exact diagnostic IDs, arguments, and source locations;
- C# and ESM type checking from packed artifacts;
- tree-shaking verification by inspecting production bundle contents;
- bundle size and build time at representative key/locale scales;
- locale fallback and unsupported-locale behavior;
- exact portable formatting and semantic locale-sensitive formatting;
- hostile and oversized argument inputs;
- SSR request isolation;
- HMR and clean rebuild behavior;
- output path containment and safe cleanup;
- plain-text and structured-markup security boundaries;
- package consumption without repository-private build configuration;
- existing Native AOT and trimming guarantees.

## Risks and controls

| Risk | Control |
|---|---|
| JavaScript cannot exactly represent every .NET numeric value | Canonical transport strings, explicit ESM ranges, conformance fixtures, optional future decimal adapter |
| Platform globalization data produces different punctuation or names | Separate exact and semantic guarantees; keep resolved .NET text when byte identity is required |
| A convenient aggregate export defeats tree-shaking | Production bundle fixtures and an ADR-backed export layout |
| Global locale state leaks between SSR requests | Explicit locale arguments and request-scoped server adapters |
| Schema version 2 becomes difficult for translators | Preserve strings for simple messages and keep structured syntax focused on complex cases |
| Markup becomes an injection path | Structured nodes, closed tag contracts, no trusted-HTML return from ordinary messages |
| Backend implementations drift | One canonical IR, one shared corpus, and target capability diagnostics |
| Dynamic and compiled modes become ambiguous | Explicit build selections, separate manifests, and separate documentation |
| Future C++ concerns distort the initial web API | Stabilize the portable AST first; keep target ABIs independent |

## Definition of done for the cross-runtime milestone

The first cross-runtime milestone is complete when:

- existing schema/message grammar version 1 catalogs generate both C# and ESM;
- the two backends pass the shared version 1 corpus;
- ESM consumers receive typed message functions and explicit locale overrides;
- Vite removes unused messages from production bundles;
- compiled web mode needs no runtime catalog fetch or pattern parser;
- dynamic catalog mode remains available as an explicit alternative;
- build outputs are deterministic, inventoried, contained, and safely cleaned;
- SSR locale isolation is demonstrated;
- no Paraglide or inlang compatibility dependency exists;
- the version 2 source and AST work can proceed without replacing the ESM backend
  boundary.

## Research influences

This plan borrows general compiler and localization concepts from:

- [Paraglide JS architecture](https://paraglidejs.com/architecture), especially
  typed message functions and bundler-driven tree-shaking;
- [Paraglide JS generated output](https://paraglidejs.com/compiling-messages),
  especially separate message and runtime modules;
- [Paraglide JS output structures](https://paraglidejs.com/compiler-options),
  especially message-oriented production output and locale-oriented development
  output;
- [Unicode MessageFormat](https://messageformat.unicode.org/) and
  [UTS #35 Part 9](https://www.unicode.org/reports/tr35/tr35-79/tr35-messageFormat.html),
  especially declarations, selectors, variants, structured formatting, and
  markup.

These are research inputs, not compatibility targets. Runic Text Resources owns
its schemas, AST, diagnostics, generated APIs, runtime behavior, and versioning.
