# Source-local RMF2 v5 NativeAOT probe

This fixture links the runtime's v5 behavior and hostile-input tests directly to
the current runtime project. It uses no generated catalog, installed package,
reflection-based serialization or JSON metadata construction.

The probe is a TUnit test application published with NativeAOT. It links the
`Rmf2RuntimeV5Tests` and `TypedSlotBindingsTests` cases and the suite's
`Assert` helpers from `tests/dotnet/Runic.Translations.Runtime.Tests`, and keeps
that suite's assembly name because the cases use runtime internals.

From the SDK root, in the locked development environment:

```sh
dotnet publish tests/fixtures/translations/rmf2-v5-runtime-aot/Probe.csproj \
  -c Release -r linux-x64 -p:TreatWarningsAsErrors=true \
  -o artifacts/rmf2-v5-runtime-aot
artifacts/rmf2-v5-runtime-aot/Runic.Translations.Runtime.Tests
```

The executable accepts the usual TUnit arguments, for example
`--treenode-filter "/*/*/TypedSlotBindingsTests/*"`. The output directory and
fixture `bin`/`obj` directories are disposable. Use the appropriate supported
.NET runtime identifier on other hosts.
