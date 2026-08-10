using System;
using System.Collections.Generic;
using System.Globalization;

namespace RunicTranslations.Compiler.Tests;

internal sealed class TestRunner
{
    private readonly List<(string Name, Action Test)> _tests = new();

    public void Add(string name, Action test)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(test);
        _tests.Add((name, test));
    }

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
                Console.Error.WriteLine(exception.ToString());
            }
        }

        Console.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"RESULT {_tests.Count - failed}/{_tests.Count} passed"));
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

    public static void Equal<T>(T expected, T actual, string? message = null)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"{(message is null ? string.Empty : message + ": ")}Expected <{expected}>; actual <{actual}>."));
        }
    }

    public static T Single<T>(IReadOnlyList<T> items, string? message = null)
    {
        if (items.Count != 1)
        {
            throw new InvalidOperationException(
                message ?? string.Create(CultureInfo.InvariantCulture, $"Expected one item; actual count was {items.Count}."));
        }

        return items[0];
    }
}
