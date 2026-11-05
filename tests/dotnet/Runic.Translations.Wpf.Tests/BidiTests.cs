using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TUnit.Core;

namespace Runic.Translations.Wpf.Tests;

/// <summary>
/// Formatting isolates placeholders with U+2066–U+2069 (LRI, RLI, FSI, PDI). WPF has its own bidi implementation, so
/// this checks on the Windows leg that a TextBlock lays them out invisibly: no glyph, no width.
/// </summary>
internal sealed class BidiTests
{
    [Test, DisplayName("bidi: isolates around placeholders render with no width in a TextBlock, in LTR and RTL text"), NotInParallel(WpfSuite.Key, Order = 260)]
    public void IsolatesAreInvisible()
    {
        foreach ((string plain, string isolated, FlowDirection direction) in new[]
        {
            ("Hello Ada!", "Hello ⁨Ada⁩!", FlowDirection.LeftToRight),
            ("Hello نور and Ada", "Hello ⁧نور⁩ and ⁦Ada⁩", FlowDirection.LeftToRight),
            ("مرحبا Ada", "مرحبا ⁨Ada⁩", FlowDirection.RightToLeft),
            ("שלום 42 קבצים", "שלום ⁧42⁩ קבצים", FlowDirection.RightToLeft),
        })
        {
            double expected = Width(plain, direction), actual = Width(isolated, direction);
            if (Math.Abs(expected - actual) > 0.5)
                throw new InvalidOperationException(string.Create(CultureInfo.InvariantCulture,
                    $"Isolates changed the TextBlock width of '{plain}' ({direction}): {expected:0.##} without, {actual:0.##} with."));
        }

        // FormattedText, which WPF's text controls share, measures the same.
        var typeface = new Typeface("Segoe UI");
        double Formatted(string text) => new FormattedText(text, CultureInfo.GetCultureInfo("en"), FlowDirection.LeftToRight, typeface, 20, Brushes.Black, 1.0).WidthIncludingTrailingWhitespace;
        if (Math.Abs(Formatted("Total 12") - Formatted("Total ⁦12⁩")) > 0.5)
            throw new InvalidOperationException("Isolates changed the FormattedText width.");
    }

    private static double Width(string text, FlowDirection direction)
    {
        var block = new TextBlock { Text = text, FontSize = 20, FontFamily = new FontFamily("Segoe UI"), FlowDirection = direction };
        block.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return block.DesiredSize.Width;
    }
}
