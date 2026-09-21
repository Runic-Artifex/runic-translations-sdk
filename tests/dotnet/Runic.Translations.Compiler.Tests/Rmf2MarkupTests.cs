using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Diagnostics;
using Runic.Translations;
using System.Text.Json.Nodes;
using Runic.Translations.Compiler.Generation;

namespace Runic.Translations.Compiler.Tests;

internal static class Rmf2MarkupTests
{
    internal static void Register(TestRunner runner)
    {
        runner.Add("RMF2 markup contracts preserve canonical identities options and standalone tags", Model);
        runner.Add("RMF2 markup rejects slots options and illegal nesting per variant", Invalid);
        runner.Add("RMF2 external packs validate options slots and standalone .NET rendering", External);
        runner.Add("RMF2 generated ESM executes rich plans typed slots and explicit text projection", Esm);
        runner.Add("RMF2 custom plain-text policy matrix stays aligned across ESM and .NET", ProjectionMatrix);
    }
    private const string Contracts = ",\"markup\":{\"contracts\":[{\"name\":\"shop:badge\",\"kind\":\"paired\",\"children\":\"inline\",\"interactive\":false,\"plainText\":\"children\",\"options\":{\"tone\":{\"type\":\"enum\",\"values\":[\"neutral\",\"positive\"],\"default\":\"neutral\"}}}],\"aliases\":{\"badge\":\"shop:badge\"}}";
    private const string Payment = "payment = Read {#link ref=terms}terms{/link} and {#link ref=privacy}privacy{/link}. {#action ref=retry}Retry{/action} {#icon ref=star/} {#badge tone=$tone @note=|safe annotation|}Available{/badge}";
    private const string ProjectionContracts = ",\"markup\":{\"contracts\":[" +
        "{\"name\":\"shop:children\",\"kind\":\"paired\",\"children\":\"inline\",\"interactive\":false,\"plainText\":\"children\",\"options\":{}}," +
        "{\"name\":\"shop:omit\",\"kind\":\"paired\",\"children\":\"inline\",\"interactive\":false,\"plainText\":\"omit\",\"options\":{}}," +
        "{\"name\":\"shop:break\",\"kind\":\"standalone\",\"children\":\"none\",\"interactive\":false,\"plainText\":\"lineBreak\",\"options\":{}}," +
        "{\"name\":\"shop:explicit\",\"kind\":\"paired\",\"children\":\"inline\",\"interactive\":false,\"plainText\":\"explicit\",\"options\":{}}," +
        "{\"name\":\"shop:alternate\",\"kind\":\"paired\",\"children\":\"inline\",\"interactive\":false,\"plainText\":\"alternateText\",\"options\":{}}]}";
    private static TranslationSource Source(string path, string text) => new(path, Encoding.UTF8.GetBytes(text));
    private static TranslationCompilation Compile(string text, string config = "") => TranslationCompiler.CompileProject(Rmf2Tests.Project(config), [Source("translations/en.rmf2", text)]);
    private static void Model()
    {
        var result = Compile(Payment, Contracts); Assert.True(result.Success, Errors(result));
        var resource = result.Catalogs[0].CanonicalResources[0];
        Assert.Equal(4, resource.Slots.Count);
        var tags = resource.Message.Nodes.OfType<CompiledMessageMarkup>().ToArray();
        Assert.Equal("runic:link", tags[0].Name);
        Assert.True(tags.Single(t => t.Name == "runic:icon").Standalone, "Standalone kind lost.");
        var badge = tags.Single(t => t.Name == "shop:badge");
        Assert.Equal("safe annotation", badge.Annotations["note"]);
        Assert.True(badge.VariableOptions.Contains("tone"), "Variable option was flattened into a literal.");
        Assert.True(result.Catalogs[0].Rmf2MarkupContract!.Contains("\"terms\"", StringComparison.Ordinal), "Exported contract lacks slots.");
    }
    private static void Invalid()
    {
        foreach (string text in new[] { "x = {#link}{#action}Go{/action}{/link}", "x = {#strong/}", "x = {#icon/}", "x = {#link href=|https://bad|}Go{/link}", "x = {#unknown/}", "x = {#badge tone=wrong}Bad{/badge}", "x = {#link}Go{/link}{#link}Again{/link}", "x =\n  .input {$count :integer}\n  .match $count\n  one {{{#action}Go{/action}}}\n  * {{Nothing}}" })
            Assert.True(!Compile(text, Contracts).Success, "Invalid inline profile accepted: " + text);
        var missing = TranslationCompiler.CompileProject(Rmf2Tests.Project(), [Source("translations/en.rmf2", "x = {#link}Go{/link}"), Source("translations/de.rmf2", "x = Gehen")]);
        Assert.True(!missing.Success && missing.Diagnostics.Any(d => d.Id == "RTR0062"), "A target dropped a functional slot.");
    }
    private static void External()
    {
        var catalog = Compile(Payment, Contracts).Catalogs[0];
        var key = new TranslationKey("app", 0, "payment");
        var contract = new TranslationPackContract("app", "en", catalog.Fingerprint,
            [new TranslationPackMessageContract(key, [new TranslationPackArgumentContract("tone", TextArgumentType.String, TextArgumentFormat.None)])], 4, catalog.Rmf2MarkupContract);
        byte[] bytes = TranslationOutputRenderer.RenderLocaleJson(catalog, "en").GetUtf8Bytes();
        var verified = TranslationPackLoader.VerifyAsync(new ExternalTranslationPack(bytes), contract).AsTask().GetAwaiter().GetResult();
        var runtime = new CompiledTranslationCatalog("app", "en",
            [new CompiledTranslationDefinition("payment", [new TranslationPlaceholderDescriptor("tone", TextArgumentType.String, TextArgumentFormat.None)])],
            [new CompiledTranslationLocale("en", null, [new CompiledTranslationValue(0, "", verified.Messages[0].Message!)])]);
        var snapshot = new CompiledTranslationSnapshot(runtime, "en");
        var content = snapshot.FormatContent(key, [new TextArgument("tone", "positive")]);
        var renderer = new Rmf2InlineRenderer(catalog.Rmf2MarkupContract!);
        int callbacks = 0;
        var slots = new System.Collections.Generic.Dictionary<string, InlineMarkupBinding> {
            ["terms"] = new InlineLinkBinding(new Uri("https://example.test/terms")),
            ["privacy"] = new InlineLinkBinding(new Uri("https://example.test/privacy")),
            ["retry"] = new InlineActionBinding(() => callbacks++),
            ["star"] = new InlineIconBinding(new object(), false, locale => locale == "en" ? "Star" : "Stern"),
        };
        Assert.Equal("Read terms and privacy. Retry Star Available", renderer.ToPlainText("payment", content, slots, allowActionLabels: true));
        Assert.Equal(0, callbacks);
        Assert.True(content.Nodes.Span.ToArray().Any(n => n.Kind == LocalizedTextContentNodeKind.ElementStandalone), "Runtime lost standalone markup.");
        foreach ((int min, int max, bool remove, string scenario) in new[] {
            (2, 2, false, "minimum"),
            (0, 0, false, "maximum"),
            (0, 0, true, "key/content"),
        })
        {
            JsonObject alteredContract = JsonNode.Parse(catalog.Rmf2MarkupContract!)!.AsObject();
            JsonObject contractSlots = alteredContract["messages"]!["payment"]!["slots"]!.AsObject();
            if (remove) contractSlots.Remove("star");
            else
            {
                contractSlots["star"]!["min"] = min;
                contractSlots["star"]!["max"] = max;
            }
            int accessibleNameCalls = 0;
            var guardedSlots = new System.Collections.Generic.Dictionary<string, InlineMarkupBinding>(slots) {
                ["star"] = new InlineIconBinding(new object(), false, _ => { accessibleNameCalls++; return "Star"; }),
            };
            bool rejected = false;
            try { _ = new Rmf2InlineRenderer(alteredContract.ToJsonString()).ToPlainText("payment", content, guardedSlots, allowActionLabels: true); }
            catch (TranslationFormatException) { rejected = true; }
            Assert.True(rejected && accessibleNameCalls == 0, "Selected-content " + scenario + " slot mismatch reached a plain-text callback.");
        }
        foreach (string replacement in new[] { "unknown", "privacy" })
        {
            string altered = Encoding.UTF8.GetString(bytes).Replace("\"ref\":\"terms\"", "\"ref\":\"" + replacement + "\"", StringComparison.Ordinal);
            bool rejected = false;
            try { TranslationPackLoader.VerifyAsync(new ExternalTranslationPack(Encoding.UTF8.GetBytes(altered)), contract).AsTask().GetAwaiter().GetResult(); }
            catch (TranslationPackException) { rejected = true; }
            Assert.True(rejected, "External pack changed functional slots.");
        }
    }

