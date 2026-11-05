using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Json.Schema;
using TUnit.Core;

namespace Runic.Translations.Build.Tests;

// runic-translations status, validate with several projects and --sarif, and locale add/remove, on copies of the
// corpus project specs/translations/corpus/xliff-v3/project (shop: en, de with a review file).
internal sealed class ProjectCliTests
{
    private static readonly string Corpus = RepositoryPaths.Resolve("specs", "translations", "corpus", "xliff-v3", "project");

    private static TemporaryDirectory CopyProject(params string[] directories)
    {
        var temporary = new TemporaryDirectory();
        foreach (string directory in directories.Length == 0 ? ["translations"] : directories)
        {
            Directory.CreateDirectory(temporary.Resolve(directory));
            foreach (string file in Directory.GetFiles(Corpus))
                File.Copy(file, temporary.Resolve(directory, Path.GetFileName(file)));
        }
        return temporary;
    }

    [Test, DisplayName("CLI status reports translated, fallback, missing and stale per locale and fails under --min-coverage")]
    public void StatusReportsCoverage()
    {
        using TemporaryDirectory temporary = CopyProject();
        // de-CH falls back to de: what de translates is fallback coverage for it.
        File.WriteAllText(temporary.Resolve("translations", "runic.json"), File.ReadAllText(temporary.Resolve("translations", "runic.json"))
            .Replace("{ \"tag\": \"de\", \"fallback\": \"en\" }", "{ \"tag\": \"de\", \"fallback\": \"en\" }, { \"tag\": \"de-CH\", \"fallback\": \"de\" }", StringComparison.Ordinal));
        File.WriteAllText(temporary.Resolve("translations", "de-CH.rmf2"), "farewell = Uf Widerluege\n");

        ProcessResult human = TestFixture.RunTool(temporary, "status");
        Assert.Equal(0, human.ExitCode, human.Combined);
        Assert.Contains("shop (translations/runic.json): 11 message(s), base locale en", human.StandardOutput);
        Assert.Contains("  de                   3         0        8      1    27.27%", human.StandardOutput);
        Assert.Contains("  de-CH                1         2        8      0    27.27%", human.StandardOutput);

        ProcessResult json = TestFixture.RunTool(temporary, "status", "--min-coverage", "de=27,de-CH=50", "--format", "json");
        Assert.Equal(1, json.ExitCode, json.Combined);
        using JsonDocument envelope = JsonDocument.Parse(json.StandardOutput);
        Assert.Equal("RTR0099", envelope.RootElement.GetProperty("fault").GetProperty("code").GetString());
        JsonElement payload = envelope.RootElement.GetProperty("fault").GetProperty("data").GetProperty("payload");
        PayloadSchemas.Validate(PayloadSchemas.Status, payload);
        JsonElement[] locales = payload.GetProperty("projects")[0].GetProperty("locales").EnumerateArray().ToArray();
        Assert.Equal("en", locales[0].GetProperty("locale").GetString());
        JsonElement swiss = locales.Single(locale => locale.GetProperty("locale").GetString() == "de-CH");
        Assert.Equal(2, swiss.GetProperty("fallback").GetInt32());
        Assert.True(swiss.GetProperty("belowThreshold").GetBoolean() && !locales.Single(locale => locale.GetProperty("locale").GetString() == "de").GetProperty("belowThreshold").GetBoolean(), payload.GetRawText());

        ProcessResult passing = TestFixture.RunTool(temporary, "status", "--min-coverage", "*=27", "--format", "json");
        Assert.Equal(0, passing.ExitCode, passing.Combined);
        using (JsonDocument passed = JsonDocument.Parse(passing.StandardOutput))
        {
            Assert.Equal("runic.translations.status/1", passed.RootElement.GetProperty("payloadType").GetString());
            PayloadSchemas.Validate(PayloadSchemas.Status, passed.RootElement.GetProperty("payload"));
        }

        foreach (string invalid in new[] { "fr=80", "de=120", "de", "de=10,de=20" })
        {
            ProcessResult usage = TestFixture.RunTool(temporary, "status", "--min-coverage", invalid);
            Assert.Equal(2, usage.ExitCode, invalid + ": " + usage.Combined);
        }
    }

