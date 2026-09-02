using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using CompilerModel = Runic.Translations.Compiler;
using Runic.Translations.Compiler.Generation;

namespace Runic.Translations.Compiler.Tests;

internal static class EsmGenerationTests
{
    public static void Register(TestRunner runner)
    {
        runner.Add("ESM generation is deterministic and manifest-complete", DeterministicManifest);
        runner.Add("generated ESM executes v1 messages and portable formatting in Node", ExecutesInNode);
    }

    private static void DeterministicManifest()
    {
        CompilerModel.CompiledTextCatalog catalog = Catalog();
        IReadOnlyList<TranslationGeneratedOutput> first = TranslationOutputRenderer.RenderEsmModules(catalog);
        IReadOnlyList<TranslationGeneratedOutput> second = TranslationOutputRenderer.RenderEsmModules(catalog);

        Assert.Equal(15, first.Count);
        Assert.Equal(string.Join('|', first.Select(output => output.RelativePath)), string.Join('|', second.Select(output => output.RelativePath)));
        for (int index = 0; index < first.Count; index++)
        {
            Assert.Equal(first[index].Sha256, second[index].Sha256);
            Assert.True(first[index].GetUtf8Bytes().AsSpan().SequenceEqual(second[index].GetUtf8Bytes()), first[index].RelativePath);
        }

        TranslationGeneratedOutput manifest = first.Single(output => output.Kind == TranslationGeneratedOutputKind.WebModuleManifestJson);
        using JsonDocument json = JsonDocument.Parse(manifest.GetUtf8Bytes());
        JsonElement root = json.RootElement;
        Assert.Equal(1, root.GetProperty("webModuleManifestVersion").GetInt32());
        Assert.Equal(TranslationOutputRenderer.EsmAbiVersion, root.GetProperty("esmAbiVersion").GetInt32());
        Assert.Equal(first.Count - 1, root.GetProperty("assets").GetArrayLength());

        TranslationGeneratedOutput message = first.Single(output => output.RelativePath.EndsWith("m$Common$Hello.js", StringComparison.Ordinal));
        Assert.True(!message.Text.Contains("{{open}}", StringComparison.Ordinal), "Escaped braces leaked past AST normalization.");
        Assert.True(message.Text.Contains("Literal {open}", StringComparison.Ordinal), "Normalized text node was not emitted.");
        TranslationGeneratedOutput namespaceIndex = first.Single(output => output.Kind == TranslationGeneratedOutputKind.EsmMessageNamespace);
        Assert.True(namespaceIndex.Text.Contains("m$Common$Hello as \"Common.Hello\"", StringComparison.Ordinal),
            "The exact catalog key was not exported by the internal namespace index.");
        TranslationGeneratedOutput messages = first.Single(output => output.Kind == TranslationGeneratedOutputKind.EsmMessagesIndex);
        Assert.True(messages.Text.Contains("export * as m", StringComparison.Ordinal), "The public message namespace was not generated.");
    }

