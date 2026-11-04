using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using TUnit.Core;

namespace Runic.Translations.Build.Tests;

// runic-translations xliff export/import and review mark, on a copy of the corpus project
// specs/translations/corpus/xliff-v3/project.
internal sealed class InterchangeCliTests
{
    private static readonly string Corpus = RepositoryPaths.Resolve("specs", "translations", "corpus", "xliff-v3");

    [Test, DisplayName("CLI xliff import applies a TMS file per unit into .rmf2 and reports the units in its payload")]
    public void XliffImportAppliesPerUnit()
    {
        using TemporaryDirectory temporary = CopyProject();
        ProcessResult dryRun = TestFixture.RunTool(temporary, "xliff", "import", "--project", "translations", "--source", Path.Combine(Corpus, "files", "crowdin.de.xliff"), "--dry-run");
        Assert.Equal(0, dryRun.ExitCode, dryRun.Combined);
        Assert.Contains("dry run: imported de: 3 added, 1 changed, 1 unchanged, 5 skipped, 0 refused.", dryRun.StandardOutput);
        Assert.FileBytesEqual(Path.Combine(Corpus, "project", "de.rmf2"), temporary.Resolve("translations", "de.rmf2"));

        ProcessResult import = TestFixture.RunTool(temporary, "xliff", "import", "--project", "translations", "--source", Path.Combine(Corpus, "files", "crowdin.de.xliff"), "--format", "json");
        Assert.Equal(0, import.ExitCode, import.Combined);
        Assert.FileBytesEqual(Path.Combine(Corpus, "expected", "crowdin.de.rmf2"), temporary.Resolve("translations", "de.rmf2"));
        using JsonDocument envelope = JsonDocument.Parse(import.StandardOutput);
        JsonElement payload = envelope.RootElement.GetProperty("payload");
        PayloadSchemas.Validate(PayloadSchemas.Tool, payload);
        JsonElement interchange = payload.GetProperty("interchange");
        Assert.Equal("import", interchange.GetProperty("operation").GetString());
        Assert.Equal(3, interchange.GetProperty("units").GetProperty("added").GetInt32());
        Assert.Equal(5, interchange.GetProperty("units").GetProperty("skipped").GetInt32());
        Assert.True(interchange.GetProperty("entries").EnumerateArray().Any(entry => entry.GetProperty("key").GetString() == "cart_empty" && entry.GetProperty("code").GetString() == "XLIFF21-UNTRANSLATED"), payload.GetRawText());
        Assert.True(interchange.GetProperty("notices").EnumerateArray().Any(notice => notice.GetProperty("code").GetString() == "XLIFF21-NOTE-IGNORED"), payload.GetRawText());
        Assert.Equal("translations/de.rmf2", interchange.GetProperty("files")[0].GetProperty("path").GetString());

        ProcessResult validate = TestFixture.RunTool(temporary, "validate", "--project", "translations");
        Assert.Equal(0, validate.ExitCode, validate.Combined);
    }

