using System;
using System.Collections.Generic;
using System.IO;
using RunicTranslations.Authoring;
using RunicTranslations.Compiler;

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
        catch (TextResourceAuthoringException exception)
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
            TextResourceProjectPlan plan = TextResourceProjectScaffolder.Render(invocation.ProjectCreation!);
            string target = TextResourceProjectWriter.Create(plan);
            Console.Out.WriteLine($"created {plan.Files.Count} translation file(s) in {target}.");
            return Success;
        }

        CompilerInputs inputs = InputFiles.Read(invocation.CatalogPath!, invocation.DocumentPatterns);
        TextResourceCompilation compilation = TextResourceCompiler.Compile(
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

    private static void WriteDiagnostics(IReadOnlyList<TextResourceDiagnostic> diagnostics)
    {
        for (int index = 0; index < diagnostics.Count; index++)
        {
            TextResourceDiagnostic diagnostic = diagnostics[index];
            TextSourceLocation location = diagnostic.Location;
            string path = location.Path.Replace('\\', '/');
            string severity = diagnostic.Severity == TextResourceDiagnosticSeverity.Error ? "error" : "warning";
            Console.Error.WriteLine(
                $"{path}({location.Line},{location.Column},{location.EndLine},{location.EndColumn}): {severity} {diagnostic.Id}: {diagnostic.Message}");
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
        writer.WriteLine();
        writer.WriteLine("Arguments may be read from a UTF-8 response file with @<file>.");
        writer.WriteLine("Init options: --locale <tag>[:<fallback>] (repeatable) --layer <name> --no-esm --no-starter.");
        writer.WriteLine("Emit switches: --emit-csharp --emit-json --emit-typescript --emit-template-manifest --emit-esm --emit-cpp.");
        writer.WriteLine("With no emit switches, generate and verify use all four output groups.");
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
