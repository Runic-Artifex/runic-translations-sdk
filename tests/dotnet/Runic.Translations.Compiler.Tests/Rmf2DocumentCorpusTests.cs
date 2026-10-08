using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Runic.Translations.Compiler.Generation;

namespace Runic.Translations.Compiler.Tests;

// The shared rmf2-document-v1 corpus (W220-004 acceptance): compiler diagnostics, linked and
// pack-loaded .NET rendering, generated and dynamic ESM rendering, and the pack rejection
// taxonomy all consume the same index.json.
internal static class Rmf2DocumentCorpusTests
{
    private static string Root => RepositoryPaths.Resolve("specs", "translations", "corpus", "rmf2-document-v1");

    internal static void Register(TestRunner runner)
    {
        runner.Add("RMF2 document corpus freezes the linked contract skeletons and artifacts", Contract);
        runner.Add("RMF2 document corpus compiler cases report the expected diagnostics", CompilerCases);
        runner.Add("RMF2 document corpus agrees across linked .NET loaded packs generated ESM and dynamic ESM packs", Execution);
        runner.Add("RMF2 document corpus pack rejection taxonomy agrees across .NET and ESM", InvalidPacks);
        runner.Add("RMF2 document corpus renders the shared HTML oracle through the generated DOM adapter and retires callbacks", DomAdapter);
    }

    private static void Contract()
    {
        using JsonDocument index = Index();
        Rmf2ProjectV5 project = Compile();
        JsonElement root = index.RootElement;
        Assert.Equal(1, root.GetProperty("corpusVersion").GetInt32());
        Assert.Equal("runic-rmf2-document-v1-conformance", root.GetProperty("identity").GetString());
        JsonElement contracts = root.GetProperty("contracts");
        Assert.Equal(contracts.GetProperty("messageAstVersion").GetInt32(), Rmf2ProjectV5.MessageGrammarVersion);
        Assert.Equal(contracts.GetProperty("localeArtifactVersion").GetInt32(), Rmf2LocaleArtifactV5.ArtifactVersion);
        Assert.Equal(contracts.GetProperty("runtimeAbiVersion").GetInt32(), Rmf2ProjectV5.RuntimeAbiVersion);
        using JsonDocument markup = JsonDocument.Parse(project.MarkupContract);
        Assert.Equal(contracts.GetProperty("markupContractVersion").GetInt32(), markup.RootElement.GetProperty("version").GetInt32());
        foreach (System.Text.Json.JsonProperty message in root.GetProperty("messages").EnumerateObject())
        {
            JsonElement linked = markup.RootElement.GetProperty("messages").GetProperty(message.Name);
            Assert.Equal(message.Value.GetProperty("content").GetString(), linked.GetProperty("content").GetString(), message.Name + " content");
            Assert.Equal(Strings(message.Value.GetProperty("skeletons")), Strings(linked.GetProperty("skeletons")), message.Name + " skeletons");
        }
        Assert.Equal(string.Join('|', root.GetProperty("messages").EnumerateObject().Select(item => item.Name).Order(StringComparer.Ordinal)),
            string.Join('|', project.CanonicalMessages.Select(item => item.Key).Order(StringComparer.Ordinal)), "Corpus message keys");
        JsonElement deterministic = root.GetProperty("determinism");
        Assert.Equal(deterministic.GetProperty("callerFingerprint").GetString(), project.CallerFingerprint, "Caller fingerprint golden");
        foreach (Rmf2LocaleV5 locale in project.Locales)
            Assert.Equal(deterministic.GetProperty("localeArtifactSha256").GetProperty(locale.Tag).GetString(), Rmf2LocaleArtifactV5.Render(project, locale.Tag).Sha256,
                "Locale artifact digest golden for " + locale.Tag);
    }

