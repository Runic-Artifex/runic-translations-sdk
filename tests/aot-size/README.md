# NativeAOT size budgets

How much a generated catalog and the Translations runtime add to a NativeAOT executable (D072.2), checked in the
`RMF2 NativeAOT / linux-x64` CI job by `bun eng/run.mjs native-aot`. Run it alone with:

```sh
bun tests/aot-size/measure.mjs            # publish, run and check every build of budgets.json
bun tests/aot-size/measure.mjs --update   # record the measured values and limits in budgets.json
bun tests/aot-size/measure.mjs --keep     # keep the executables, mstat files and records under artifacts/aot-size/
```

Each publish takes well under a minute with a warm NuGet cache and needs the NativeAOT toolchain (clang, zlib).

- **The probe** (`Probe/AotSizeProbe.csproj`) is a console application with a generated catalog that `fixture.mjs`
  writes to `artifacts/aot-size/catalog`: `messages` messages in the first `locales` of en, de, fr, pl, ar, ja, ru, es,
  it and pt, in a pattern of 14 plain texts, 3 with a string input, 2 plurals and a number per 20 messages. Its
  `Program.cs` formats every message, so nothing of the catalog is trimmed. `measure.mjs` publishes it for linux-x64
  with an mstat file, runs it in every locale and checks that it prints every message.
- **The records** come from `MstatReader`, which prints one line per mstat record: kind, size, owning assembly and name.
  The probe's own records (owner `AotSizeProbe`) are its catalog: `RvaFields` are the embedded locale data and the
  message table, `Methods` the generated code. `DeduplicatedMethods` records carry node IDs, not sizes, and are left out.
- **The budgets** in `budgets.json` name a build from `builds` (or two, for a growth class) and a limit. A `total` is
  checked against its limit. An `O(n)` has a value at each of its two `sizes`: the larger may be at most the smaller
  times their ratio, plus the `margin`, and at most the limit. `--update` sets the limit to the measured value plus the
  entry's `margin`. mstat bytes do not move with section alignment, so only `fixed-bytes` depends on the linker's
  layout.

| Budget | Measures |
| --- | --- |
| `aot/translations/fixed-bytes` | The executable of `m200-l2` less the catalog's own records: the runtime, the framework and their metadata. |
| `aot/translations/per-message-bytes` | The catalog's own records (locale data and generated code) at 200 and 800 messages in 2 locales, `O(n)`. |
| `aot/translations/per-locale-message-bytes` | The catalog's `RvaFields` for 200 messages in 2 and 10 locales, `O(n)`: the locale data and the message table, which does not grow with the locales. |
| `aot/translations/reflection-runtime-bytes` | mstat bytes of `System.Reflection.Runtime.*`, the reflection stack behind `Delegate.Method` and `MethodInfo`. Hot Reload's tracking needs it, so it stays out only while `TranslationHotReload.IsSupported` folds to a constant in a trimmed publish. |

To see why something is in the executable, publish with `--keep` and `-p:IlcGenerateDgmlFile=true` (add it to the
`dotnet publish` arguments in `measure.mjs`), then search the scan graph for the shortest path from a root.
