using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using RunicTextResources.Compiler.Generation;

namespace RunicTextResources.Compiler.Tests;

internal static class CppGenerationTests
{
    public static void Register(TestRunner runner) =>
        runner.Add("experimental C++20 backend compiles and executes from canonical AST", Compiles);

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