    private static void CompilerCases()
    {
        using JsonDocument index = Index();
        foreach (JsonElement test in index.RootElement.GetProperty("compilerCases").EnumerateArray())
        {
            string id = test.GetProperty("id").GetString()!;
            const string manifest = "{\"schemaVersion\":1,\"catalog\":\"cases\",\"code\":{\"namespace\":\"Corpus\",\"className\":\"CaseText\"},\"baseLocale\":\"en\",\"locales\":[\"en\",\"de\"],\"validation\":{\"translationCompleteness\":\"allow\"}}";
            TranslationSource[] sources = test.GetProperty("sources").EnumerateObject()
                .Select(source => new TranslationSource("translations/" + source.Name + ".rmf2", Encoding.UTF8.GetBytes(source.Value.GetString()!))).ToArray();
            Rmf2ProjectCompilationV5 result = TranslationCompiler.CompileRmf2ProjectV5(new TranslationSource("translations/runic.json", Encoding.UTF8.GetBytes(manifest)), sources);
            Assert.Equal(Strings(test.GetProperty("diagnostics")), DiagnosticList(result), id + ": " + string.Join("; ", result.Diagnostics.Select(item => item.Id + " " + item.Message)));
            Assert.Equal(test.GetProperty("success").GetBoolean(), result.Success, id + " success");
        }
    }

    private static void Execution()
    {
        using JsonDocument index = Index();
        Rmf2ProjectV5 project = Compile();
        var contract = Rmf2MarkupContract.Link(project.MarkupContract);
        var documents = new Rmf2DocumentRenderer(contract);
        var inline = new Rmf2InlineRenderer(contract);
        var verified = new Dictionary<string, VerifiedExternalTranslationPack>(StringComparer.Ordinal);
        foreach (Rmf2LocaleV5 locale in project.Locales)
            verified.Add(locale.Tag, TranslationPackLoader.VerifyAsync(new ExternalTranslationPack(Rmf2LocaleArtifactV5.Render(project, locale.Tag).GetUtf8Bytes()),
                Rmf2V1CorpusTests.PackContract(project, locale.Tag)).AsTask().GetAwaiter().GetResult());
        JsonElement slotDefinitions = index.RootElement.GetProperty("slots");

        foreach (JsonElement test in index.RootElement.GetProperty("executions").EnumerateArray())
        {
            string id = test.GetProperty("id").GetString()!, locale = test.GetProperty("locale").GetString()!, key = test.GetProperty("key").GetString()!;
            JsonElement expected = test.GetProperty("expected");
            Rmf2TranslationV5 linked = project.Locales.Single(item => item.Tag == locale).ResolvedResources.Single(item => item.Key == key);
            Rmf2MessageContractV5 messageContract = project.CanonicalMessages.Single(item => item.Key == key);
            TextArgument[] arguments = Arguments(test.GetProperty("arguments"));
            var slots = new Dictionary<string, MarkupBinding>(StringComparer.Ordinal);
            foreach (JsonElement slot in test.GetProperty("slots").EnumerateArray())
            {
                JsonElement definition = slotDefinitions.GetProperty(slot.GetString()!);
                slots.Add(slot.GetString()!, definition.GetProperty("kind").GetString() == "runic:link"
                    ? new InlineLinkBinding(new Uri(definition.GetProperty("href").GetString()!, UriKind.Absolute))
                    : new InlineActionBinding(() => { }));
            }
            CompiledRmf2Message direct = Rmf2V1CorpusTests.Lower(linked.Message with { Inputs = messageContract.Inputs }, linked.ContentLocale);
            CompiledRmf2Message loaded = verified[locale].Messages.Single(item => item.Key.Name == key).Message!.Rmf2V5!;
            foreach ((string source, CompiledRmf2Message message) in new[] { ("linked", direct), ("pack", loaded) })
            {
                var content = new LocalizedDocumentContent(message.FormatContent(arguments, locale));
                string name = id + "/" + source;
                Assert.Equal(expected.GetProperty("contentLocale").GetString(), content.Locale, name + " content locale");
                Assert.Equal(Canonical(expected.GetProperty("blocks")), Blocks(documents.Render(key, content, slots)), name + " blocks");
                foreach (JsonElement projection in expected.GetProperty("plainText").EnumerateArray())
                    Assert.Equal(projection.GetProperty("value").GetString(), documents.ToPlainText(key, content, slots, Options(projection.GetProperty("options"))), name + " plain text");
                if (expected.TryGetProperty("plainTextRejected", out JsonElement rejected))
                    foreach (JsonElement projection in rejected.EnumerateArray())
                        Assert.Throws<TranslationFormatException>(() => documents.ToPlainText(key, content, slots, Options(projection.GetProperty("options"))), name + " rejected plain text");
            }
        }

        foreach (JsonElement test in index.RootElement.GetProperty("rendererRejections").EnumerateArray())
        {
            string key = test.GetProperty("key").GetString()!;
            LocalizedTextContent content = verified["en"].Messages.Single(item => item.Key.Name == key).Message!.Rmf2V5!.FormatContent([], "en");
            if (test.GetProperty("renderer").GetString() == "document")
                Assert.Throws<TranslationFormatException>(() => documents.Render(key, new LocalizedDocumentContent(content)), test.GetProperty("id").GetString()!);
            else
                Assert.Throws<TranslationFormatException>(() => inline.Render(key, content), test.GetProperty("id").GetString()!);
        }

        RunEsm(project, index.RootElement);
    }

