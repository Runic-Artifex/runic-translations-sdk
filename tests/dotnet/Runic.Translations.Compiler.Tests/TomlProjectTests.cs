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
        runner.Add("TOML nested groups preserve identifiers metadata and physical spans", Groups);
        runner.Add("TOML grouping rejects flattened collisions and namespace redefinitions", GroupCollisions);
        runner.Add("TOML inline containers preserve spans nesting and compiled identifiers", InlineTables);
        runner.Add("TOML inline namespaces collisions and limits are validated", InlineValidation);
        runner.Add("TOML array tables use stable identities across order and nested parents", ArrayTables);
        runner.Add("TOML array table identities reject missing duplicate and ambiguous leaves", ArrayTableValidation);
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
        string[] invalid = ["[[table]]\nx='a'", "'x.y'='a'", "x=[1]", "x=1", "x=true",
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

    private static void Groups()
    {
        const string text = "# 😀 groups\r\nroot.dotted = 'Root'\r\n[document] # scope\r\nsaved = 'Saved'\r\n" +
            "dialog.\"title\" = 'Dialog'\r\n[document.nested]\r\nmessage = 'Nested'\r\n[empty]\r\n";
        TranslationSource source = Source("translations/en.toml", text);
        TranslationLocaleDocument document = TranslationLocaleReader.Read(source, "en");
        Assert.True(document.Success, string.Join("\n", document.Diagnostics.Select(d => d.Message)));
        Assert.Equal("root_dotted,document_saved,document_dialog_title,document_nested_message",
            string.Join(",", document.Entries.Select(entry => entry.Key)));
        TranslationLocaleEntry nested = document.Entries[3];
        Assert.Equal("document,nested", string.Join(",", nested.TablePath));
        Assert.Equal("message", string.Join(",", nested.KeyPath));
        Assert.Equal("dialog,title", string.Join(",", document.Entries[2].KeyPath));
        Assert.Equal("dialog.\"title\"", Encoding.UTF8.GetString(source.GetUtf8Bytes(),
            document.Entries[2].KeyLocation.StartByte, document.Entries[2].KeyLocation.LengthBytes));
        Assert.Equal(Encoding.UTF8.GetByteCount(text.Substring(0, text.IndexOf("[document]", StringComparison.Ordinal))), document.RootInsertionByte);
        Assert.Equal(3, document.Tables.Count);
        Assert.Equal("document,nested", string.Join(",", document.Tables[1].Path));
        Assert.Equal("[document]", Encoding.UTF8.GetString(source.GetUtf8Bytes(),
            document.Tables[0].HeaderLocation.StartByte, document.Tables[0].HeaderLocation.LengthBytes));
        Assert.Equal(document.Tables[1].HeaderLocation.StartByte, document.Tables[0].InsertionByte);
        Assert.Equal(source.GetUtf8Bytes().Length, document.Tables[2].InsertionByte);
        TranslationCompilation grouped = TranslationCompiler.CompileProject(Project(), [source]);
        TranslationCompilation flat = TranslationCompiler.CompileProject(Project(),
            [Source("translations/en.toml", "root_dotted='Root'\ndocument_saved='Saved'\ndocument_dialog_title='Dialog'\ndocument_nested_message='Nested'")]);
        Assert.True(grouped.Success && flat.Success, Errors(grouped));
        Assert.Equal(flat.Catalogs[0].Fingerprint, grouped.Catalogs[0].Fingerprint);
        Assert.True(flat.Catalogs[0].CanonicalResources.Select(entry => entry.Pattern)
            .SequenceEqual(grouped.Catalogs[0].CanonicalResources.Select(entry => entry.Pattern)), "Grouping changed messages.");
        Assert.Equal(0, TranslationLocaleReader.Read(Source("en.toml", "[empty]\n"), "en").RootInsertionByte);
        Assert.Equal(5, TranslationLocaleReader.Read(Source("en.toml", "x='y'"), "en").RootInsertionByte);
        Assert.True(TranslationLocaleReader.Read(Source("en.toml", "[a.b]\nx='X'\n[a]\ny='Y'"), "en").Success,
            "TOML implicit parent followed by explicit parent was rejected.");
    }

    private static void GroupCollisions()
    {
        string[] invalid = ["document_saved='A'\n[document]\nsaved='B'", "a_b.c='A'\na.b_c='B'",
            "x='A'\n[x]\ny='B'", "[x]\na='A'\n[x]\nb='B'", "x.y='A'\nx.y='B'",
            "[bad-name]\nx='A'", "[\"literal.dot\"]\nx='A'", "[[x]]\na='A'", "[x]\na=[1]"];
        foreach (string text in invalid)
            Assert.True(!TranslationLocaleReader.Read(Source("en.toml", text), "en").Success, "Invalid grouped source accepted: " + text);
        TranslationLocaleDocument collision = TranslationLocaleReader.Read(Source("en.toml", "x_y='A'\n[x]\ny='B'"), "en");
        Assert.True(collision.Diagnostics.Any(d => d.Id == "RTR0002" && d.Location.Line == 3), "Flattened collision lacks the conflicting physical key location.");
        Assert.True(!TranslationLocaleReader.Read(Source("en.toml", "root='A'\n[a]\nx='B'"), "en",
            new TranslationCompilerOptions(maximumKeysPerCatalog: 1)).Success, "Total key count across groups was not bounded.");
        Assert.True(!TranslationLocaleReader.Read(Source("en.toml", "[a.b]\nc='C'"), "en",
            new TranslationCompilerOptions(maximumDepth: 2)).Success, "Combined table/key nesting depth was not bounded.");
        Assert.True(!TranslationLocaleReader.Read(Source("en.toml", "[a.b.c]"), "en",
            new TranslationCompilerOptions(maximumDepth: 2)).Success, "Empty table nesting depth was not bounded.");
    }

    private static void InlineTables()
    {
        const string text = "# 😀 inline\r\n\"app\" = { \"title\" = 'Title', nested = { 'message' = \"\"\"\nLine one\nLine two\"\"\", }, empty = {} }\r\n" +
            "[\"document\".dialog]\r\nstate = { saved = 'Saved', details.text = 'Detail' }\r\n";
        TranslationSource source = Source("translations/en.toml", text);
        TranslationLocaleDocument document = TranslationLocaleReader.Read(source, "en");
        Assert.True(document.Success, string.Join("\n", document.Diagnostics.Select(d => d.Message)));
        Assert.Equal("app_title,app_nested_message,document_dialog_state_saved,document_dialog_state_details_text",
            string.Join(",", document.Entries.Select(entry => entry.Key)));
        Assert.Equal("app,nested", string.Join(",", document.Entries[1].InlinePath));
        Assert.Equal("document,dialog", string.Join(",", document.Entries[2].TablePath));
        Assert.Equal("state", string.Join(",", document.Entries[2].InlinePath));
        Assert.Equal("details,text", string.Join(",", document.Entries[3].KeyPath));
        Assert.Equal("Line one\nLine two", Decode(document.Entries[1].Message));
        Assert.Equal(4, document.InlineTables.Count);
        Assert.Equal("document,dialog,state", string.Join(",", document.InlineTables[3].Path));
        foreach (TranslationLocaleInlineTable table in document.InlineTables)
        {
            string value = Slice(table.ValueLocation);
            Assert.True(value.StartsWith('{') && value.EndsWith('}'), "Inline table span excludes braces.");
            Assert.True(TranslationLocaleReader.Read(Source("en.toml", "container = " + value), "en").Success, "Inline table value span is not independently valid.");
            foreach (TranslationLocaleInlineMember member in table.Members)
            {
                Assert.True(Slice(member.StatementLocation).Contains('=', StringComparison.Ordinal), "Inline statement span omitted equals.");
                if (member.SeparatorLocation is not null) Assert.Equal(",", Slice(member.SeparatorLocation));
            }
        }
        Assert.True(document.InlineTables[1].Members[0].SeparatorLocation is not null, "TOML 1.1 trailing comma metadata missing.");
        TranslationCompilation inline = TranslationCompiler.CompileProject(Project(), [source]);
        TranslationCompilation flat = TranslationCompiler.CompileProject(Project(), [Source("translations/en.toml",
            "app_title='Title'\napp_nested_message=\"Line one\\nLine two\"\ndocument_dialog_state_saved='Saved'\ndocument_dialog_state_details_text='Detail'")]);
        Assert.True(inline.Success && flat.Success, Errors(inline));
        Assert.Equal(flat.Catalogs[0].Fingerprint, inline.Catalogs[0].Fingerprint);
        string Slice(TextSourceLocation location) => Encoding.UTF8.GetString(source.GetUtf8Bytes(), location.StartByte, location.LengthBytes);
    }

    private static void InlineValidation()
    {
        string[] invalid = ["app={title='A'}\n[app]\nother='B'", "app={title='A'}\napp.other='B'",
            "app_title='A'\napp={title='B'}", "a_b={c='A'}\na={b_c='B'}", "app={x=[1]}", "app={x=1}",
            "app={x='A',x='B'}", "app={\"bad.name\"='A'}", "[normal]\napp_title='A'\napp={title='B'}"];
        foreach (string text in invalid)
            Assert.True(!TranslationLocaleReader.Read(Source("en.toml", text), "en").Success, "Invalid inline source accepted: " + text);
        Assert.True(!TranslationLocaleReader.Read(Source("en.toml", "app={a='A',b='B'}"), "en",
            new TranslationCompilerOptions(maximumKeysPerCatalog: 2)).Success, "Inline keys were not included in the document bound.");
        Assert.True(!TranslationLocaleReader.Read(Source("en.toml", "app={nested={leaf='A'}}"), "en",
            new TranslationCompilerOptions(maximumDepth: 2)).Success, "Inline nesting depth was not bounded.");
    }

    private static void ArrayTables()
    {
        const string text = "_id='Ordinary'\n[[notifications]]\n_id='saved'\ntitle='Saved'\n" +
            "[notifications.details]\n_id='Ordinary nested'\nbody='Body'\n" +
            "[[notifications.items]]\n_id='open'\nlabel='Open saved'\n" +
            "[[notifications]]\n_id='dismissed'\ntitle='Dismissed'\n" +
            "[[notifications.items]]\n_id='open'\nlabel='Open dismissed'\n";
        TranslationLocaleDocument document = TranslationLocaleReader.Read(Source("translations/en.toml", text), "en");
        Assert.True(document.Success, string.Join("\n", document.Diagnostics.Select(d => d.Message)));
        Assert.Equal("_id,notifications_saved_title,notifications_saved_details__id,notifications_saved_details_body,notifications_saved_items_open_label,notifications_dismissed_title,notifications_dismissed_items_open_label",
            string.Join(",", document.Entries.Select(entry => entry.Key)));
        Assert.Equal("notifications,saved,items,open", string.Join(",", document.Tables[2].Path));
        Assert.Equal("notifications,items", string.Join(",", document.Tables[2].DeclaredPath));
        Assert.True(document.Tables[2].IsArrayElement && !document.Tables[1].IsArrayElement, "Array metadata kind incorrect.");
        Assert.Equal("notifications,saved,items,open", string.Join(",", document.Entries[4].TablePath));
        const string first = "[[notifications]]\n_id='saved'\ntitle='Saved'\n[[notifications]]\n_id='dismissed'\ntitle='Dismissed'";
        const string reordered = "[[notifications]]\n_id='dismissed'\ntitle='Dismissed'\n[[notifications]]\n_id='saved'\ntitle='Saved'";
        const string inline = "notifications=[{_id='saved', title='Saved'},{_id='dismissed', title='Dismissed'}]";
        var compiled = new[] { first, reordered, inline }.Select(value => TranslationCompiler.CompileProject(Project(),
            [Source("translations/en.toml", value)])).ToArray();
        Assert.True(compiled.All(result => result.Success), string.Join("\n", compiled.Select(Errors)));
        Assert.Equal(compiled[0].Catalogs[0].Fingerprint, compiled[1].Catalogs[0].Fingerprint);
        Assert.Equal(compiled[0].Catalogs[0].Fingerprint, compiled[2].Catalogs[0].Fingerprint);
        TranslationLocaleDocument inlineDocument = TranslationLocaleReader.Read(Source("en.toml", "cards=[{_id='first', nested=[{_id='child', title='Title'}]}]"), "en");
        Assert.True(inlineDocument.Success, string.Join("\n", inlineDocument.Diagnostics.Select(d => d.Message)));
        Assert.Equal("cards_first_nested_child_title", inlineDocument.Entries[0].Key);
        Assert.Equal("cards,first,nested,child", string.Join(",", inlineDocument.Entries[0].InlinePath));
        Assert.True(inlineDocument.InlineTables.All(table => table.IsArrayElement), "Inline array metadata kind missing.");
        Assert.Equal("_id", inlineDocument.InlineTables[0].Members[0].KeyPath[0]);
        TranslationLocaleDocument empty = TranslationLocaleReader.Read(Source("en.toml", "empty=[]"), "en");
        Assert.True(empty.Success && empty.Entries.Count == 0 && empty.InlineTables.Count == 0, "Empty array must be an empty group without synthetic identity.");
    }

    private static void ArrayTableValidation()
    {
        string[] invalid = ["[[a]]\ntitle='X'", "[[a]]\n_id=1\ntitle='X'", "[[a]]\n_id='bad-id'\ntitle='X'",
            "[[a]]\n_id=''", "[[a]]\n_id='same'\n[[a]]\n_id='same'", "a=[{_id='same'},{_id='same'}]",
            "a=[{title='X'}]", "a=[{_id='x'},1]", "a=['X']",
            "a_saved_title='X'\n[[a]]\n_id='saved'\ntitle='Y'", "a_b=[{_id='c', title='X'}]\na=[{_id='b_c',title='Y'}]",
            "[[a]]\n_id='parent'\n[[a.items]]\n_id='same'\n[[a.items]]\n_id='same'"];
        foreach (string text in invalid)
            Assert.True(!TranslationLocaleReader.Read(Source("en.toml", text), "en").Success, "Invalid array table accepted: " + text);
        Assert.True(!TranslationLocaleReader.Read(Source("en.toml", "[[a]]\n_id='item'\nx='X'"), "en",
            new TranslationCompilerOptions(maximumDepth: 2)).Success, "Stable identity was omitted from the effective depth bound.");
    }

    private static string Errors(TranslationCompilation compilation) => string.Join("\n", compilation.Diagnostics.Select(d => d.Id + ": " + d.Message));
    private static string Decode(TranslationSource source) => Encoding.UTF8.GetString(source.GetUtf8Bytes());
    private static TranslationSource Source(string path, string text) => new(path, Encoding.UTF8.GetBytes(text));
}
