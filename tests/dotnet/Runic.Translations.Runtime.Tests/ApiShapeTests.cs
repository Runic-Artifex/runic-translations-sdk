using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Runic.Translations;
using TUnit.Core;

namespace Runic.Translations.Runtime.Tests;

internal sealed class ApiShapeTests
{
    [Test, DisplayName("a pack exception carries its reason and rejection ID")]
    public void PackExceptionCarriesItsReason()
    {
        var exception = new TranslationPackException("bad catalog", TranslationPackFailureReason.CatalogMismatch);
        Assert.Equal(TranslationPackFailureReason.CatalogMismatch, exception.Reason);
        Assert.Equal("RTR0023/catalog-mismatch", exception.RejectionId);
        Assert.Equal("RTR0023", TranslationPackException.DiagnosticId);
        var plain = new TranslationPackException("no reason");
        Assert.Equal(TranslationPackFailureReason.Unknown, plain.Reason);
        Assert.Equal("RTR0023/unknown", plain.RejectionId);
        // The reason survives a rethrow of the same instance and is read without a side table.
        Assert.Equal(TranslationPackFailureReason.CatalogMismatch, TranslationPackFailure.GetReason(exception));
        Assert.Equal(TranslationPackFailureReason.Cancelled, TranslationPackFailure.GetReason(new OperationCanceledException()));
        Assert.Equal(TranslationPackFailureReason.Unknown, TranslationPackFailure.GetReason(new InvalidOperationException()));
        Assert.Throws<ArgumentOutOfRangeException>(() => _ = new TranslationPackException("x", (TranslationPackFailureReason)999));
        foreach (TranslationPackFailureReason reason in Enum.GetValues<TranslationPackFailureReason>().Where(value => value != TranslationPackFailureReason.Unknown))
            Assert.True(new TranslationPackException("x", reason).RejectionId != "RTR0023/unknown", reason + " has no rejection name.");
    }

    [Test, DisplayName("reference diagnostic reasons keep the ESM spelling and default to Unknown")]
    public void ReferenceDiagnosticReasonsKeepTheirSpelling()
    {
        Assert.Equal(TranslationReferenceDiagnosticReason.Unknown, default(TranslationReferenceDiagnostic).Reason);
        string[] wire = Enum.GetValues<TranslationReferenceDiagnosticReason>().Select(TranslationReferenceDiagnosticReasons.ToWire).ToArray();
        Assert.Equal("unknown|contract-changed|catalog-mismatch|fingerprint-mismatch|unknown-key|incompatible-arguments|structured-content|format-failed",
            string.Join('|', wire));
    }

    [Test, DisplayName("the markup binding set is closed and the transport version is a property")]
    public void ClosedBindingsAndTransportVersion()
    {
        foreach (Type type in new[] { typeof(MarkupBinding), typeof(InlineMarkupBinding) })
        {
            ConstructorInfo[] derivable = type.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(constructor => (constructor.IsPublic || constructor.IsFamily || constructor.IsFamilyOrAssembly) && constructor.GetParameters().Length == 0)
                .ToArray();
            Assert.True(derivable.Length == 0, type.Name + " can be derived from outside the runtime.");
        }
        PropertyInfo? version = typeof(TranslationTransport).GetProperty(nameof(TranslationTransport.Version), BindingFlags.Public | BindingFlags.Static);
        Assert.True(version is not null && typeof(TranslationTransport).GetField("Version") is null, "TranslationTransport.Version must be a property.");
        Assert.Equal(2, TranslationTransport.Version);
        Assert.True(typeof(ITranslationManager).GetEvent(nameof(ITranslationManager.SnapshotPublished)) is not null, "ITranslationManager.SnapshotPublished is missing.");
        Assert.True(typeof(TranslationManager).Assembly.GetType("Runic.Translations.ITranslationSnapshotNotifier") is null, "ITranslationSnapshotNotifier still exists.");
    }

    // runic-sdk's generated Bridge code (Runic.Application.Views.Codegen, BridgeTypeGraph) and its test host
    // (Runic.Application.Testing, RunicWireValue) use these members by name, without a reference to this package from
    // the Views packages: keep them, with these shapes, in every release (L3-19).
    [Test, DisplayName("the members runic-sdk's generated Bridge code and test host call keep their shape")]
    public void RunicSdkBridgeContract()
    {
        var reference = new TranslationReference("app", "sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef", "saved",
            new Dictionary<string, TranslationReferenceArgument> { ["title"] = new(TextArgumentType.String, "Notes") }, "Saved Notes.");

        // The generated writer and reader, as BridgeTypeGraph emits them.
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
            global::System.Text.Json.JsonSerializer.Serialize(writer, reference, global::Runic.Translations.TranslationReferenceJsonContext.Default.TranslationReference);
        using JsonDocument document = JsonDocument.Parse(buffer.ToArray());
        JsonElement element = document.RootElement;
        global::Runic.Translations.TranslationReference read = global::Runic.Translations.TranslationReference.Parse(element.GetRawText());
        Assert.Equal("saved", read.Key);
        Assert.Equal("Saved Notes.", read.FallbackText);
        Assert.Throws<JsonException>(() => TranslationReference.Parse("{}"));

        // Recognized by name and assembly (BridgeTextReferences.IsTranslationReference, RunicWireValue.IsTextReference).
        Type type = typeof(TranslationReference);
        Assert.Equal("Runic.Translations.TranslationReference", type.FullName);
        Assert.Equal("Runic.Translations", type.Assembly.GetName().Name);

        // Reflection in RunicWireValue: TranslationReferenceJsonContext.Default.TranslationReference and Parse(string).
        object? context = type.Assembly.GetType("Runic.Translations.TranslationReferenceJsonContext")
            ?.GetProperty("Default", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
        Assert.True(context?.GetType().GetProperty("TranslationReference")?.GetValue(context) is JsonTypeInfo<TranslationReference>,
            "TranslationReferenceJsonContext.Default.TranslationReference must be a public JsonTypeInfo<TranslationReference>.");
        MethodInfo? parse = type.GetMethod("Parse", BindingFlags.Public | BindingFlags.Static, [typeof(string)]);
        Assert.True(parse is not null && parse.ReturnType == type, "TranslationReference.Parse(string) must be public and static.");
    }
}