    [Test, DisplayName("CLI validate checks several projects and writes every diagnostic as SARIF")]
    public void ValidateSeveralProjectsWithSarif()
    {
        using TemporaryDirectory temporary = CopyProject("app", "lib");
        File.WriteAllText(temporary.Resolve("lib", "de.rmf2"), "greeting = Hallo!\n");
        ProcessResult result = TestFixture.RunTool(temporary, "validate", "--project", "app", "--project", "lib", "--sarif", "out/translations.sarif");
        Assert.Equal(0, result.ExitCode, result.Combined);
        Assert.Contains("app/de.rmf2(3,1,3,9): warning RTR0090", result.StandardError);
        Assert.Contains("lib/de.rmf2(1,1,1,9): warning RTR0096", result.StandardError);
        using JsonDocument sarif = JsonDocument.Parse(File.ReadAllText(temporary.Resolve("out", "translations.sarif")));
        Assert.Equal("2.1.0", sarif.RootElement.GetProperty("version").GetString());
        JsonElement run = sarif.RootElement.GetProperty("runs")[0];
        Assert.Equal("runic-translations", run.GetProperty("tool").GetProperty("driver").GetProperty("name").GetString());
        JsonElement[] results = run.GetProperty("results").EnumerateArray().ToArray();
        Assert.True(results.Any(item => item.GetProperty("ruleId").GetString() == "RTR0096" && item.GetProperty("level").GetString() == "warning" &&
            item.GetProperty("locations")[0].GetProperty("physicalLocation").GetProperty("artifactLocation").GetProperty("uri").GetString() == "lib/de.rmf2"), run.GetRawText());
        ValidateSarif(sarif.RootElement);
        Assert.True(run.GetProperty("tool").GetProperty("driver").GetProperty("rules").EnumerateArray().All(rule => rule.GetProperty("helpUri").GetString()!.EndsWith("#" + rule.GetProperty("id").GetString()!.ToLowerInvariant(), StringComparison.Ordinal)),
            "A SARIF rule does not link its reference entry.");

        // A failing project fails the command, and the report is still written.
        File.WriteAllText(temporary.Resolve("lib", "de.rmf2"), "greeting = Hallo {$nobody}\n");
        ProcessResult failing = TestFixture.RunTool(temporary, "validate", "--project", "app", "--project", "lib", "--sarif", "out/failing.sarif");
        Assert.Equal(2, failing.ExitCode, failing.Combined);
        Assert.Contains("\"RTR0016\"", File.ReadAllText(temporary.Resolve("out", "failing.sarif")));

        // A later project that cannot be read: the earlier one is still reported, and the run records the failure.
        File.WriteAllText(temporary.Resolve("lib", "de.rmf2"), "greeting = Hallo!\n");
        ProcessResult missing = TestFixture.RunTool(temporary, "validate", "--project", "app", "--project", "nowhere", "--sarif", "out/missing.sarif");
        Assert.Equal(2, missing.ExitCode, missing.Combined);
        Assert.Contains("app/de.rmf2(3,1,3,9): warning RTR0090", missing.StandardError);
        using JsonDocument partial = JsonDocument.Parse(File.ReadAllText(temporary.Resolve("out", "missing.sarif")));
        ValidateSarif(partial.RootElement);
        JsonElement partialRun = partial.RootElement.GetProperty("runs")[0];
        Assert.True(partialRun.GetProperty("results").EnumerateArray().Any(item => item.GetProperty("ruleId").GetString() == "RTR0090"), partialRun.GetRawText());
        JsonElement invocation = partialRun.GetProperty("invocations")[0];
        Assert.True(!invocation.GetProperty("executionSuccessful").GetBoolean() &&
            invocation.GetProperty("toolExecutionNotifications")[0].GetProperty("message").GetProperty("text").GetString()!.Contains("'nowhere'", StringComparison.Ordinal), invocation.GetRawText());
    }

