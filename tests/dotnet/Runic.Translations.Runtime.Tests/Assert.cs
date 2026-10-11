using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;

namespace Runic.Translations.Runtime.Tests;

// The suite's original assertions; TUnit reports the exception message and stack trace of a failing case.
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
