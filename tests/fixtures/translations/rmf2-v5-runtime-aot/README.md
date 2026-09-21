# Source-local RMF2 v5 NativeAOT probe

This fixture links the runtime's v5 behavior and hostile-input tests directly to
the current runtime project. It uses no generated catalog, installed package,
reflection-based serialization or JSON metadata construction.

From the SDK root, in the locked development environment:

```sh
dotnet publish tests/fixtures/translations/rmf2-v5-runtime-aot/Probe.csproj \
  -c Release -r linux-x64 -p:TreatWarningsAsErrors=true \
  -o artifacts/rmf2-v5-runtime-aot
artifacts/rmf2-v5-runtime-aot/Probe
```

The output directory and fixture `bin`/`obj` directories are disposable. Use the
appropriate supported .NET runtime identifier on other hosts.
