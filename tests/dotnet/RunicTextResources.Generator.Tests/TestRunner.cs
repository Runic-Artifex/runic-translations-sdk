using System;
using System.Collections.Generic;
using System.Globalization;

namespace RunicTextResources.Generator.Tests;

internal sealed class TestRunner
{
    private readonly List<(string Name, Action Test)> _tests = new();

    internal void Add(string name, Action test) => _tests.Add((name, test));

    internal int Run()
    {
        int failures = 0;
        foreach ((string name, Action test) in _tests)
        {
            try
            {
                test();
                Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"PASS {name}"));
            }
            catch (Exception exception)
            {
                failures++;
                Console.Error.WriteLine(string.Create(CultureInfo.InvariantCulture, $"FAIL {name}"));
                Console.Error.WriteLine(exception);
            }
        }

        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"RESULT {_tests.Count - failures}/{_tests.Count} passed"));
        return failures == 0 ? 0 : 1;
    }
}

internal static class Assert
{
    internal static void True(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    internal static void Equal<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException(string.Create(CultureInfo.InvariantCulture, $"{message}: expected <{expected}>; actual <{actual}>."));
    }
}
