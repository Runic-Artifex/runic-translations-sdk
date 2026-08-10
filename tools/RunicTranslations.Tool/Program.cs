using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using RunicTranslations.Authoring;
using RunicTranslations.Compiler;
using RunicTranslations.Compiler.Analysis;

namespace RunicTranslations.Tool;

internal static class Program
{
    private const int Success = 0;
    private const int DiagnosticFailure = 1;
    private const int InvocationFailure = 2;

    public static int Main(string[] arguments)
    {
        try
        {
            ToolInvocation invocation = CommandLine.Parse(arguments);
            return Run(invocation);
        }
        catch (ToolOutputException exception)
        {
            Console.Error.WriteLine($"error {exception.Message}");
            return DiagnosticFailure;
        }
        catch (ToolDiagnosticException exception)
        {
            Console.Error.WriteLine(exception.Message);
            return DiagnosticFailure;
        }
        catch (ToolUsageException exception)
        {
            WriteToolError(exception.Message);
            WriteUsage(Console.Error);
            return InvocationFailure;
        }
        catch (TranslationAuthoringException exception)
        {
            WriteToolError(exception.Message);
            return InvocationFailure;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            WriteToolError(exception.Message);
            return InvocationFailure;
        }
        catch (Exception exception)
        {
            WriteToolError($"internal failure: {exception.Message}");
            return InvocationFailure;
        }
    }

    private static int Run(ToolInvocation invocation)
    {
        if (invocation.Command == ToolCommand.Help)
        {
            WriteUsage(Console.Out);
            return Success;
        }

        if (invocation.Command == ToolCommand.Schema)
        {
            IReadOnlyList<ToolArtifact> schemas = SchemaResources.Read();
            ArtifactFiles.WriteAtomically(invocation.OutputPath!, schemas);
            Console.Out.WriteLine($"wrote {schemas.Count} schemas.");
            return Success;
        }

        if (invocation.Command == ToolCommand.Init)
        {
            TranslationProjectPlan plan = TranslationProjectScaffolder.Render(invocation.ProjectCreation!);
            string target = TranslationProjectWriter.Create(plan);
            Console.Out.WriteLine($"created {plan.Files.Count} translation file(s) in {target}.");
            return Success;
        }

        if (invocation.Command == ToolCommand.Import)
        {
            CatalogImportResult result = CatalogImporter.Import(invocation.CatalogImport!);
            WriteImportDiagnostics(result.Diagnostics);
            if (result.Compilation is not null)
            {
                WriteDiagnostics(result.Compilation.Diagnostics);
            }

            if (invocation.CatalogImport!.DryRun)
            {
                Console.Out.Write(Encoding.UTF8.GetString(result.Report));
                return result.CanWrite ? Success : DiagnosticFailure;
            }

            if (!result.CanWrite)
            {
                Console.Error.WriteLine("import: no files were written; see the diagnostics above or run with --dry-run for the full report.");
                return DiagnosticFailure;
            }

            ArtifactFiles.WriteAtomically(invocation.CatalogImport.OutputPath, result.Artifacts);
            Console.Out.WriteLine($"imported {result.Artifacts.Count - 1} catalog file(s) and wrote runic-import-report.json.");
            return Success;
        }

        CompilerInputs inputs = InputFiles.Read(invocation.CatalogPath!, invocation.DocumentPatterns);
        TranslationCompilation compilation = TranslationCompiler.Compile(
            [inputs.Catalog],
            inputs.Documents);
        WriteDiagnostics(compilation.Diagnostics);
        if (!compilation.Success)
        {
            return DiagnosticFailure;
        }

        if (invocation.Command == ToolCommand.Validate)
        {
            Console.Out.WriteLine($"validated {compilation.Catalogs.Count} catalog(s) and {inputs.Documents.Count} document(s).");
            return Success;
        }

        if (invocation.Command == ToolCommand.Analyze)
        {
            CatalogAnalysisRequest request = invocation.CatalogAnalysis!;
            CompiledTextCatalog catalog = compilation.Catalogs[0];
            IReadOnlyList<TranslationUsageSource> sources = InputFiles.ReadUsageSources(request.SourcePatterns, catalog.Id);
            var options = new TranslationAnalysisOptions(request.DynamicUsagePolicy);
            TranslationAnalysisReport report = request.ArtifactFingerprint is null
                ? TranslationAnalyzer.Analyze(compilation, sources, options)
                : TranslationAnalyzer.Analyze(
                    compilation,
                    sources,
                    [new TranslationArtifactSnapshot(catalog.Id, request.ArtifactFingerprint, request.ArtifactPath!)],
                    options);
            Console.Out.Write(request.Json
                ? TranslationAnalysisRenderer.RenderJson(report)
                : TranslationAnalysisRenderer.RenderText(report));
            return request.FailOnFindings && report.HasFindings ? DiagnosticFailure : Success;
        }

        IReadOnlyList<ToolArtifact> artifacts = CompilerOutputAdapter.Render(compilation.Catalogs, invocation.Emission);
        if (invocation.Command == ToolCommand.Generate)
        {
            ArtifactFiles.WriteAtomically(invocation.OutputPath!, artifacts);
            Console.Out.WriteLine($"generated {artifacts.Count} artifact(s).");
            return Success;
        }

        IReadOnlyList<string> differences = ArtifactFiles.Verify(invocation.OutputPath!, artifacts);
        if (differences.Count != 0)
        {
            for (int index = 0; index < differences.Count; index++)
            {
                Console.Error.WriteLine($"verify: {differences[index]}");
            }

            return DiagnosticFailure;
        }

        Console.Out.WriteLine($"verified {artifacts.Count} artifact(s).");
        return Success;
    }

