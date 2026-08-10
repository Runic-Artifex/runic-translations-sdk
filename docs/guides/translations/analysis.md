# Catalog analysis

Runic Translations analysis is advisory: catalogs remain the authoring source of
truth. The analyzer combines the compiler's resolved catalog model with lexical
usage evidence from C# and TypeScript consumers. It does not turn source
extraction into a second catalog format.

## Facts reported

For every canonical key and configured locale, the report distinguishes:

- `direct`: the locale defines the key itself;
- `fallback-only`: the value is resolved from another configured locale;
- `missing`: neither the locale nor its explicit fallback chain defines the key;
- `matches`, `drift`, or `missing` placeholder/input contracts, compared with
  the compiler-owned default-locale contract.

Every catalog also gets a deterministic `sourceFingerprint`. Unlike the public
contract fingerprint, it includes locale values and translatable metadata.
Comparing it with a saved `TranslationArtifactSnapshot` reports generated
artifacts as `current`, `stale`, or `missing`. With no snapshot the state is
`unknown`; analysis never guesses from file timestamps.

## Usage confidence

Usage is classified per key:

- `proven`: a recognized generated C# key/accessor, TypeScript `m["Exact.Key"]`,
  generated ESM identifier, or literal `TranslationKey` reference names the key;
- `possible-dynamic`: a dynamic API can address the catalog but its key cannot
  be proven statically;
- `unknown`: no recognized evidence was supplied.

Comments and unrelated string literals are ignored. The scanner is deliberately
lexical and narrow; `unknown` means “review this key,” not “proved unused.” Pass
consumer sources with a catalog ID in multi-catalog workspaces. An unscoped
`m["Shared.Key"]` that could refer to several catalogs becomes possible dynamic
usage for all matching catalogs.

Dynamic access is conservative by default. If any unresolved dynamic lookup can
address a catalog, no otherwise-unreferenced key in that catalog is a deletion
candidate. `IgnoreForDeletionCandidates` is an explicit unsafe policy for teams
that have independently audited their dynamic key space; the report still keeps
the `possible-dynamic` classification and evidence.

Do not pass Runic's generated source files as consumer inputs. Doing so would
correctly find the generated declarations themselves and make every key appear
used.

## Compiler API

```csharp
TranslationCompilation compilation = TranslationCompiler.Compile(manifests, documents);

TranslationAnalysisReport report = TranslationAnalyzer.Analyze(
    compilation,
    new[]
    {
        new TranslationUsageSource(
            "src/Checkout.cs",
            csharpSource,
            TranslationUsageSourceLanguage.CSharp,
            catalogId: "shop"),
        new TranslationUsageSource(
            "web/checkout.ts",
            typescriptSource,
            TranslationUsageSourceLanguage.TypeScript,
            catalogId: "shop"),
    });

string json = TranslationAnalysisRenderer.RenderJson(report);
string text = TranslationAnalysisRenderer.RenderText(report);
```

Both renderers are deterministic. JSON uses `analysisReportVersion: 1`; consumers
must reject unsupported report versions instead of silently interpreting new
fields. Human output is stable enough for review artifacts but JSON is the CI
contract.

## CLI and CI

The CLI scans explicitly supplied consumer files; their language is inferred
from C#, TypeScript, JavaScript, or Svelte file extensions:

```bash
runic-translations analyze \
  --catalog translations/app.catalog.json \
  --documents 'translations/app.*.json' \
  --sources 'src/**/*.cs' 'web/**/*.ts' 'web/**/*.svelte' \
  --format json \
  --fail-on-findings
```

Shells and the built-in globber do not all support brace expansion, so use
separate patterns when necessary. Omit `--sources` for completeness-only
analysis. `--format text` is the default. Machine output stays on stdout and
compiler or operational diagnostics stay on stderr.

To compare generated state without relying on timestamps, pass the
`sourceFingerprint` from a prior report with both `--artifact-fingerprint` and
`--artifact-path`. A mismatch is reported as stale. Exit codes are:

- `0`: the report was produced and no configured analysis gate failed;
- `1`: compilation failed, or a configured gate found incomplete translations,
  contract drift, stale/missing artifacts, or reviewed usage findings;
- `2`: invocation, input, encoding, or filesystem failure.

Possible dynamic and unknown usage are report data by default. The explicit
`--unsafe-ignore-dynamic` option permits possible dynamic keys to become deletion
candidates, but keeps the classification and evidence visible. It should only be
used after independently auditing the application's dynamic key space.
