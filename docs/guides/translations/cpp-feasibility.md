# C++ feasibility study

The dependency-free C++20 emitter was a pre-v5 feasibility experiment. It is not
part of the selected translation contract and is not shipped by the current
compiler, CLI, or MSBuild integration. `--emit-cpp` and
`TranslationsEmitCpp` fail with `RTR0065` rather than emitting an artifact under
an older grammar or runtime ABI.

[ADR 0002](adr/0002-cpp-formatter-provider.md) selects ICU4C for a future
production formatter adapter. Any future C++ work remains a new, explicitly
versioned generator backend over the v5 semantic model, never a second source
compiler or a compatibility promise for the removed feasibility output.