    private static void WriteDiagnostics(IReadOnlyList<TranslationDiagnostic> diagnostics)
    {
        for (int index = 0; index < diagnostics.Count; index++)
        {
            TranslationDiagnostic diagnostic = diagnostics[index];
            TextSourceLocation location = diagnostic.Location;
            string path = location.Path.Replace('\\', '/');
            string severity = diagnostic.Severity == TranslationDiagnosticSeverity.Error ? "error" : "warning";
            Console.Error.WriteLine(
                $"{path}({location.Line},{location.Column},{location.EndLine},{location.EndColumn}): {severity} {diagnostic.Id}: {diagnostic.Message}");
        }
    }

    private static void WriteImportDiagnostics(IReadOnlyList<CatalogImportDiagnostic> diagnostics)
    {
        for (int index = 0; index < diagnostics.Count; index++)
        {
            CatalogImportDiagnostic diagnostic = diagnostics[index];
            string path = string.IsNullOrEmpty(diagnostic.Path) ? "import" : diagnostic.Path.Replace('\\', '/');
            string key = diagnostic.Key is null ? string.Empty : $" [{diagnostic.Key}]";
            Console.Error.WriteLine($"{path}: {diagnostic.Severity} {diagnostic.Code}{key}: {diagnostic.Message}");
        }
    }

    private static void WriteToolError(string message) => Console.Error.WriteLine($"runic-translations: {message}");

    private static void WriteUsage(TextWriter writer)
    {
        writer.WriteLine("Usage:");
        writer.WriteLine("  runic-translations init --directory <directory> --catalog <id> --default-locale <tag> --namespace <namespace> --class <name> [init-options]");
        writer.WriteLine("  runic-translations validate --catalog <file> --documents <path-or-glob...>");
        writer.WriteLine("  runic-translations generate --catalog <file> --documents <path-or-glob...> --output <directory> [emit-switches]");
        writer.WriteLine("  runic-translations verify --catalog <file> --documents <path-or-glob...> --output <directory> [emit-switches]");
        writer.WriteLine("  runic-translations schema --output <directory>");
        writer.WriteLine("  runic-translations import --source <locale>=<file>... --catalog <id> --default-locale <tag> --namespace <namespace> --class <name> --output <directory> [--format auto|json|inlang] [--dry-run] [--allow-partial]");
        writer.WriteLine("  runic-translations analyze --catalog <file> --documents <path-or-glob...> [--sources <path-or-glob...>] [analysis-options]");
        writer.WriteLine();
        writer.WriteLine("Arguments may be read from a UTF-8 response file with @<file>.");
        writer.WriteLine("Init options: --locale <tag>[:<fallback>] (repeatable) --layer <name> --no-esm --no-starter --vscode.");
        writer.WriteLine("Emit switches: --emit-csharp --emit-json --emit-typescript --emit-template-manifest --emit-esm --emit-cpp.");
        writer.WriteLine("Analysis options: --format text|json --fail-on-findings --unsafe-ignore-dynamic --artifact-fingerprint <sha256:...> --artifact-path <path>.");
        writer.WriteLine("With no emit switches, generate and verify use all output groups.");
        writer.WriteLine("Exit codes: 0 success; 1 validation or verification diagnostics; 2 invocation or operational failure.");
    }
}

internal sealed class ToolDiagnosticException : Exception
{
    internal ToolDiagnosticException(string message)
        : base(message)
    {
    }
}
