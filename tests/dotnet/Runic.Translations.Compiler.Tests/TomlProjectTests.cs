using System;
using System.Linq;
using System.Text;
using System.Threading;
using Runic.Translations.Compiler;

namespace Runic.Translations.Compiler.Tests;

internal static class TomlProjectTests
{
    public static void Register(TestRunner runner)
    {
        runner.Add("TOML locale messages preserve legacy compiled patterns and inputs", Equivalent);
        runner.Add("TOML syntax reader decodes strings and maps UTF8 token spans", DecodingAndSpans);
        runner.Add("TOML profile rejects structural and duplicate entries", RejectedProfiles);
        runner.Add("TOML project rejects mixed layouts and canonical locale collisions", Identity);
        runner.Add("TOML reader bounds bytes keys values depth and cancellation", Limits);
        runner.Add("TOML MF2 diagnostics identify the physical value", Mf2Location);
    }

    private static TranslationSource Project(bool toml = true) => Source("translations/runic.json",
        "{\"schemaVersion\":1,\"catalog\":\"app\",\"code\":{\"namespace\":\"Example\",\"className\":\"AppText\"},\"baseLocale\":\"en\"" +
        (toml ? ",\"sourceLayout\":\"locale-toml\"" : string.Empty) + "}");

    private static void Equivalent()
    {
        const string plural = ".input {$count :integer select=plural}\n.match $count\none {{One file}}\n* {{{$count} files}}";
        TranslationCompilation legacy = TranslationCompiler.CompileMf2Project(Project(false),
            [Source("translations/en/title.mf2", " Title \r\n"), Source("translations/en/count.mf2", plural)]);
        TranslationCompilation toml = TranslationCompiler.CompileProject(Project(),
            [Source("translations/en.toml", "title = ''' Title \r\n'''\ncount = '''\n" + plural + "'''\n")]);
        Assert.True(legacy.Success && toml.Success, Errors(toml));
        Assert.Equal(legacy.Catalogs[0].CanonicalResources.Count, toml.Catalogs[0].CanonicalResources.Count);
        for (int i = 0; i < legacy.Catalogs[0].CanonicalResources.Count; i++)
        {
            CompiledTranslation left = legacy.Catalogs[0].CanonicalResources[i];
            CompiledTranslation right = toml.Catalogs[0].CanonicalResources[i];
            Assert.Equal(left.Key, right.Key);
            Assert.Equal(left.Pattern, right.Pattern);
            Assert.Equal(left.Placeholders.Count, right.Placeholders.Count);
        }
        Assert.True(TranslationCompiler.CompileMf2Project(Project(), [Source("translations/en.toml", "title = 'Title'")]).Success,
            "Legacy API did not delegate layout dispatch.");
    }

    private static void DecodingAndSpans()
    {
        const string text = "# 😀 comment\r\nplain = 'café 😀' # keep\r\n\"escaped\" = \"x\\n\\u00E9\\U0001F600\"\r\n" +
            "multi = '''\r\nfirst\r\nsecond'''\r\ncontinued = \"\"\"\nfirst\\\n  second\"\"\"\nempty = ''\n";
        TranslationSource source = Source("en.toml", text);
        TranslationLocaleDocument document = TranslationLocaleReader.Read(source, "en");
        Assert.True(document.Success, string.Join("\n", document.Diagnostics.Select(d => d.Message)));
        Assert.Equal(5, document.Entries.Count);
        Assert.Equal("café 😀", Decode(document.Entries[0].Message));
        Assert.Equal("x\né😀", Decode(document.Entries[1].Message));
        Assert.Equal("first\r\nsecond", Decode(document.Entries[2].Message));
        Assert.Equal("firstsecond", Decode(document.Entries[3].Message));
        Assert.Equal(string.Empty, Decode(document.Entries[4].Message));
        foreach (TranslationLocaleEntry entry in document.Entries)
        {
            byte[] bytes = source.GetUtf8Bytes();
            string raw = Encoding.UTF8.GetString(bytes, entry.ValueLocation.StartByte, entry.ValueLocation.LengthBytes);
            TranslationLocaleDocument reparsed = TranslationLocaleReader.Read(Source("en.toml", "same = " + raw), "en");
            Assert.True(reparsed.Success, "Raw value span did not delimit a complete TOML string.");
            Assert.Equal(Decode(entry.Message), Decode(reparsed.Entries[0].Message));
            Assert.Equal("en.toml", entry.Message.Path);
        }
        Assert.Equal(2, document.Entries[0].KeyLocation.Line);
        Assert.Equal(1, document.Entries[0].KeyLocation.Column);
        Assert.Equal("plain = 'café 😀'", Encoding.UTF8.GetString(source.GetUtf8Bytes(),
            document.Entries[0].StatementLocation.StartByte, document.Entries[0].StatementLocation.LengthBytes));
    }

