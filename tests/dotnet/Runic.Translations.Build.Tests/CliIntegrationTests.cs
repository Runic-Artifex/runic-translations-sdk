using System;
using System.IO;
using System.Linq;
using System.Text;

namespace Runic.Translations.Build.Tests;

internal static class CliIntegrationTests
{
    public static void Register(TestRunner runner)
    {
        runner.Add("CLI help and invalid invocation use stable exit codes", HelpAndUsageExitCodes);
        runner.Add("CLI init creates and validates a one-locale MF2 project", InitCreatesOneLocaleProject);
        runner.Add("CLI init creates canonical locale files and explicit fallbacks", InitCreatesMultipleLocales);
        runner.Add("CLI init rejects conflicts without changing the target", InitConflictDoesNotWrite);
        runner.Add("CLI init supports an empty MF2 project", InitWithoutStarterIsValid);
        runner.Add("CLI project mode validates and generates conventional MF2", ProjectModeValidatesAndGenerates);
        runner.Add("CLI schema writes exact bundled versioned schemas", SchemaWritesExactSchemas);
    }

    private static void ProjectModeValidatesAndGenerates()
    {
        using TemporaryDirectory temporary = new();
        Directory.CreateDirectory(temporary.Resolve("translations", "en"));
        Directory.CreateDirectory(temporary.Resolve("translations", "de"));
        File.WriteAllText(temporary.Resolve("translations", "runic.json"), """
            { "schemaVersion": 1, "catalog": "app", "code": { "namespace": "Example", "className": "AppText" }, "baseLocale": "en" }
            """, new UTF8Encoding(false));
        File.WriteAllText(temporary.Resolve("translations", "en", "application_title.mf2"), "Runic application\n", new UTF8Encoding(false));
        File.WriteAllText(temporary.Resolve("translations", "de", "application_title.mf2"), "Runische Anwendung\n", new UTF8Encoding(false));

        ProcessResult validate = TestFixture.RunTool(temporary, "validate", "--project", "translations");
        Assert.Equal(0, validate.ExitCode, validate.Combined);
        Assert.Contains("2 MF2 message(s)", validate.StandardOutput);

        ProcessResult generate = TestFixture.RunTool(temporary, "generate", "--project", "translations", "--output", "generated", "--emit-esm");
        Assert.Equal(0, generate.ExitCode, generate.Combined);
        Assert.True(File.Exists(temporary.Resolve("generated", "app.esm", "server.js")), "Project mode did not generate the server entrypoint.");
    }

    private static void HelpAndUsageExitCodes()
    {
        using TemporaryDirectory temporary = new();
        ProcessResult help = TestFixture.RunTool(temporary, "--help");
        Assert.Equal(0, help.ExitCode);
        Assert.Contains("validate --project", help.StandardOutput);
        Assert.False(help.StandardOutput.Contains("--documents", StringComparison.Ordinal), "Help still advertises removed document inputs.");
        Assert.Contains("Exit codes: 0", help.StandardOutput);

        ProcessResult namedHelp = TestFixture.RunTool(temporary, "help");
        Assert.Equal(0, namedHelp.ExitCode, namedHelp.Combined);
        Assert.Contains("validate --project", namedHelp.StandardOutput);

        AssertUsageFailure(temporary, "help does not accept additional arguments.", "help", "validate");
        AssertUsageFailure(temporary, "a command is required.");
        AssertUsageFailure(temporary, "unknown command 'unknown-command'.", "unknown-command");
        AssertUsageFailure(temporary, "unknown option or positional argument '--bogus'.", "validate", "--bogus");
        AssertUsageFailure(temporary, "unknown option or positional argument '--bogus'.", "schema", "--bogus");
        AssertUsageFailure(temporary, "unknown option or positional argument '--documents'.", "validate", "--project", "translations", "--documents", "document.json");

        ProcessResult invalid = TestFixture.RunTool(temporary, "unknown-command");
        Assert.Equal(2, invalid.ExitCode);
        Assert.Contains("unknown command", invalid.StandardError);
    }

    private static void AssertUsageFailure(TemporaryDirectory temporary, string message, params string[] arguments)
    {
        ProcessResult result = TestFixture.RunTool(temporary, arguments);
        Assert.Equal(2, result.ExitCode, result.Combined);
        Assert.Contains($"runic-translations: {message}", result.StandardError);
        Assert.Equal(string.Empty, result.StandardOutput);
    }

