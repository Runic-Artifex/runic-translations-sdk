# ADR 0002: ICU4C for a production C++ formatter backend

Status: accepted for a future production backend  
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

The current dependency-free `--emit-cpp` output remains an explicitly
experimental feasibility surface. It supports text, typed scalar substitution,
literal selectors, and the closed plural subset exercised by its conformance
fixture. It rejects structured formatter and markup nodes before emitting files;
it does not flatten them or claim locale equivalence.

## Consequences

- ICU data/version selection becomes part of the production C++ deployment
  contract and reproducible-build policy.
- Applications that cannot accept ICU may keep using the feasibility subset or
  provide a future compatible formatter adapter, but unsupported AST functions
  fail generation.
- C++ formatting is tested semantically against the shared corpus for the exact
  locales/functions enabled by the adapter.
- No C++ source parser or authoring-schema implementation is introduced.
