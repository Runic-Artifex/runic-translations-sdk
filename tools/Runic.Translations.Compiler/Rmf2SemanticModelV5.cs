using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;

namespace Runic.Translations.Compiler;

// Deliberately separate from CompiledMessagePattern: v4 consumers cannot represent
// these values without losing local formatting, option types or key identity.
internal sealed record Rmf2ValueV5(string Kind, string Value, string? Canonical = null);
internal sealed record Rmf2OptionV5(string Name, Rmf2ValueV5 Value);
internal sealed record Rmf2AnnotationV5(string Name, Rmf2ValueV5? Value);
// ValueType describes the resolved underlying carrier. Function/options describe
// its presentation and may be inherited or replaced without changing that carrier.
internal sealed record Rmf2ExpressionV5(Rmf2ValueV5 Operand, string ValueType, string? Function,
    IReadOnlyList<Rmf2OptionV5> Options, IReadOnlyList<Rmf2AnnotationV5> Annotations);
internal sealed record Rmf2DeclarationV5(string Kind, string Name, Rmf2ExpressionV5 Expression);
internal sealed record Rmf2InputV5(string Name, string Type);
internal sealed record Rmf2SelectorV5(Rmf2ValueV5 Value, string Type, string Function);
internal sealed record Rmf2KeyV5(string Kind, string? Value = null, string? Canonical = null);
internal abstract record Rmf2NodeV5;
internal sealed record Rmf2TextV5(string Value) : Rmf2NodeV5;
internal sealed record Rmf2ExpressionNodeV5(Rmf2ExpressionV5 Expression) : Rmf2NodeV5;
// Markup events retain closing annotations too. Balanced-inline lowering belongs
// to the future backend adapter; no event is silently erased here.
internal sealed record Rmf2MarkupV5(string Name, string MarkupKind,
    IReadOnlyList<Rmf2OptionV5> Options, IReadOnlyList<Rmf2AnnotationV5> Annotations) : Rmf2NodeV5;
internal sealed record Rmf2VariantV5(IReadOnlyList<Rmf2KeyV5> Keys, IReadOnlyList<Rmf2NodeV5> Nodes);
internal sealed record Rmf2MessageV5(Mf2SyntaxDocument Syntax, IReadOnlyList<Rmf2InputV5> Inputs,
    IReadOnlyList<Rmf2DeclarationV5> Declarations, IReadOnlyList<Rmf2SelectorV5> Selectors,
    IReadOnlyList<Rmf2VariantV5> Variants);
internal sealed record Rmf2SemanticResultV5(Rmf2MessageV5? Message, IReadOnlyList<TranslationDiagnostic> Diagnostics)
{
    internal bool Success => Message is not null;
}

internal static class Rmf2MessageJsonV5
{
    // This is the normalized-AST boundary, not locale artifact emission.
    internal static string Serialize(Rmf2MessageV5 message)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteNumber("astVersion", 5);
            writer.WriteString("profile", "rmf2-execution-v2");
            writer.WriteStartArray("inputs");
            foreach (var input in message.Inputs)
            { writer.WriteStartObject(); writer.WriteString("name", input.Name); writer.WriteString("type", input.Type); writer.WriteEndObject(); }
            writer.WriteEndArray();
            writer.WriteStartArray("declarations");
            foreach (var declaration in message.Declarations)
            {
                writer.WriteStartObject(); writer.WriteString("kind", declaration.Kind); writer.WriteString("name", declaration.Name);
                writer.WritePropertyName("expression"); Expression(writer, declaration.Expression); writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteStartArray("selectors");
            foreach (var selector in message.Selectors)
            {
                writer.WriteStartObject(); writer.WritePropertyName("value"); Value(writer, selector.Value);
                writer.WriteString("type", selector.Type); writer.WriteString("function", selector.Function); writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteStartArray("variants");
            foreach (var variant in message.Variants)
            {
                writer.WriteStartObject(); writer.WriteStartArray("keys");
                foreach (var key in variant.Keys)
                {
                    writer.WriteStartObject(); writer.WriteString("kind", key.Kind);
                    if (key.Value is not null) writer.WriteString("value", key.Value);
                    if (key.Canonical is not null) writer.WriteString("canonical", key.Canonical);
                    writer.WriteEndObject();
                }
                writer.WriteEndArray(); writer.WriteStartArray("nodes");
                foreach (var node in variant.Nodes)
                {
                    writer.WriteStartObject();
                    switch (node)
                    {
                        case Rmf2TextV5 text: writer.WriteString("kind", "text"); writer.WriteString("value", text.Value); break;
                        case Rmf2ExpressionNodeV5 expression:
                            writer.WriteString("kind", "expression"); writer.WritePropertyName("expression"); Expression(writer, expression.Expression); break;
                        case Rmf2MarkupV5 markup:
                            writer.WriteString("kind", "markup"); writer.WriteString("name", markup.Name); writer.WriteString("markupKind", markup.MarkupKind);
                            Properties(writer, markup.Options, markup.Annotations); break;
                        default: throw new InvalidOperationException("Unknown v5 message node.");
                    }
                    writer.WriteEndObject();
                }
                writer.WriteEndArray(); writer.WriteEndObject();
            }
            writer.WriteEndArray(); writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }
    private static void Expression(Utf8JsonWriter writer, Rmf2ExpressionV5 expression)
    {
        writer.WriteStartObject(); writer.WritePropertyName("operand"); Value(writer, expression.Operand);
        writer.WriteString("valueType", expression.ValueType);
        if (expression.Function is not null) writer.WriteString("function", expression.Function);
        Properties(writer, expression.Options, expression.Annotations); writer.WriteEndObject();
    }
    private static void Properties(Utf8JsonWriter writer, IReadOnlyList<Rmf2OptionV5> options, IReadOnlyList<Rmf2AnnotationV5> annotations)
    {
        writer.WriteStartArray("options");
        foreach (var option in options)
        { writer.WriteStartObject(); writer.WriteString("name", option.Name); writer.WritePropertyName("value"); Value(writer, option.Value); writer.WriteEndObject(); }
        writer.WriteEndArray(); writer.WriteStartArray("annotations");
        foreach (var annotation in annotations)
        {
            writer.WriteStartObject(); writer.WriteString("name", annotation.Name);
            if (annotation.Value is not null) { writer.WritePropertyName("value"); Value(writer, annotation.Value); }
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
    }
    private static void Value(Utf8JsonWriter writer, Rmf2ValueV5 value)
    {
        writer.WriteStartObject(); writer.WriteString("kind", value.Kind); writer.WriteString("value", value.Value);
        if (value.Canonical is not null) writer.WriteString("canonical", value.Canonical);
        writer.WriteEndObject();
    }
}