    // W220-005: the generated DOM document adapter renders every execution to the canonical HTML in
    // html.json (the Svelte adapter checks the same oracle), and retires links and actions on replace and clear.
    private static void DomAdapter()
    {
        using JsonDocument index = Index();
        Rmf2ProjectV5 project = Compile();
        string directory = Rmf2V1CorpusTests.Write(TranslationOutputRenderer.RenderRmf2V5EsmModules(project));
        try
        {
            File.WriteAllText(Path.Combine(directory, "index.json"), index.RootElement.GetRawText(), new UTF8Encoding(false));
            File.Copy(Path.Combine(Root, "html.json"), Path.Combine(directory, "html.json"));
            foreach (Rmf2LocaleV5 locale in project.Locales)
                File.WriteAllBytes(Path.Combine(directory, locale.Tag + ".json"), Rmf2LocaleArtifactV5.Render(project, locale.Tag).GetUtf8Bytes());
            File.WriteAllText(Path.Combine(directory, "dom.mjs"), EsmDomScript, new UTF8Encoding(false));
            Rmf2V1CorpusTests.Run("bun", [Path.Combine(directory, "dom.mjs")], directory);
        }
        finally { Directory.Delete(directory, true); }
    }

    private static void InvalidPacks()
    {
        using JsonDocument index = Index();
        Rmf2ProjectV5 project = Compile();
        TranslationGeneratedOutput artifact = Rmf2LocaleArtifactV5.Render(project, "de");
        TranslationPackContract contract = Rmf2V1CorpusTests.PackContract(project, "de");
        string directory = Rmf2V1CorpusTests.Write(TranslationOutputRenderer.RenderRmf2V5EsmModules(project));
        try
        {
            foreach (JsonElement test in index.RootElement.GetProperty("invalidPacks").EnumerateArray())
            {
                string id = test.GetProperty("id").GetString()!;
                byte[] bytes = Mutate(artifact.Text, test);
                File.WriteAllBytes(Path.Combine(directory, id + ".json"), bytes);
                try
                {
                    _ = TranslationPackLoader.VerifyAsync(new ExternalTranslationPack(bytes), contract).AsTask().GetAwaiter().GetResult();
                    throw new InvalidOperationException("Invalid pack was accepted: " + id);
                }
                catch (TranslationPackException exception)
                {
                    Assert.Equal(test.GetProperty("rejection").GetString(), TranslationPackFailure.GetRejectionId(exception), id);
                }
            }
            // Equivalent encodings of the same pack that both loaders accept.
            foreach (JsonElement test in index.RootElement.GetProperty("validPacks").EnumerateArray())
            {
                string id = test.GetProperty("id").GetString()!;
                byte[] bytes = Mutate(artifact.Text, test);
                File.WriteAllBytes(Path.Combine(directory, id + ".json"), bytes);
                try { _ = TranslationPackLoader.VerifyAsync(new ExternalTranslationPack(bytes), contract).AsTask().GetAwaiter().GetResult(); }
                catch (TranslationPackException exception) { throw new InvalidOperationException("Valid pack was rejected: " + id + " " + TranslationPackFailure.GetRejectionId(exception), exception); }
            }
            File.WriteAllText(Path.Combine(directory, "index.json"), index.RootElement.GetRawText(), new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(directory, "invalid.mjs"), EsmInvalidScript, new UTF8Encoding(false));
            Rmf2V1CorpusTests.Run("bun", [Path.Combine(directory, "invalid.mjs")], directory);
        }
        finally { Directory.Delete(directory, true); }
    }

