using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using RunicTextResources.Compiler.Generation;

namespace RunicTextResources.Compiler.Tests;

internal static class SchemaV2Tests
{
    public static void Register(TestRunner runner)
    {
        runner.Add("schema v2 lowers simple and plural variant messages for .NET and ESM", LowersVariants);
        runner.Add("schema v2 requires deterministic catch-all coverage", RequiresCatchAll);
    }

    private static void LowersVariants()
    {
        var compilation = Compile(includeCatchAll: true);
        Assert.True(compilation.Success, CompilerTests.DiagnosticsText(compilation.Diagnostics));
        var catalog = Assert.Single(compilation.Catalogs);
        Assert.Equal(2, catalog.SchemaVersion);
        Assert.Equal(2, catalog.MessageGrammarVersion);
        TextResourceGeneratedOutput accessors = TextResourceOutputRenderer.RenderCSharpAccessors(catalog);
        Assert.True(accessors.Text.Contains("TextMessageSelector.SelectPlural", StringComparison.Ordinal), "C# did not consume the v2 selector AST.");

        IReadOnlyList<TextResourceGeneratedOutput> esm = TextResourceOutputRenderer.RenderEsmModules(catalog);
        string directory = Path.Combine(Path.GetTempPath(), "runic-v2-esm-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            foreach (TextResourceGeneratedOutput output in esm)
            {
                string path = Path.Combine(directory, output.RelativePath.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllBytes(path, output.GetUtf8Bytes());
            }
            string script = Path.Combine(directory, "test.mjs");
            File.WriteAllText(script, """
                import { m$Files$Deleted } from "./v2.esm/messages.js";
                if (m$Files$Deleted({ count: 1n }) !== "One file") throw new Error("cardinal one failed");
                if (m$Files$Deleted({ count: 3n }) !== "3 files") throw new Error("catch-all failed");
                if (m$Files$Deleted({ count: 1n }, { locale: "de" }) !== "Eine Datei") throw new Error("localized variant failed");
                """, new UTF8Encoding(false));
            var start = new ProcessStartInfo("node", script) { RedirectStandardError = true, UseShellExecute = false };
            using Process process = Process.Start(start) ?? throw new InvalidOperationException("Could not start Node.js.");
            string error = process.StandardError.ReadToEnd();
            process.WaitForExit();
            Assert.Equal(0, process.ExitCode, error);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static void RequiresCatchAll()
    {
        var compilation = Compile(includeCatchAll: false);
        Assert.True(!compilation.Success, "A selector without catch-all coverage was accepted.");
        bool found = false;
        foreach (var diagnostic in compilation.Diagnostics) found |= diagnostic.Id == "RTR0030" && diagnostic.Message.Contains("catch-all", StringComparison.Ordinal);
        Assert.True(found, CompilerTests.DiagnosticsText(compilation.Diagnostics));
    }

    private static RunicTextResources.Compiler.TextResourceCompilation Compile(bool includeCatchAll)
    {
        const string manifest = """
            { "schemaVersion": 2, "catalog": "v2", "code": { "namespace": "Tests", "className": "V2Text" },
              "defaultLocale": "en", "locales": [{"tag":"en"},{"tag":"de","fallback":"en"}],
              "layers": [{"name":"base","priority":0}] }
            """;
        string otherEn = includeCatchAll ? ", { " + "\"match\":{\"quantity\":\"*\"},\"value\":\"{count} files\"}" : string.Empty;
        string otherDe = includeCatchAll ? ", { " + "\"match\":{\"quantity\":\"*\"},\"value\":\"{count} Dateien\"}" : string.Empty;
        string english = "{\"schemaVersion\":2,\"catalog\":\"v2\",\"locale\":\"en\",\"layer\":\"base\",\"resources\":{\"Files\":{\"Deleted\":{\"$value\":{\"inputs\":{\"count\":{\"type\":\"int64\"}},\"selectors\":[{\"name\":\"quantity\",\"input\":\"count\",\"function\":\"plural\"}],\"variants\":[{\"match\":{\"quantity\":\"one\"},\"value\":\"One file\"}" + otherEn + "]}}}}}";
        string german = "{\"schemaVersion\":2,\"catalog\":\"v2\",\"locale\":\"de\",\"layer\":\"base\",\"resources\":{\"Files\":{\"Deleted\":{\"$value\":{\"inputs\":{\"count\":{\"type\":\"int64\"}},\"selectors\":[{\"name\":\"quantity\",\"input\":\"count\",\"function\":\"plural\"}],\"variants\":[{\"match\":{\"quantity\":\"one\"},\"value\":\"Eine Datei\"}" + otherDe + "]}}}}}";
        return RunicTextResources.Compiler.TextResourceCompiler.Compile(
            [CompilerTests.Source("manifest.json", manifest)],
            [CompilerTests.Source("en.json", english), CompilerTests.Source("de.json", german)]);
    }
}
