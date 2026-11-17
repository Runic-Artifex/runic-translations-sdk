using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Runic.Translations.Generated;
using TUnit.Core;

namespace Runic.Translations.Runtime.Tests;

// RMF2 runtime ABI 4 (D072.2, T3): a generated catalog reads its definitions, keys and external-pack contracts from one
// message table instead of code per message, and its accessors create their arguments with the typed CreateArgument
// overloads. Each must equal what the per-message factories of ABI 3 create.
internal sealed class GeneratedMessageTableTests
{
    private const string Catalog = "table";
    private const string Fingerprint = "sha256:0000000000000000000000000000000000000000000000000000000000000000";
    private const string Markup = "{\"version\":2,\"contracts\":{},\"messages\":{}}";

    // As the compiler orders them: the canonical messages alpha (every carrier), mid.name (a Unicode input) and zeta (no
    // inputs), then the permitted locale extras extra.a and extra.b, each in ordinal key order.
    private static ReadOnlySpan<byte> Table =>
        "alpha b:boolean d:date dt:datetime g:guid i:int64 l:list n:decimal s:string t:time\n"u8 +
        "mid.name grüße:string\n"u8 +
        "zeta\n"u8 +
        "extra.a\n"u8 +
        "extra.b x:int64\n"u8;

    private static readonly (string Name, (string Name, TextArgumentType Type)[] Inputs)[] Expected =
    [
        ("alpha", [("b", TextArgumentType.Bool), ("d", TextArgumentType.Date), ("dt", TextArgumentType.DateTime), ("g", TextArgumentType.Guid),
            ("i", TextArgumentType.Int), ("l", TextArgumentType.List), ("n", TextArgumentType.Number), ("s", TextArgumentType.String), ("t", TextArgumentType.Time)]),
        ("mid.name", [("grüße", TextArgumentType.String)]),
        ("zeta", []),
        ("extra.a", []),
        ("extra.b", [("x", TextArgumentType.Int)]),
    ];

    private static CompiledRmf2Input[] Inputs(int id) => Expected[id].Inputs.Select(input => new CompiledRmf2Input(input.Name, input.Type)).ToArray();

    [Test, DisplayName("the message table reads the definitions the per-message factory creates")]
    public void Definitions()
    {
        IReadOnlyList<CompiledTranslationDefinition> definitions = GeneratedCatalogSupport.ReadDefinitions(Table, 3);
        Assert.Equal(Expected.Length, definitions.Count);
        for (int id = 0; id < Expected.Length; id++)
        {
            CompiledTranslationDefinition expected = GeneratedCatalogSupport.CreateDefinition(Expected[id].Name, Inputs(id), isCanonical: id < 3);
            Assert.Equal(expected.Name, definitions[id].Name, "name " + id);
            Assert.Equal(expected.IsCanonical, definitions[id].IsCanonical, "canonical " + id);
            Assert.True(expected.Placeholders.Span.SequenceEqual(definitions[id].Placeholders.Span), "placeholders " + id);
        }
    }

    [Test, DisplayName("the message table creates the canonical keys by ID")]
    public void Keys()
    {
        TranslationKey[] keys = GeneratedCatalogSupport.CreateKeys(Catalog, Table, 3);
        Assert.Equal(3, keys.Length);
        for (int id = 0; id < keys.Length; id++) Assert.Equal(new TranslationKey(Catalog, id, Expected[id].Name), keys[id]);
    }