    // Each mutation edits the first variant of one message in the German artifact.
    private static byte[] Mutate(string artifact, JsonElement test)
    {
        JsonObject root = JsonNode.Parse(artifact)!.AsObject();
        string mutation = test.GetProperty("mutation").GetString()!;
        if (mutation == "markupContract")
        {
            root["markupContract"] = JsonNode.Parse(test.GetProperty("value").GetRawText());
            return Encoding.UTF8.GetBytes(root.ToJsonString());
        }
        if (mutation == "skeletons")
        {
            root["markupContract"]!["messages"]![test.GetProperty("key").GetString()!]!["skeletons"] = JsonNode.Parse(test.GetProperty("value").GetRawText());
            return Encoding.UTF8.GetBytes(root.ToJsonString());
        }
        JsonArray nodes = root["messages"]![test.GetProperty("key").GetString()!]!["ast"]!["variants"]![test.TryGetProperty("variant", out JsonElement variant) ? variant.GetInt32() : 0]!["nodes"]!.AsArray();
        switch (mutation)
        {
            case "dropLastBlock":
            {
                // Remove the last top-level block: from its opening node to the end of the variant.
                int depth = 0, start = -1;
                for (int position = 0; position < nodes.Count; position++)
                {
                    if (nodes[position]!["kind"]!.GetValue<string>() != "markup") continue;
                    string kind = nodes[position]!["markupKind"]!.GetValue<string>();
                    if (kind == "open" && depth++ == 0) start = position;
                    else if (kind == "close") depth--;
                }
                while (nodes.Count > start) nodes.RemoveAt(nodes.Count - 1);
                break;
            }
            case "wrapInParagraph":
                nodes.Insert(0, new JsonObject { ["kind"] = "markup", ["name"] = "runic:p", ["markupKind"] = "open", ["options"] = new JsonArray(), ["annotations"] = new JsonArray() });
                nodes.Add(new JsonObject { ["kind"] = "markup", ["name"] = "runic:p", ["markupKind"] = "close", ["options"] = new JsonArray(), ["annotations"] = new JsonArray() });
                break;
            case "insertRootText":
                nodes.Insert(0, new JsonObject { ["kind"] = "text", ["value"] = test.GetProperty("value").GetString() });
                break;
            case "prefixFirstText":
            {
                JsonObject text = nodes.Select(item => item!.AsObject()).First(item => item["kind"]!.GetValue<string>() == "text");
                text["value"] = test.GetProperty("value").GetString() + text["value"]!.GetValue<string>();
                break;
            }
            case "editText":
            {
                // "text" is the zero-based index among the variant's text nodes.
                JsonObject text = nodes.Select(item => item!.AsObject()).Where(item => item["kind"]!.GetValue<string>() == "text").ElementAt(test.GetProperty("text").GetInt32());
                string prefix = test.TryGetProperty("prefix", out JsonElement before) ? before.GetString()! : "";
                string suffix = test.TryGetProperty("suffix", out JsonElement after) ? after.GetString()! : "";
                text["value"] = prefix + text["value"]!.GetValue<string>() + suffix;
                break;
            }
            case "insertNodes":
            {
                int at = test.GetProperty("at").GetInt32();
                foreach (JsonElement node in test.GetProperty("nodes").EnumerateArray().Reverse()) nodes.Insert(at, JsonNode.Parse(node.GetRawText()));
                break;
            }
            case "setOption":
            {
                // Replaces the value of the named option on the first markup node that has it.
                string option = test.GetProperty("option").GetString()!;
                JsonObject target = nodes.Select(item => item!.AsObject()).Where(item => item["kind"]!.GetValue<string>() == "markup" && item["options"] is JsonArray)
                    .SelectMany(item => item["options"]!.AsArray().Select(value => value!.AsObject())).First(value => value["name"]!.GetValue<string>() == option);
                target["value"] = JsonNode.Parse(test.GetProperty("value").GetRawText());
                break;
            }
            default: throw new InvalidOperationException("Unknown document corpus mutation " + mutation);
        }
        return Encoding.UTF8.GetBytes(root.ToJsonString());
    }