    [Test, DisplayName("CLI validate --sarif writes encoded URIs relative to the repository root, from any directory")]
    public void SarifUrisAreRelativeToTheRepositoryRoot()
    {
        using TemporaryDirectory temporary = CopyProject("My App/translations");
        Directory.CreateDirectory(temporary.Resolve(".git"));
        Directory.CreateDirectory(temporary.Resolve("tools", "ci"));
        File.WriteAllText(temporary.Resolve("My App", "translations", "de.rmf2"), "greeting = Hallo!\n");
        string root = new Uri(temporary.Path.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar).AbsoluteUri;

        ProcessResult fromRoot = TestFixture.RunTool(temporary, "validate", "--project", "My App/translations", "--sarif", "root.sarif");
        Assert.Equal(0, fromRoot.ExitCode, fromRoot.Combined);
        ProcessResult fromSubdirectory = Processes.DotNet(temporary.Resolve("tools", "ci"), RepositoryPaths.ToolAssembly,
            "validate", "--project", "../../My App/translations", "--sarif", "../../sub.sarif");
        Assert.Equal(0, fromSubdirectory.ExitCode, fromSubdirectory.Combined);
        foreach (string report in new[] { "root.sarif", "sub.sarif" })
        {
            using JsonDocument sarif = JsonDocument.Parse(File.ReadAllText(temporary.Resolve(report)));
            ValidateSarif(sarif.RootElement);
            JsonElement run = sarif.RootElement.GetProperty("runs")[0];
            Assert.Equal(root, run.GetProperty("originalUriBaseIds").GetProperty("%SRCROOT%").GetProperty("uri").GetString(), report);
            JsonElement location = run.GetProperty("results").EnumerateArray().First(item => item.GetProperty("ruleId").GetString() == "RTR0096")
                .GetProperty("locations")[0].GetProperty("physicalLocation").GetProperty("artifactLocation");
            Assert.Equal("My%20App/translations/de.rmf2", location.GetProperty("uri").GetString(), report);
            Assert.Equal("%SRCROOT%", location.GetProperty("uriBaseId").GetString(), report);
        }
    }

    // The report against the OASIS SARIF 2.1.0 schema, with formats (uri, uri-reference) checked.
    private static readonly Lazy<JsonSchema> SarifSchema = new(() => JsonSchema.FromFile(
        RepositoryPaths.Resolve("tests", "dotnet", "Runic.Translations.Build.Tests", "Fixtures", "sarif-schema-2.1.0.json"), new BuildOptions { Dialect = Dialect.Draft07 }));

    private static void ValidateSarif(JsonElement report)
    {
        EvaluationResults result = SarifSchema.Value.Evaluate(report, new EvaluationOptions { OutputFormat = OutputFormat.List, RequireFormatValidation = true });
        Assert.True(result.IsValid, "The SARIF report does not validate: " + string.Join("; ", (result.Details ?? []).Where(detail => detail.Errors is { Count: > 0 })
            .Select(detail => detail.InstanceLocation + ": " + string.Join(", ", detail.Errors!.Values))));
    }

    [Test, DisplayName("CLI status counts stale translations as validate reports them, and warns about an unreadable review file")]
    public void StatusStaleMatchesValidate()
    {
        using TemporaryDirectory temporary = CopyProject();
        // The stale approval of farewell stays stale when its translation is removed: validate keeps warning (RTR0090).
        File.WriteAllText(temporary.Resolve("translations", "de.rmf2"), File.ReadAllText(temporary.Resolve("translations", "de.rmf2")).Replace("farewell = Auf Wiedersehen\n", "", StringComparison.Ordinal));
        ProcessResult validate = TestFixture.RunTool(temporary, "validate");
        int warnings = validate.StandardError.Split('\n').Count(line => line.Contains("warning RTR0090", StringComparison.Ordinal));
        Assert.Equal(1, warnings, validate.Combined);
        ProcessResult status = TestFixture.RunTool(temporary, "status", "--format", "json");
        Assert.Equal(0, status.ExitCode, status.Combined);
        using (JsonDocument report = JsonDocument.Parse(status.StandardOutput))
            Assert.Equal(warnings, report.RootElement.GetProperty("payload").GetProperty("projects")[0].GetProperty("locales").EnumerateArray()
                .Single(locale => locale.GetProperty("locale").GetString() == "de").GetProperty("stale").GetInt32(), status.StandardOutput);

        File.WriteAllText(temporary.Resolve("translations", "shop.review.json"), "{ not json");
        ProcessResult unreadable = TestFixture.RunTool(temporary, "status");
        Assert.Equal(0, unreadable.ExitCode, unreadable.Combined);
        Assert.Contains("RTR0091", unreadable.Combined);
    }