    private static void InitCreatesOneLocaleProject()
    {
        using TemporaryDirectory temporary = new();
        ProcessResult create = TestFixture.RunTool(
            temporary,
            "init",
            "--directory",
            "Resources",
            "--catalog",
            "product",
            "--default-locale",
            "de",
            "--namespace",
            "Customer.Product",
            "--class",
            "ProductText");

        Assert.Equal(0, create.ExitCode, create.Combined);
        Assert.Contains("created 2 translation file(s)", create.StandardOutput);
        Assert.Equal(
            "de/application_title.mf2|runic.json",
            string.Join('|', TestFixture.RelativeFiles(temporary.Resolve("Resources"))));
        ProcessResult validate = TestFixture.RunTool(temporary, "validate", "--project", "Resources");
        Assert.Equal(0, validate.ExitCode, validate.Combined);

        ProcessResult generate = TestFixture.RunTool(
            temporary,
            "generate",
            "--project",
            "Resources",
            "--output",
            "generated");
        Assert.Equal(0, generate.ExitCode, generate.Combined);
        Assert.True(File.Exists(temporary.Resolve("generated", "product.de.locale-v2.json")), "The default schema-v2 locale asset was not generated.");

        ProcessResult verify = TestFixture.RunTool(
            temporary,
            "verify",
            "--project",
            "Resources",
            "--output",
            "generated");
        Assert.Equal(0, verify.ExitCode, verify.Combined);
    }

    private static void InitCreatesMultipleLocales()
    {
        using TemporaryDirectory temporary = new();
        ProcessResult create = TestFixture.RunTool(
            temporary,
            "init",
            "--directory",
            "Resources",
            "--catalog",
            "product",
            "--default-locale",
            "de-de",
            "--locale",
            "en-us",
            "--locale",
            "fr:en-US",
            "--namespace",
            "Customer.Product",
            "--class",
            "ProductText");

        Assert.Equal(0, create.ExitCode, create.Combined);
        Assert.Equal(
            "de-DE/application_title.mf2|en-US/application_title.mf2|fr/application_title.mf2|runic.json",
            string.Join('|', TestFixture.RelativeFiles(temporary.Resolve("Resources"))));
        string manifest = File.ReadAllText(temporary.Resolve("Resources", "runic.json"), Encoding.UTF8);
        Assert.Contains("\"en-US\"", manifest);
        Assert.Contains("\"fallback\": \"en-US\"", manifest);
    }

    private static void InitConflictDoesNotWrite()
    {
        using TemporaryDirectory temporary = new();
        Directory.CreateDirectory(temporary.Resolve("Resources"));
        File.WriteAllText(temporary.Resolve("Resources", "customer.txt"), "keep", new UTF8Encoding(false));
        ProcessResult create = TestFixture.RunTool(
            temporary,
            "init",
            "--directory",
            "Resources",
            "--catalog",
            "product",
            "--default-locale",
            "de",
            "--namespace",
            "Customer.Product",
            "--class",
            "ProductText");

        Assert.Equal(2, create.ExitCode, create.Combined);
        Assert.Contains("already exists; no files were written", create.StandardError);
        Assert.Equal("customer.txt", string.Join('|', TestFixture.RelativeFiles(temporary.Resolve("Resources"))));
        Assert.Equal("keep", File.ReadAllText(temporary.Resolve("Resources", "customer.txt"), Encoding.UTF8));
    }

    private static void InitWithoutStarterIsValid()
    {
        using TemporaryDirectory temporary = new();
        ProcessResult create = TestFixture.RunTool(
            temporary,
            "init",
            "--directory",
            "Resources",
            "--catalog",
            "empty",
            "--default-locale",
            "en",
            "--namespace",
            "Customer.Empty",
            "--class",
            "EmptyText",
            "--no-starter");
        Assert.Equal(0, create.ExitCode, create.Combined);
        Assert.Equal("runic.json", string.Join('|', TestFixture.RelativeFiles(temporary.Resolve("Resources"))));
        ProcessResult validate = TestFixture.RunTool(temporary, "validate", "--project", "Resources");
        Assert.Equal(0, validate.ExitCode, validate.Combined);
    }

    private static void SchemaWritesExactSchemas()
    {
        using TemporaryDirectory temporary = new();
        ProcessResult result = TestFixture.RunTool(temporary, "schema", "--output", "schemas");
        Assert.Equal(0, result.ExitCode, result.Combined);
        string source = RepositoryPaths.Resolve("spec", "schemas");
        string[] excluded = ["catalog-v1.schema.json", "catalog-v2.schema.json", "resources-v1.schema.json", "resources-v2.schema.json", "resources-v3.schema.json", "message-ast-v3.schema.json"];
        string[] expected = Directory.EnumerateFiles(source, "*.schema.json").Select(path => Path.GetFileName(path)!)
            .Where(name => !excluded.Contains(name, StringComparer.Ordinal)).Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(string.Join('|', expected), string.Join('|', TestFixture.RelativeFiles(temporary.Resolve("schemas"))));
        foreach (string schema in expected)
            Assert.FileBytesEqual(Path.Combine(source, schema), temporary.Resolve("schemas", schema));
    }
}