    [Test, DisplayName("the message table creates the external-pack contract the per-message factory creates")]
    public void PackContract()
    {
        foreach (int[] extras in new[] { Array.Empty<int>(), [4], [3, 4] })
        {
            TranslationPackContract actual = GeneratedCatalogSupport.CreatePackContract(Catalog, "de", Fingerprint, Table, 3, extras, Markup);
            TranslationPackMessageContract[] messages = Enumerable.Range(0, 3).Concat(extras)
                .Select(id => GeneratedCatalogSupport.CreatePackMessageContract(new TranslationKey(Catalog, id, Expected[id].Name), Inputs(id)))
                .OrderBy(message => message.Key.Name, StringComparer.Ordinal).ToArray();
            TranslationPackContract expected = GeneratedCatalogSupport.CreatePackContract(Catalog, "de", Fingerprint, messages, Markup);
            string label = "extras " + string.Join(",", extras);
            Assert.Equal(Describe(expected), Describe(actual), label);
            Assert.Equal(expected.Rmf2MarkupContract, actual.Rmf2MarkupContract, label);
        }

        static string Describe(TranslationPackContract contract) => string.Join(" | ", contract.Messages.Select(message =>
            message.Key + ": " + string.Join(", ", message.Arguments.Select(argument => argument.ToString()))));
    }

    [Test, DisplayName("a catalog over the message table formats as one over the per-message definitions")]
    public void CatalogFormats()
    {
        byte[] json = Encoding.UTF8.GetBytes("""
            [[0,{"contentLocale":"en","ast":{"astVersion":5,"profile":"rmf2-execution-v2","inputs":[{"name":"grüße","type":"string"}],"declarations":[],"selectors":[],"variants":[{"keys":[],"nodes":[{"kind":"text","value":"Hi "},{"kind":"expression","expression":{"operand":{"kind":"input","value":"grüße"},"valueType":"string","function":"string","options":[],"annotations":[]}}]}]}}],
             [1,{"contentLocale":"en","ast":{"astVersion":5,"profile":"rmf2-execution-v2","inputs":[],"declarations":[],"selectors":[],"variants":[{"keys":[],"nodes":[{"kind":"text","value":"Zeta"}]}]}}]]
            """);
        ReadOnlySpan<byte> table = "mid.name grüße:string\nzeta\n"u8;
        var catalog = new CompiledTranslationCatalog(Catalog, "en", GeneratedCatalogSupport.ReadDefinitions(table, 2),
            [GeneratedCatalogSupport.CreateDeferredLocale("en", null, () => GeneratedCatalogSupport.ReadValues(json))]);
        ITranslationSnapshot snapshot = new CompiledTranslationProvider(catalog).GetSnapshotAsync("en").AsTask().GetAwaiter().GetResult();
        TranslationKey[] keys = GeneratedCatalogSupport.CreateKeys(Catalog, table, 2);
        Assert.Equal("Zeta", snapshot.Format(keys[1], []));
        Assert.Equal("Hi Ada", snapshot.Format(keys[0], [GeneratedCatalogSupport.CreateArgument("grüße", "Ada")]));
    }

    [Test, DisplayName("a malformed message table or extra list is refused")]
    public void Malformed()
    {
        foreach ((string label, byte[] table, int canonical) in new (string, byte[], int)[]
        {
            ("empty", [], 1),
            ("no final line break", "zeta"u8.ToArray(), 1),
            ("no canonical message", "zeta\n"u8.ToArray(), 0),
            ("more canonical messages than lines", "zeta\n"u8.ToArray(), 2),
            ("empty key", "\n"u8.ToArray(), 1),
            ("empty input list", "zeta \n"u8.ToArray(), 1),
            ("input without type", "zeta name\n"u8.ToArray(), 1),
            ("input without name", "zeta :string\n"u8.ToArray(), 1),
            ("unknown type", "zeta name:integer\n"u8.ToArray(), 1),
            ("invalid UTF-8", [(byte)'z', 0xC3, (byte)'\n'], 1),
        })
        {
            Assert.Throws<TranslationPackException>(() => GeneratedCatalogSupport.ReadDefinitions(table, canonical), null);
            if (label is not ("empty input list" or "input without type" or "input without name" or "unknown type"))
                Assert.Throws<TranslationPackException>(() => GeneratedCatalogSupport.CreateKeys(Catalog, table, canonical), null);
        }
        // A catalog without messages has an empty table; the catalog refuses its empty definitions, as before.
        Assert.Equal(0, GeneratedCatalogSupport.ReadDefinitions(default, 0).Count);
        Assert.Equal(0, GeneratedCatalogSupport.CreateKeys(Catalog, default, 0).Length);
        // The definitions validate what the table holds: input names in ordinal order, keys as resource names.
        Assert.Throws<ArgumentException>(() => GeneratedCatalogSupport.ReadDefinitions("zeta b:string a:string\n"u8, 1));
        Assert.Throws<ArgumentException>(() => GeneratedCatalogSupport.ReadDefinitions("9lives\n"u8, 1));
        foreach (int[] extras in new[] { new[] { 1 }, [5], [4, 3], [3, 3] })
            Assert.Throws<TranslationPackException>(() => GeneratedCatalogSupport.CreatePackContract(Catalog, "de", Fingerprint, Table, 3, extras, Markup), null);
    }

