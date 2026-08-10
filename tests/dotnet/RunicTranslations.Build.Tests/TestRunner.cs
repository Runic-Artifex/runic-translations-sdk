using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace RunicTranslations.Build.Tests;

internal sealed class TestRunner
{
    private readonly List<(string Name, Action Test)> _tests = [];

    public void Add(string name, Action test) => _tests.Add((name, test));

    public int Run()
    {
        int failed = 0;
        foreach ((string name, Action test) in _tests)
        {
            try
            {
                test();
                Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"PASS {name}"));
            }
            catch (Exception exception)
            {
                failed++;
                Console.Error.WriteLine(string.Create(CultureInfo.InvariantCulture, $"FAIL {name}"));
                Console.Error.WriteLine(exception);
            }
        }

        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"RESULT {_tests.Count - failed}/{_tests.Count} passed"));
        return failed == 0 ? 0 : 1;
    }
}

internal static class Assert
{
    public static void True(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    public static void False(bool condition, string message) => True(!condition, message);

    public static void Equal<T>(T expected, T actual, string? message = null)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{message ?? "Values differ"}. Expected <{expected}>; actual <{actual}>.");
        }
    }

    public static void Contains(string expected, string actual, string? message = null)
    {
        if (!actual.Contains(expected, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"{message ?? "Text not found"}. Expected <{expected}> in:{Environment.NewLine}{actual}");
        }
    }

    public static void FileBytesEqual(string expected, string actual)
    {
        Assert.True(File.Exists(expected), $"Expected file does not exist: {expected}");
        Assert.True(File.Exists(actual), $"Actual file does not exist: {actual}");
        Assert.True(File.ReadAllBytes(expected).AsSpan().SequenceEqual(File.ReadAllBytes(actual)), $"Files differ: {expected} and {actual}");
    }
}