    [Test, DisplayName("CLI xliff import refuses single units with a warning and whole documents with RTR0091")]
    public void XliffImportRefusals()
    {
        using TemporaryDirectory temporary = CopyProject();
        ProcessResult partial = TestFixture.RunTool(temporary, "xliff", "import", "--project", "translations", "--source", Path.Combine(Corpus, "files", "partial.de.xliff"), "--format", "json");
        Assert.Equal(0, partial.ExitCode, partial.Combined);
        Assert.FileBytesEqual(Path.Combine(Corpus, "expected", "partial.de.rmf2"), temporary.Resolve("translations", "de.rmf2"));
        using (JsonDocument envelope = JsonDocument.Parse(partial.StandardOutput))
            Assert.True(envelope.RootElement.GetProperty("diagnostics").EnumerateArray().Any(diagnostic =>
                diagnostic.GetProperty("code").GetString() == "RCLI8216" && diagnostic.GetProperty("message").GetString()!.Contains("cart_checkout_total: XLIFF21-PLACEHOLDER", StringComparison.Ordinal)),
                partial.StandardOutput);

        File.WriteAllText(temporary.Resolve("other.xliff"), File.ReadAllText(Path.Combine(Corpus, "files", "partial.de.xliff")).Replace("<file id=\"shop\"", "<file id=\"other\"", StringComparison.Ordinal), new UTF8Encoding(false));
        ProcessResult other = TestFixture.RunTool(temporary, "xliff", "import", "--project", "translations", "--source", "other.xliff", "--format", "json");
        Assert.Equal(2, other.ExitCode, other.Combined);
        using JsonDocument failure = JsonDocument.Parse(other.StandardOutput);
        Assert.Equal("RTR0091", failure.RootElement.GetProperty("fault").GetProperty("code").GetString());
        Assert.True(failure.RootElement.GetProperty("diagnostics").EnumerateArray().Any(diagnostic =>
            diagnostic.GetProperty("code").GetString() == "RCLI8215" && diagnostic.GetProperty("message").GetString()!.StartsWith("XLIFF21-CATALOG-MISMATCH", StringComparison.Ordinal)), other.StandardOutput);
        ProcessResult human = TestFixture.RunTool(temporary, "xliff", "import", "--project", "translations", "--source", "other.xliff");
        Assert.Equal(2, human.ExitCode, human.Combined);
        Assert.Contains("XLIFF21-CATALOG-MISMATCH", human.StandardError);
    }

    [Test, DisplayName("CLI review mark, validate RTR0089 and xliff export demote stale approvals per unit")]
    public void ReviewFileTracksStaleTranslations()
    {
        using TemporaryDirectory temporary = CopyProject();
        // The corpus review file approved farewell while its source read "Goodbye".
        ProcessResult stale = TestFixture.RunTool(temporary, "validate", "--project", "translations", "--format", "json");
        Assert.Equal(0, stale.ExitCode, stale.Combined);
        using (JsonDocument envelope = JsonDocument.Parse(stale.StandardOutput))
        {
            JsonElement diagnostic = envelope.RootElement.GetProperty("payload").GetProperty("diagnostics").EnumerateArray().Single();
            Assert.Equal("RTR0089", diagnostic.GetProperty("code").GetString());
            Assert.Equal("warning", diagnostic.GetProperty("severity").GetString());
            Assert.Equal("translations/de.rmf2", diagnostic.GetProperty("path").GetString());
            Assert.Contains("'farewell'", diagnostic.GetProperty("message").GetString()!);
            TestFixture.AssertReleaseHelpUri(diagnostic.GetProperty("helpUri").GetString(), "RTR0089");
        }

        ProcessResult export = TestFixture.RunTool(temporary, "xliff", "export", "--project", "translations", "--output", "xliff", "--format", "json");
        Assert.Equal(0, export.ExitCode, export.Combined);
        Assert.FileBytesEqual(Path.Combine(Corpus, "files", "export.de.xliff"), temporary.Resolve("xliff", "shop.de.xliff"));
        using (JsonDocument envelope = JsonDocument.Parse(export.StandardOutput))
        {
            JsonElement payload = envelope.RootElement.GetProperty("payload");
            PayloadSchemas.Validate(PayloadSchemas.Tool, payload);
            Assert.Equal(1, payload.GetProperty("interchange").GetProperty("units").GetProperty("demoted").GetInt32());
            Assert.Equal("farewell", payload.GetProperty("interchange").GetProperty("entries")[0].GetProperty("key").GetString());
        }

        ProcessResult mark = TestFixture.RunTool(temporary, "review", "mark", "--project", "translations", "--key", "farewell", "--locale", "de", "--state", "approved", "--note", "Checked.");
        Assert.Equal(0, mark.ExitCode, mark.Combined);
        Assert.Contains("marked 1 translation(s) in de as approved; wrote translations/shop.review.json.", mark.StandardOutput);
        Assert.Contains("\"note\": \"Checked.\"", File.ReadAllText(temporary.Resolve("translations", "shop.review.json")));
        ProcessResult current = TestFixture.RunTool(temporary, "validate", "--project", "translations");
        Assert.Equal(0, current.ExitCode, current.Combined);
        Assert.False(current.Combined.Contains("RTR0089", StringComparison.Ordinal), current.Combined);

        ProcessResult invalid = TestFixture.RunTool(temporary, "review", "mark", "--project", "translations", "--key", "farewell", "--locale", "en", "--state", "approved");
        Assert.Equal(2, invalid.ExitCode, invalid.Combined);
        Assert.Contains("--locale en is not a translated locale", invalid.StandardError);
        ProcessResult state = TestFixture.RunTool(temporary, "review", "mark", "--project", "translations", "--key", "farewell", "--locale", "de", "--state", "final");
        Assert.Equal(2, state.ExitCode, state.Combined);
        Assert.Contains("--state expects draft, translated, needs-review or approved.", state.StandardError);

        // A broken review file is a warning, not a failed validation.
        File.WriteAllText(temporary.Resolve("translations", "shop.review.json"), "{", new UTF8Encoding(false));
        ProcessResult broken = TestFixture.RunTool(temporary, "validate", "--project", "translations");
        Assert.Equal(0, broken.ExitCode, broken.Combined);
        Assert.Contains("warning RTR0090", broken.StandardError);
    }

