# .NET implementation

- `src/` contains the runtime, compiler, generator, and build packages.
- `tools/` contains the independently packaged command-line tool.
- `tests/` contains executable test suites and isolated package consumers.

`RunicTextResources.slnx` includes the source projects, tool, and project-reference
tests. The package-only and NativeAOT consumers are deliberately excluded because
they restore version `1.0.0` exclusively from the local feed produced by
`eng/verify.sh`.
