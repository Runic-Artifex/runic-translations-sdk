using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using TUnit.Core;

namespace Runic.Translations.Build.Tests;

internal sealed class CliIntegrationTests
{
    [Test, DisplayName("CLI response files keep backslashes unless they precede a quote")]
    public void ResponseFilesUseMsvcrtBackslashRules()
    {
        using TemporaryDirectory temporary = new();
        string responseFile = temporary.Resolve("arguments.rsp");
        File.WriteAllText(responseFile, string.Join('\n',
            "# comment",
            "\"\\\\server\\share\\translations\\runic.json\"",
            "C:\\work\\obj\\",
            "\"a\\\\\\\"b\"",
            "\"trailing\\\\\"",
            "plain\\\"quote",
            "\"two words\""), new UTF8Encoding(false));

        var expanded = Runic.Translations.Tool.CommandLine.ExpandResponseFiles(["@" + responseFile]);

        string[] expected =
        [
            @"\\server\share\translations\runic.json",
            @"C:\work\obj\",
            "a\\\"b",
            "trailing\\",
            "plain\"quote",
            "two words",
        ];
        Assert.Equal(string.Join('|', expected), string.Join('|', expanded));
    }

    [Test, DisplayName("CLI project mode validates and generates direct MF2")]
    public void ProjectModeValidatesAndGenerates()
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
        Assert.Contains("2 source document(s)", validate.StandardOutput);