    [Test, DisplayName("CLI locale add keeps CRLF line endings, and remove lists directories and files outside the project")]
    public void LocaleEditsKeepTheFile()
    {
        using TemporaryDirectory temporary = CopyProject();
        string path = temporary.Resolve("translations", "runic.json");
        File.WriteAllText(path, File.ReadAllText(path).Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\n", "\r\n", StringComparison.Ordinal)
            .Replace("\"locales\": [\"en\", { \"tag\": \"de\", \"fallback\": \"en\" }]", "\"locales\": [\r\n    \"en\",\r\n    { \"tag\": \"de\", \"fallback\": \"en\" }\r\n  ]", StringComparison.Ordinal));
        Assert.Equal(0, TestFixture.RunTool(temporary, "locale", "add", "fr").ExitCode, "fr was not added.");
        string crlf = File.ReadAllText(path);
        Assert.True(crlf.Contains("{ \"tag\": \"fr\", \"fallback\": \"en\" }", StringComparison.Ordinal) && !crlf.Replace("\r\n", "", StringComparison.Ordinal).Contains('\n'),
            "runic.json has mixed line endings: " + crlf.Replace("\r", "\\r", StringComparison.Ordinal));

        // A source root outside the runic.json directory is part of the project; its files are marked.
        Directory.CreateDirectory(temporary.Resolve("shared"));
        File.WriteAllText(temporary.Resolve("shared", "en.rmf2"), "shared_title = Shared\n");
        File.WriteAllText(temporary.Resolve("shared", "de.rmf2"), "shared_title = Geteilt\n");
        File.WriteAllText(path, File.ReadAllText(path).Replace("\"baseLocale\"", "\"sourceRoots\": [{ \"path\": \".\" }, { \"path\": \"../shared\", \"namespace\": [\"shared\"] }],\r\n  \"baseLocale\"", StringComparison.Ordinal));
        ProcessResult dryRun = TestFixture.RunTool(temporary, "locale", "remove", "de", "--dry-run");
        Assert.Equal(0, dryRun.ExitCode, dryRun.Combined);
        Assert.Contains("would delete translations/de.rmf2.", dryRun.StandardOutput);
        Assert.Contains("would delete shared/de.rmf2 (outside the project directory).", dryRun.StandardOutput);
        Assert.True(File.Exists(temporary.Resolve("shared", "de.rmf2")), "The dry run deleted a file.");
        ProcessResult removed = TestFixture.RunTool(temporary, "locale", "remove", "de");
        Assert.Equal(0, removed.ExitCode, removed.Combined);
        Assert.True(!File.Exists(temporary.Resolve("shared", "de.rmf2")) && !File.ReadAllText(path).Contains("\"de\"", StringComparison.Ordinal), removed.Combined);
    }

