# C++ feasibility backend

`--emit-cpp` (or `TextResourcesEmitCpp`) emits a deterministic C++20 header/source
pair from the canonical compiler AST. It provides typed argument structs,
injective message function names, compiled locale branches, scalar formatting,
and initial selector support. The fixture compiles with Clang under `-Werror`.

This surface is experimental and excluded from default emission. Its dependency-
free formatter intentionally makes no broad locale-equivalence claim. A production
backend still needs an ADR choosing an ICU-class provider, expanded conformance
and platform measurements; it will remain a generator backend, never a second
source compiler.