    private static void ExecutesInNode()
    {
        IReadOnlyList<TranslationGeneratedOutput> outputs = TranslationOutputRenderer.RenderEsmModules(Catalog());
        string directory = Path.Combine(Path.GetTempPath(), "runic-esm-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            foreach (TranslationGeneratedOutput output in outputs)
            {
                string path = Path.Combine(directory, output.RelativePath.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllBytes(path, output.GetUtf8Bytes());
            }

            string script = Path.Combine(directory, "test.mjs");
            File.WriteAllText(script, """
                import { m } from "./portable.esm/messages.js";
                import { configureLocaleResolver, createLocaleSource, resolveLocale, contractFingerprint } from "./portable.esm/runtime.js";
                import { runWithLocale } from "./portable.esm/server.js";
                import { decodeTextReference, formatTextReference } from "./portable.esm/transport.js";
                const equal = (actual, expected) => { if (actual !== expected) throw new Error(`expected ${expected}; actual ${actual}`); };
                equal(m.Plain(), "Plain");
                equal(m["Common.Hello"]({ name: "Ada" }), "Literal {open} Ada");
                equal(m["Common.Hello"]({ name: "Ada" }, { locale: "de-DE" }), "Wörtlich {offen} Ada");
                equal(resolveLocale("de-AT"), "de");
                const source = createLocaleSource({ initialLocale: "de-DE" });
                equal(source.getLocale(), "de");
                const observed = [];
                const unsubscribe = source.subscribe(locale => observed.push(locale));
                source.setLocale("en-US");
                source.setLocale("en");
                unsubscribe();
                unsubscribe();
                source.setLocale("de");
                equal(observed.join(","), "en");
                const isolated = await Promise.all(Array.from({ length: 100 }, (_, index) => Promise.resolve().then(() =>
                  m.Plain({ locale: index % 2 === 0 ? "en" : "de" }))));
                if (isolated.some((value, index) => value !== (index % 2 === 0 ? "Plain" : "Einfach"))) throw new Error("explicit SSR locales leaked across calls");
                const requestLocal = await Promise.all(Array.from({ length: 100 }, (_, index) =>
                  runWithLocale(index % 2 === 0 ? "en" : "de", async () => { await Promise.resolve(); return m.Plain(); })));
                if (requestLocal.some((value, index) => value !== (index % 2 === 0 ? "Plain" : "Einfach"))) throw new Error("request-local SSR locales leaked across calls");
                const restore = configureLocaleResolver(() => "de");
                equal(m.Plain(), "Einfach");
                restore();
                equal(m["Formats.All"]({ count: 1234n, amount: 0.125, day: "2024-02-29", clock: "23:59:58.12", instant: "2024-02-29T12:34:56.123Z", id: "00112233-4455-6677-8899-aabbccddeeff", enabled: true }), "1234|0.13|2024-02-29|23:59:58.12|2024-02-29T12:34:56.1230000Z|00112233445566778899aabbccddeeff|true");
                let rejected = false;
                try { m["Common.Hello"]({ name: "Ada", extra: "no" }); } catch (error) { rejected = error instanceof TypeError; }
                if (!rejected) throw new Error("extra input was accepted");
                const decoded = decodeTextReference({ version: 1, catalog: "portable", contractFingerprint, key: "Common.Hello", arguments: { name: "Grace" }, fallbackText: "fallback" });
                if (!decoded.ok) throw new Error(`transport rejected: ${decoded.reason}`);
                equal(formatTextReference(decoded.value, m), "Literal {open} Grace");
                if (decodeTextReference({ version: 1, catalog: "portable", contractFingerprint: "sha256:bad", key: "Plain", arguments: {} }).ok) throw new Error("fingerprint skew accepted");
                """, new UTF8Encoding(false));

            var start = new ProcessStartInfo("node", script)
            {
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
            };
            using Process process = Process.Start(start) ?? throw new InvalidOperationException("Could not start Node.js.");
            string standardOutput = process.StandardOutput.ReadToEnd();
            string standardError = process.StandardError.ReadToEnd();
            process.WaitForExit();
            Assert.Equal(0, process.ExitCode, standardOutput + standardError);

            string types = Path.Combine(directory, "usage.ts");
            File.WriteAllText(types, """
                import { m } from "./portable.esm/messages.js";
                import { createLocaleSource } from "./portable.esm/runtime.js";
                import type { LocaleSource, LocalizedString } from "./portable.esm/runtime.js";
                import { decodeLocaleArtifact } from "./portable.esm/dynamic.js";
                const value: LocalizedString = m["Common.Hello"]({ name: "Ada" }, { locale: "de" });
                m["Formats.All"]({ count: 1n, amount: 1.5, day: "2024-01-01", clock: "12:00:00", instant: "2024-01-01T12:00:00Z", id: "00112233-4455-6677-8899-aabbccddeeff", enabled: true });
                decodeLocaleArtifact({});
                const source: LocaleSource = createLocaleSource({ initialLocale: "de" });
                source.subscribe(locale => { const exact: "en" | "de" = locale; void exact; });
                // @ts-expect-error Generated inputs are exact and typed.
                m["Common.Hello"]({ name: 42 });
                void value;
                """, new UTF8Encoding(false));
            string typeScript = Path.Combine(RepositoryPaths.RepositoryRoot, "web", "node_modules", ".bin", "tsc");
            var typeCheck = new ProcessStartInfo(typeScript)
            {
                WorkingDirectory = directory,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
            };
            foreach (string argument in new[] { "--noEmit", "--strict", "--target", "ES2022", "--module", "NodeNext", "--moduleResolution", "NodeNext", types })
                typeCheck.ArgumentList.Add(argument);
            using Process checker = Process.Start(typeCheck) ?? throw new InvalidOperationException("Could not start TypeScript.");
            string checkOutput = checker.StandardOutput.ReadToEnd() + checker.StandardError.ReadToEnd();
            checker.WaitForExit();
            Assert.Equal(0, checker.ExitCode, checkOutput);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static CompilerModel.CompiledTextCatalog Catalog()
    {
        const string manifest = """
            {
              "schemaVersion": 1,
              "catalog": "portable",
              "code": { "namespace": "Tests", "className": "PortableText" },
              "defaultLocale": "en",
              "locales": [{ "tag": "en" }, { "tag": "de", "fallback": "en" }],
              "layers": [{ "name": "base", "priority": 0 }],
              "validation": { "translationCompleteness": "allow" },
              "runtime": { "unsupportedLocale": "parentsThenDefault" }
            }
            """;
        const string english = """
            {
              "schemaVersion": 1, "catalog": "portable", "locale": "en", "layer": "base",
              "resources": {
                "Plain": "Plain",
                "Common": { "Hello": { "$value": "Literal {{open}} {name}", "$placeholders": { "name": { "type": "string" } } } },
                "Formats": { "All": {
                  "$value": "{count}|{amount}|{day}|{clock}|{instant}|{id}|{enabled}",
                  "$placeholders": {
                    "count": { "type": "int", "format": "plain" },
                    "amount": { "type": "number", "format": "fixed2" },
                    "day": { "type": "date", "format": "iso" },
                    "clock": { "type": "time", "format": "iso" },
                    "instant": { "type": "datetime", "format": "iso" },
                    "id": { "type": "guid", "format": "n" },
                    "enabled": { "type": "bool" }
                  }
                } }
              }
            }
            """;
        const string german = """
            {
              "schemaVersion": 1, "catalog": "portable", "locale": "de", "layer": "base",
              "resources": {
                "Plain": "Einfach",
                "Common": { "Hello": { "$value": "Wörtlich {{offen}} {name}", "$placeholders": { "name": { "type": "string" } } } }
              }
            }
            """;

        CompilerModel.TranslationCompilation compilation = CompilerModel.TranslationCompiler.Compile(
            [CompilerTests.Source("manifest.json", manifest)],
            [CompilerTests.Source("en.json", english), CompilerTests.Source("de.json", german)]);
        Assert.True(compilation.Success, CompilerTests.DiagnosticsText(compilation.Diagnostics));
        return Assert.Single(compilation.Catalogs);
    }
}