    [Test, DisplayName("CLI xliff import of a changed source records needs-review with the old fingerprint, or refuses it")]
    public void XliffImportOfChangedSources()
    {
        using TemporaryDirectory temporary = CopyProject();
        string file = Path.Combine(Corpus, "files", "stale-approval.de.xliff");
        ProcessResult refuse = TestFixture.RunTool(temporary, "xliff", "import", "--project", "translations", "--source", file, "--source-changed", "refuse");
        Assert.Equal(0, refuse.ExitCode, refuse.Combined);
        Assert.Contains("refused farewell: XLIFF21-SOURCE-CHANGED", refuse.StandardOutput);

        using TemporaryDirectory second = CopyProject();
        ProcessResult review = TestFixture.RunTool(second, "xliff", "import", "--project", "translations", "--source", file);
        Assert.Equal(0, review.ExitCode, review.Combined);
        Assert.Contains("1 translation(s) of changed sources were imported as needs-review.", review.StandardOutput);
        Assert.FileBytesEqual(Path.Combine(Corpus, "expected", "stale-approval.de.rmf2"), second.Resolve("translations", "de.rmf2"));
        using (JsonDocument reviewFile = JsonDocument.Parse(File.ReadAllBytes(second.Resolve("translations", "shop.review.json"))))
        {
            Assert.Equal("runic.translations.interchange-review/2", reviewFile.RootElement.GetProperty("$schema").GetString());
            JsonElement farewell = reviewFile.RootElement.GetProperty("entries").EnumerateArray().Single(entry => entry.GetProperty("key").GetString() == "farewell");
            Assert.Equal("needs-review", farewell.GetProperty("state").GetString());
        }
        ProcessResult validate = TestFixture.RunTool(second, "validate", "--project", "translations");
        Assert.Contains("warning RTR0089", validate.StandardError);

        ProcessResult help = TestFixture.RunTool(second, "--help");
        Assert.Contains("xliff ", help.StandardOutput);
        Assert.Contains("review ", help.StandardOutput);
        ProcessResult policy = TestFixture.RunTool(second, "xliff", "import", "--project", "translations", "--source", file, "--source-changed", "keep");
        Assert.Equal(2, policy.ExitCode, policy.Combined);
        Assert.Contains("--source-changed expects needs-review or refuse.", policy.StandardError);
    }

    private static TemporaryDirectory CopyProject()
    {
        var temporary = new TemporaryDirectory();
        Directory.CreateDirectory(temporary.Resolve("translations"));
        foreach (string file in Directory.GetFiles(Path.Combine(Corpus, "project")))
            File.Copy(file, temporary.Resolve("translations", Path.GetFileName(file)));
        return temporary;
    }
}
