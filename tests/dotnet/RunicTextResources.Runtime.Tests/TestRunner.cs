using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;

namespace RunicTextResources.Runtime.Tests;

internal sealed class TestRunner
{
    private readonly List<(string Name, Func<Task> Test)> _tests = new();

    public void Add(string name, Action test) =>
        Add(name, () => { test(); return Task.CompletedTask; });

    public void Add(string name, Func<Task> test)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(test);
        _tests.Add((name, test));
    }

    public bool HasTest(string name) => _tests.Exists(test => string.Equals(test.Name, name, StringComparison.Ordinal));

    public async Task<int> RunAsync()
    {
        int failed = 0;
        foreach ((string name, Func<Task> test) in _tests)
        {
            try
            {
                await test().ConfigureAwait(false);
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

    public static void Same(object expected, object actual, string? message = null)
    {
        if (!ReferenceEquals(expected, actual))
            throw new InvalidOperationException(message ?? "Expected the same object reference.");
    }

    public static void Equal<T>(T expected, T actual, string? message = null)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException(string.Create(CultureInfo.InvariantCulture,
                $"{(message is null ? string.Empty : message + ": ")}Expected <{expected}>; actual <{actual}>."));
    }

    public static T Throws<T>(Action action, string? messageContains = null) where T : Exception
    {
        try { action(); }
        catch (T exception)
        {
            if (messageContains is not null && !exception.Message.Contains(messageContains, StringComparison.Ordinal))
                throw new InvalidOperationException($"Exception did not contain <{messageContains}>: {exception.Message}");
            return exception;
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException($"Expected {typeof(T).Name}; actual {exception.GetType().Name}.", exception);
        }

        throw new InvalidOperationException($"Expected {typeof(T).Name}; no exception was thrown.");
    }

    public static async Task<T> ThrowsAsync<T>(Func<Task> action, string? messageContains = null) where T : Exception
    {
        try { await action().ConfigureAwait(false); }
        catch (T exception)
        {
            if (messageContains is not null && !exception.Message.Contains(messageContains, StringComparison.Ordinal))
                throw new InvalidOperationException($"Exception did not contain <{messageContains}>: {exception.Message}");
            return exception;
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException($"Expected {typeof(T).Name}; actual {exception.GetType().Name}.", exception);
        }

        throw new InvalidOperationException($"Expected {typeof(T).Name}; no exception was thrown.");
    }
}
