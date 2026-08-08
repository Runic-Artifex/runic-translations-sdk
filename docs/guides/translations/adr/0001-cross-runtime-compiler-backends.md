# ADR 0001: One compiler with cross-runtime generated backends

Status: accepted  
Date: 7 August 2026

## Context

Runic Text Resources already owns a deterministic compiler, versioned source and
artifact contracts, generated C#, and a Native-AOT-compatible .NET runtime. The
next product tranche must support TypeScript/ESM without creating a second source
compiler or making the browser depend on .NET runtime behavior. A later C++
backend must fit the same design.

Compiler-oriented JavaScript localization systems demonstrate useful output
properties such as typed message functions and message-level tree-shaking. Runic
needs those properties without adopting another system's source format, project
model, runtime, or compatibility policy.

## Decision

1. The .NET compiler is the only implementation that parses and validates Runic
   authoring sources.
2. Authored messages lower to a normalized, target-neutral message AST before any
   backend renders output.
3. C#, ESM, locale-artifact, template, transport, and future C++ outputs consume
   the same canonical catalog and message IR.
4. Source schema, message grammar, normalized AST, serialized artifacts, and each
   generated runtime ABI have independent integer versions.
5. ESM compiled mode emits independently tree-shakable message functions and no
   runtime authoring-pattern parser.
6. Runtime-loaded locale artifacts remain an explicit alternative for dynamic
   packs and large-locale deployments.
7. Locale persistence, URL routing, navigation, framework reactivity, and request
   lifecycle remain host responsibilities.
8. Cross-process localization uses catalog ID, contract fingerprint, stable key
   name, and typed arguments. Integer key IDs never cross process boundaries.
9. Paraglide JS and inlang are research influences only. Runic provides no
   compatibility contract for them.
10. A future C++ target is a generator backend over the same IR, not a second
    compiler.

## Version boundaries

The following selectors evolve independently:

- source schema version;
- message grammar version;
- normalized message AST version;
- locale artifact version;
- template and asset manifest versions;
- .NET runtime/generated-code ABI version;
- ESM generated-code ABI version;
- future C++ generated-code ABI version;
- cross-process text-reference version.

An unsupported value fails explicitly. Existing version 1 contracts are not
extended in place with incompatible behavior.

## Initial backend boundary

Grammar version 1 lowers to an ordered pattern containing text and input nodes.
Escaped braces are resolved during lowering. The ESM backend renders those nodes
directly. Existing public version 1 patterns remain available for compatibility,
but new backends do not parse them independently.

Schema and AST version 2 add declarations, formatter expressions, selectors,
ordered variants, literal and catch-all matches, plural selection, and structured
markup. Simple authored messages remain strings.

## Consequences

- Cross-runtime semantics have one validation authority and one conformance
  corpus.
- Generated backends can evolve without changing source schemas.
- ESM output can be framework-independent and tree-shakable.
- Browser dynamic catalogs remain possible but are visibly different from
  compiled mode.
- Backend capability differences must be diagnosed at compile time.
- Numeric and globalization differences require explicit exact-versus-semantic
  guarantees.
- Generated output inventories must support nested paths without weakening path
  containment or cleanup safety.

