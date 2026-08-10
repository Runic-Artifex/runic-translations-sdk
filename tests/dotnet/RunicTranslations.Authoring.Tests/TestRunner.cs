using System;
using System.Collections.Generic;
using System.Globalization;

namespace RunicTranslations.Authoring.Tests;

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
        if (!condition) throw new InvalidOperationException(message);
    }

    public static void False(bool condition, string message) => True(!condition, message);

    public static void Equal<T>(T expected, T actual, string? message = null)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException(
                string.Create(CultureInfo.InvariantCulture, $"{message ?? "Values differ"}. Expected <{expected}>; actual <{actual}>."));
        }
    }

    public static void Throws<T>(Action action, string expectedMessage)
        where T : Exception
    {
        try
        {
            action();
        }
        catch (T exception)
        {
            if (!exception.Message.Contains(expectedMessage, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Exception did not contain '{expectedMessage}': {exception.Message}");
            }

            return;
        }

        throw new InvalidOperationException($"Expected {typeof(T).Name}.");
    }
}
