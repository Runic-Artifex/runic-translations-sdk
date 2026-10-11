using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Runic.Translations.Compiler.Generation;
using TUnit.Core;

namespace Runic.Translations.Compiler.Tests;

// Runtime API boundaries of the document profile that the shared corpus does not cover:
// contract linking, custom plain-text projections, option validation and slot checks.
[Category("rmf2-semantic-v5")]
internal sealed class Rmf2DocumentRuntimeTests
{
    private const string Config = ",\"markup\":{\"contracts\":[{\"name\":\"app:term\",\"kind\":\"paired\",\"children\":\"inline\",\"interactive\":false,\"plainText\":\"explicit\"}],\"aliases\":{\"term\":\"app:term\"}}";
    private const string Source = "term = See {#term}API{/term}.\nnotice =\n  {#p}Open the {#link ref=guide}guide{/link}.{/p}\n  {#ul}{#li}One{/li}{/ul}\n";

    private static Rmf2ProjectV5 Compile()
    {
        Rmf2ProjectCompilationV5 result = TranslationCompiler.CompileRmf2ProjectV5(Rmf2ProjectV5Tests.Project(Config),
            [new TranslationSource("translations/en.rmf2", Encoding.UTF8.GetBytes(Source))]);
        Assert.True(result.Success, string.Join("; ", result.Diagnostics.Select(item => item.Id + ": " + item.Message)));
        return result.Project!;
    }

    private static LocalizedTextContent Format(Rmf2ProjectV5 project, string key)
    {
        Rmf2TranslationV5 linked = project.Locales.Single().ResolvedResources.Single(item => item.Key == key);
        return Rmf2V1CorpusTests.Lower(linked.Message, linked.ContentLocale).FormatContent([], "en");
    }

    [Test, DisplayName("RMF2 markup contract linking rejects other versions and custom block placements")]
    public void Linking()
    {
        string contract = Compile().MarkupContract;
        Assert.Throws<ArgumentException>(() => Rmf2MarkupContract.Link(Mutate(contract, "{\"version\":2", "{\"version\":1")), "markup contract v1");
        Assert.Throws<ArgumentException>(() => Rmf2MarkupContract.Link(Mutate(contract, "\"app:term\":{\"kind\":\"paired\",\"placement\":\"inline\"", "\"app:term\":{\"kind\":\"paired\",\"placement\":\"block\"")), "custom block placement");
        Assert.Throws<ArgumentException>(() => Rmf2MarkupContract.Link("{"), "malformed JSON");
        Assert.Throws<TranslationFormatException>(() => new Rmf2DocumentRenderer(Rmf2MarkupContract.Link(contract)).Render("missing", new LocalizedDocumentContent(Format(Compile(), "term"))), "unknown key");
    }

    private static string Mutate(string json, string from, string to)
    {
        Assert.True(json.Contains(from, StringComparison.Ordinal), "Contract fragment not found: " + from);
        return json.Replace(from, to, StringComparison.Ordinal);
    }

    [Test, DisplayName("RMF2 plain-text options project custom elements and validate the list marker")]
    public void PlainTextOptions()
    {
        Rmf2ProjectV5 project = Compile();
        var renderer = new Rmf2InlineRenderer(Rmf2MarkupContract.Link(project.MarkupContract));
        LocalizedTextContent content = Format(project, "term");
        Assert.Throws<TranslationFormatException>(() => renderer.ToPlainText("term", content, null, new Rmf2PlainTextOptions()), "missing custom projection");
        var custom = new Dictionary<string, Func<Rmf2PlainTextElement, string>>(StringComparer.Ordinal)
        {
            ["app:term"] = element => element.Text + " [" + element.Locale + "]",
        };
        Assert.Equal("See API [en].", renderer.ToPlainText("term", content, null, new Rmf2PlainTextOptions { Custom = custom }), "custom projection");
        Assert.Throws<ArgumentNullException>(() => _ = new Rmf2PlainTextOptions { ListMarker = null! }, "null list marker");
    }

    [Test, NotInParallel, DisplayName("RMF2 document renderer checks slot bindings and generated ESM document factories")]
    public void Boundaries()
    {
        Rmf2ProjectV5 project = Compile();
        var documents = new Rmf2DocumentRenderer(Rmf2MarkupContract.Link(project.MarkupContract));
        var content = new LocalizedDocumentContent(Format(project, "notice"));
        Assert.Throws<TranslationFormatException>(() => documents.Render("notice", content), "missing slot binding");
        Assert.Throws<TranslationFormatException>(() => documents.Render("notice", content, new Dictionary<string, MarkupBinding> { ["guide"] = new InlineActionBinding(() => { }) }), "wrong slot kind");
        var slots = new Dictionary<string, MarkupBinding> { ["guide"] = new InlineLinkBinding(new Uri("https://example.test/")) };
        Assert.Equal("Open the guide.\n\n> One", documents.ToPlainText("notice", content, slots, new Rmf2PlainTextOptions { ListMarker = "> " }), "list marker");

        string directory = Rmf2V1CorpusTests.Write(TranslationOutputRenderer.RenderRmf2V5EsmModules(project));
        try
        {
            File.WriteAllText(Path.Combine(directory, "boundaries.mjs"), """
                import { m } from "./app.esm-v5/messages.js";
                import { createDocumentRenderer, linkBinding, toPlainText } from "./app.esm-v5/runtime.js";
                const fails=(action,name)=>{try{action();}catch(error){if(error instanceof TypeError)return;throw error;}throw new Error(name+": accepted");};
                const factory={text:value=>value,element:({children})=>children.join(""),block:(name,options,children)=>children.join("")};
                fails(()=>createDocumentRenderer({text:factory.text,element:factory.element}),"missing block factory");
                const notice=m.notice({locale:"en"}),slots={guide:linkBinding({href:"https://example.test/"})};
                fails(()=>createDocumentRenderer(factory).render(notice,{slots:{}}),"missing slot binding");
                fails(()=>toPlainText(notice,{slots,listMarker:1}),"non-string list marker");
                fails(()=>toPlainText(m.term({locale:"en"}),{slots:{}}),"missing custom adapter");
                const contexts=[];createDocumentRenderer({...factory,block:(name,options,children,context)=>{contexts.push(name+"@"+context.occurrence+"@"+context.locale);return "";}}).render(notice,{slots});
                if(contexts.join("|")!=="runic:p@p[1]@en|runic:li@ul[1]/li[1]@en|runic:ul@ul[1]@en")throw new Error("block contexts: "+contexts.join("|"));
                if(toPlainText(notice,{slots,listMarker:"> "})!=="Open the guide.\n\n> One")throw new Error("list marker");
                """, new UTF8Encoding(false));
            Rmf2V1CorpusTests.Run("bun", [Path.Combine(directory, "boundaries.mjs")], directory);
        }
        finally { Directory.Delete(directory, true); }
    }
}
