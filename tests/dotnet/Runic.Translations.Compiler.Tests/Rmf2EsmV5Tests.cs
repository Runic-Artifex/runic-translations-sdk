using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using Runic.Translations.Compiler.Generation;

namespace Runic.Translations.Compiler.Tests;

internal static class Rmf2EsmV5Tests
{
    internal static void Register(TestRunner runner)
    {
        runner.Add("RMF2 v5 generated ESM executes exact static dynamic transport and SSR paths", Executes);
        runner.Add("RMF2 v5 ESM preserves exact selection dynamic options aliases and ordered annotations", SemanticParity);
        runner.Add("RMF2 v5 ESM manifest is closed versioned and rejected by the shipping Vite adapter", ManifestIsolation);
        runner.Add("RMF2 v5 ESM preserves hostile NFC caller names without prototype mutation", HostileNames);
    }

    private static void Executes()
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
                import { decimal, linkBinding, toPlainText } from "./billing.esm-v5/runtime.js";
                import { runWithLocale } from "./billing.esm-v5/server.js";
                import { decodeLocaleArtifact, decodeLocalePack, formatDynamicMessage } from "./billing.esm-v5/dynamic.js";
                import { decodeTextReference } from "./billing.esm-v5/transport.js";
                const check = (condition, message) => { if (!condition) throw new Error(message); };
                check(m.account_total({ rate: decimal("0.125") }, { locale: "de" }) === "12,50%", "exact percent formatting");
                check(m.account_total({ rate: { coefficient: 125n, scale: 3, negative: false } }, { locale: "en" }) === "12.50%", "coefficient/scale input");
                const bill = m.account_bill({ count: 2n, rate: decimal("0.125"), "用户": "Ada" }, { locale: "de" });
                check(bill.kind === "localized-content" && bill.locale === "de" && Object.isFrozen(bill.nodes), "structured content");
                check(bill.nodes[0].children.map(node => node.value).join("") === "2 Rechnungen" && bill.nodes[0].attributes.ref === "invoice", "linked markup and options");
                const plain=toPlainText(bill,{slots:{invoice:linkBinding({href:"/invoice"})}});check(plain==="2 Rechnungen: 12,5%", `plain-text markup projection: ${JSON.stringify(plain)}`);
                const concurrent = await Promise.all([
                  runWithLocale("en", async () => { await Promise.resolve(); return m.account_total({ rate: decimal("0.125") }); }),
                  runWithLocale("de", async () => { await Promise.resolve(); return m.account_total({ rate: decimal("0.125") }); }),
                ]);
                check(concurrent[0] === "12.50%" && concurrent[1] === "12,50%", "request-local SSR isolation");
                const raw = JSON.parse(await readFile(new URL("./artifact.json", import.meta.url), "utf8"));
                const decoded = decodeLocaleArtifact(raw); check(decoded.ok, decoded.reason);
                check(formatDynamicMessage(decoded.value, "account_total", { rate: decimal("0.125") }) === "12,50%", "dynamic parity");
                let forgedRejected=false;try{formatDynamicMessage(Object.freeze(structuredClone(decoded.value)),"account_total",{rate:decimal("0.125")});}catch{forgedRejected=true;}check(forgedRejected,"forged frozen artifact bypassed validation");
                const bytes = new TextEncoder().encode(JSON.stringify(raw));
                const packed = await decodeLocalePack(bytes, "de", copy => { copy[0] = 0; return true; }); check(packed.ok, packed.reason);
                const hostile = structuredClone(raw); hostile.messages.account_total.ast.declarations[0].expression.operand.value = "missing";
                check(decodeLocaleArtifact(hostile).reason === "RTR0023/argument-contract-mismatch", "unbound AST accepted");
                const decimalDrift = structuredClone(raw); decimalDrift.messages.account_total.ast.declarations[1].expression.options[1].value.canonical = "125";
                check(!decodeLocaleArtifact(decimalDrift).ok, "canonical decimal drift accepted");
                const duplicate = new TextEncoder().encode(JSON.stringify(raw).replace('{"artifactVersion":5','{"artifactVersion":5,"artifactVersion":5'));
                check((await decodeLocalePack(duplicate, "de")).reason === "RTR0023/malformed", "duplicate JSON property accepted");
                const reference = decodeTextReference({ version:1, catalog:"billing", contractFingerprint:raw.contractFingerprint, key:"account_total", arguments:{rate:"0.125"} });
                check(reference.ok && reference.value.arguments.rate.coefficient === 125n && reference.value.arguments.rate.scale === 3, "exact decimal transport");
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
                const bill: LocalizedContent = m.account_bill({ count: 2n, rate: decimal("0.125"), "用户": "Ada" });
                void total; void bill;
                """, new UTF8Encoding(false));
            Run("bun", [RepositoryPaths.Resolve("node_modules", "typescript", "bin", "tsc"), "--strict", "--noEmit", "--target", "ES2022", "--module", "NodeNext", "--moduleResolution", "NodeNext", "--lib", "ES2022,DOM", "--skipLibCheck", typecheck], directory);
        }
        finally { Directory.Delete(directory, true); }
    }

    private static void SemanticParity()
    {
        string semantic = File.ReadAllText(RepositoryPaths.Resolve("specs", "translations", "corpus", "semantic-v5", "message.mf2"));
        string indented = string.Join('\n', semantic.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n').Select(line => "  " + line));
        string source = "sample =\n" + indented + "\nhuge =\n  .input {$n :number select=exact}\n  .match $n\n  9007199254740993 {{exact}}\n  * {{other}}\n";
        Rmf2ProjectCompilationV5 compilation = TranslationCompiler.CompileRmf2ProjectV5(
            Rmf2Tests.Project(), [new TranslationSource("translations/en.rmf2", Encoding.UTF8.GetBytes(source))]);
        Assert.True(compilation.Success, string.Join("; ", compilation.Diagnostics.Select(item => item.Message)));
        string directory = Write(TranslationOutputRenderer.RenderRmf2V5EsmModules(compilation.Project!));
        try
        {
            string script = Path.Combine(directory, "semantic.mjs");
            File.WriteAllText(script, """
                import { m } from "./app.esm-v5/messages.js";
                import { decimal } from "./app.esm-v5/runtime.js";
                const exact=m.sample({count:decimal("1.0"),digits:2n,style:"percent"});
                if(exact.kind!=="localized-content"||exact.nodes.map(node=>node.value).join("")!=="exact 100.00% 4200%")throw new Error(`alias/dynamic/exact mismatch: ${JSON.stringify(exact)}`);
                if(m.huge({n:decimal("9007199254740993")})!=="exact")throw new Error("binary64-unsafe exact selector failed");
                let rejected=false;try{m.sample({count:decimal("1"),digits:2n,style:"bogus"});}catch{rejected=true;}if(!rejected)throw new Error("invalid dynamic enum accepted");
                const other=m.sample({count:decimal("2"),digits:0n,style:"decimal"});
                if(other.kind!=="localized-content"||other.nodes[1].annotations[0].name!=="note"||other.nodes[1].closingAnnotations[0].name!=="end"||other.nodes[1].closingAnnotations[0].value.coefficient!==0n)throw new Error("ordered annotations lost");
                """, new UTF8Encoding(false));
            Run("bun", [script], directory);
        }
        finally { Directory.Delete(directory, true); }
    }

    private static void ManifestIsolation()
    {
        Rmf2ProjectV5 project = Fixture();
        IReadOnlyList<TranslationGeneratedOutput> outputs = TranslationOutputRenderer.RenderRmf2V5EsmModules(project);
        TranslationGeneratedOutput manifest = outputs.Single(item => item.Kind == TranslationGeneratedOutputKind.WebModuleManifestJson);
        Assert.Equal("billing.esm-v5/web-module-manifest-v3.json", manifest.RelativePath);
        JsonObject json = JsonNode.Parse(manifest.Text)!.AsObject();
        Assert.Equal(3, json["webModuleManifestVersion"]!.GetValue<int>());
        Assert.Equal(4, json["esmAbiVersion"]!.GetValue<int>());
        Assert.Equal(2, json["rmf2RuntimeAbiVersion"]!.GetValue<int>());
        Assert.Equal(5, json["messageGrammarVersion"]!.GetValue<int>());
        Assert.Equal("rmf2-execution-v2", json["profile"]!.GetValue<string>());
        Rmf2SemanticV5SchemaTests.AssertValidation(
            Rmf2SemanticV5SchemaTests.ReadSchema("web-module-manifest-v3.schema.json"), json, true, "Staged v5 ESM manifest");
        string directory = Write(outputs);
        try
        {
            string plugin = RepositoryPaths.Resolve("packages", "web", "vite-plugin-runic-translations", "index.js");
            string script = Path.Combine(directory, "vite-reject.mjs");
            File.WriteAllText(script, "import { runicTranslations } from " + JsonString(plugin) + ";\n" +
                "const plugin=runicTranslations({manifest:" + JsonString(Path.Combine(directory, manifest.RelativePath)) + "});let rejected=false;try{await plugin.buildStart.call({addWatchFile(){}});}catch(e){rejected=String(e).includes(\"manifest version '3'\");}if(!rejected)throw new Error('shipping Vite adapter accepted staged v5');\n", new UTF8Encoding(false));
            Run("bun", [script], directory);
        }
        finally { Directory.Delete(directory, true); }
    }

    private static void HostileNames()
    {
        const string message = "__proto__ = Proto\nconstructor = Constructor\nx =\n  .input {$__proto__ :string}\n  .input {$constructor :string}\n  .input {$user-name :string}\n  .input {$用户 :string}\n  {{ {$__proto__} {$constructor} {$user-name} {$用户} }}";
        Rmf2ProjectCompilationV5 result = TranslationCompiler.CompileRmf2ProjectV5(
            Rmf2Tests.Project(), [new TranslationSource("translations/en.rmf2", Encoding.UTF8.GetBytes(message))]);
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

    private static Rmf2ProjectV5 Fixture()
    {
        string root = RepositoryPaths.Resolve("specs", "translations", "corpus", "v5-project");
        Rmf2ProjectCompilationV5 result = TranslationCompiler.CompileRmf2ProjectV5(
            new TranslationSource("translations/runic.json", File.ReadAllBytes(Path.Combine(root, "runic.json"))),
            [new("translations/en.rmf2", File.ReadAllBytes(Path.Combine(root, "en.rmf2"))), new("translations/de.rmf2", File.ReadAllBytes(Path.Combine(root, "de.rmf2")))]);
        Assert.True(result.Success, string.Join("; ", result.Diagnostics.Select(item => item.Message)));
        return result.Project!;
    }

    private static string Write(IReadOnlyList<TranslationGeneratedOutput> outputs)
    {
        string directory = Path.Combine(Path.GetTempPath(), "runic-rmf2-esm-v5-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        foreach (TranslationGeneratedOutput output in outputs) { string path = Path.Combine(directory, output.RelativePath.Replace('/', Path.DirectorySeparatorChar)); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllBytes(path, output.GetUtf8Bytes()); }
        return directory;
    }

    private static string JsonString(string value) => System.Text.Json.JsonSerializer.Serialize(value);
    private static void Run(string file, IReadOnlyList<string> arguments, string directory)
    {
        var start = new ProcessStartInfo(file) { WorkingDirectory = directory, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        using Process process = Process.Start(start) ?? throw new InvalidOperationException("Could not start " + file); string output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd(); process.WaitForExit(); Assert.Equal(0, process.ExitCode, output);
    }
}