    [Test, DisplayName("CLI locale remove lists and removes the emptied locale directory of direct .mf2 sources")]
    public void LocaleRemoveDirectDirectory()
    {
        using var temporary = new TemporaryDirectory();
        Directory.CreateDirectory(temporary.Resolve("translations", "en"));
        Directory.CreateDirectory(temporary.Resolve("translations", "de"));
        File.WriteAllText(temporary.Resolve("translations", "runic.json"),
            "{\"schemaVersion\":1,\"catalog\":\"shop\",\"code\":{\"namespace\":\"Shop\",\"className\":\"ShopText\"},\"baseLocale\":\"en\",\"locales\":[\"en\",\"de\"],\"validation\":{\"translationCompleteness\":\"allow\"}}\n");
        File.WriteAllText(temporary.Resolve("translations", "en", "title.mf2"), "Shop\n");
        File.WriteAllText(temporary.Resolve("translations", "de", "title.mf2"), "Laden\n");
        ProcessResult valid = TestFixture.RunTool(temporary, "validate");
        Assert.Equal(0, valid.ExitCode, "The direct fixture does not validate: " + valid.Combined);
        ProcessResult dryRun = TestFixture.RunTool(temporary, "locale", "remove", "de", "--dry-run");
        Assert.Equal(0, dryRun.ExitCode, dryRun.Combined);
        Assert.Contains("would delete translations/de/title.mf2.", dryRun.StandardOutput);
        Assert.Contains("would remove the directory translations/de/.", dryRun.StandardOutput);
        Assert.Equal(0, TestFixture.RunTool(temporary, "locale", "remove", "de").ExitCode, "de was not removed.");
        Assert.True(!Directory.Exists(temporary.Resolve("translations", "de")), "The emptied locale directory remains.");
    }

    [Test, DisplayName("CLI locale add and remove edit runic.json in place, its files and the review file")]
    public void LocaleAddAndRemove()
    {
        using TemporaryDirectory temporary = CopyProject();
        string original = File.ReadAllText(temporary.Resolve("translations", "runic.json"));
        ProcessResult dryRun = TestFixture.RunTool(temporary, "locale", "add", "fr", "--dry-run");
        Assert.Equal(0, dryRun.ExitCode, dryRun.Combined);
        Assert.Equal(original, File.ReadAllText(temporary.Resolve("translations", "runic.json")));

        ProcessResult added = TestFixture.RunTool(temporary, "locale", "add", "fr");
        Assert.Equal(0, added.ExitCode, added.Combined);
        Assert.Equal(original.Replace("{ \"tag\": \"de\", \"fallback\": \"en\" }]", "{ \"tag\": \"de\", \"fallback\": \"en\" }, { \"tag\": \"fr\", \"fallback\": \"en\" }]", StringComparison.Ordinal),
            File.ReadAllText(temporary.Resolve("translations", "runic.json")));
        Assert.True(File.Exists(temporary.Resolve("translations", "fr.rmf2")), "fr.rmf2 was not created.");
        // The project allows incomplete locales, so it still validates.
        Assert.Equal(0, TestFixture.RunTool(temporary, "validate").ExitCode, "The project with fr does not validate.");

        Assert.Equal(2, TestFixture.RunTool(temporary, "locale", "add", "fr").ExitCode, "A declared locale was added again.");
        Assert.Equal(2, TestFixture.RunTool(temporary, "locale", "add", "it", "--fallback", "es").ExitCode, "An undeclared fallback was accepted.");
        Assert.Equal(0, TestFixture.RunTool(temporary, "locale", "add", "de-AT", "--fallback", "de").ExitCode, "de-AT was not added.");
        Assert.Equal(2, TestFixture.RunTool(temporary, "locale", "remove", "de").ExitCode, "A locale with a dependent was removed.");
        Assert.Equal(2, TestFixture.RunTool(temporary, "locale", "remove", "en").ExitCode, "The base locale was removed.");

        Assert.Equal(0, TestFixture.RunTool(temporary, "locale", "remove", "de-AT").ExitCode, "de-AT was not removed.");
        ProcessResult removed = TestFixture.RunTool(temporary, "locale", "remove", "de");
        Assert.Equal(0, removed.ExitCode, removed.Combined);
        Assert.Contains("deleted translations/de.rmf2.", removed.StandardOutput);
        Assert.True(!File.Exists(temporary.Resolve("translations", "de.rmf2")), "de.rmf2 was not deleted.");
        Assert.Contains("\"locales\": [\"en\", { \"tag\": \"fr\", \"fallback\": \"en\" }]", File.ReadAllText(temporary.Resolve("translations", "runic.json")));
        Assert.True(!File.ReadAllText(temporary.Resolve("translations", "shop.review.json")).Contains("\"de\"", StringComparison.Ordinal), "The review file still has de entries.");
        Assert.Equal(0, TestFixture.RunTool(temporary, "validate").ExitCode, "The project does not validate after remove.");
    }
}
