using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using TUnit.Core;

namespace Runic.Translations.Build.Tests;

// runic-translations import --from resx|i18next on copies of specs/translations/corpus/migration.
internal sealed class MigrationCliTests
{
    private static readonly string Corpus = RepositoryPaths.Resolve("specs", "translations", "corpus", "migration");

    [Test, DisplayName("CLI import --from resx writes base messages with descriptions, translations beside them, and reports what it could not map")]
    public void ImportsResx()
    {
        using TemporaryDirectory temporary = CopyCorpus();
        ProcessResult import = TestFixture.RunTool(temporary, "import", "--from", "resx", "resx/Strings.resx", "--format", "json");
        Assert.Equal(0, import.ExitCode, import.Combined);
        Assert.FileBytesEqual(Path.Combine(Corpus, "expected", "resx.en.rmf2"), temporary.Resolve("translations", "en.rmf2"));
        Assert.FileBytesEqual(Path.Combine(Corpus, "expected", "resx.de.rmf2"), temporary.Resolve("translations", "de.rmf2"));
        using (JsonDocument envelope = JsonDocument.Parse(import.StandardOutput))
        {
            JsonElement payload = envelope.RootElement.GetProperty("payload");
            PayloadSchemas.Validate(PayloadSchemas.Tool, payload);
            JsonElement interchange = payload.GetProperty("interchange");
            Assert.Equal("import", interchange.GetProperty("operation").GetString());
            Assert.Equal("resx", interchange.GetProperty("format").GetString());
            Assert.Equal(13, interchange.GetProperty("units").GetProperty("added").GetInt32());
            Assert.Equal(2, interchange.GetProperty("units").GetProperty("skipped").GetInt32());
            Assert.Equal(2, interchange.GetProperty("units").GetProperty("refused").GetInt32());
            AssertEntry(interchange, "en", "Logo", "skipped", "RESX-NON-STRING");
            AssertEntry(interchange, "en", ">>saveButton.Name", "skipped", "RESX-DESIGNER");
            AssertEntry(interchange, "de", "Save", "refused", "IMPORT-INVALID");
            AssertEntry(interchange, "de", "OnlyGerman", "refused", "IMPORT-NO-BASE");
            AssertNotice(interchange, "resx/Strings.resx:DueDate", "RESX-FORMAT");
            AssertNotice(interchange, "resx/Strings.fr.resx", "IMPORT-LOCALE");
            Assert.Equal("resx/Strings.de.resx", interchange.GetProperty("documents")[1].GetProperty("path").GetString());
            Assert.True(envelope.RootElement.GetProperty("diagnostics").EnumerateArray().Any(diagnostic =>
                diagnostic.GetProperty("code").GetString() == "RCLI8216" && diagnostic.GetProperty("message").GetString()!.StartsWith("de Save: IMPORT-INVALID: RTR0016", StringComparison.Ordinal)),
                import.StandardOutput);
        }

        ProcessResult validate = TestFixture.RunTool(temporary, "validate");
        Assert.Equal(0, validate.ExitCode, validate.Combined);

        // Importing the same files again changes nothing.
        ProcessResult again = TestFixture.RunTool(temporary, "import", "--from", "resx", "resx/Strings.resx");
        Assert.Equal(0, again.ExitCode, again.Combined);
        Assert.Contains("imported resx: en 0 added, 0 changed, 10 unchanged; de 0 added, 0 changed, 3 unchanged; 2 skipped, 2 refused.", again.StandardOutput);
        Assert.False(again.StandardOutput.Contains("wrote ", StringComparison.Ordinal), again.StandardOutput);

        // A changed base text updates the message in place and keeps its description.
        File.WriteAllText(temporary.Resolve("resx", "Strings.resx"), File.ReadAllText(temporary.Resolve("resx", "Strings.resx")).Replace("Hello, {0}!", "Hi, {0}!", StringComparison.Ordinal), new UTF8Encoding(false));
        ProcessResult changed = TestFixture.RunTool(temporary, "import", "--from", "resx", "resx/Strings.resx", "--locale", "en");
        Assert.Equal(0, changed.ExitCode, changed.Combined);
        Assert.Contains("imported resx: en 0 added, 1 changed, 9 unchanged; 2 skipped, 0 refused.", changed.StandardOutput);
        Assert.Contains("# Shown on the start page. {0} is the user's name.\nGreeting = Hi, {$arg0}!\n", File.ReadAllText(temporary.Resolve("translations", "en.rmf2")));
    }

