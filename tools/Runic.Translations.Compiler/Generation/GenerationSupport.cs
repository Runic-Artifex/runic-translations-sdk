using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Runic.Translations.Compiler.Generation;

internal sealed class GenerationWriter
{
    private readonly StringBuilder _builder = new StringBuilder();
    private int _indent;

    internal void Indent() => _indent++;
    internal void Unindent() => _indent--;
    internal void Blank() => _builder.Append('\n');

    internal void Line(string value = "")
    {
        for (int i = 0; i < _indent; i++) _builder.Append("    ");
        _builder.Append(value).Append('\n');
    }

    public override string ToString() => _builder.ToString();
}

internal static class GenerationSupport
{
    private static readonly HashSet<string> CSharpKeywords = new HashSet<string>(StringComparer.Ordinal)
    {
        "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked", "class",
        "const", "continue", "decimal", "default", "delegate", "do", "double", "else", "enum", "event",
        "explicit", "extern", "false", "finally", "fixed", "float", "for", "foreach", "goto", "if", "implicit",
        "in", "int", "interface", "internal", "is", "lock", "long", "namespace", "new", "null", "object",
        "operator", "out", "override", "params", "private", "protected", "public", "readonly", "ref", "return",
        "sbyte", "sealed", "short", "sizeof", "stackalloc", "static", "string", "struct", "switch", "this",
        "throw", "true", "try", "typeof", "uint", "ulong", "unchecked", "unsafe", "ushort", "using", "virtual",
        "void", "volatile", "while", "add", "alias", "and", "ascending", "async", "await", "by", "descending",
        "dynamic", "equals", "file", "from", "get", "global", "group", "init", "into", "join", "let", "managed",
        "nameof", "not", "notnull", "on", "or", "orderby", "partial", "record", "remove", "required", "scoped",
        "select", "set", "unmanaged", "value", "var", "when", "where", "with", "yield",
    };

    internal static string CSharpIdentifier(string value) => CSharpKeywords.Contains(value) ? "@" + value : value;

    internal static string CSharpNamespace(string value)
    {
        string[] segments = value.Split('.');
        for (int i = 0; i < segments.Length; i++) segments[i] = CSharpIdentifier(segments[i]);
        return string.Join(".", segments);
    }

    internal static string CSharpString(string value)
    {
        var result = new StringBuilder(value.Length + 2);
        result.Append('"');
        for (int i = 0; i < value.Length; i++)
        {
            char character = value[i];
            switch (character)
            {
                case '"': result.Append("\\\""); break;
                case '\\': result.Append("\\\\"); break;
                case '\0': result.Append("\\0"); break;
                case '\a': result.Append("\\a"); break;
                case '\b': result.Append("\\b"); break;
                case '\f': result.Append("\\f"); break;
                case '\n': result.Append("\\n"); break;
                case '\r': result.Append("\\r"); break;
                case '\t': result.Append("\\t"); break;
                case '\v': result.Append("\\v"); break;
                default:
                    if (character < ' ' || character == '\u2028' || character == '\u2029' || IsUnpairedSurrogate(value, i))
                        result.Append("\\u").Append(((int)character).ToString("x4", CultureInfo.InvariantCulture));
                    else
                        result.Append(character);
                    break;
            }
        }
        return result.Append('"').ToString();
    }

    internal static string JsonString(string value)
    {
        var result = new StringBuilder(value.Length + 2);
        result.Append('"');
        for (int i = 0; i < value.Length; i++)
        {
            char character = value[i];
            switch (character)
            {
                case '"': result.Append("\\\""); break;
                case '\\': result.Append("\\\\"); break;
                case '\b': result.Append("\\b"); break;
                case '\f': result.Append("\\f"); break;
                case '\n': result.Append("\\n"); break;
                case '\r': result.Append("\\r"); break;
                case '\t': result.Append("\\t"); break;
                default:
                    if (character < ' ' || IsUnpairedSurrogate(value, i))
                        result.Append("\\u").Append(((int)character).ToString("x4", CultureInfo.InvariantCulture));
                    else
                        result.Append(character);
                    break;
            }
        }
        return result.Append('"').ToString();
    }

    internal static string XmlDocumentation(string value)
    {
        var result = new StringBuilder(value.Length);
        for (int i = 0; i < value.Length; i++)
        {
            char character = value[i];
            switch (character)
            {
                case '&': result.Append("&amp;"); break;
                case '<': result.Append("&lt;"); break;
                case '>': result.Append("&gt;"); break;
                case '"': result.Append("&quot;"); break;
                case '\'': result.Append("&apos;"); break;
                case '\r': break;
                case '\n': result.Append(' '); break;
                default:
                    if ((character < ' ' && character != '\t') || character == '\uFFFE' || character == '\uFFFF' || IsUnpairedSurrogate(value, i)) result.Append('\uFFFD');
                    else result.Append(character);
                    break;
            }
        }
        return result.ToString();
    }

    private static bool IsUnpairedSurrogate(string value, int index)
    {
        char character = value[index];
        if (char.IsHighSurrogate(character)) return index + 1 >= value.Length || !char.IsLowSurrogate(value[index + 1]);
        return char.IsLowSurrogate(character) && (index == 0 || !char.IsHighSurrogate(value[index - 1]));
    }
}