    [Test, DisplayName("the typed argument factories equal the carrier with its default format")]
    public void TypedArguments()
    {
        var date = new DateOnly(2026, 11, 17);
        var time = new TimeOnly(18, 59, 29);
        var dateTime = new DateTimeOffset(2026, 11, 17, 18, 59, 29, TimeSpan.FromHours(1));
        Guid guid = Guid.Parse("8d3b7a0e-6a51-4c1c-9a65-2b3d9b1f0c7e");
        string[] list = ["a", "b"];
        foreach ((TextArgument actual, TextArgument expected, object value) in new (TextArgument, TextArgument, object)[]
        {
            (GeneratedCatalogSupport.CreateArgument("grüße", "text"), GeneratedCatalogSupport.CreateArgument("grüße", new TextArgument("_", "text")), "text"),
            (GeneratedCatalogSupport.CreateArgument("n", 42L), GeneratedCatalogSupport.CreateArgument("n", new TextArgument("_", 42L)), 42L),
            (GeneratedCatalogSupport.CreateArgument("n", 4.2m), GeneratedCatalogSupport.CreateArgument("n", new TextArgument("_", 4.2m)), 4.2m),
            (GeneratedCatalogSupport.CreateArgument("n", true), GeneratedCatalogSupport.CreateArgument("n", new TextArgument("_", true)), true),
            (GeneratedCatalogSupport.CreateArgument("n", date), GeneratedCatalogSupport.CreateArgument("n", new TextArgument("_", date)), date),
            (GeneratedCatalogSupport.CreateArgument("n", time), GeneratedCatalogSupport.CreateArgument("n", new TextArgument("_", time)), time),
            (GeneratedCatalogSupport.CreateArgument("n", dateTime), GeneratedCatalogSupport.CreateArgument("n", new TextArgument("_", dateTime)), dateTime),
            (GeneratedCatalogSupport.CreateArgument("n", guid), GeneratedCatalogSupport.CreateArgument("n", new TextArgument("_", guid)), guid),
        })
        {
            Assert.Equal(expected.Name, actual.Name);
            Assert.Equal(expected.Type, actual.Type, value.ToString());
            Assert.Equal(expected.Format, actual.Format, value.ToString());
            Assert.True(actual.TryGetValue(out object? stored) && Equals(stored, value), value.ToString()!);
        }
        TextArgument listArgument = GeneratedCatalogSupport.CreateArgument("items", (IReadOnlyList<string>)list);
        Assert.Equal(TextArgumentType.List, listArgument.Type);
        Assert.True(listArgument.TryGetValue(out IReadOnlyList<string>? items) && items.SequenceEqual(list), "list items");
        Assert.Throws<ArgumentException>(() => GeneratedCatalogSupport.CreateArgument("not a name", "x"));
        Assert.Throws<ArgumentNullException>(() => GeneratedCatalogSupport.CreateArgument("n", (string)null!));
    }
}
