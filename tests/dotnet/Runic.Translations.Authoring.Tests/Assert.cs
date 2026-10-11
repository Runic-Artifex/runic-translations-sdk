using System;
using System.Collections.Generic;
using System.Globalization;

namespace Runic.Translations.Authoring.Tests;

// The suite's original assertions; TUnit reports the exception message and stack trace of a failing case.
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