    private static void RejectedProfiles()
    {
        string[] invalid = ["[table]\nx='a'", "x.y='a'", "'x.y'='a'", "x=[]", "x={y='a'}", "x=1", "x=true",
            "x='a'\nx='b'", "x='a'\n\"x\"='b'", "bad-key='a'", "x=\"unterminated", "x=\"\\q\""];
        foreach (string text in invalid)
            Assert.True(!TranslationLocaleReader.Read(Source("en.toml", text), "en").Success, "Accepted invalid profile: " + text);
        Assert.True(!TranslationLocaleReader.Read(new TranslationSource("en.toml", [0xff]), "en").Success, "Accepted invalid UTF8.");
    }

    private static void Identity()
    {
        foreach (TranslationSource[] sources in new[]
        {
            new[] { Source("translations/en.toml", "x='a'"), Source("translations/en/x.mf2", "a") },
            new[] { Source("translations/en.toml", "x='a'"), Source("translations/EN.toml", "x='a'") },
            new[] { Source("translations/sub/en.toml", "x='a'") },
            new[] { Source("elsewhere/en.toml", "x='a'") },
        }) Assert.True(!TranslationCompiler.CompileProject(Project(), sources).Success, "Accepted invalid source identities.");
        Assert.True(!TranslationCompiler.CompileProject(Project(false), [Source("translations/en.toml", "x='a'")]).Success,
            "Absent layout silently interpreted TOML.");
        Assert.True(TranslationCompiler.CompileProject(Project(), [Source("translations/en.toml", "# empty")]).Success,
            "Empty locale document rejected.");
    }

    private static void Limits()
    {
        Assert.True(!TranslationLocaleReader.Read(Source("en.toml", "x='abcdef'"), "en", new TranslationCompilerOptions(maximumDocumentBytes: 5)).Success, "Document limit ignored.");
        Assert.True(!TranslationLocaleReader.Read(Source("en.toml", "x='éé'"), "en", new TranslationCompilerOptions(maximumValueBytes: 3)).Success, "Decoded UTF8 limit ignored.");
        Assert.True(!TranslationLocaleReader.Read(Source("en.toml", "x='a'\ny='b'"), "en", new TranslationCompilerOptions(maximumKeysPerCatalog: 1)).Success, "Key limit ignored.");
        Assert.True(!TranslationLocaleReader.Read(Source("en.toml", "x=[[[['a']]]]"), "en", new TranslationCompilerOptions(maximumDepth: 2)).Success, "Depth limit ignored.");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        bool canceled = false;
        try { TranslationLocaleReader.Read(Source("en.toml", "x='a'"), "en", cancellationToken: cancellation.Token); }
        catch (OperationCanceledException) { canceled = true; }
        Assert.True(canceled, "Cancellation ignored.");
    }

    private static void Mf2Location()
    {
        const string content = "first='Fine'\nsecond='{broken'\n";
        TranslationCompilation result = TranslationCompiler.CompileProject(Project(), [Source("translations/en.toml", content)]);
        Assert.True(!result.Success, "Invalid MF2 accepted.");
        TranslationDiagnostic diagnostic = result.Diagnostics.First(d => d.Location.Line == 2);
        Assert.Equal("translations/en.toml", diagnostic.Location.Path);
        Assert.Equal("'{broken'", Encoding.UTF8.GetString(Encoding.UTF8.GetBytes(content), diagnostic.Location.StartByte, diagnostic.Location.LengthBytes));
    }

    private static string Errors(TranslationCompilation compilation) => string.Join("\n", compilation.Diagnostics.Select(d => d.Id + ": " + d.Message));
    private static string Decode(TranslationSource source) => Encoding.UTF8.GetString(source.GetUtf8Bytes());
    private static TranslationSource Source(string path, string text) => new(path, Encoding.UTF8.GetBytes(text));
}