    [Test, DisplayName("CLI import --from i18next maps nested keys, namespaces, interpolation and plural suffixes to RMF2")]
    public void ImportsI18next()
    {
        using TemporaryDirectory temporary = CopyCorpus();
        ProcessResult dryRun = TestFixture.RunTool(temporary, "import", "--from", "i18next", "i18next/locales", "--dry-run");
        Assert.Equal(0, dryRun.ExitCode, dryRun.Combined);
        Assert.Contains("dry run: imported i18next: en 13 added, 0 changed, 0 unchanged; de 6 added, 0 changed, 0 unchanged; 2 skipped, 2 refused.", dryRun.StandardOutput);
        Assert.Contains("would write translations/en.rmf2.", dryRun.StandardOutput);
        Assert.FileBytesEqual(Path.Combine(Corpus, "project", "en.rmf2"), temporary.Resolve("translations", "en.rmf2"));

        ProcessResult import = TestFixture.RunTool(temporary, "import", "--from", "i18next", "i18next/locales", "--format", "json");
        Assert.Equal(0, import.ExitCode, import.Combined);
        Assert.FileBytesEqual(Path.Combine(Corpus, "expected", "i18next.en.rmf2"), temporary.Resolve("translations", "en.rmf2"));
        Assert.FileBytesEqual(Path.Combine(Corpus, "expected", "i18next.de.rmf2"), temporary.Resolve("translations", "de.rmf2"));
        using (JsonDocument envelope = JsonDocument.Parse(import.StandardOutput))
        {
            JsonElement payload = envelope.RootElement.GetProperty("payload");
            PayloadSchemas.Validate(PayloadSchemas.Tool, payload);
            JsonElement interchange = payload.GetProperty("interchange");
            Assert.Equal("i18next", interchange.GetProperty("format").GetString());
            AssertEntry(interchange, "en", "seeWelcome", "refused", "I18NEXT-NESTING");
            AssertEntry(interchange, "en", "colors", "skipped", "I18NEXT-ARRAY");
            AssertEntry(interchange, "en", "limit", "skipped", "I18NEXT-NON-STRING");
            AssertEntry(interchange, "de", "unknown", "refused", "IMPORT-NO-BASE");
            AssertNotice(interchange, "i18next/locales/de/translation.json:cart.items_*", "I18NEXT-PLURAL-CATEGORY");
            AssertNotice(interchange, "i18next/locales/en/translation.json:price", "I18NEXT-FORMAT");
            AssertNotice(interchange, "i18next/locales/en/translation.json:terms", "I18NEXT-MARKUP");
            AssertNotice(interchange, "i18next/locales/en/translation.json:sign-in", "IMPORT-KEY-RENAMED");
            AssertNotice(interchange, "i18next/locales/en/translation.json:lonely_one", "I18NEXT-PLURAL-INCOMPLETE");
            AssertNotice(interchange, "i18next/locales/fr/translation.json", "IMPORT-LOCALE");
        }

        ProcessResult generate = TestFixture.RunTool(temporary, "generate", "--output", "generated", "--emit-csharp");
        Assert.Equal(0, generate.ExitCode, generate.Combined);
        string csharp = string.Concat(Directory.GetFiles(temporary.Resolve("generated"), "*.cs", SearchOption.AllDirectories).Select(File.ReadAllText));
        foreach (string member in new[] { "cart_items", "common_cancel", "place", "sign_in", "profile" })
            Assert.Contains(member, csharp);
    }

