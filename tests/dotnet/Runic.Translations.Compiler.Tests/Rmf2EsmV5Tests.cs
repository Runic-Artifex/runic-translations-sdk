using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Runic.Translations.Compiler.Generation;
using TUnit.Core;

namespace Runic.Translations.Compiler.Tests;

[Category("rmf2-semantic-v5"), NotInParallel]
internal sealed class Rmf2EsmV5Tests
{
    private static readonly JsonSerializerOptions ProjectJsonOptions = new() { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull };

    [Test, DisplayName("RMF2 v5 generated ESM executes exact static dynamic transport and SSR paths")]
    public void Executes()
    {
        Rmf2ProjectV5 project = Fixture();
        IReadOnlyList<TranslationGeneratedOutput> outputs = TranslationOutputRenderer.RenderRmf2V5EsmModules(project);
        string directory = Write(outputs);
        try
        {
            TranslationGeneratedOutput artifact = Rmf2LocaleArtifactV5.Render(project, "de");
            File.WriteAllBytes(Path.Combine(directory, "artifact.json"), artifact.GetUtf8Bytes());
            string script = Path.Combine(directory, "test.mjs");
            File.WriteAllText(script, """
                import { readFile } from "node:fs/promises";
                import { m } from "./billing.esm-v5/messages.js";
                import { createInlineRenderer, decimal, linkBinding, toPlainText } from "./billing.esm-v5/runtime.js";
                import { runWithLocale } from "./billing.esm-v5/server.js";
                import { decodeLocaleArtifact, decodeLocalePack, formatDynamicMessage } from "./billing.esm-v5/dynamic.js";
                import { createTextReference, decodeTextReference } from "./billing.esm-v5/transport.js";
                const check = (condition, message) => { if (!condition) throw new Error(message); };
                // #86: a :number input takes a finite JavaScript number as the decimal String() writes.
                check(m.account_total({ rate: 0.125 }, { locale: "de" }) === "12,50%", "number decimal input");
                check(m.account_total({ rate: 1e-7 }, { locale: "en" }) === m.account_total({ rate: "0.0000001" }, { locale: "en" }), "exponent number decimal input");
                for (const value of [NaN, Infinity, -Infinity]) { let rejected=false; try { m.account_total({ rate: value }); } catch (error) { rejected = error instanceof TypeError; } check(rejected, `non-finite decimal accepted: ${value}`); }
                check(createTextReference("account_total", { rate: 0.125 }).arguments.rate.value === "0.125", "number decimal reference");
                check(createTextReference("account_total", { rate: -0 }).arguments.rate.value === "0", "negative zero decimal reference");
                check(m.account_total({ rate: decimal("0.125") }, { locale: "de" }) === "12,50%", "exact percent formatting");
                check(m.account_total({ rate: { coefficient: 125n, scale: 3, negative: false } }, { locale: "en" }) === "12.50%", "coefficient/scale input");
                const bill = m.account_bill({ count: 2n, rate: decimal("0.125"), "用户": "Ada" }, { locale: "de" });
                check(bill.kind === "localized-content" && bill.locale === "de" && Object.isFrozen(bill.nodes), "structured content");
                check(bill.nodes[0].children.map(node => node.value).join("") === "2 Rechnungen" && bill.nodes[0].attributes.ref === "invoice", "linked markup and options");
                const plain=toPlainText(bill,{slots:{invoice:linkBinding({href:"/invoice"})}});check(plain==="2 Rechnungen: 12,5%", `plain-text markup projection: ${JSON.stringify(plain)}`);
                let forgedLink=false;try{toPlainText(bill,{slots:{invoice:{kind:"runic:link",href:"javascript:alert(1)"}},annotateLinkDestinations:true});}catch{forgedLink=true;}check(forgedLink,"forged unsafe link binding accepted");
                let hrefReads=0;const changingLink={kind:"runic:link",get href(){return ++hrefReads===1?"https://safe.example/":"javascript:alert(1)";}};const snapshottedLink=toPlainText(bill,{slots:{invoice:changingLink},annotateLinkDestinations:true});check(hrefReads===1&&snapshottedLink.includes("(https://safe.example/)"),"validated slot binding was not snapshotted before rendering");
                const concurrent = await Promise.all([
                  runWithLocale("en", async () => { await Promise.resolve(); return m.account_total({ rate: decimal("0.125") }); }),
                  runWithLocale("de", async () => { await Promise.resolve(); return m.account_total({ rate: decimal("0.125") }); }),
                ]);
                check(concurrent[0] === "12.50%" && concurrent[1] === "12,50%", "request-local SSR isolation");
                const raw = JSON.parse(await readFile(new URL("./artifact.json", import.meta.url), "utf8"));
                const declared=raw.markupContract.contracts["runic:link"];let incompatible=false;try{createInlineRenderer({text:value=>value,element:element=>element.children.join("")},[{contract:{name:"runic:link",children:"none",...declared},render:()=>""}]);}catch{incompatible=true;}check(incompatible,"incompatible renderer contract accepted");
                const decoded = decodeLocaleArtifact(raw); check(decoded.ok, decoded.reason);
                check(formatDynamicMessage(decoded.value, "account_total", { rate: decimal("0.125") }) === "12,50%", "dynamic parity");
                const validAst=raw.messages.account_total.ast;let reads=0;
                const getterWrapper={contentLocale:raw.messages.account_total.contentLocale};Object.defineProperty(getterWrapper,"ast",{enumerable:true,get(){reads++;return validAst;}});
                const getterArtifact=structuredClone(raw);getterArtifact.messages.account_total=getterWrapper;const getterDecoded=decodeLocaleArtifact(getterArtifact);check(!getterDecoded.ok&&getterDecoded.reason==="RTR0023/malformed"&&reads===0,"artifact accessor was invoked or accepted");
                let forgedRejected=false;try{formatDynamicMessage(Object.freeze(structuredClone(decoded.value)),"account_total",{rate:decimal("0.125")});}catch{forgedRejected=true;}check(forgedRejected,"forged frozen artifact bypassed validation");
                const bytes = new TextEncoder().encode(JSON.stringify(raw));
                const packed = await decodeLocalePack(bytes, "de", copy => { copy[0] = 0; return true; }); check(packed.ok, packed.reason);
                const hostile = structuredClone(raw); hostile.messages.account_total.ast.declarations[0].expression.operand.value = "missing";
                check(decodeLocaleArtifact(hostile).reason === "RTR0023/argument-contract-mismatch", "unbound AST accepted");
                const decimalDrift = structuredClone(raw); decimalDrift.messages.account_total.ast.declarations[1].expression.options[1].value.canonical = "125";
                check(!decodeLocaleArtifact(decimalDrift).ok, "canonical decimal drift accepted");
                const malformedOptions = [
                  ["expression-options-null", artifact => artifact.messages.account_total.ast.declarations.find(item => item.expression.function === undefined).expression.options = null, "RTR0023/malformed-pattern"],
                  ["expression-options-object", artifact => artifact.messages.account_total.ast.declarations.find(item => item.expression.function === undefined).expression.options = Object.create(null), "RTR0023/malformed-pattern"],
                  ["close-markup-options-null", artifact => artifact.messages.account_bill.ast.variants[0].nodes.find(item => item.kind === "markup" && item.markupKind === "close").options = null, "RTR0023/argument-contract-mismatch"],
                  ["close-markup-options-object", artifact => artifact.messages.account_bill.ast.variants[0].nodes.find(item => item.kind === "markup" && item.markupKind === "close").options = Object.create(null), "RTR0023/argument-contract-mismatch"],
                ];
                for (const [name, mutate, expected] of malformedOptions) {
                  const directArtifact=structuredClone(raw);mutate(directArtifact);const direct=decodeLocaleArtifact(directArtifact);
                  check(!direct.ok&&direct.reason===expected,`${name} direct decode: ${direct.reason}`);
                  const packedArtifact=structuredClone(raw);mutate(packedArtifact);const packedResult=await decodeLocalePack(new TextEncoder().encode(JSON.stringify(packedArtifact)),"de");
                  check(!packedResult.ok&&packedResult.reason===expected,`${name} byte decode: ${packedResult.reason}`);
                }
                let deep=[];for(let depth=0;depth<64;depth++)deep=[deep];const deepResult=decodeLocaleArtifact(deep);check(!deepResult.ok&&deepResult.reason==="RTR0023/limit-exceeded",`deep direct object: ${deepResult.reason}`);
                const largeResult=decodeLocaleArtifact("x".repeat(8388608));check(!largeResult.ok&&largeResult.reason==="RTR0023/limit-exceeded",`large direct object: ${largeResult.reason}`);
                const nonJson=structuredClone(raw);nonJson.contractFingerprint=1n;const nonJsonResult=decodeLocaleArtifact(nonJson);check(!nonJsonResult.ok&&nonJsonResult.reason==="RTR0023/malformed",`non-JSON direct object: ${nonJsonResult.reason}`);
                const largeValid=structuredClone(raw);for(const variant of largeValid.messages.account_bill.ast.variants)for(let index=0;index<2100;index++)variant.nodes.push({kind:"text",value:""});check(decodeLocaleArtifact(largeValid).ok,"per-pattern node limit was incorrectly accumulated across variants");
                const independentPatterns=structuredClone(raw);for(const variant of independentPatterns.messages.account_bill.ast.variants)variant.nodes.push({kind:"text",value:"x".repeat(40000)});check(decodeLocaleArtifact(independentPatterns).ok,"per-pattern byte limit was incorrectly accumulated across variants");
                const duplicate = new TextEncoder().encode(JSON.stringify(raw).replace('{"artifactVersion":5','{"artifactVersion":5,"artifactVersion":5'));
                check((await decodeLocalePack(duplicate, "de")).reason === "RTR0023/malformed", "duplicate JSON property accepted");
                const reference = decodeTextReference({ version:1, catalog:"billing", contractFingerprint:raw.contractFingerprint, key:"account_total", arguments:{rate:"0.125"} });
                check(reference.ok && reference.value.arguments.rate.coefficient === 125n && reference.value.arguments.rate.scale === 3, "exact decimal transport");
                let fallbackReads=0;const changingReference={version:1,catalog:"billing",contractFingerprint:raw.contractFingerprint,key:"account_total",arguments:{rate:"0.125"},get fallbackText(){return ++fallbackReads===1?"safe":Object.create(null);}};const snapshottedReference=decodeTextReference(changingReference);check(snapshottedReference.ok&&fallbackReads===1&&snapshottedReference.value.fallbackText==="safe","transport decoder did not snapshot caller-owned fields");
                for (const spelling of ["9007199254740993", "1e+2", "1.2300e-2", "-0.000", "79228162514264337593543950335"]) decimal(spelling);
                for (const spelling of ["1e29", "1e-29", "79228162514264337593543950336"]) { let rejected=false; try { decimal(spelling); } catch { rejected=true; } check(rejected, `decimal domain accepted ${spelling}`); }
                console.log("RMF2 ESM v5 OK");
                """, new UTF8Encoding(false));
            Run("bun", [script], directory);
            string typecheck = Path.Combine(directory, "typecheck.ts");
            File.WriteAllText(typecheck, """
                import { m } from "./billing.esm-v5/messages.js";
                import { decimal, type LocalizedContent } from "./billing.esm-v5/runtime.js";
                const total: string = m.account_total({ rate: decimal("0.125") }, { locale: "de" });
                const fromNumber: string = m.account_total({ rate: 0.125 });
                const typedBill = m.account_bill({ count: 2n, rate: decimal("0.125"), "用户": "Ada" });
                const bill: LocalizedContent = typedBill;
                // @ts-expect-error invoice is a required link slot.
                import("./billing.esm-v5/runtime.js").then(({toPlainText}) => toPlainText(typedBill, { slots: {} }));
                import { baseLocale, createLocaleSource, locales, type Locale } from "./billing.esm-v5/runtime.js";
                import { getRequestLocale } from "./billing.esm-v5/server.js";
                interface TypedSource<L extends string> { getLocale(): L; setLocale(locale: L): void; subscribe(listener: (locale: L) => void): () => void; }
                const source: TypedSource<"de" | "en"> = createLocaleSource({ initialLocale: "de-AT" });
                const exact: readonly ("de" | "en")[] = locales;
                const catalogLocales: readonly Locale[] = ["de", "en"];
                const requestLocale: "de" | "en" = getRequestLocale();
                const base: Locale = baseLocale;
                // @ts-expect-error fr is not a locale of the catalog.
                source.setLocale("fr");
                // @ts-expect-error fr is not a locale of the catalog.
                const unknown: Locale = "fr";
                void total; void fromNumber; void bill; void exact; void catalogLocales; void requestLocale; void base; void unknown;
                """, new UTF8Encoding(false));
            Run("bun", [RepositoryPaths.Resolve("node_modules", "typescript", "bin", "tsc"), "--strict", "--noEmit", "--target", "ES2022", "--module", "NodeNext", "--moduleResolution", "NodeNext", "--lib", "ES2022,DOM", "--skipLibCheck", typecheck], directory);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Test, DisplayName("RMF2 v5 ESM preserves exact selection dynamic options aliases and ordered annotations")]
    public void SemanticParity()
    {
        string semantic = File.ReadAllText(RepositoryPaths.Resolve("specs", "translations", "corpus", "semantic-v5", "message.mf2"));
        string indented = string.Join('\n', semantic.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n').Select(line => "  " + line));
        string source = "sample =\n" + indented + "\nhuge =\n  .input {$n :number select=exact}\n  .match $n\n  9007199254740993 {{exact}}\n  * {{other}}\n";
        Rmf2ProjectCompilationV5 compilation = TranslationCompiler.CompileRmf2ProjectV5(
            Rmf2ProjectV5Tests.Project(), [new TranslationSource("translations/en.rmf2", Encoding.UTF8.GetBytes(source))]);
        Assert.True(compilation.Success, string.Join("; ", compilation.Diagnostics.Select(item => item.Message)));
        string directory = Write(TranslationOutputRenderer.RenderRmf2V5EsmModules(compilation.Project!));
        try
        {
            File.WriteAllBytes(Path.Combine(directory, "semantic-artifact.json"), Rmf2LocaleArtifactV5.Render(compilation.Project!, "en").GetUtf8Bytes());
            string script = Path.Combine(directory, "semantic.mjs");
            File.WriteAllText(script, """
                import { readFile } from "node:fs/promises";
                import { m } from "./app.esm-v5/messages.js";
                import { decimal } from "./app.esm-v5/runtime.js";
                import { decodeLocaleArtifact } from "./app.esm-v5/dynamic.js";
                const text=content=>content.nodes.map(node=>node.value??"").join("");
                // Percent selection compares the displayed 1.00, which exactly matches key 1.0.
                const exact=m.sample({count:decimal("0.01"),digits:2n,style:"percent"});
                if(exact.kind!=="localized-content"||text(exact)!=="exact 1.00% 4,200%")throw new Error(`alias/dynamic/exact mismatch: ${text(exact)}`);
                const hundred=m.sample({count:decimal("1.0"),digits:2n,style:"percent"});
                if(hundred.nodes[0].value!=="other ")throw new Error(`displayed 100.00% matched a key for 1: ${text(hundred)}`);
                if(m.huge({n:decimal("9007199254740993")})!=="exact")throw new Error("binary64-unsafe exact selector failed");
                let rejected=false;try{m.sample({count:decimal("1"),digits:2n,style:"bogus"});}catch{rejected=true;}if(!rejected)throw new Error("invalid dynamic enum accepted");
                const other=m.sample({count:decimal("2"),digits:0n,style:"decimal"});
                if(other.kind!=="localized-content"||other.nodes[1].annotations[0].name!=="note"||other.nodes[1].closingAnnotations[0].name!=="end"||other.nodes[1].closingAnnotations[0].value.coefficient!==0n)throw new Error("ordered annotations lost");
                const artifact=JSON.parse(await readFile(new URL("./semantic-artifact.json",import.meta.url),"utf8"));
                const declarations=artifact.messages.sample.ast.declarations;const explicit=declarations.findIndex(item=>item.kind==="input"&&item.name==="digits");if(explicit<0)throw new Error("fixture lost explicit dynamic dependency");declarations.splice(explicit,1);
                if(decodeLocaleArtifact(artifact).ok)throw new Error("implicit dynamic option dependency accepted");
                """, new UTF8Encoding(false));
            Run("bun", [script], directory);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Test, DisplayName("RMF2 v5 renderers hide annotations and preserve custom plain-text policies")]
    public void RendererParity()
    {
        const string contracts = """
            ,"markup":{"contracts":[
              {"name":"app:children","kind":"paired","children":"inline","interactive":false,"plainText":"children","options":{}},
              {"name":"app:omit","kind":"paired","children":"inline","interactive":false,"plainText":"omit","options":{}},
              {"name":"app:break","kind":"standalone","children":"none","interactive":false,"plainText":"lineBreak","options":{}},
              {"name":"app:explicit","kind":"paired","children":"inline","interactive":false,"plainText":"explicit","options":{}},
              {"name":"app:alternate","kind":"paired","children":"inline","interactive":false,"plainText":"alternateText","options":{}}
            ]}
            """;
        const string source = "children = {#app:children @note}keep{/app:children @end=0}\nomit = before {#app:omit}{#app:explicit}hidden{/app:explicit}{/app:omit} after\nbreak = before{#app:break/}after\nexplicit = {#app:explicit}label{/app:explicit}\nalternate = {#app:alternate}alt{/app:alternate}\nvalidated = {#app:children}keep{/app:children} {#link ref=destination}link{/link}\n";
        Rmf2ProjectCompilationV5 result = TranslationCompiler.CompileRmf2ProjectV5(
            Rmf2ProjectV5Tests.Project(contracts), [new TranslationSource("translations/en.rmf2", Encoding.UTF8.GetBytes(source))]);
        Assert.True(result.Success, string.Join("; ", result.Diagnostics.Select(item => item.Message)));
        string directory = Write(TranslationOutputRenderer.RenderRmf2V5EsmModules(result.Project!));
        try
        {
            string script = Path.Combine(directory, "renderer-parity.mjs");
            File.WriteAllText(script, """
                import { m } from "./app.esm-v5/messages.js";
                import { bindMarkup, createDomInlineRenderer, createInlineRenderer, defineMarkup, linkBinding, projectMarkup, toPlainText } from "./app.esm-v5/runtime.js";
                const check=(condition,message)=>{if(!condition)throw new Error(message);};
                const children=defineMarkup({name:"app:children",kind:"paired",children:"inline",interactive:false,plainText:"children",options:{}});
                const content=m.children();
                check(content.nodes[0].annotations[0].name==="note"&&content.nodes[0].closingAnnotations[0].name==="end","semantic annotations were dropped");
                let adapterElement;
                const renderer=createInlineRenderer({text:value=>value,element:element=>element.children.join("")},[
                  bindMarkup(children,element=>{adapterElement=element;return element.children.join("");})
                ]);
                check(renderer.render(content).join("")==="keep","custom renderer output");
                check(!Object.hasOwn(adapterElement,"annotations")&&!Object.hasOwn(adapterElement,"closingAnnotations"),"compiler annotations leaked into the renderer adapter");
                // Issue #96: a project tag needs no restated contract. Without a binding it reaches factory.element, like a
                // built-in tag, so a factory that dispatches by name (Svelte's LocalizedInline snippets) renders it.
                const seen=[];
                const structural=createInlineRenderer({text:value=>value,element:element=>{seen.push(element.name+"@"+element.occurrence);return `<${element.name}>${element.children.join("")}</${element.name}>`;}});
                check(structural.render(content).join("")==="<app:children>keep</app:children>"&&seen.join()==="app:children@children:v0.0","an unbound project tag did not reach factory.element: "+seen.join());
                check(Object.keys(projectMarkup).sort().join()==="app:alternate,app:break,app:children,app:explicit,app:omit","projectMarkup lists the project's tags");
                check(Object.isFrozen(projectMarkup)&&Object.isFrozen(projectMarkup["app:children"])&&projectMarkup["app:children"].name==="app:children"&&projectMarkup["app:break"].kind==="standalone","projectMarkup carries frozen named contracts");
                check(JSON.stringify(defineMarkup(projectMarkup["app:children"]))===JSON.stringify(projectMarkup["app:children"]),"projectMarkup is in defineMarkup form");
                check(structural.extend([bindMarkup(projectMarkup["app:children"],({children})=>`[${children.join("")}]`)]).render(content).join("")==="[keep]","an explicit binding from projectMarkup did not override the factory");
                check(createInlineRenderer({text:value=>value,element:()=>"factory"},[bindMarkup(children,({children})=>`(${children.join("")})`)]).render(content).join("")==="(keep)","a restated binding no longer overrides the factory");
                // Unbound omit and line-break tags reach the factory too; only plain-text projection drops them.
                check(structural.render(m.break()).join("")==="before<app:break></app:break>after","an unbound line-break tag was projected instead of rendered");
                const fakeDocument={createTextNode:value=>({value}),createElement:name=>{throw new Error("created "+name);}};
                let rejected=false;try{createDomInlineRenderer(fakeDocument).render(content);}catch(error){rejected=String(error).includes("No DOM renderer linked for 'app:children'");}check(rejected,"the DOM renderer created an element for an unbound project tag");
                const forgedKind=Object.freeze({...content,nodes:Object.freeze([Object.freeze({...content.nodes[0],kind:"script"})])});
                rejected=false;try{renderer.render(forgedKind);}catch{rejected=true;}check(rejected,"forged content-node discriminator reached the renderer");
                check(toPlainText(content)==="keep","children projection");
                const explicit=defineMarkup({name:"app:explicit",kind:"paired",children:"inline",interactive:false,plainText:"explicit",options:{}});
                let hiddenCalls=0;
                check(toPlainText(m.omit(),{custom:[bindMarkup(explicit,({children})=>{hiddenCalls++;return children.join("");})]})==="before  after"&&hiddenCalls===0,"omit projection materialized its subtree");
                check(toPlainText(m.break())==="before\nafter","line-break projection");
                for(const value of [m.explicit(),m.alternate()]){rejected=false;try{toPlainText(value);}catch{rejected=true;}check(rejected,"explicit adapter requirement");}
                const alternate=defineMarkup({name:"app:alternate",kind:"paired",children:"inline",interactive:false,plainText:"alternateText",options:{}});
                const custom=[bindMarkup(explicit,({children})=>children.join("")),bindMarkup(alternate,({children})=>children.join(""))];
                check(toPlainText(m.explicit(),{custom})==="label"&&toPlainText(m.alternate(),{custom})==="alt","custom plain-text adapters");
                let invalidCalls=0;
                const guarded=createInlineRenderer({text:value=>value,element:element=>element.children.join("")},[bindMarkup(children,element=>{invalidCalls++;return element.children.join("");})]);
                const linked=m.validated();
                const missingLink=Object.freeze({...linked,nodes:Object.freeze(linked.nodes.filter(node=>node.name!=="runic:link"))});
                rejected=false;try{guarded.render(missingLink,{slots:{destination:linkBinding({href:"/safe"})}});}catch{rejected=true;}check(rejected&&invalidCalls===0,"renderer callback ran before slot multiplicity validation");
                rejected=false;try{createInlineRenderer({text:value=>value,element:element=>element.children.join("")},[{contract:{name:"runic:strong",kind:"paired",children:"inline",interactive:false,plainText:"children",options:{}},render:()=>"forged"}]);}catch{rejected=true;}check(rejected,"built-in renderer override accepted");
                """, new UTF8Encoding(false));
            Run("bun", [script], directory);
            string typecheck = Path.Combine(directory, "typecheck.ts");
            File.WriteAllText(typecheck, """
                import { bindMarkup, createInlineRenderer, projectMarkup, type MarkupContract } from "./app.esm-v5/runtime.js";
                const contract: MarkupContract = projectMarkup["app:children"];
                const renderer = createInlineRenderer({ text: (value: string) => value, element: element => element.children.join("") },
                  [bindMarkup(contract, element => element.children.join(""))]);
                // @ts-expect-error projectMarkup declares only the project's tags.
                void projectMarkup["app:missing"];
                void renderer;
                """, new UTF8Encoding(false));
            Run("bun", [RepositoryPaths.Resolve("node_modules", "typescript", "bin", "tsc"), "--strict", "--noEmit", "--target", "ES2022", "--module", "NodeNext", "--moduleResolution", "NodeNext", "--lib", "ES2022,DOM", "--skipLibCheck", typecheck], directory);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Test, DisplayName("RMF2 markup contract v2 ESM links placement and bounded integer options")]
    public void IntegerOptions()
    {
        const string contracts = """
            ,"markup":{"contracts":[
              {"name":"app:step","kind":"paired","placement":"inline","children":"inline","interactive":false,"plainText":"children","options":{"level":{"type":"integer","minimum":1,"maximum":6,"default":"1"}}}
            ]}
            """;
        const string source = "fixed = {#app:step level=|3|}three{/app:step}\ndefaulted = {#app:step}one{/app:step}\ndynamic =\n  .input {$n :integer}\n  {{{#app:step level=$n}n{/app:step}}}\n";
        Rmf2ProjectCompilationV5 result = TranslationCompiler.CompileRmf2ProjectV5(
            Rmf2ProjectV5Tests.Project(contracts), [new TranslationSource("translations/en.rmf2", Encoding.UTF8.GetBytes(source))]);
        Assert.True(result.Success, string.Join("; ", result.Diagnostics.Select(item => item.Message)));
        string directory = Write(TranslationOutputRenderer.RenderRmf2V5EsmModules(result.Project!));
        try
        {
            string script = Path.Combine(directory, "integer-options.mjs");
            File.WriteAllText(script, """
                import { m } from "./app.esm-v5/messages.js";
                import { bindMarkup, createInlineRenderer, defineMarkup, rmf2Contract, rmf2RuntimeAbiVersion } from "./app.esm-v5/runtime.js";
                const check=(condition,message)=>{if(!condition)throw new Error(message);};
                check(rmf2RuntimeAbiVersion===3&&rmf2Contract.version===2,"contract markers");
                const exported=rmf2Contract.contracts["app:step"];
                check(exported.placement==="inline"&&exported.children==="inline"&&exported.options.level.minimum===1&&exported.options.level.maximum===6,"exported integer contract");
                const step=defineMarkup({name:"app:step",kind:"paired",placement:"inline",children:"inline",interactive:false,plainText:"children",options:{level:{type:"integer",values:[],default:"1",literalOnly:false,minimum:1,maximum:6}}});
                const renderer=createInlineRenderer({text:value=>value,element:element=>element.children.join("")},[bindMarkup(step,({options,children})=>`[${options.level}:${children.join("")}]`)]);
                check(renderer.render(m.fixed()).join("")==="[3:three]","quoted integer literal");
                check(renderer.render(m.defaulted()).join("")==="[1:one]","integer default");
                check(renderer.render(m.dynamic({n:6n})).join("")==="[6:n]","dynamic integer within bounds");
                for(const value of [0n,7n,-1n]){let rejected=false;try{renderer.render(m.dynamic({n:value}));}catch{rejected=true;}check(rejected,"dynamic integer outside bounds was rendered: "+value);}
                for(const bad of [{...step,placement:"block"},{...step,options:{level:{...step.options.level,maximum:5}}}]){let rejected=false;try{createInlineRenderer({text:v=>v,element:e=>e.children.join("")},[bindMarkup(bad,()=>"")]);}catch{rejected=true;}check(rejected,"mismatched renderer contract accepted");}
                let rejected=false;try{defineMarkup({name:"app:block",kind:"paired",placement:"block",children:"inline",interactive:false,plainText:"children",options:{}});}catch{rejected=true;}check(rejected,"custom block placement accepted");
                """, new UTF8Encoding(false));
            Run("bun", [script], directory);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Test, DisplayName("RMF2 v5 ESM manifest is closed versioned and accepted only as the exact shipping Vite contract")]
    public void ManifestIsolation()
    {
        Rmf2ProjectV5 project = Fixture();
        IReadOnlyList<TranslationGeneratedOutput> outputs = TranslationOutputRenderer.RenderRmf2V5EsmModules(project);
        TranslationGeneratedOutput manifest = outputs.Single(item => item.Kind == TranslationGeneratedOutputKind.WebModuleManifestJson);
        Assert.Equal("billing.esm-v5/web-module-manifest-v3.json", manifest.RelativePath);
        JsonObject json = JsonNode.Parse(manifest.Text)!.AsObject();
        Assert.Equal(3, json["webModuleManifestVersion"]!.GetValue<int>());
        Assert.Equal(4, json["esmAbiVersion"]!.GetValue<int>());
        Assert.Equal(3, json["rmf2RuntimeAbiVersion"]!.GetValue<int>());
        Assert.Equal(5, json["messageGrammarVersion"]!.GetValue<int>());
        Assert.Equal("rmf2-execution-v2", json["profile"]!.GetValue<string>());
        Rmf2SemanticV5SchemaTests.AssertValidation(
            Rmf2SemanticV5SchemaTests.ReadSchema("web-module-manifest-v3.schema.json"), json, true, "Staged v5 ESM manifest");
        string directory = Write(outputs);
        try
        {
            string plugin = RepositoryPaths.Resolve("packages", "web", "vite-plugin-runic-translations", "dist", "index.js");
            string script = Path.Combine(directory, "vite-reject.mjs");
            string manifestPath = Path.Combine(directory, manifest.RelativePath);
            File.WriteAllText(script, "import { readFile, writeFile } from 'node:fs/promises';\nimport { runicTranslations } from " + JsonString(plugin) + ";\n" +
                "const path=" + JsonString(manifestPath) + ";const adapter=runicTranslations({manifest:path});await adapter.buildStart.call({addWatchFile(){}});if(await adapter.resolveId('virtual:runic-translations/billing/runtime')!=='\\0virtual:runic-translations/billing/runtime')throw new Error('shipping Vite adapter did not activate v5');const document=JSON.parse(await readFile(path,'utf8'));document.profile='future';await writeFile(path,JSON.stringify(document));let rejected=false;try{await runicTranslations({manifest:path}).buildStart.call({addWatchFile(){}});}catch(e){rejected=String(e).includes('execution contract');}if(!rejected)throw new Error('shipping Vite adapter accepted mismatched v5 profile');\n", new UTF8Encoding(false));
            Run("bun", [script], directory);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Test, DisplayName("RMF2 v5 ESM preserves hostile NFC caller names without prototype mutation")]
    public void HostileNames()
    {
        const string message = "__proto__ = Proto\nconstructor = Constructor\nx =\n  .input {$__proto__ :string}\n  .input {$constructor :string}\n  .input {$user-name :string}\n  .input {$用户 :string}\n  {{ {$__proto__} {$constructor} {$user-name} {$用户} }}";
        Rmf2ProjectCompilationV5 result = TranslationCompiler.CompileRmf2ProjectV5(
            Rmf2ProjectV5Tests.Project(), [new TranslationSource("translations/en.rmf2", Encoding.UTF8.GetBytes(message))]);
        Assert.True(result.Success, string.Join("; ", result.Diagnostics.Select(item => item.Message)));
        IReadOnlyList<TranslationGeneratedOutput> outputs = TranslationOutputRenderer.RenderRmf2V5EsmModules(result.Project!);
        string declarations = outputs.Single(item => item.Kind == TranslationGeneratedOutputKind.EsmMessagesTypes).Text;
        foreach (string name in new[] { "__proto__", "constructor", "user-name", "用户" })
            Assert.True(declarations.Contains("readonly \"" + name + "\"", StringComparison.Ordinal), "TypeScript key was not quoted: " + name);
        string directory = Write(outputs);
        try
        {
            string script = Path.Combine(directory, "hostile.mjs");
            File.WriteAllText(script, """
                import { m } from "./app.esm-v5/messages.js";
                const inputs=Object.create(null); inputs.__proto__="p"; inputs.constructor="c"; inputs["user-name"]="u"; inputs["用户"]="雪";
                if(m.x(inputs)!==" p c u 雪 ")throw new Error("hostile names failed");
                if(m["__proto__"]()!=="Proto"||m.constructor()!=="Constructor"||Object.getPrototypeOf(m)!==null)throw new Error("hostile message keys failed");
                if(({}).polluted!==undefined||Object.getPrototypeOf(inputs)!==null)throw new Error("prototype pollution");
                """, new UTF8Encoding(false));
            Run("bun", [script], directory);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Test, DisplayName("RMF2 v5 ESM hardens locale dynamic pack and renderer boundaries")]
    public void RuntimeHardening()
    {
        Rmf2ProjectV5 european = CompileProject("numbers", "en", ["en", "nl"], false,
            ("en", "hello = Hello\namount =\n  .input {$value :number}\n  {{ {$value :number minimumFractionDigits=2 maximumFractionDigits=2} }}"),
            ("nl", "hello = Hallo\namount =\n  .input {$value :number}\n  {{ {$value :number minimumFractionDigits=2 maximumFractionDigits=2} }}"));
        string europeanDirectory = Write(TranslationOutputRenderer.RenderRmf2V5EsmModules(european));
        try
        {
            string script = Path.Combine(europeanDirectory, "locale.mjs");
            File.WriteAllText(script, """
                import { m } from "./numbers.esm-v5/messages.js";
                import { decimal, decodeWireValue, resolveLocale } from "./numbers.esm-v5/runtime.js";
                if(m.hello({locale:"nl"})!=="Hallo")throw new Error("zero-input locale options were treated as inputs");
                const amount=m.amount({value:decimal("1.25")},{locale:"nl"});if(amount!==" 1,25 ")throw new Error(`Dutch decimal punctuation diverged from .NET: ${amount}`);
                if(decodeWireValue("0000-01-01","date").ok||decodeWireValue("0000-01-01T00:00:00Z","datetime").ok)throw new Error("year zero date carrier accepted");
                for(const locale of ["en-a","en-a-b","en-a-foo-a-bar","en-US-Latn","de-1901-1901"]){let rejected=false;try{resolveLocale(locale);}catch(error){rejected=error instanceof RangeError&&String(error).includes("Invalid locale");}if(!rejected)throw new Error(`invalid locale accepted: ${locale}`);}
                """, new UTF8Encoding(false));
            Run("bun", [script], europeanDirectory);
        }
        finally { Directory.Delete(europeanDirectory, true); }

        // An empty tag, such as the invariant culture's name under LANG=C, follows the unsupported-locale policy as in .NET.
        string invariantDirectory = Write(TranslationOutputRenderer.RenderRmf2V5EsmModules(european));
        try
        {
            string script = Path.Combine(invariantDirectory, "invariant.mjs");
            File.WriteAllText(script, """
                import { m } from "./numbers.esm-v5/messages.js";
                import { createLocaleSource, resolveLocale } from "./numbers.esm-v5/runtime.js";
                if(resolveLocale("")!=="en")throw new Error("an empty locale did not resolve to the base locale");
                if(createLocaleSource({initialLocale:""}).getLocale()!=="en")throw new Error("an empty initial locale did not resolve to the base locale");
                const source=createLocaleSource({initialLocale:"nl"});source.setLocale("");if(source.getLocale()!=="en")throw new Error("setLocale('') did not select the base locale");
                if(m.hello({locale:""})!=="Hello")throw new Error("an empty format locale did not use the base locale");
                for(const locale of [" ",undefined,null]){let rejected=false;try{resolveLocale(locale);}catch(error){rejected=error instanceof RangeError;}if(!rejected)throw new Error(`locale accepted: ${String(locale)}`);}
                """, new UTF8Encoding(false));
            Run("bun", [script], invariantDirectory);
        }
        finally { Directory.Delete(invariantDirectory, true); }

        Rmf2ProjectCompilationV5 exactCompilation = TranslationCompiler.CompileRmf2ProjectV5(
            new TranslationSource("translations/runic.json", Encoding.UTF8.GetBytes(
                "{\"schemaVersion\":1,\"catalog\":\"exact\",\"code\":{\"namespace\":\"Example\",\"className\":\"Text\"},\"baseLocale\":\"en\",\"locales\":[\"en\"],\"runtime\":{\"unsupportedLocale\":\"exact\"}}")),
            [new TranslationSource("translations/en.rmf2", Encoding.UTF8.GetBytes("hello = Hello"))]);
        Assert.True(exactCompilation.Success, string.Join("; ", exactCompilation.Diagnostics.Select(item => item.Message)));
        string exactDirectory = Write(TranslationOutputRenderer.RenderRmf2V5EsmModules(exactCompilation.Project!));
        try
        {
            string script = Path.Combine(exactDirectory, "exact.mjs");
            File.WriteAllText(script, """
                import { resolveLocale } from "./exact.esm-v5/runtime.js";
                let rejected=false;try{resolveLocale("");}catch(error){rejected=error instanceof RangeError&&String(error).includes("Unsupported locale");}
                if(!rejected)throw new Error("the exact policy accepted an empty locale");
                """, new UTF8Encoding(false));
            Run("bun", [script], exactDirectory);
        }
        finally { Directory.Delete(exactDirectory, true); }

        Rmf2ProjectV5 legacyTag = CompileProject("legacy", "iw", ["iw"], false, ("iw", "hello = שלום"));
        string legacyDirectory = Write(TranslationOutputRenderer.RenderRmf2V5EsmModules(legacyTag));
        try
        {
            string script = Path.Combine(legacyDirectory, "legacy.mjs");
            File.WriteAllText(script, "import { m } from './legacy.esm-v5/messages.js'; if(m.hello({locale:'iw'})!=='שלום')throw new Error('preserved locale tag became unreachable');\n", new UTF8Encoding(false));
            Run("bun", [script], legacyDirectory);
        }
        finally { Directory.Delete(legacyDirectory, true); }

        Rmf2ProjectV5 extras = CompileProject("extras", "en", ["en", "de"], true,
            ("en", "x = X"), ("de", "x = X\nextra = {$n :integer}"));
        string extraDirectory = Write(TranslationOutputRenderer.RenderRmf2V5EsmModules(extras));
        try
        {
            File.WriteAllBytes(Path.Combine(extraDirectory, "extra.json"), Rmf2LocaleArtifactV5.Render(extras, "de").GetUtf8Bytes());
            string script = Path.Combine(extraDirectory, "extra.mjs");
            File.WriteAllText(script, """
                import { readFile } from "node:fs/promises";
                import { decodeLocaleArtifact, formatDynamicMessage } from "./extras.esm-v5/dynamic.js";
                const decoded=decodeLocaleArtifact(JSON.parse(await readFile(new URL("./extra.json",import.meta.url),"utf8")));
                if(!decoded.ok)throw new Error(decoded.reason);if(formatDynamicMessage(decoded.value,"extra",{n:2n})!=="2")throw new Error("allowed dynamic extra was not executable");
                """, new UTF8Encoding(false));
            Run("bun", [script], extraDirectory);
        }
        finally { Directory.Delete(extraDirectory, true); }
    }

    // #85: message modules were indented AST JSON per locale, repeated for every locale that falls back. They are now
    // text or text-and-placeholder parts where that is the whole message, minified JSON otherwise, and a fallback
    // locale shares its source's object. The runtime rebuilds the same AST, so the output equals the locale artifacts'.
    [Test, DisplayName("RMF2 v5 ESM reports the text direction of any locale tag")]
    public void LocaleDirection()
    {
        Rmf2ProjectV5 project = CompileProject("direction", "en", ["en", "ar", "he"], false,
            ("en", "hello = Hello"), ("ar", "hello = مرحبا"), ("he", "hello = שלום"));
        string directory = Write(TranslationOutputRenderer.RenderRmf2V5EsmModules(project));
        try
        {
            string script = Path.Combine(directory, "direction.mjs");
            File.WriteAllText(script, """
                import { locales, localeDirection } from "./direction.esm-v5/runtime.js";
                if(locales.map(localeDirection).join()!=="rtl,ltr,rtl")throw new Error(`catalog directions: ${locales.map(localeDirection)}`);
                // The likely script decides, so a region or an explicit script can change the language's default.
                const expected={ar:"rtl","ar-EG":"rtl","AR-eg":"rtl",he:"rtl",iw:"rtl",fa:"rtl",ur:"rtl",ps:"rtl",dv:"rtl",yi:"rtl",ckb:"rtl",sd:"rtl",ug:"rtl",ks:"rtl",
                  "pa-PK":"rtl","pa-Arab":"rtl","az-IR":"rtl","uz-AF":"rtl","ku-Arab":"rtl","en-Arab":"rtl","und-Hebr":"rtl","ar-u-nu-latn":"rtl",
                  en:"ltr","en-US":"ltr",de:"ltr",ja:"ltr","zh-Hant-TW":"ltr","sr-Cyrl":"ltr",pa:"ltr",ku:"ltr","he-Latn":"ltr","sd-Deva":"ltr","ks-Deva":"ltr","ar-Latn":"ltr",und:"ltr"};
                for(const [tag,direction] of Object.entries(expected))if(localeDirection(tag)!==direction)throw new Error(`${tag}: ${localeDirection(tag)}`);
                for(const tag of [""," ","en-","x","en-a",undefined,null,1]){let rejected=false;try{localeDirection(tag);}catch(error){rejected=error instanceof RangeError;}if(!rejected)throw new Error(`accepted ${String(tag)}`);}
                // Every language the engine has data for agrees with the engine's own Intl.Locale text info. Tags with
                // an explicit script are not compared: JavaScriptCore reports "ltr" for en-Arab, ku-Arab, az-IR and
                // und-Hebr, where V8 and CLDR's script metadata say "rtl".
                const letters="abcdefghijklmnopqrstuvwxyz",candidates=[];
                for(const a of letters)for(const b of letters){candidates.push(a+b);for(const c of letters)candidates.push(a+b+c);}
                const languages=Intl.DateTimeFormat.supportedLocalesOf(candidates);if(languages.length<200)throw new Error(`only ${languages.length} languages`);
                const info=(tag)=>{const locale=new Intl.Locale(tag);return (typeof locale.getTextInfo==="function"?locale.getTextInfo():locale.textInfo).direction;};
                const mismatches=languages.filter(tag=>localeDirection(tag)!==info(tag));
                if(mismatches.length)throw new Error(`differs from Intl text info: ${mismatches.join(" ")}`);
                if(!languages.some(language=>localeDirection(language)==="rtl"))throw new Error("no right-to-left language");
                """, new UTF8Encoding(false));
            Run("bun", [script], directory);

            // Without Intl.Locale only an explicit script subtag counts.
            string fallback = Path.Combine(directory, "fallback.mjs");
            File.WriteAllText(fallback, """
                delete Intl.Locale;
                const { localeDirection } = await import("./direction.esm-v5/runtime.js");
                if(localeDirection("ar-Arab")!=="rtl"||localeDirection("ar")!=="ltr"||localeDirection("en")!=="ltr")throw new Error("explicit scripts only");
                """, new UTF8Encoding(false));
            Run("bun", [fallback], directory);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Test, DisplayName("RMF2 v5 ESM message modules are compact and format as the locale artifacts do")]
    public void CompactMessageModules()
    {
        const string english = """
            title = Updates
            empty = {{}}
            greet = Hello {$name}, welcome to {$place}
            count = {$count :integer} updates
            braces = Literal \{x\} and \\ backslash
            plural =
              .input {$count :integer}
              .match $count
              one {{One update}}
              *   {{{$count} updates}}
            """;
        const string german = """
            title = Aktualisierungen
            greet = Hallo {$name}, willkommen in {$place}
            """;
        const string json = """{"schemaVersion":1,"catalog":"compact","code":{"namespace":"Example","className":"Text"},"baseLocale":"en","locales":["en","de"],"validation":{"translationCompleteness":"allow"}}""";
        Rmf2ProjectCompilationV5 compilation = TranslationCompiler.CompileRmf2ProjectV5(new TranslationSource("translations/runic.json", Encoding.UTF8.GetBytes(json)),
            [new("translations/en.rmf2", Encoding.UTF8.GetBytes(english)), new("translations/de.rmf2", Encoding.UTF8.GetBytes(german))]);
        Assert.True(compilation.Success, string.Join("; ", compilation.Diagnostics.Select(item => item.Message)));
        Rmf2ProjectV5 project = compilation.Project!;
        IReadOnlyList<TranslationGeneratedOutput> outputs = TranslationOutputRenderer.RenderRmf2V5EsmModules(project);
        string Module(string key) => outputs.Single(output => output.RelativePath.EndsWith("/" + Rmf2GeneratedNamesV1.Identifier(key) + ".js", StringComparison.Ordinal)).Text;
        Assert.True(Module("title").Contains("messages[\"de\"] = freezeGeneratedText(\"title\", \"de\", \"Aktualisierungen\");", StringComparison.Ordinal), Module("title"));
        Assert.True(Module("greet").Contains("freezeGeneratedPattern(\"greet\", \"en\", [\"Hello \", [\"name\"], \", welcome to \", [\"place\"]])", StringComparison.Ordinal), Module("greet"));
        Assert.True(Module("plural").Contains("messages[\"de\"] = messages[\"en\"];", StringComparison.Ordinal), Module("plural"));
        Assert.True(Module("plural").Contains("JSON.parse(", StringComparison.Ordinal), Module("plural"));
        Assert.True(Module("count").Contains("JSON.parse(", StringComparison.Ordinal), Module("count"));
        foreach (TranslationGeneratedOutput output in outputs.Where(output => output.RelativePath.Contains("/messages/r_", StringComparison.Ordinal)))
            Assert.True(!output.Text.Contains("\\n  ", StringComparison.Ordinal), output.RelativePath + " contains indented JSON.");

        string directory = Write(outputs);
        try
        {
            foreach (string locale in new[] { "en", "de" })
                File.WriteAllBytes(Path.Combine(directory, locale + ".json"), Rmf2LocaleArtifactV5.Render(project, locale).GetUtf8Bytes());
            string script = Path.Combine(directory, "compact.mjs");
            File.WriteAllText(script, """
                import { readFile } from "node:fs/promises";
                import { m } from "./compact.esm-v5/messages.js";
                import { decodeLocaleArtifact, formatDynamicMessage } from "./compact.esm-v5/dynamic.js";
                const calls = { title: [], empty: [], greet: [{ name: "Ada", place: "{Runic}" }], count: [{ count: 1234 }], braces: [], plural: [{ count: 1 }, { count: 3 }] };
                for (const locale of ["en", "de"]) {
                  const artifact = decodeLocaleArtifact(JSON.parse(await readFile(new URL(`./${locale}.json`, import.meta.url), "utf8")));
                  if (!artifact.ok) throw new Error(`artifact ${locale}: ${artifact.reason}`);
                  for (const [key, inputs] of Object.entries(calls))
                    for (const input of inputs.length ? inputs : [undefined]) {
                      const compiled = input ? m[key](input, { locale }) : m[key]({ locale });
                      const dynamic = formatDynamicMessage(artifact.value, key, input ?? {}, { locale });
                      if (compiled !== dynamic) throw new Error(`${locale} ${key}: ${compiled} !== ${dynamic}`);
                    }
                }
                const expected = { title: "Aktualisierungen", greet: "Hallo Ada, willkommen in {Runic}", braces: "Literal {x} and \\ backslash", empty: "" };
                for (const [key, text] of Object.entries(expected)) {
                  const actual = key === "greet" ? m.greet({ name: "Ada", place: "{Runic}" }, { locale: "de" }) : m[key]({ locale: "de" });
                  if (actual !== text) throw new Error(`${key}: ${actual}`);
                }
                if (m.plural({ count: 1 }, { locale: "de" }) !== "One update") throw new Error("shared fallback");
                console.log("compact OK");
                """, new UTF8Encoding(false));
            Run("bun", [script], directory);
        }
        finally { Directory.Delete(directory, true); }
    }

    internal static Rmf2ProjectV5 CompileProject(string id, string baseLocale, IReadOnlyList<string> locales,
        bool allowExtras, params (string Locale, string Source)[] sources)
    {
        string json = JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            catalog = id,
            code = new { @namespace = "Example", className = "Text" },
            baseLocale,
            locales,
            validation = allowExtras ? new { extraLocaleKeys = "allow" } : null,
        }, ProjectJsonOptions);
        Rmf2ProjectCompilationV5 result = TranslationCompiler.CompileRmf2ProjectV5(
            new TranslationSource("translations/runic.json", Encoding.UTF8.GetBytes(json)),
            sources.Select(source => new TranslationSource("translations/" + source.Locale + ".rmf2", Encoding.UTF8.GetBytes(source.Source))).ToArray());
        Assert.True(result.Success, string.Join("; ", result.Diagnostics.Select(item => item.Message)));
        return result.Project!;
    }

    private static Rmf2ProjectV5 Fixture()
    {
        string root = RepositoryPaths.Resolve("specs", "translations", "corpus", "v5-project");
        Rmf2ProjectCompilationV5 result = TranslationCompiler.CompileRmf2ProjectV5(
            new TranslationSource("translations/runic.json", File.ReadAllBytes(Path.Combine(root, "runic.json"))),
            [new("translations/en.rmf2", File.ReadAllBytes(Path.Combine(root, "en.rmf2"))), new("translations/de.rmf2", File.ReadAllBytes(Path.Combine(root, "de.rmf2")))]);
        Assert.True(result.Success, string.Join("; ", result.Diagnostics.Select(item => item.Message)));
        return result.Project!;
    }

    internal static string Write(IReadOnlyList<TranslationGeneratedOutput> outputs)
    {
        string directory = Path.Combine(Path.GetTempPath(), "runic-rmf2-esm-v5-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        foreach (TranslationGeneratedOutput output in outputs) { string path = Path.Combine(directory, output.RelativePath.Replace('/', Path.DirectorySeparatorChar)); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllBytes(path, output.GetUtf8Bytes()); }
        return directory;
    }

    private static string JsonString(string value) => System.Text.Json.JsonSerializer.Serialize(value);
    internal static void Run(string file, IReadOnlyList<string> arguments, string directory)
    {
        var start = new ProcessStartInfo(file) { WorkingDirectory = directory, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        using Process process = Process.Start(start) ?? throw new InvalidOperationException("Could not start " + file); string output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd(); process.WaitForExit(); Assert.Equal(0, process.ExitCode, output);
    }
}
