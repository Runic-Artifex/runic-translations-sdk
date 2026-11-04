using System;
using System.IO;
using System.Linq;
using System.Text.Json;
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
        Assert.Contains("shop (translations/runic.json): 10 message(s), base locale en", human.StandardOutput);
        Assert.Contains("  de                   3         0        7      1       30%", human.StandardOutput);
        Assert.Contains("  de-CH                1         2        7      0       30%", human.StandardOutput);

        ProcessResult json = TestFixture.RunTool(temporary, "status", "--min-coverage", "de=30,de-CH=50", "--format", "json");
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

        ProcessResult passing = TestFixture.RunTool(temporary, "status", "--min-coverage", "*=30", "--format", "json");
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
        Assert.True(run.GetProperty("tool").GetProperty("driver").GetProperty("rules").EnumerateArray().All(rule => rule.GetProperty("helpUri").GetString()!.EndsWith("#" + rule.GetProperty("id").GetString()!.ToLowerInvariant(), StringComparison.Ordinal)),
            "A SARIF rule does not link its reference entry.");

        // A failing project fails the command, and the report is still written.
        File.WriteAllText(temporary.Resolve("lib", "de.rmf2"), "greeting = Hallo {$nobody}\n");
        ProcessResult failing = TestFixture.RunTool(temporary, "validate", "--project", "app", "--project", "lib", "--sarif", "out/failing.sarif");
        Assert.Equal(2, failing.ExitCode, failing.Combined);
        Assert.Contains("\"RTR0016\"", File.ReadAllText(temporary.Resolve("out", "failing.sarif")));
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