    private static void RunEsm(Rmf2ProjectV5 project, JsonElement index)
    {
        string directory = Rmf2V1CorpusTests.Write(TranslationOutputRenderer.RenderRmf2V5EsmModules(project));
        try
        {
            File.WriteAllText(Path.Combine(directory, "index.json"), index.GetRawText(), new UTF8Encoding(false));
            foreach (Rmf2LocaleV5 locale in project.Locales)
                File.WriteAllBytes(Path.Combine(directory, locale.Tag + ".json"), Rmf2LocaleArtifactV5.Render(project, locale.Tag).GetUtf8Bytes());
            File.WriteAllText(Path.Combine(directory, "corpus.mjs"), EsmExecutionScript, new UTF8Encoding(false));
            Rmf2V1CorpusTests.Run("bun", [Path.Combine(directory, "corpus.mjs")], directory);
        }
        finally { Directory.Delete(directory, true); }
    }

    private static Rmf2ProjectV5 Compile()
    {
        using JsonDocument index = Index();
        JsonElement project = index.RootElement.GetProperty("project");
        TranslationSource manifest = new("translations/runic.json", File.ReadAllBytes(Path.Combine(Root, project.GetProperty("manifest").GetString()!)));
        TranslationSource[] sources = project.GetProperty("sources").EnumerateArray()
            .Select(path => new TranslationSource("translations/" + path.GetString(), File.ReadAllBytes(Path.Combine(Root, path.GetString()!)))).ToArray();
        Rmf2ProjectCompilationV5 result = TranslationCompiler.CompileRmf2ProjectV5(manifest, sources);
        Assert.True(result.Success, string.Join("; ", result.Diagnostics.Select(item => item.Id + ": " + item.Message)));
        Assert.Equal(Strings(project.GetProperty("diagnostics")), DiagnosticList(result),
            "The corpus project diagnostics: " + string.Join("; ", result.Diagnostics.Select(item => item.Id + ": " + item.Message)));
        return result.Project!;
    }

    private static string DiagnosticList(Rmf2ProjectCompilationV5 result) => string.Join('|', result.Diagnostics
        .Select(diagnostic => diagnostic.Id + ":" + Path.GetFileNameWithoutExtension(diagnostic.Location.Path) + ":" + (diagnostic.Severity == TranslationDiagnosticSeverity.Error ? "error" : "warning"))
        .Order(StringComparer.Ordinal));

    private static JsonDocument Index() => JsonDocument.Parse(File.ReadAllBytes(Path.Combine(Root, "index.json")));

    private static string Strings(JsonElement values) => string.Join('|', values.EnumerateArray().Select(item => item.GetString()));

    private static TextArgument[] Arguments(JsonElement values) => values.EnumerateObject().Select(item =>
    {
        string type = item.Value.GetProperty("type").GetString()!, value = item.Value.GetProperty("value").GetString()!;
        TextArgument carrier = type switch
        {
            "string" => new TextArgument("_", value),
            "int64" => new TextArgument("_", long.Parse(value, CultureInfo.InvariantCulture)),
            _ => throw new InvalidOperationException("Unsupported corpus argument carrier " + type),
        };
        return TextArgument.CreateRmf2(item.Name, carrier);
    }).ToArray();

    private static Rmf2PlainTextOptions Options(JsonElement options) => new()
    {
        AllowActionLabels = options.TryGetProperty("allowActionLabels", out JsonElement actions) && actions.GetBoolean(),
        AnnotateLinkDestinations = options.TryGetProperty("annotateLinkDestinations", out JsonElement links) && links.GetBoolean(),
        ListMarker = options.TryGetProperty("listMarker", out JsonElement marker) ? marker.GetString()! : "- ",
        // A corpus projection is a template in which {text} stands for the projected children.
        Custom = options.TryGetProperty("custom", out JsonElement custom)
            ? custom.EnumerateObject().ToDictionary(item => item.Name, item =>
            {
                string template = item.Value.GetString()!;
                return (Func<Rmf2PlainTextElement, string>)(element => template.Replace("{text}", element.Text, StringComparison.Ordinal));
            }, StringComparer.Ordinal)
            : null,
    };