    private static void Esm()
    {
        var compilation = Compile(Payment + "\nitems =\n  .input {$count :integer}\n  .match $count\n  0 {{Empty}}\n  one {{One}}\n  * {{{$count} items}}", Contracts);
        Assert.True(compilation.Success, Errors(compilation));
        string root = Path.Combine(Path.GetTempPath(), "runic-rmf2-esm-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            foreach (var output in TranslationOutputRenderer.RenderEsmModules(compilation.Catalogs[0]))
            { string path = Path.Combine(root, output.RelativePath); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllBytes(path, output.GetUtf8Bytes()); }
            File.WriteAllBytes(Path.Combine(root, "pack.json"), TranslationOutputRenderer.RenderLocaleJson(compilation.Catalogs[0], "en").GetUtf8Bytes());
            File.WriteAllText(Path.Combine(root, "test.mjs"), """
                import { decodeLocalePackV2, formatDynamicMessage } from './app.esm/dynamic.js';
                import { m } from './app.esm/messages.js';
                import { linkBinding, actionBinding, iconBinding, createInlineRenderer, toPlainText, defineMarkup, enumOption, bindMarkup } from './app.esm/runtime.js';
                const check = (ok, why) => { if (!ok) throw new Error(why); };
                let calls = 0;
                const slots = { terms: linkBinding({href:'/terms'}), privacy: linkBinding({href:'/privacy'}), retry: actionBinding({onActivate:()=>calls++}), star: iconBinding({asset:{},decorative:false,accessibleName: locale => locale === 'en' ? 'Star' : 'Stern'}) };
                const badge = defineMarkup({name:'shop:badge',kind:'paired',children:'inline',interactive:false,plainText:'children',options:{tone:enumOption(['neutral','positive'],'neutral')}});
                const custom = [bindMarkup(badge, ({children,options})=>{ check(options.tone==='positive','option resolution'); return children.join(''); })];
                const incompatibleBadge = defineMarkup({name:'shop:badge',kind:'standalone',children:'none',interactive:false,plainText:'children',options:{}});
                let incompatible=false; try { createInlineRenderer({text:value=>value,element:node=>node},[bindMarkup(incompatibleBadge,node=>node)]); } catch { incompatible=true; } check(incompatible,'renderer contract child/kind mismatch accepted');
                const content = m.payment({tone:'positive'});
                check(content.key==='payment' && content.locale==='en','content identity');
                for (const href of ['javascript:alert(1)','java\\tscript:alert(1)','data:text/html,bad']) {
                  let rejected=false; try { linkBinding({href:href.replace('\\t','\t')}); } catch { rejected=true; } check(rejected,'unsafe destination');
                }
                check(content.nodes.some(n=>n.name==='runic:icon' && n.standalone),'standalone lost');
                let rejected=false; try { toPlainText(content,{slots,custom}); } catch { rejected=true; } check(rejected,'implicit action projection');
                const text = toPlainText(content,{slots,custom,allowActionLabels:true});
                check(text === 'Read terms and privacy. Retry Star Available','plain text: '+text);
                check(toPlainText(content,{slots,allowActionLabels:true})===text,'policy-driven custom plain text projection');
                check(!Object.hasOwn(content.nodes.find(node=>node.name==='shop:badge'),'annotations'),'annotations leaked into ESM content');
                check(calls===0,'render invoked callback');
                check(m.items({count:0})==='Empty','numeric exact selection');
                check(m.items({count:1})==='One','default cardinal selection');
                check(m.items({count:2})==='2 items','fallback selection');
                rejected=false; try { toPlainText(m.payment({tone:'bad'}),{slots,custom,allowActionLabels:true}); } catch { rejected=true; } check(rejected,'dynamic enum validation');
                rejected=false; try { toPlainText(content,{slots:{},custom,allowActionLabels:true}); } catch { rejected=true; } check(rejected,'missing binding');
                const pack = await decodeLocalePackV2(new Uint8Array(await Bun.file('./pack.json').arrayBuffer()), 'en');
                check(pack.ok, 'external RMF2 decode: '+pack.reason);
                const external = formatDynamicMessage(pack.value, 'payment', {tone:'positive'});
                check(toPlainText(external,{slots,custom,allowActionLabels:true})===text,'external rendering mismatch');
                const altered = await Bun.file('./pack.json').text();
                const invalid = await decodeLocalePackV2(new TextEncoder().encode(altered.replace('"ref":"terms"','"ref":"unknown"')), 'en');
                check(!invalid.ok,'external slot tampering accepted');
                const ids = createInlineRenderer({text:()=>null,element:({occurrence})=>occurrence},[bindMarkup(badge,({occurrence})=>occurrence)]);
                check(JSON.stringify(ids.render(content,{slots}))===JSON.stringify(ids.render(formatDynamicMessage(pack.value,'payment',{tone:'positive'}),{slots})),'static/external occurrence mismatch');
                check(Object.isFrozen(badge.options.tone.values),'custom contract not deeply frozen');
                console.log('RMF2 ESM OK');
                """);
            File.WriteAllText(Path.Combine(root, "types.mts"), """
                import {m} from './app.esm/messages.js';
                import {createInlineRenderer,linkBinding,actionBinding,iconBinding} from './app.esm/runtime.js';
                const renderer=createInlineRenderer({text:(text:string)=>text,element:({children})=>children.join('')});
                const slots={terms:linkBinding({href:'/terms'}),privacy:linkBinding({href:'/privacy'}),retry:actionBinding({onActivate:()=>{}}),star:iconBinding({asset:{},decorative:true})};
                renderer.render(m.payment({tone:'positive'}),{slots});
                // @ts-expect-error Missing functional bindings must be diagnosed.
                renderer.render(m.payment({tone:'positive'}),{slots:{}});
                // @ts-expect-error Links and actions are different slot kinds.
                renderer.render(m.payment({tone:'positive'}),{slots:{...slots,retry:slots.terms}});
                // @ts-expect-error Message inputs remain typed.
                m.payment({tone:42});
                """);
            var typeCheck = new ProcessStartInfo("bun") { WorkingDirectory = root, RedirectStandardOutput = true, RedirectStandardError = true };
            typeCheck.ArgumentList.Add(Path.Combine(RepositoryPaths.RepositoryRoot, "node_modules", "typescript", "bin", "tsc"));
            foreach (string argument in new[] { "--noEmit", "--strict", "--target", "ES2022", "--module", "NodeNext", "--moduleResolution", "NodeNext", "types.mts" }) typeCheck.ArgumentList.Add(argument);
            using (var checker = Process.Start(typeCheck)!)
            {
                string diagnostics = checker.StandardOutput.ReadToEnd() + checker.StandardError.ReadToEnd(); checker.WaitForExit(); Assert.Equal(0, checker.ExitCode, diagnostics);
            }
            using var process = Process.Start(new ProcessStartInfo("bun", "test.mjs") { WorkingDirectory = root, RedirectStandardOutput = true, RedirectStandardError = true })!;
            string outputText = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd(); process.WaitForExit(); Assert.Equal(0, process.ExitCode, outputText);
        }
        finally { Directory.Delete(root, true); }
    }

