using System;
using System.Collections.Generic;
using System.Globalization;

namespace Runic.Translations.Generator.Tests;

// The suite's original assertions; TUnit reports the exception message and stack trace of a failing case.
internal static class Assert
{
    internal static T Throws<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T exception) { return exception; }
        throw new InvalidOperationException("Expected " + typeof(T).Name + ".");
    }

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
