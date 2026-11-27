using System.Collections.Generic;
using System.Linq;
using Runic.Translations.Editor;
using TUnit.Core;

namespace Runic.Translations.Authoring.Tests;

// D074.10: the editor spell checks only the words of a text; placeholders, markup, escapes and a complex message's
// syntax are skipped.
internal sealed class EditorSpellingRangesTests
{
    private static string Checked(string text, EditorSpellingTextKind kind)
    {
        IReadOnlyList<(int Start, int Length)> skip = EditorSpellingRanges.Skip(text, kind);
        char[] visible = text.ToCharArray();
        foreach (var (start, length) in skip)
            for (int index = start; index < start + length; index++) visible[index] = '_';
        return new string(visible);
    }

    private static string Words(string text, EditorSpellingTextKind kind) =>
        string.Join(",", Checked(text, kind).Split(['_', ' ', '\n', ',', '.', ':'], System.StringSplitOptions.RemoveEmptyEntries));

    [Test, DisplayName("Spelling skips a pattern's placeholders, markup and escapes")]
    public void Pattern()
    {
        Assert.Equal("Hello,see,docs,now", Words("Hello {$name}, see {#link}docs{/link} \\{now\\}.", EditorSpellingTextKind.Pattern));
        Assert.Equal("Total,items", Words("Total: {$count :number style=|a}b|} items", EditorSpellingTextKind.Pattern));
        Assert.Equal("Hello _______ there", Checked("Hello {$name} there", EditorSpellingTextKind.Pattern));
    }

    [Test, DisplayName("Spelling skips the rest of an unclosed placeholder")]
    public void Unclosed()
    {
        Assert.Equal("Hello ______", Checked("Hello {$name", EditorSpellingTextKind.Pattern));
    }

    [Test, DisplayName("Spelling reads a complex message's quoted patterns only")]
    public void ComplexMessage()
    {
        Assert.Equal("One,file,Many,files",
            Words(".input {$n :number}\n.match $n\none {{One file {$n}}}\n* {{Many files}}", EditorSpellingTextKind.Message));
        Assert.Equal("__Quoted ____ text__", Checked("{{Quoted {$x} text}}", EditorSpellingTextKind.Message));
        Assert.Equal("Brace", Words(".local $x = {|a}}b| :string} {{Brace}}", EditorSpellingTextKind.Message));
    }

    [Test, DisplayName("Spelling reads a simple message and a pattern that starts with a dot as text")]
    public void SimpleMessage()
    {
        Assert.Equal("Plain ____ text", Checked("Plain {$x} text", EditorSpellingTextKind.Message));
        Assert.Equal(".NET is ready", Checked(".NET is ready", EditorSpellingTextKind.Pattern));
    }
}
