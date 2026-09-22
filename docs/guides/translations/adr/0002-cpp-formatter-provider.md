# ADR 0002: ICU4C for a production C++ formatter backend

Status: superseded as an active output; retained as feasibility rationale
Date: 8 August 2026

## Context

The feasibility generator proves that the canonical Runic message AST can emit
typed C++20 functions without introducing a second source compiler. The C++
standard library does not provide CLDR plural rules, relative-time formatting, or
portable locale-sensitive number/date behavior equivalent to JavaScript `Intl`
and .NET globalization.

## Decision

A production C++ backend will use ICU4C behind a small generated-runtime adapter.
The adapter will own locale construction, plural/ordinal selection, number and
date/time formatting, relative time, and bounded error conversion. Generated
message functions and semantic markup values remain Runic-owned and consume the
same normalized AST as .NET and ESM.

The dependency-free C++ output described by this decision was removed when the
translation stack collapsed onto the single v5 contract. The current CLI and
MSBuild surfaces reject C++ emission with `RTR0065`. ICU4C remains the preferred
foundation if a future, explicitly versioned C++ backend is proposed.

## Consequences

- ICU data/version selection becomes part of the production C++ deployment
  contract and reproducible-build policy.
- Applications cannot select the removed feasibility subset from current
  packages. A future adapter must define a new supported output contract.
- C++ formatting is tested semantically against the shared corpus for the exact
  locales/functions enabled by the adapter.
- No C++ source parser or authoring-schema implementation is introduced.
