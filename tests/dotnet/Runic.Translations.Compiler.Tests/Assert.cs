using System;
using System.Collections.Generic;
using System.Globalization;

namespace Runic.Translations.Compiler.Tests;

// The suite's original assertions; TUnit reports the exception message and stack trace of a failing case.
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

    public static void Throws<TException>(Action action, string message) where TException : Exception
    {
        try { action(); }
        catch (TException) { return; }
        throw new InvalidOperationException(message + ": expected " + typeof(TException).Name + ".");
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