        ProcessResult generate = TestFixture.RunTool(temporary, "generate", "--project", "translations", "--output", "generated", "--emit-esm");
        Assert.Equal(0, generate.ExitCode, generate.Combined);
        Assert.True(File.Exists(temporary.Resolve("generated", "app.esm-v5", "server.js")), "Project mode did not generate the server entrypoint.");
    }

    [Test, DisplayName("CLI help and invalid invocation use stable exit codes")]
    public void HelpAndUsageExitCodes()
    {
        using TemporaryDirectory temporary = new();
        ProcessResult help = TestFixture.RunTool(temporary, "--help");
        Assert.Equal(0, help.ExitCode);
        AssertHelpEntry(help.StandardOutput, "validate", "Validate catalogs and report translation diagnostics.");
        Assert.False(help.StandardOutput.Contains("--documents", StringComparison.Ordinal), "Help still advertises removed document inputs.");

        ProcessResult namedHelp = TestFixture.RunTool(temporary, "help");
        Assert.Equal(0, namedHelp.ExitCode, namedHelp.Combined);
        Assert.Equal(help.StandardOutput, namedHelp.StandardOutput);

        ProcessResult commandHelp = TestFixture.RunTool(temporary, "help", "validate");
        Assert.Equal(0, commandHelp.ExitCode, commandHelp.Combined);
        Assert.Contains("runic-translations validate", commandHelp.StandardOutput);
        // --project is optional and described, in the same aligned help as every other command.
        string? project = commandHelp.StandardOutput.Split('\n').FirstOrDefault(value => value.TrimStart().StartsWith("--project <directory>", StringComparison.Ordinal));
        Assert.True(project is not null && project.Contains("Translations directory or its runic.json.", StringComparison.Ordinal) && !project.Contains("[required]", StringComparison.Ordinal), commandHelp.StandardOutput);
        Assert.Contains("Defaults to ./runic.json", System.Text.RegularExpressions.Regex.Replace(commandHelp.StandardOutput, "\\s+", " "));
        // Emitters without a renderer fail with RTR0065, so help and usage list only working ones.
        foreach (string command in new[] { "generate", "verify" })
        {
            ProcessResult emitHelp = TestFixture.RunTool(temporary, "help", command);
            Assert.Equal(0, emitHelp.ExitCode, emitHelp.Combined);
            ProcessResult usage = TestFixture.RunTool(temporary, command, "--bogus");
            foreach (string text in new[] { emitHelp.StandardOutput, usage.Combined })
            {
                Assert.Contains("--emit-esm", text);
                foreach (string unsupported in new[] { "--emit-typescript", "--emit-template-manifest", "--emit-cpp" })
                    Assert.False(text.Contains(unsupported, StringComparison.Ordinal), command + " help advertises " + unsupported + ":\n" + text);
            }
        }
        ProcessResult empty = TestFixture.RunTool(temporary);
        Assert.Equal(0, empty.ExitCode, empty.Combined);
        Assert.Equal(help.StandardOutput, empty.StandardOutput);
        AssertUsageFailure(temporary, "unknown command 'unknown-command'.", "unknown-command");
        AssertUsageFailure(temporary, "unknown option or positional argument '--bogus'.", "validate", "--bogus");
        AssertUsageFailure(temporary, "unknown option or positional argument '--bogus'.", "schema", "--bogus");
        AssertUsageFailure(temporary, "unknown option or positional argument '--documents'.", "validate", "--project", "translations", "--documents", "document.json");

        ProcessResult invalid = TestFixture.RunTool(temporary, "unknown-command");
        Assert.Equal(2, invalid.ExitCode);
        Assert.Contains("unknown command", invalid.StandardError);
    }

    // The command attributes in the tool's TranslationsToolCommandModule are the CLI contract: descriptions,
    // required options and the payload type.
    [Test, DisplayName("CLI help lists the command descriptions and required options")]
    public void HelpListsCommandContract()
    {
        using TemporaryDirectory temporary = new();
        ProcessResult help = TestFixture.RunTool(temporary, "--help");
        Assert.Equal(0, help.ExitCode, help.Combined);
        AssertHelpEntry(help.StandardOutput, "generate", "Generate translation artifacts.");
        AssertHelpEntry(help.StandardOutput, "init", "Create an RMF2 translation project and starter resources.");
        AssertHelpEntry(help.StandardOutput, "import-text", "Import a text-keyed catalog into an RMF2 group.");
        // lsp serves the IDE extensions and is outside the 1.0 compatibility rules: hidden from help, still callable.
        Assert.True(!help.StandardOutput.Split('\n').Any(line => line.TrimStart().StartsWith("lsp ", StringComparison.Ordinal)), "Help lists the hidden lsp command:\n" + help.StandardOutput);
        AssertHelpEntry(help.StandardOutput, "serve", "Run a persistent compiler for development servers");
        // One aligned column: every command description starts at the same offset.
        Assert.True(help.StandardOutput.Split('\n').SkipWhile(line => line != "Commands:").Skip(1).TakeWhile(line => line.Length != 0).Where(line => !line.StartsWith("   ", StringComparison.Ordinal))
            .Select(line => line.Length - line[line.IndexOf("  ", 2, StringComparison.Ordinal)..].TrimStart().Length).Distinct().Count() == 1, "Command descriptions are not aligned:\n" + help.StandardOutput);

        ProcessResult initHelp = TestFixture.RunTool(temporary, "help", "init");
        Assert.Equal(0, initHelp.ExitCode, initHelp.Combined);
        foreach (string option in new[] { "--directory <directory>", "--catalog <catalog>", "--default-locale <locale>", "--namespace <namespace>", "--class <name>" })
        {
            string? line = initHelp.StandardOutput.Split('\n').FirstOrDefault(value => value.TrimStart().StartsWith(option, StringComparison.Ordinal));
            Assert.True(line?.Contains("[required]", StringComparison.Ordinal) == true, $"init help does not mark {option} as required.");
        }
        string? locale = initHelp.StandardOutput.Split('\n').FirstOrDefault(value => value.TrimStart().StartsWith("--locale", StringComparison.Ordinal));
        Assert.True(locale is not null && !locale.Contains("[required]", StringComparison.Ordinal), "init help must list --locale as optional.");

        ProcessResult importHelp = TestFixture.RunTool(temporary, "help", "import-text");
        Assert.Equal(0, importHelp.ExitCode, importHelp.Combined);
        foreach (string option in new[] { "--group <name>", "--table <locale=path...>", "--base <path>", "--dry-run", "--project <directory>" })
        {
            string? line = importHelp.StandardOutput.Split('\n').FirstOrDefault(value => value.TrimStart().StartsWith(option, StringComparison.Ordinal));
            Assert.True(line is not null && line.Contains("[required]", StringComparison.Ordinal) == (option == "--group <name>"), $"import-text help lists {option} wrongly:\n{importHelp.StandardOutput}");
        }
    }

    [Test, DisplayName("CLI JSON output carries the tool payload type")]
    public void JsonOutputCarriesPayloadType()
    {
        using TemporaryDirectory temporary = new();
        Directory.CreateDirectory(temporary.Resolve("translations", "en"));
        File.WriteAllText(temporary.Resolve("translations", "runic.json"), """
            { "schemaVersion": 1, "catalog": "app", "code": { "namespace": "Example", "className": "AppText" }, "baseLocale": "en" }
            """, new UTF8Encoding(false));
        File.WriteAllText(temporary.Resolve("translations", "en", "application_title.mf2"), "Runic application\n", new UTF8Encoding(false));

        ProcessResult validate = TestFixture.RunTool(temporary, "validate", "--project", "translations", "--format", "json");
        Assert.Equal(0, validate.ExitCode, validate.Combined);
        using JsonDocument envelope = JsonDocument.Parse(validate.StandardOutput);
        JsonElement root = envelope.RootElement;
        Assert.Equal("validate", root.GetProperty("command").GetString());
        Assert.True(root.GetProperty("success").GetBoolean(), validate.StandardOutput);
        Assert.Equal("runic.translations.tool/1", root.GetProperty("payloadType").GetString());
        Assert.Contains("1 source document(s)", root.GetProperty("payload").GetProperty("output").GetString() ?? string.Empty);
        Assert.Equal(0, root.GetProperty("payload").GetProperty("diagnostics").GetArrayLength());

        // Without --project the conventional ./translations directory is used.
        ProcessResult defaulted = TestFixture.RunTool(temporary, "validate");
        Assert.Equal(0, defaulted.ExitCode, defaulted.Combined);
        Assert.Contains("1 source document(s)", defaulted.StandardOutput);

        // Translation diagnostics are native RTR entries: in fault.data of a failure, never RCLI9012 strings.
        File.WriteAllText(temporary.Resolve("translations", "en", "application_title.mf2"), ".local $a = {$n}\n.input {$n :number}\n{{{$a}}}\n", new UTF8Encoding(false));
        ProcessResult failed = TestFixture.RunTool(temporary, "validate", "--format", "json");
        Assert.Equal(2, failed.ExitCode, failed.Combined);
        Assert.False(failed.StandardOutput.Contains("RCLI9012", StringComparison.Ordinal), failed.StandardOutput);
        using JsonDocument failure = JsonDocument.Parse(failed.StandardOutput);
        JsonElement data = failure.RootElement.GetProperty("fault").GetProperty("data");
        Assert.Equal("runic.translations.tool/1", data.GetProperty("type").GetString());
        JsonElement diagnostic = data.GetProperty("payload").GetProperty("diagnostics").EnumerateArray().Single();
        Assert.Equal("RTR0067", diagnostic.GetProperty("code").GetString());
        Assert.Equal("error", diagnostic.GetProperty("severity").GetString());
        Assert.Equal("translations/en/application_title.mf2", (diagnostic.GetProperty("path").GetString() ?? string.Empty).Replace('\\', '/'));
        Assert.Equal(2, diagnostic.GetProperty("line").GetInt32());
        TestFixture.AssertReleaseHelpUri(diagnostic.GetProperty("helpUri").GetString(), "RTR0067");

        // Human output prints each diagnostic once, in the canonical file(line,col): form, without a prefix.
        ProcessResult human = TestFixture.RunTool(temporary, "validate");
        Assert.Equal(2, human.ExitCode, human.Combined);
        string[] lines = human.StandardError.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.True(lines.Length == 1 && lines[0].Replace('\\', '/').StartsWith("translations/en/application_title.mf2(2,", StringComparison.Ordinal) && lines[0].Contains("): error RTR0067: ", StringComparison.Ordinal), human.Combined);
        Assert.Equal(string.Empty, human.StandardOutput.Trim());

        // Outside a translations project, the missing default is a usage error.
        Directory.CreateDirectory(temporary.Resolve("empty"));
        ProcessResult missing = Processes.DotNet(temporary.Resolve("empty"), RepositoryPaths.ToolAssembly, "validate");
        Assert.Equal(2, missing.ExitCode, missing.Combined);
        Assert.Contains("no runic.json in the current directory or in ./translations; pass --project <directory>.", missing.StandardError);
    }

    private static void AssertHelpEntry(string help, string command, string description)
    {
        string? line = help.Split('\n').FirstOrDefault(value => value.TrimStart().StartsWith(command + " ", StringComparison.Ordinal));
        Assert.True(line is not null && line.Contains("  " + description, StringComparison.Ordinal), $"Help has no entry '{command}  {description}':\n{help}");
    }

    [Test, DisplayName("CLI rejects a missing required option as a usage failure")]
    public void MissingRequiredOptionIsUsageFailure()
    {
        using TemporaryDirectory temporary = new();
        AssertUsageFailure(temporary, "generate requires --output <directory>.", "generate", "--project", "translations");

        ProcessResult json = TestFixture.RunTool(temporary, "init", "--directory", "target", "--format", "json");
        Assert.Equal(2, json.ExitCode, json.Combined);
        using JsonDocument envelope = JsonDocument.Parse(json.StandardOutput);
        JsonElement diagnostic = envelope.RootElement.GetProperty("diagnostics")[0];
        Assert.Equal("RCLI1012", diagnostic.GetProperty("code").GetString());
        Assert.Equal("missing-required-option", diagnostic.GetProperty("kind").GetString());
        Assert.Equal("--catalog", diagnostic.GetProperty("arguments")[0].GetString());
        Assert.False(Directory.Exists(temporary.Resolve("target")), "A rejected init created its target directory.");
    }

    private static void AssertUsageFailure(TemporaryDirectory temporary, string message, params string[] arguments)
    {
        ProcessResult result = TestFixture.RunTool(temporary, arguments);
        Assert.Equal(2, result.ExitCode, result.Combined);
        Assert.Contains($"runic-translations: {message}", result.StandardError);
        Assert.Equal(string.Empty, result.StandardOutput);
    }

    [Test, DisplayName("CLI init creates and validates a one-locale RMF2 project")]
    public void InitCreatesOneLocaleProject()
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
            "de.rmf2|runic.json",
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
        Assert.True(File.Exists(temporary.Resolve("generated", "product.de.locale-v5.json")), "The default RMF2 locale artifact was not generated.");

        ProcessResult verify = TestFixture.RunTool(
            temporary,
            "verify",
            "--project",
            "Resources",
            "--output",
            "generated");
        Assert.Equal(0, verify.ExitCode, verify.Combined);
    }

    [Test, DisplayName("CLI init creates canonical locale files and explicit fallbacks")]
    public void InitCreatesMultipleLocales()
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
            "de-DE.rmf2|en-US.rmf2|fr.rmf2|runic.json",
            string.Join('|', TestFixture.RelativeFiles(temporary.Resolve("Resources"))));
        string manifest = File.ReadAllText(temporary.Resolve("Resources", "runic.json"), Encoding.UTF8);
        Assert.Contains("\"en-US\"", manifest);
        Assert.Contains("\"fallback\": \"en-US\"", manifest);
    }

    [Test, DisplayName("CLI init rejects conflicts without changing the target")]
    public void InitConflictDoesNotWrite()
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

    [Test, DisplayName("CLI init supports an empty RMF2 project")]
    public void InitWithoutStarterIsValid()
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
        Assert.Equal("en.rmf2|runic.json", string.Join('|', TestFixture.RelativeFiles(temporary.Resolve("Resources"))));
        ProcessResult validate = TestFixture.RunTool(temporary, "validate", "--project", "Resources");
        Assert.Equal(0, validate.ExitCode, validate.Combined);
    }

    private const string ImportHeader = "# Imported by `runic-translations import-text`. The next import rewrites this file; edit the source tables instead.\n\n";

    // A project like nix-manager's: English base texts, a German table, the group directory as the namespace.
    private static void WriteImportProject(TemporaryDirectory temporary, string validation = "")
    {
        Directory.CreateDirectory(temporary.Resolve("translations"));
        Directory.CreateDirectory(temporary.Resolve("tables"));
        File.WriteAllText(temporary.Resolve("translations", "runic.json"), """
            { "schemaVersion": 1, "catalog": "app", "code": { "namespace": "Example", "className": "AppText" }, "baseLocale": "en", "locales": ["en", "de"]
            """ + validation + " }\n", new UTF8Encoding(false));
    }

    private static void WriteTable(TemporaryDirectory temporary, string name, string json) =>
        File.WriteAllText(temporary.Resolve("tables", name), json, new UTF8Encoding(false));

    private static string ReadImported(TemporaryDirectory temporary, string file) =>
        File.ReadAllText(temporary.Resolve("translations", "stored", file), Encoding.UTF8);

    private static void RunBun(string directory, string script)
    {
        string path = Path.Combine(directory, "check.mjs");
        File.WriteAllText(path, script, new UTF8Encoding(false));
        var start = new System.Diagnostics.ProcessStartInfo("bun") { WorkingDirectory = directory, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        start.ArgumentList.Add(path);
        using System.Diagnostics.Process process = System.Diagnostics.Process.Start(start) ?? throw new InvalidOperationException("Could not start bun.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        Assert.True(process.WaitForExit(60_000), "bun did not finish.");
        Assert.Equal(0, process.ExitCode, output.Result + error.Result);
    }

    [Test, DisplayName("CLI import-text writes a valid group whose ESM formats placeholders, escapes and quoted patterns")]
    public void ImportTextValidatesAndGenerates()
    {
        using TemporaryDirectory temporary = new();
        WriteImportProject(temporary);
        WriteTable(temporary, "de.json", """
            {
              "Remove user {name}": "Benutzer {name} entfernen",
              "Use {braces} \\ and {0} }": "Nutze {braces} \\ und {0} }",
              " Leading space": " Führendes Leerzeichen",
              "Trailing space ": "Leerzeichen am Ende ",
              ".hidden files": ".versteckte Dateien",
              "Two\nlines": "Zwei\nZeilen"
            }
            """);

        ProcessResult import = TestFixture.RunTool(temporary, "import-text", "--group", "stored", "--table", "de=tables/de.json");
        Assert.Equal(0, import.ExitCode, import.Combined);
        Assert.Contains("imported 6 base text(s) into translations/stored: 6 new, 0 kept, 0 removed.", import.StandardOutput);
        Assert.Contains("validated 1 project(s)", import.StandardOutput);
        Assert.Equal("base-text.json|de.rmf2|en.rmf2", string.Join('|', TestFixture.RelativeFiles(temporary.Resolve("translations", "stored"))));

        // {name} becomes {$name}; other braces and backslashes are escaped; text that an unquoted
        // pattern would change (outer whitespace, a leading '.', a newline) is quoted.
        Assert.Equal(ImportHeader + """
            remove_user = Remove user {$name}
            use_and_0 = Use {$braces} \\ and \{0\} \}
            leading_space = {{ Leading space}}
            trailing_space = {{Trailing space }}
            hidden_files = {{.hidden files}}
            two_lines =
              {{Two
              lines}}

            """.Replace("\r\n", "\n", StringComparison.Ordinal), ReadImported(temporary, "en.rmf2"));
        Assert.Contains("use_and_0 = Nutze {$braces} \\\\ und \\{0\\} \\}\n", ReadImported(temporary, "de.rmf2"));

        ProcessResult validate = TestFixture.RunTool(temporary, "validate");
        Assert.Equal(0, validate.ExitCode, validate.Combined);
        ProcessResult generate = TestFixture.RunTool(temporary, "generate", "--output", "generated", "--emit-esm");
        Assert.Equal(0, generate.ExitCode, generate.Combined);
        RunBun(temporary.Path, """
            import { m } from "./generated/app.esm-v5/messages.js";
            const checks = [
              [m.stored_remove_user({ name: "Ada" }, { locale: "de" }), "Benutzer Ada entfernen"],
              [m.stored_remove_user({ name: "Ada" }, { locale: "en" }), "Remove user Ada"],
              [m.stored_use_and_0({ braces: "B" }, { locale: "en" }), "Use B \\ and {0} }"],
              [m.stored_leading_space({ locale: "en" }), " Leading space"],
              [m.stored_trailing_space({ locale: "de" }), "Leerzeichen am Ende "],
              [m.stored_hidden_files({ locale: "en" }), ".hidden files"],
              [m.stored_two_lines({ locale: "de" }), "Zwei\nZeilen"],
            ];
            for (const [actual, expected] of checks)
              if (actual !== expected) throw new Error(`expected ${JSON.stringify(expected)}, got ${JSON.stringify(actual)}`);
            """);
    }

    [Test, DisplayName("CLI import-text generates deterministic keys, resolves collisions and writes the base-text index")]
    public void ImportTextGeneratesKeysAndIndex()
    {
        using TemporaryDirectory temporary = new();
        WriteImportProject(temporary, """, "validation": { "translationCompleteness": "warning" }""");
        File.WriteAllText(temporary.Resolve("tables", "base.json"), """["Not translated yet", "Remove user {name}"]""", new UTF8Encoding(false));
        WriteTable(temporary, "de.json", """
            {
              "Remove user {name}": "Benutzer {name} entfernen",
              "Remove user {name}!": "Benutzer {name} entfernen!",
              "Über die Brücke": "Über die Brücke",
              "42 apples": "42 Äpfel",
              "!!!": "!!!",
              "One two three four five six seven eight nine": "Eins"
            }
            """);

        ProcessResult import = TestFixture.RunTool(temporary, "import-text", "--project", "translations", "--group", "stored", "--base", "tables/base.json", "--table", "de=tables/de.json");
        Assert.Equal(0, import.ExitCode, import.Combined);
        // --base first, then the table's keys, each text once; the JSON keeps {name} as given.
        Assert.Equal("""
            {
              "Not translated yet": "stored_not_translated_yet",
              "Remove user {name}": "stored_remove_user",
              "Remove user {name}!": "stored_remove_user_2",
              "Über die Brücke": "stored_uber_die_brucke",
              "42 apples": "stored_n_42_apples",
              "!!!": "stored_text",
              "One two three four five six seven eight nine": "stored_one_two_three_four_five_six_seven"
            }

            """.Replace("\r\n", "\n", StringComparison.Ordinal), ReadImported(temporary, "base-text.json"));
        // An untranslated text is omitted from the locale's file.
        string german = ReadImported(temporary, "de.rmf2");
        Assert.False(german.Contains("not_translated_yet", StringComparison.Ordinal), german);
        Assert.Contains("remove_user_2 = Benutzer {$name} entfernen!\n", german);
        Assert.Contains("uber_die_brucke = Über die Brücke\n", german);
    }

    [Test, DisplayName("CLI import-text re-import keeps existing keys, adds new ones and reports removed texts")]
    public void ImportTextReimportKeepsKeys()
    {
        using TemporaryDirectory temporary = new();
        WriteImportProject(temporary);
        WriteTable(temporary, "de.json", """{ "Delete item": "Eintrag löschen", "Save": "Speichern", "Save!": "Speichern!" }""");
        ProcessResult first = TestFixture.RunTool(temporary, "import-text", "--group", "stored", "--table", "de=tables/de.json");
        Assert.Equal(0, first.ExitCode, first.Combined);
        Assert.Contains("save_2 = Save!\n", ReadImported(temporary, "en.rmf2"));

        // "Save" and "Delete item" go, so their slugs are free; the kept "Save!" stays save_2, and the
        // new "Delete item!" does not reuse the removed text's key.
        WriteTable(temporary, "de.json", """{ "Save!": "Speichern!", "Delete item!": "Eintrag löschen!", "Open": "Öffnen" }""");
        ProcessResult second = TestFixture.RunTool(temporary, "import-text", "--group", "stored", "--table", "de=tables/de.json");
        Assert.Equal(0, second.ExitCode, second.Combined);
        Assert.Contains("imported 3 base text(s) into translations/stored: 2 new, 1 kept, 2 removed.", second.StandardOutput);
        Assert.Contains("removed stored_delete_item: \"Delete item\"", second.StandardOutput);
        Assert.Contains("removed stored_save: \"Save\"", second.StandardOutput);
        Assert.Equal(ImportHeader + "save_2 = Save!\ndelete_item_2 = Delete item!\nopen = Open\n", ReadImported(temporary, "en.rmf2"));
        Assert.Equal(ImportHeader + "save_2 = Speichern!\ndelete_item_2 = Eintrag löschen!\nopen = Öffnen\n", ReadImported(temporary, "de.rmf2"));
        Assert.Equal("{\n  \"Save!\": \"stored_save_2\",\n  \"Delete item!\": \"stored_delete_item_2\",\n  \"Open\": \"stored_open\"\n}\n", ReadImported(temporary, "base-text.json"));
    }

    [Test, DisplayName("CLI import-text rejects a translation placeholder the base text lacks and writes nothing")]
    public void ImportTextRejectsPlaceholderMismatch()
    {
        using TemporaryDirectory temporary = new();
        WriteImportProject(temporary);
        WriteTable(temporary, "de.json", """{ "Fine": "Gut", "Remove user {name}": "Benutzer {user} entfernen" }""");

        ProcessResult import = TestFixture.RunTool(temporary, "import-text", "--group", "stored", "--table", "de=tables/de.json");
        Assert.Equal(2, import.ExitCode, import.Combined);
        Assert.Contains("The de translation of \"Remove user {name}\" uses the placeholder {user}, which the base text does not contain.", import.StandardError);
        Assert.Contains("No files were written.", import.StandardOutput);
        Assert.False(Directory.Exists(temporary.Resolve("translations", "stored")), "A rejected import wrote files.");

        WriteTable(temporary, "de.json", """{ "Count": 3 }""");
        ProcessResult json = TestFixture.RunTool(temporary, "import-text", "--group", "stored", "--table", "de=tables/de.json", "--format", "json");
        Assert.Equal(2, json.ExitCode, json.Combined);
        using JsonDocument envelope = JsonDocument.Parse(json.StandardOutput);
        JsonElement diagnostic = envelope.RootElement.GetProperty("diagnostics")[0];
        Assert.Equal("RCLI9014", diagnostic.GetProperty("code").GetString());
        Assert.Contains("tables/de.json: the value of \"Count\" is not a string.", diagnostic.GetProperty("message").GetString() ?? string.Empty);
        Assert.False(Directory.Exists(temporary.Resolve("translations", "stored")), "A rejected import wrote files.");

        AssertUsageFailure(temporary, "--table locale 'fr' is not declared in runic.json.", "import-text", "--group", "stored", "--table", "fr=tables/de.json");
        AssertUsageFailure(temporary, "--table en names the base locale; list base texts without a translation with --base.", "import-text", "--group", "stored", "--table", "en=tables/de.json");
        AssertUsageFailure(temporary, "--group must be an RMF2 identifier ([A-Za-z_][A-Za-z0-9_]*), not 'stored-text'.", "import-text", "--group", "stored-text");
    }

    [Test, DisplayName("CLI import-text dry run reports and validates the files without writing them")]
    public void ImportTextDryRunWritesNothing()
    {
        using TemporaryDirectory temporary = new();
        WriteImportProject(temporary);
        WriteTable(temporary, "de.json", """{ "Remove user {name}": "Benutzer {name} entfernen" }""");

        ProcessResult dryRun = TestFixture.RunTool(temporary, "import-text", "--group", "stored", "--table", "de=tables/de.json", "--dry-run");
        Assert.Equal(0, dryRun.ExitCode, dryRun.Combined);
        Assert.Contains("would write translations/stored/en.rmf2", dryRun.StandardOutput);
        Assert.Contains("would write translations/stored/base-text.json", dryRun.StandardOutput);
        Assert.Contains("validated 1 project(s) and 2 source document(s).", dryRun.StandardOutput);
        Assert.False(Directory.Exists(temporary.Resolve("translations", "stored")), "A dry run wrote files.");

        // The planned content is validated: an untranslated text fails RTR0010 without writing.
        File.WriteAllText(temporary.Resolve("tables", "base.json"), """["Untranslated"]""", new UTF8Encoding(false));
        ProcessResult incomplete = TestFixture.RunTool(temporary, "import-text", "--group", "stored", "--base", "tables/base.json", "--table", "de=tables/de.json", "--dry-run");
        Assert.Equal(2, incomplete.ExitCode, incomplete.Combined);
        Assert.Contains("error RTR0010: Locale 'de' does not translate 'stored.untranslated'", incomplete.StandardError);
        Assert.False(Directory.Exists(temporary.Resolve("translations", "stored")), "A dry run wrote files.");
    }

    [Test, DisplayName("CLI schema writes exact bundled versioned schemas")]
    public void SchemaWritesExactSchemas()
    {
        using TemporaryDirectory temporary = new();
        ProcessResult result = TestFixture.RunTool(temporary, "schema", "--output", "schemas");
        Assert.Equal(0, result.ExitCode, result.Combined);
        string source = RepositoryPaths.Resolve("specs", "translations", "schemas");
        string[] expected = ["asset-manifest-v1.schema.json", "capabilities-v1.schema.json", "external-pack-v5.schema.json", "locale-artifact-v5.schema.json", "message-ast-v5.schema.json", "project-v1.schema.json", "web-module-manifest-v3.schema.json"];
        Array.Sort(expected, StringComparer.Ordinal);
        Assert.Equal(string.Join('|', expected), string.Join('|', TestFixture.RelativeFiles(temporary.Resolve("schemas"))));
        foreach (string schema in expected)
            Assert.FileBytesEqual(Path.Combine(source, schema), temporary.Resolve("schemas", schema));
    }
}
