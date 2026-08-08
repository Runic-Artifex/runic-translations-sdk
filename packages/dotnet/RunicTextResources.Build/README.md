# RunicTextResources.Build

This package supplies dependency-free MSBuild integration for Runic Text Resources. It contains no MSBuild task assembly and takes no `Microsoft.Build` package dependency. Instead, its targets map explicitly declared catalog and document items to Roslyn `AdditionalFiles` and invoke the separately installed `runic-textresources` tool for non-C# artifacts.

```xml
<ItemGroup>
  <TextResourceCatalog Include="Resources/Text/app.textcatalog.json" />
  <TextResourceDocument Include="Resources/Text/*.texts.json" />
</ItemGroup>

<PropertyGroup>
  <TextResourcesEmitTypeScript>true</TextResourcesEmitTypeScript>
  <TextResourcesEmitEsm>true</TextResourcesEmitEsm>
</PropertyGroup>
```

`AdditionalFiles` receive `RunicTextResourceKind` metadata with the value `Catalog` or `Document`. Artifact generation is opt-in: set one or more of `TextResourcesEmitJson`, `TextResourcesEmitTypeScript`, `TextResourcesEmitTemplateManifest`, `TextResourcesEmitEsm`, or experimental `TextResourcesEmitCpp` to select exact output groups. JSON/template/TypeScript-contract selections use `{catalog}.asset-manifest-v1.json`; nested ESM output uses its own `web-module-manifest-v1.json`. `TextResourcesGenerateOnBuild=true` with no individual emit property selects JSON, TypeScript, template, and ESM. C++ remains explicit. This package never adds generated C# files to compilation; C# generation belongs to the generator surface.

Generated files default to `$(IntermediateOutputPath)text-resources` and are exposed as `@(TextResourcesGeneratedFile)`. An override through `TextResourcesOutputPath` or the compatibility alias `TextResourcesWebOutputPath` must still resolve beneath `IntermediateOutputPath`; an unsafe path fails with `RTR0020`. `dotnet clean` removes only files recorded in the validated owned-output inventory plus private build bookkeeping. Unrelated files survive, and the output directory is removed only when empty.

The default launcher is `dotnet tool run runic-textresources`, which deliberately requires a locally installed, version-pinned tool manifest entry. Install the `runic-textresources` tool at the same version as this package before enabling generation. Set `TextResourcesToolCommand` only for a different pinned launcher or a package test, and use `TextResourcesToolAdditionalArguments` for additional CLI options. Add the launcher or its version manifest to `@(TextResourcesToolInput)` when it does not use the project-local `.config/dotnet-tools.json`; changes then invalidate incremental generation.

The current build target accepts exactly one catalog and one or more documents, matching the frozen CLI surface. It writes a private UTF-8 response file under the owned intermediate directory, so large document sets do not expand the shell command line. Inputs, generation settings, the response file, an output inventory, and a private stamp file are declared to MSBuild so an unchanged build skips generation while changed or missing declared files regenerate.

The CLI first writes the complete selected set into a contained private staging directory. Successful per-file moves replace current names, then prior inventoried names that are no longer selected are removed. This prevents flag changes from leaving stale TypeScript, JSON, or template artifacts while preserving unrelated consumer files. Byte-level verification, including extra-file detection, remains available through `runic-textresources verify`.
