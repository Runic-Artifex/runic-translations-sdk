# C++ feasibility backend

`--emit-cpp` (or `TextResourcesEmitCpp`) emits a deterministic C++20 header/source
pair from the canonical compiler AST. It provides typed argument structs,
injective message function names, compiled locale branches, scalar formatting,
and initial selector support. The fixture compiles with Clang under `-Werror`.

This surface is experimental and excluded from default emission. Its dependency-
free formatter intentionally makes no broad locale-equivalence claim. It rejects
structured format and markup nodes explicitly instead of flattening them. The
schema-v2 fixture proves multi-selector lowering still consumes the canonical AST.

[ADR 0002](adr/0002-cpp-formatter-provider.md) selects ICU4C for a future
production formatter adapter. That work remains a generator backend, never a
second source compiler.