    [Test, DisplayName("CLI import --from selects locales, names a file's locale and reports unreadable sources with RTR0092")]
    public void ImportSelectionAndFailures()
    {
        using TemporaryDirectory temporary = CopyCorpus();
        // A satellite alone, before its base: every message lacks a base.
        ProcessResult satellite = TestFixture.RunTool(temporary, "import", "--from", "resx", "resx/Strings.de.resx");
        Assert.Equal(0, satellite.ExitCode, satellite.Combined);
        Assert.Contains("imported resx: de 0 added, 0 changed, 0 unchanged; 0 skipped, 5 refused.", satellite.StandardOutput);

        // A file without a locale in its name takes the single --locale.
        Directory.CreateDirectory(temporary.Resolve("flat"));
        File.WriteAllText(temporary.Resolve("flat", "messages.json"), "{ \"hello\": \"Hello\" }", new UTF8Encoding(false));
        ProcessResult named = TestFixture.RunTool(temporary, "import", "--from", "i18next", "flat/messages.json", "--locale", "en", "--group", "legacy");
        Assert.Equal(0, named.ExitCode, named.Combined);
        Assert.Equal("hello = Hello\n", File.ReadAllText(temporary.Resolve("translations", "legacy", "en.rmf2")));
        ProcessResult unnamed = TestFixture.RunTool(temporary, "import", "--from", "i18next", "flat/messages.json");
        Assert.Equal(2, unnamed.ExitCode, unnamed.Combined);
        Assert.Contains("pass one --locale", unnamed.StandardError);

        File.WriteAllText(temporary.Resolve("flat", "en.json"), "{ \"hello\": ", new UTF8Encoding(false));
        ProcessResult malformed = TestFixture.RunTool(temporary, "import", "--from", "i18next", "flat/en.json", "--format", "json");
        Assert.Equal(2, malformed.ExitCode, malformed.Combined);
        using (JsonDocument failure = JsonDocument.Parse(malformed.StandardOutput))
        {
            Assert.Equal("RTR0092", failure.RootElement.GetProperty("fault").GetProperty("code").GetString());
            Assert.True(failure.RootElement.GetProperty("diagnostics").EnumerateArray().Any(diagnostic =>
                diagnostic.GetProperty("code").GetString() == "RCLI8215" && diagnostic.GetProperty("message").GetString()!.StartsWith("I18NEXT-MALFORMED", StringComparison.Ordinal)), malformed.StandardOutput);
        }

        ProcessResult po = TestFixture.RunTool(temporary, "import", "--from", "po", "messages.po");
        Assert.Equal(2, po.ExitCode, po.Combined);
        ProcessResult undeclared = TestFixture.RunTool(temporary, "import", "--from", "i18next", "i18next/locales", "--locale", "fr");
        Assert.Equal(2, undeclared.ExitCode, undeclared.Combined);
        Assert.Contains("--locale 'fr' is not declared in runic.json.", undeclared.StandardError);

        ProcessResult help = TestFixture.RunTool(temporary, "help", "import");
        Assert.Equal(0, help.ExitCode, help.Combined);
        Assert.Contains("PO files are not supported", help.StandardOutput);
    }

    private static void AssertEntry(JsonElement interchange, string locale, string key, string outcome, string code) =>
        Assert.True(interchange.GetProperty("entries").EnumerateArray().Any(entry =>
            entry.GetProperty("key").GetString() == key && entry.GetProperty("locale").GetString() == locale &&
            entry.GetProperty("outcome").GetString() == outcome && entry.GetProperty("code").GetString() == code),
            $"no {outcome} {code} entry for {locale} {key}:\n{interchange.GetRawText()}");

    private static void AssertNotice(JsonElement interchange, string location, string code) =>
        Assert.True(interchange.GetProperty("notices").EnumerateArray().Any(notice =>
            notice.GetProperty("location").GetString() == location && notice.GetProperty("code").GetString() == code),
            $"no {code} notice at {location}:\n{interchange.GetRawText()}");

    private static TemporaryDirectory CopyCorpus()
    {
        var temporary = new TemporaryDirectory();
        Copy(Path.Combine(Corpus, "project"), temporary.Resolve("translations"));
        Copy(Path.Combine(Corpus, "resx"), temporary.Resolve("resx"));
        Copy(Path.Combine(Corpus, "i18next"), temporary.Resolve("i18next"));
        return temporary;
    }

    private static void Copy(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (string file in Directory.GetFiles(source)) File.Copy(file, Path.Combine(target, Path.GetFileName(file)));
        foreach (string directory in Directory.GetDirectories(source)) Copy(directory, Path.Combine(target, Path.GetFileName(directory)));
    }
}