    private static readonly JsonWriterOptions WriterOptions = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private static string Canonical(JsonElement value) => JsonNode.Parse(value.GetRawText())!.ToJsonString(new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });

    // The corpus tree shape: a list block has "blocks", a leaf block "inlines"; an inline element
    // has "children"; a text run is a string. Options are written in ordinal key order.
    private static string Blocks(IReadOnlyList<DocumentBlock> blocks)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, WriterOptions))
        {
            writer.WriteStartArray();
            foreach (DocumentBlock block in blocks) WriteBlock(writer, block);
            writer.WriteEndArray();
        }
        return Encoding.UTF8.GetString(stream.ToArray());

        static void WriteBlock(Utf8JsonWriter writer, DocumentBlock block)
        {
            Assert.True(block.Binding is null && !block.Implicit, "Document profile v1 blocks are unbound and explicit.");
            writer.WriteStartObject();
            writer.WriteString("name", block.Name);
            WriteOptions(writer, block.Options);
            writer.WriteString("occurrence", block.Occurrence);
            if (block.Name is "runic:ul" or "runic:ol")
            {
                writer.WriteStartArray("blocks");
                foreach (DocumentBlock child in block.Blocks) WriteBlock(writer, child);
            }
            else
            {
                writer.WriteStartArray("inlines");
                foreach (InlineMarkupRun run in block.Inlines) WriteRun(writer, run);
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        static void WriteRun(Utf8JsonWriter writer, InlineMarkupRun run)
        {
            if (run.Text is not null) { writer.WriteStringValue(run.Text); return; }
            writer.WriteStartObject();
            writer.WriteString("name", run.Name);
            WriteOptions(writer, run.Options);
            writer.WriteString("occurrence", run.Occurrence);
            writer.WriteStartArray("children");
            foreach (InlineMarkupRun child in run.Children) WriteRun(writer, child);
            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        static void WriteOptions(Utf8JsonWriter writer, IReadOnlyDictionary<string, string> options)
        {
            writer.WriteStartObject("options");
            foreach (KeyValuePair<string, string> option in options.OrderBy(item => item.Key, StringComparer.Ordinal)) writer.WriteString(option.Key, option.Value);
            writer.WriteEndObject();
        }
    }

    private const string EsmExecutionScript = """
        import { readFile } from "node:fs/promises";
        import { m } from "./documents.esm-v5/messages.js";
        import { actionBinding, bindMarkup, createDocumentRenderer, createInlineRenderer, defineMarkup, linkBinding, rmf2Contract, toPlainText } from "./documents.esm-v5/runtime.js";
        import { decodeLocalePack, formatDynamicMessage } from "./documents.esm-v5/dynamic.js";
        const index=JSON.parse(await readFile(new URL("./index.json",import.meta.url),"utf8"));
        const artifacts=Object.create(null);
        for(const locale of ["en","de","fr"]){const decoded=await decodeLocalePack(new Uint8Array(await readFile(new URL("./"+locale+".json",import.meta.url))),locale);if(!decoded.ok)throw new Error(locale+": "+decoded.reason);artifacts[locale]=decoded.value;}
        const sorted=options=>Object.fromEntries(Object.keys(options).sort().map(name=>[name,options[name]]));
        const lists=new Set(["runic:ul","runic:ol"]);
        const element=({name,options,occurrence,children})=>({name,options:sorted(options),occurrence,children});
        const contractOf=name=>defineMarkup({name,...rmf2Contract.contracts[name]});
        const customNames=Object.keys(rmf2Contract.contracts).filter(name=>!name.startsWith("runic:"));
        const tree=createDocumentRenderer({text:value=>value,element,block:(name,options,children,{occurrence})=>lists.has(name)?{name,options:sorted(options),occurrence,blocks:children}:{name,options:sorted(options),occurrence,inlines:children}},customNames.map(name=>bindMarkup(contractOf(name),element)));
        // A corpus projection is a template in which {text} stands for the projected children.
        const projectionOf=({custom,...options})=>({...options,custom:Object.entries(custom??{}).map(([name,template])=>bindMarkup(contractOf(name),({children})=>template.replaceAll("{text}",children.join(""))))});
        const argsOf=spec=>{const args=Object.create(null);for(const [name,item] of Object.entries(spec))args[name]=item.type==="int64"?BigInt(item.value):item.value;return args;};
        const slotsOf=names=>Object.fromEntries(names.map(name=>{const definition=index.slots[name];return [name,definition.kind==="runic:link"?linkBinding({href:definition.href}):actionBinding({onActivate(){}})];}));
        const check=(value,test,source)=>{
          const id=test.id+"/"+source,slots=slotsOf(test.slots);
          if(value.kind!=="localized-document"||value.locale!==test.expected.contentLocale)throw new Error(id+": kind or content locale "+value.kind+" "+value.locale);
          const actual=JSON.stringify(tree.render(value,{slots}));if(actual!==JSON.stringify(test.expected.blocks))throw new Error(id+": blocks "+actual);
          for(const projection of test.expected.plainText){const text=toPlainText(value,{slots,...projectionOf(projection.options)});if(text!==projection.value)throw new Error(id+": plain text "+JSON.stringify(text));}
          for(const projection of test.expected.plainTextRejected??[]){let failed=false;try{toPlainText(value,{slots,...projectionOf(projection.options)});}catch{failed=true;}if(!failed)throw new Error(id+": plain text accepted");}
        };
        for(const test of index.executions){const args=argsOf(test.arguments);check(formatDynamicMessage(artifacts[test.locale],test.key,args),test,"dynamic");check(Object.keys(test.arguments).length===0?m[test.key]({locale:test.locale}):m[test.key](args,{locale:test.locale}),test,"generated");}
        const inline=createInlineRenderer({text:value=>value,element:({children})=>children.join("")});
        for(const test of index.rendererRejections){const value=m[test.key]({locale:"en"});let failed=false;try{(test.renderer==="document"?tree:inline).render(value,{slots:Object.create(null)});}catch{failed=true;}if(!failed)throw new Error(test.id+": accepted");}
        """;

    private const string EsmDomScript = """
        import { readFile } from "node:fs/promises";
        import { m } from "./documents.esm-v5/messages.js";
        import { actionBinding, bindMarkup, createDomDocumentRenderer, defineMarkup, linkBinding, rmf2Contract } from "./documents.esm-v5/runtime.js";
        import { decodeLocalePack, formatDynamicMessage } from "./documents.esm-v5/dynamic.js";
        const index=JSON.parse(await readFile(new URL("./index.json",import.meta.url),"utf8"));
        const oracle=JSON.parse(await readFile(new URL("./html.json",import.meta.url),"utf8"));
        // A minimal DOM: the adapter only creates elements and text nodes, sets attributes, appends and listens for clicks.
        class FakeElement{constructor(tag){this.tag=tag;this.attributes=new Map();this.children=[];this.listeners=[];}setAttribute(name,value){this.attributes.set(name,String(value));}removeAttribute(name){this.attributes.delete(name);}getAttribute(name){return this.attributes.get(name)??null;}append(...nodes){this.children.push(...nodes);}replaceChildren(...nodes){this.children=[...nodes];}addEventListener(type,listener){if(type==="click")this.listeners.push(listener);}click(){for(const listener of this.listeners)listener();}}
        const fake={createElement:tag=>new FakeElement(tag),createTextNode:value=>({text:value})};
        // Canonical HTML: attributes in ordinal name order, text escaped for &, < and >, attribute values for & and ".
        const escape=value=>value.replaceAll("&","&amp;").replaceAll("<","&lt;").replaceAll(">","&gt;");
        const html=node=>node.tag===undefined?escape(node.text):"<"+node.tag+[...node.attributes].sort(([a],[b])=>a<b?-1:a>b?1:0).map(([name,value])=>" "+name+"=\""+value.replaceAll("&","&amp;").replaceAll("\"","&quot;")+"\"").join("")+">"+(node.tag==="br"?"":node.children.map(html).join("")+"</"+node.tag+">");
        const find=(node,tag)=>node.tag===tag?node:(node.children??[]).map(child=>find(child,tag)).find(Boolean);
        const contractOf=name=>defineMarkup({name,...rmf2Contract.contracts[name]});
        const customNames=Object.keys(rmf2Contract.contracts).filter(name=>!name.startsWith("runic:"));
        const custom=customNames.map(name=>bindMarkup(contractOf(name),({name,occurrence,children})=>{const node=fake.createElement("span");node.setAttribute("data-runic-markup",name);node.setAttribute("data-runic-occurrence",occurrence);node.append(...children);return node;}));
        const dom=createDomDocumentRenderer(fake,{custom});
        const argsOf=spec=>{const args=Object.create(null);for(const [name,item] of Object.entries(spec))args[name]=item.type==="int64"?BigInt(item.value):item.value;return args;};
        const slotsOf=names=>Object.fromEntries(names.map(name=>{const definition=index.slots[name];return [name,definition.kind==="runic:link"?linkBinding({href:definition.href}):actionBinding({onActivate(){}})];}));
        const artifacts=Object.create(null);
        for(const locale of ["en","de","fr"]){const decoded=await decodeLocalePack(new Uint8Array(await readFile(new URL("./"+locale+".json",import.meta.url))),locale);if(!decoded.ok)throw new Error(locale+": "+decoded.reason);artifacts[locale]=decoded.value;}
        for(const test of index.executions){
          const args=argsOf(test.arguments),slots=slotsOf(test.slots);
          for(const [source,value] of [["dynamic",formatDynamicMessage(artifacts[test.locale],test.key,args)],["generated",Object.keys(test.arguments).length===0?m[test.key]({locale:test.locale}):m[test.key](args,{locale:test.locale})]]){
            const actual=dom.render(value,{slots}).map(html).join("");
            if(actual!==oracle.executions[test.id])throw new Error(test.id+"/"+source+": html "+actual);
          }
        }
        if(Object.keys(oracle.executions).sort().join()!==index.executions.map(test=>test.id).sort().join())throw new Error("html.json must cover every execution");
        // Heading levels are relative to the host's base; beyond h6 the element stays h6 with an explicit aria-level.
        const heading=createDomDocumentRenderer(fake,{headingBase:6}).render(m.whitespace_headings({locale:"en"}),{slots:{}}).map(html).join("");
        if(!heading.startsWith("<h6 class=\"runic-leaf\" data-runic-occurrence=\"h[1]\" lang=\"en\">Title</h6><h6 aria-level=\"7\" class=\"runic-leaf\" data-runic-occurrence=\"h[2]\" lang=\"en\" role=\"heading\">Section</h6>"))throw new Error("heading base: "+heading);
        for(const headingBase of [0,10,1.5,"2"]){let failed=false;try{createDomDocumentRenderer(fake,{headingBase});}catch{failed=true;}if(!failed)throw new Error("headingBase accepted "+headingBase);}
        // Callback retirement: setContent and clearContent retire the previous render, even for retained detached nodes.
        let calls=0;
        const live={guide:linkBinding({href:"https://example.test/guide"}),check:actionBinding({onActivate(){calls++;}})};
        const backup=m.guide_backup({fileName:"report.txt"},{locale:"en"}),target=fake.createElement("div");
        dom.setContent(target,backup,{slots:live});
        const first=find(target,"button"),firstLink=find(target,"a");
        first.click();if(calls!==1)throw new Error("the action did not fire once");
        let rejected=false;try{dom.setContent(target,backup,{slots:{guide:live.guide}});}catch{rejected=true;}
        if(!rejected||find(target,"button")!==first)throw new Error("a rejected render replaced the displayed content");
        first.click();if(calls!==2)throw new Error("a rejected render retired the displayed content");
        dom.setContent(target,backup,{slots:live});
        const second=find(target,"button");
        if(second===first)throw new Error("setContent reused the previous action");
        first.click();if(calls!==2)throw new Error("a replaced action remained active");
        if(firstLink.getAttribute("href")!==null||first.getAttribute("disabled")===null)throw new Error("a replaced link or action was not retired");
        second.click();if(calls!==3)throw new Error("the replacement action is inactive");
        dom.clearContent(target);
        second.click();if(calls!==3||target.children.length!==0)throw new Error("clearContent left an active action");
        """;

    private const string EsmInvalidScript = """
        import { readFile } from "node:fs/promises";
        import { decodeLocalePack } from "./documents.esm-v5/dynamic.js";
        const index=JSON.parse(await readFile(new URL("./index.json",import.meta.url),"utf8"));
        for(const test of index.invalidPacks){const decoded=await decodeLocalePack(new Uint8Array(await readFile(new URL("./"+test.id+".json",import.meta.url))),"de");if(decoded.ok||decoded.reason!==test.rejection)throw new Error(test.id+": "+decoded.reason+" != "+test.rejection);}
        for(const test of index.validPacks){const decoded=await decodeLocalePack(new Uint8Array(await readFile(new URL("./"+test.id+".json",import.meta.url))),"de");if(!decoded.ok)throw new Error(test.id+": valid pack rejected as "+decoded.reason);}
        """;
}