    private static void ProjectionMatrix()
    {
        const string source = "children = {#shop:children}keep{/shop:children}\nomit = before {#shop:omit}{#shop:explicit}hidden{/shop:explicit}{/shop:omit} after\nbreak = before{#shop:break/}after\nexplicit = {#shop:explicit}label{/shop:explicit}\nalternate = {#shop:alternate}alt{/shop:alternate}";
        var compilation = TranslationCompiler.CompileProject(Rmf2Tests.Project(ProjectionContracts), [Source("translations/en.rmf2", source)]);
        Assert.True(compilation.Success, Errors(compilation));
        var catalog = compilation.Catalogs[0];
        var contract = new TranslationPackContract("app", "en", catalog.Fingerprint,
            catalog.CanonicalResources.OrderBy(resource => resource.Key, StringComparer.Ordinal).Select(resource =>
                new TranslationPackMessageContract(new TranslationKey("app", resource.Id, resource.Key))).ToArray(), 4, catalog.Rmf2MarkupContract);
        var verified = TranslationPackLoader.VerifyAsync(new ExternalTranslationPack(TranslationOutputRenderer.RenderLocaleJson(catalog, "en").GetUtf8Bytes()), contract).AsTask().GetAwaiter().GetResult();
        var runtime = new CompiledTranslationCatalog("app", "en",
            catalog.CanonicalResources.OrderBy(resource => resource.Key, StringComparer.Ordinal).Select(resource => new CompiledTranslationDefinition(resource.Key, Array.Empty<TranslationPlaceholderDescriptor>())).ToArray(),
            [new CompiledTranslationLocale("en", null, verified.Messages.Select(message => new CompiledTranslationValue(message.Key.Id, "", message.Message!)).ToArray())]);
        var snapshot = new CompiledTranslationSnapshot(runtime, "en");
        var renderer = new Rmf2InlineRenderer(catalog.Rmf2MarkupContract!);
        TranslationKey Key(string name) => new("app", catalog.CanonicalResources.Single(resource => resource.Key == name).Id, name);
        void RequireAdapter(string name, bool allowActionLabels = false)
        {
            try { _ = renderer.ToPlainText(name, snapshot.FormatContent(Key(name), []), allowActionLabels: allowActionLabels); }
            catch (TranslationFormatException exception) when (exception.Message.Contains("adapter", StringComparison.Ordinal)) { return; }
            throw new InvalidOperationException("Custom projection unexpectedly omitted its adapter requirement for " + name + ".");
        }
        Assert.Equal("keep", renderer.ToPlainText("children", snapshot.FormatContent(Key("children"), [])));
        Assert.Equal("before  after", renderer.ToPlainText("omit", snapshot.FormatContent(Key("omit"), [])));
        Assert.Equal("before\nafter", renderer.ToPlainText("break", snapshot.FormatContent(Key("break"), [])));
        RequireAdapter("explicit", allowActionLabels: true);
        RequireAdapter("alternate");

        string root = Path.Combine(Path.GetTempPath(), "runic-rmf2-projection-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            foreach (var output in TranslationOutputRenderer.RenderEsmModules(catalog))
            { string path = Path.Combine(root, output.RelativePath); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllBytes(path, output.GetUtf8Bytes()); }
            File.WriteAllText(Path.Combine(root, "matrix.mjs"), """
                import {m} from './app.esm/messages.js';
                import {toPlainText,defineMarkup,bindMarkup} from './app.esm/runtime.js';
                const check=(ok,why)=>{if(!ok)throw new Error(why);};
                check(toPlainText(m.children())==='keep','children policy');
                check(toPlainText(m.omit())==='before  after','omit policy');
                check(toPlainText(m.break())==='before\nafter','lineBreak policy');
                let rejected=false; try { toPlainText(m.explicit()); } catch { rejected=true; } check(rejected,'explicit adapter requirement');
                rejected=false; try { toPlainText(m.alternate()); } catch { rejected=true; } check(rejected,'alternateText adapter requirement');
                const explicit=defineMarkup({name:'shop:explicit',kind:'paired',children:'inline',interactive:false,plainText:'explicit',options:{}});
                const alternate=defineMarkup({name:'shop:alternate',kind:'paired',children:'inline',interactive:false,plainText:'alternateText',options:{}});
                const custom=[bindMarkup(explicit,({children})=>children.join('')),bindMarkup(alternate,({children})=>children.join(''))];
                check(toPlainText(m.explicit(),{custom})==='label','explicit adapter');
                check(toPlainText(m.alternate(),{custom})==='alt','alternate adapter');
                console.log('RMF2 projection matrix OK');
                """);
            using var process = Process.Start(new ProcessStartInfo("bun", "matrix.mjs") { WorkingDirectory = root, RedirectStandardOutput = true, RedirectStandardError = true })!;
            string outputText = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd(); process.WaitForExit(); Assert.Equal(0, process.ExitCode, outputText);
        }
        finally { Directory.Delete(root, true); }
    }
    internal static int Benchmark()
    {
        const int iterations = 10000;
        var catalog = Compile(Payment, Contracts).Catalogs[0];
        var key = new TranslationKey("app", 0, "payment");
        var contract = new TranslationPackContract("app", "en", catalog.Fingerprint,
            [new TranslationPackMessageContract(key, [new TranslationPackArgumentContract("tone", TextArgumentType.String, TextArgumentFormat.None)])], 4, catalog.Rmf2MarkupContract);
        var verified = TranslationPackLoader.VerifyAsync(new ExternalTranslationPack(TranslationOutputRenderer.RenderLocaleJson(catalog, "en").GetUtf8Bytes()), contract).AsTask().GetAwaiter().GetResult();
        var runtime = new CompiledTranslationCatalog("app", "en",
            [new CompiledTranslationDefinition("payment", [new TranslationPlaceholderDescriptor("tone", TextArgumentType.String, TextArgumentFormat.None)]), new CompiledTranslationDefinition("plain", [])],
            [new CompiledTranslationLocale("en", null, [new CompiledTranslationValue(0, "", verified.Messages[0].Message!), new CompiledTranslationValue(1, "Payment details")])]);
        var snapshot = new CompiledTranslationSnapshot(runtime, "en");
        var renderer = new Rmf2InlineRenderer(catalog.Rmf2MarkupContract!);
        var slots = new System.Collections.Generic.Dictionary<string, InlineMarkupBinding> {
            ["terms"] = new InlineLinkBinding(new Uri("https://example.test/terms")), ["privacy"] = new InlineLinkBinding(new Uri("https://example.test/privacy")),
            ["retry"] = new InlineActionBinding(() => { }), ["star"] = new InlineIconBinding(new object(), false, _ => "Star"),
        };
        TextArgument[] arguments = [new("tone", "positive")];
        var content = snapshot.FormatContent(key, arguments);
        Measure("dotnet-plain-message", () => snapshot.Format(new TranslationKey("app", 1, "plain"), []), iterations);
        Measure("dotnet-rich-content", () => snapshot.FormatContent(key, arguments), iterations);
        Measure("dotnet-linked-rich-render", () => renderer.Render("payment", content, slots), iterations);
        var large = Source("en.rmf2", string.Join("\n", Enumerable.Range(0, 1000).Select(index => "message_" + index + " = Hello {$name}")));
        Measure("resource-parse-1000-messages", () => Rmf2ResourceReader.Read(large), 30);
        string root = Path.Combine(Path.GetTempPath(), "runic-rmf2-benchmark-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            foreach (var output in TranslationOutputRenderer.RenderEsmModules(Compile(Payment + "\nplain = Payment details", Contracts).Catalogs[0]))
            { string path = Path.Combine(root, output.RelativePath); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllBytes(path, output.GetUtf8Bytes()); }
            File.WriteAllText(Path.Combine(root, "benchmark.mjs"), """
                import {m} from './app.esm/messages.js';
                import {createInlineRenderer,defineMarkup,enumOption,bindMarkup,linkBinding,actionBinding,iconBinding} from './app.esm/runtime.js';
                const badge=defineMarkup({name:'shop:badge',kind:'paired',children:'inline',interactive:false,plainText:'children',options:{tone:enumOption(['neutral','positive'],'neutral')}});
                const renderer=createInlineRenderer({text:value=>value,element:node=>node},[bindMarkup(badge,node=>node)]);
                const slots={terms:linkBinding({href:'/terms'}),privacy:linkBinding({href:'/privacy'}),retry:actionBinding({onActivate:()=>{}}),star:iconBinding({asset:{},decorative:false,accessibleName:()=> 'Star'})};
                const content=m.payment({tone:'positive'});
                function measure(name,fn){for(let i=0;i<1000;i++)fn();const start=performance.now();let value;for(let i=0;i<10000;i++)value=fn();console.log(JSON.stringify({name,iterations:10000,microsecondsPerOperation:(performance.now()-start)*1000/10000,runtime:Bun.version}));if(value===undefined)throw new Error('Missing result');}
                measure('esm-plain-message',()=>m.plain());measure('esm-rich-content',()=>m.payment({tone:'positive'}));measure('esm-linked-rich-render',()=>renderer.render(content,{slots}));
                """);
            using var process = Process.Start(new ProcessStartInfo("bun", "benchmark.mjs") { WorkingDirectory = root })!; process.WaitForExit(); return process.ExitCode;
        }
        finally { Directory.Delete(root, true); }
        static void Measure(string name, Func<object> action, int count)
        {
            for (int i = 0; i < Math.Min(count, 1000); i++) GC.KeepAlive(action());
            long before = GC.GetAllocatedBytesForCurrentThread(); var watch = Stopwatch.StartNew();
            for (int i = 0; i < count; i++) GC.KeepAlive(action());
            watch.Stop(); long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new { name, iterations = count, microsecondsPerOperation = watch.Elapsed.TotalMicroseconds / count, bytesPerOperation = allocated / (double)count, runtime = Environment.Version.ToString() }));
        }
    }
    private static string Errors(TranslationCompilation result) => string.Join("\n", result.Diagnostics.Select(d => d.Message));
}
