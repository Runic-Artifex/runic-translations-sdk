# Runic Text Resources

Runic Text Resources is a deterministic, language-neutral localization system.
It started inside Runic Toolkit, but its contracts, compiler, runtime, generator,
build integration, and command-line tool are intentionally independent of any UI
framework.

The portable contract family is named `runic.textresources/1`. Package versions
evolve independently from its versioned source schemas, message grammar, generated
artifacts, and runtime ABI.

## Packages

| Package | Purpose |
|---|---|
| `RunicTextResources` | NativeAOT-compatible .NET runtime contracts and snapshots |
| `RunicTextResources.Compiler` | Deterministic, UI-independent compiler kernel |
| `RunicTextResources.Generator` | Incremental C# source generator |
| `RunicTextResources.Build` | Dependency-free MSBuild integration |
| `RunicTextResources.Tool` | `runic-textresources` validation and generation tool |

The normative schemas and compatibility corpus live in [`spec/`](spec/README.md).
The `.NET` implementation is under [`dotnet/`](dotnet/).

## Development

Enter the Nix development shell, then run the full verification pipeline:

```bash
nix develop
./eng/verify.sh
```

The pipeline restores and builds the standalone solution, runs every project-level
test executable, packs all five packages into an isolated local feed, installs and
executes the packed tool, consumes only those packages from a fixture project, and
publishes the runtime consumer with NativeAOT.

## Project status

This repository is being extracted from Runic Toolkit and has not made its first
independent public release. Package identity is intentionally clean-break
`RunicTextResources.*`; retired Toolkit identities are not compatibility aliases.

## License

Runic Text Resources is licensed under the [MIT License](LICENSE). Third-party
components, when present, retain their own license and attribution terms.
