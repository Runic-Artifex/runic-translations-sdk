using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using RunicTextResources.Compiler.Generation;

namespace RunicTextResources.Compiler.Tests;

internal static class CppGenerationTests
{
    public static void Register(TestRunner runner)
    {
        runner.Add("experimental C++20 backend compiles and executes from canonical AST", Compiles);
        runner.Add("experimental C++20 backend consumes schema v2 multi-selector AST", CompilesV2Selectors);
        runner.Add("experimental C++20 backend rejects unsupported structured nodes", RejectsStructuredNodes);
    }

    private static void Compiles()
    {
        var compilation = CompilerTests.CompileCase("valid", "minimal");
        Assert.True(compilation.Success, CompilerTests.DiagnosticsText(compilation.Diagnostics));
        IReadOnlyList<TextResourceGeneratedOutput> outputs = TextResourceOutputRenderer.RenderCpp(Assert.Single(compilation.Catalogs));
        Assert.Equal(2, outputs.Count);
        string directory = Path.Combine(Path.GetTempPath(), "runic-cpp-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            foreach (TextResourceGeneratedOutput output in outputs)
                File.WriteAllBytes(Path.Combine(directory, output.RelativePath), output.GetUtf8Bytes());
            File.WriteAllText(Path.Combine(directory, "main.cpp"), """
                #include "minimal.text-resources-v1.hpp"
                #include <iostream>
                int main() {
                  using namespace runic_text_resources::catalog_minimal;
                  std::cout << m_5Hello("en");
                }
                """, new UTF8Encoding(false));
            Run(directory, "clang++", "-std=c++20", "-Wall", "-Wextra", "-Werror", "minimal.text-resources-v1.cpp", "main.cpp", "-o", "test");
            string outputText = Run(directory, Path.Combine(directory, "test"));
            Assert.Equal("Hello", outputText);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static void CompilesV2Selectors()
    {
        const string manifest = """
            {"schemaVersion":2,"catalog":"cppv2","code":{"namespace":"Tests","className":"CppV2Text"},"defaultLocale":"en",
             "locales":[{"tag":"en"}],"layers":[{"name":"base","priority":0}]}
            """;
        const string english = """
            {"schemaVersion":2,"catalog":"cppv2","locale":"en","layer":"base","resources":{"Result":{"$value":{
              "inputs":{"count":{"type":"int64"},"role":{"type":"string"}},
              "selectors":[{"name":"quantity","input":"count","function":"plural"},{"name":"roleKind","input":"role","function":"literal"}],
              "variants":[{"match":{"quantity":"one","roleKind":"admin"},"value":"one admin"},{"match":{"quantity":"*","roleKind":"*"},"value":"{count} {role}"}]}}}}
            """;
        var compilation = RunicTextResources.Compiler.TextResourceCompiler.Compile(
            [CompilerTests.Source("manifest.json", manifest)], [CompilerTests.Source("en.json", english)]);
        Assert.True(compilation.Success, CompilerTests.DiagnosticsText(compilation.Diagnostics));
        IReadOnlyList<TextResourceGeneratedOutput> outputs = TextResourceOutputRenderer.RenderCpp(Assert.Single(compilation.Catalogs));
        string directory = Path.Combine(Path.GetTempPath(), "runic-cpp-v2-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            foreach (TextResourceGeneratedOutput output in outputs) File.WriteAllBytes(Path.Combine(directory, output.RelativePath), output.GetUtf8Bytes());
            File.WriteAllText(Path.Combine(directory, "main.cpp"), """
                #include "cppv2.text-resources-v1.hpp"
                #include <iostream>
                int main() {
                  using namespace runic_text_resources::catalog_cppv2;
                  std::cout << m_6Result("en", m_6Result_args{2, "guest"});
                }
                """, new UTF8Encoding(false));
            Run(directory, "clang++", "-std=c++20", "-Wall", "-Wextra", "-Werror", "cppv2.text-resources-v1.cpp", "main.cpp", "-o", "test");
            Assert.Equal("2 guest", Run(directory, Path.Combine(directory, "test")));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static void RejectsStructuredNodes()
    {
        const string manifest = """
            {"schemaVersion":2,"catalog":"cppformat","code":{"namespace":"Tests","className":"CppFormatText"},"defaultLocale":"en","locales":[{"tag":"en"}],"layers":[{"name":"base","priority":0}]}
            """;
        const string english = """
            {"schemaVersion":2,"catalog":"cppformat","locale":"en","layer":"base","resources":{"Value":{"$value":{"inputs":{"count":{"type":"int64"}},"selectors":[],"variants":[{"match":{},"value":[{"format":{"input":"count","function":"integer","format":"grouped"}}]}]}}}}
            """;
        var compilation = RunicTextResources.Compiler.TextResourceCompiler.Compile(
            [CompilerTests.Source("manifest.json", manifest)], [CompilerTests.Source("en.json", english)]);
        Assert.True(compilation.Success, CompilerTests.DiagnosticsText(compilation.Diagnostics));
        try
        {
            TextResourceOutputRenderer.RenderCpp(Assert.Single(compilation.Catalogs));
            throw new InvalidOperationException("C++ accepted an unsupported structured format node.");
        }
        catch (NotSupportedException exception)
        {
            Assert.True(exception.Message.Contains("ICU4C", StringComparison.Ordinal), exception.Message);
        }
    }

    private static string Run(string directory, string fileName, params string[] arguments)
    {
        var start = new ProcessStartInfo(fileName)
        {
            WorkingDirectory = directory,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        using Process process = Process.Start(start) ?? throw new InvalidOperationException("Could not start " + fileName);
        string output = process.StandardOutput.ReadToEnd();
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.Equal(0, process.ExitCode, output + error);
        return output;
    }
}
