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
    }
    private const string Contracts = ",\"markup\":{\"contracts\":[{\"name\":\"shop:badge\",\"kind\":\"paired\",\"children\":\"inline\",\"interactive\":false,\"plainText\":\"children\",\"options\":{\"tone\":{\"type\":\"enum\",\"values\":[\"neutral\",\"positive\"],\"default\":\"neutral\"}}}],\"aliases\":{\"badge\":\"shop:badge\"}}";
    private const string Payment = "payment = Read {#link ref=terms}terms{/link} and {#link ref=privacy}privacy{/link}. {#action ref=retry}Retry{/action} {#icon ref=star/} {#badge tone=$tone @note=|safe annotation|}Available{/badge}";
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
                const content = m.payment({tone:'positive'});
                check(content.key==='payment' && content.locale==='en','content identity');
                for (const href of ['javascript:alert(1)','java\\tscript:alert(1)','data:text/html,bad']) {
                  let rejected=false; try { linkBinding({href:href.replace('\\t','\t')}); } catch { rejected=true; } check(rejected,'unsafe destination');
                }
                check(content.nodes.some(n=>n.name==='runic:icon' && n.standalone),'standalone lost');
                let rejected=false; try { toPlainText(content,{slots,custom}); } catch { rejected=true; } check(rejected,'implicit action projection');
                const text = toPlainText(content,{slots,custom,allowActionLabels:true});
                check(text === 'Read terms and privacy. Retry Star Available','plain text: '+text);
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
    private static string Errors(TranslationCompilation result) => string.Join("\n", result.Diagnostics.Select(d => d.Message));
}
