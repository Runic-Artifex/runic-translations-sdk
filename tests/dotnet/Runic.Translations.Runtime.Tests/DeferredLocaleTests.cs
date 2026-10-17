using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TUnit.Core;

namespace Runic.Translations.Runtime.Tests;

// Generated catalogs embed each locale as data and build it on first use (issue #87): a process pays only for the
// locales it formats, and the JIT never compiles the catalog's messages as code.
internal sealed class DeferredLocaleTests
{
    private static readonly TranslationKey Greeting = new("deferred", 1, "greeting");
    private static readonly TranslationKey Farewell = new("deferred", 0, "farewell");

    private const string English = """
        [[0,{"contentLocale":"en","ast":{"astVersion":5,"profile":"rmf2-execution-v2","inputs":[],"declarations":[],"selectors":[],"variants":[{"keys":[],"nodes":[{"kind":"text","value":"Goodbye"}]}]}}],
         [1,{"contentLocale":"en","ast":{"astVersion":5,"profile":"rmf2-execution-v2","inputs":[{"name":"name","type":"string"}],"declarations":[],"selectors":[],"variants":[{"keys":[],"nodes":[{"kind":"text","value":"Hello "},{"kind":"expression","expression":{"operand":{"kind":"input","value":"name"},"valueType":"string","function":"string","options":[],"annotations":[]}}]}]}}]]
        """;

    private const string German = """
        [[1,{"contentLocale":"de","ast":{"astVersion":5,"profile":"rmf2-execution-v2","inputs":[{"name":"name","type":"string"}],"declarations":[],"selectors":[],"variants":[{"keys":[],"nodes":[{"kind":"text","value":"Grüß dich, "},{"kind":"expression","expression":{"operand":{"kind":"input","value":"name"},"valueType":"string","function":"string","options":[],"annotations":[]}}]}]}}]]
        """;

    private sealed class Counter
    {
        public int English;
        public int German;
    }

    private static CompiledTranslationCatalog Catalog(Counter counter, string german = German) => new(
        "deferred",
        "en",
        [
            CompiledTranslationDefinition.FromRmf2Inputs("farewell", []),
            CompiledTranslationDefinition.FromRmf2Inputs("greeting", [new CompiledRmf2Input("name", TextArgumentType.String)]),
        ],
        [
            CompiledTranslationLocale.CreateDeferred("de", "en", () =>
            {
                Interlocked.Increment(ref counter.German);
                return CompiledTranslationLocale.ReadRmf2Values(Encoding.UTF8.GetBytes(german));
            }),
            CompiledTranslationLocale.CreateDeferred("en", null, () =>
            {
                Interlocked.Increment(ref counter.English);
                return CompiledTranslationLocale.ReadRmf2Values(Encoding.UTF8.GetBytes(English));
            }),
        ]);

    [Test, DisplayName("a deferred locale is built only when a snapshot needs it")]
    public async Task BuiltOnFirstUse()
    {
        var counter = new Counter();
        var provider = new CompiledTranslationProvider(Catalog(counter));
        Assert.Equal(0, counter.English + counter.German, "Creating the catalog built a locale.");

        ITranslationSnapshot english = await provider.GetSnapshotAsync("en");
        Assert.Equal("Hello Ada", english.Format(Greeting, [new TextArgument("name", "Ada")]));
        Assert.Equal(1, counter.English, "The English snapshot did not build English once.");
        Assert.Equal(0, counter.German, "The English snapshot built German.");

        ITranslationSnapshot german = await provider.GetSnapshotAsync("de");
        Assert.Equal("Grüß dich, Ada", german.Format(Greeting, [new TextArgument("name", "Ada")]));
        Assert.Equal("Goodbye", german.Format(Farewell, []));
        Assert.Equal(1, counter.German, "The German snapshot did not build German once.");
        Assert.Equal(1, counter.English, "German's fallback rebuilt English.");
    }

    [Test, DisplayName("a fallback locale is built with the locale that falls back to it")]
    public void FallbackBuiltWithLocale()
    {
        var counter = new Counter();
        CompiledTranslationCatalog catalog = Catalog(counter);
        var german = new CompiledTranslationSnapshot(catalog, "de");
        Assert.Equal("Goodbye", german.Format(Farewell, []));
        Assert.Equal(1, counter.English, "German did not build its fallback.");
        Assert.Equal(1, counter.German, "German was not built once.");
    }

    [Test, DisplayName("concurrent first snapshots build a deferred locale once")]
    public async Task ConcurrentFirstUse()
    {
        var counter = new Counter();
        CompiledTranslationCatalog catalog = Catalog(counter);
        await Task.WhenAll(Enumerable.Range(0, 16).Select(_ => Task.Run(() => new CompiledTranslationSnapshot(catalog, "de"))));
        Assert.Equal(1, counter.German, "German was built more than once.");
        Assert.Equal(1, counter.English, "English was built more than once.");
    }

    [Test, DisplayName("a catalog view with other options shares the built locales")]
    public void OptionsShareLocales()
    {
        var counter = new Counter();
        CompiledTranslationCatalog catalog = Catalog(counter);
        _ = new CompiledTranslationSnapshot(catalog, "en");
        CompiledTranslationCatalog view = catalog.WithOptions(new TranslationOptions { MissingKey = MissingTranslationPolicy.ReturnKey });
        _ = new CompiledTranslationSnapshot(view, "en");
        Assert.Equal(1, counter.English, "The options view rebuilt English.");
    }

    [Test, DisplayName("invalid deferred data fails the first snapshot of its locale and leaves the others usable")]
    public void InvalidDataFailsOnUse()
    {
        var counter = new Counter();
        // German declares an input the definition does not have.
        string invalid = German.Replace("\"name\":\"name\",\"type\":\"string\"", "\"name\":\"other\",\"type\":\"string\"", StringComparison.Ordinal)
            .Replace("\"value\":\"name\"", "\"value\":\"other\"", StringComparison.Ordinal);
        CompiledTranslationCatalog catalog = Catalog(counter, invalid);
        Assert.Equal("Goodbye", new CompiledTranslationSnapshot(catalog, "en").Format(Farewell, []));
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => _ = new CompiledTranslationSnapshot(catalog, "de"));
        Assert.True(error.Message.Contains("locale 'de'", StringComparison.Ordinal), error.Message);
        Assert.True(error.InnerException is ArgumentException, "The contract error is not the inner exception.");
    }

    [Test, DisplayName("a deferred default locale without every canonical key fails every snapshot")]
    public void IncompleteDefaultFailsEveryTime()
    {
        var catalog = new CompiledTranslationCatalog("deferred", "en",
            [
                CompiledTranslationDefinition.FromRmf2Inputs("farewell", []),
                CompiledTranslationDefinition.FromRmf2Inputs("greeting", [new CompiledRmf2Input("name", TextArgumentType.String)]),
            ],
            [CompiledTranslationLocale.CreateDeferred("en", null, () =>
                CompiledTranslationLocale.ReadRmf2Values(Encoding.UTF8.GetBytes(English)).Take(1).ToArray())]);
        for (int attempt = 0; attempt < 2; attempt++)
            Assert.Throws<InvalidOperationException>(() => _ = new CompiledTranslationSnapshot(catalog, "en"), "canonical key 'greeting'");
    }

    [Test, DisplayName("reading values of a deferred locale builds it once")]
    public void ValuesBuildOnce()
    {
        var counter = new Counter();
        CompiledTranslationLocale english = Catalog(counter).Locales.Span[1];
        Assert.Equal(2, english.Values.Length);
        Assert.Equal(2, english.Values.Length);
        Assert.Equal(1, counter.English, "Reading the values twice built English twice.");
    }

    [Test, DisplayName("generated locale data rejects malformed entries")]
    public void ReadRejectsMalformed()
    {
        foreach (string data in new[] { "{}", "[[0]]", "[[\"0\",{}]]", "[[0,{\"contentLocale\":\"en\"}]]", "[[0,{\"contentLocale\":\"en\",\"ast\":{\"astVersion\":4}}]]" })
            Assert.Throws<TranslationPackException>(() => CompiledTranslationLocale.ReadRmf2Values(Encoding.UTF8.GetBytes(data)));
        // Each change breaks the shape of otherwise valid data: the reader checks members, kinds, types and limits.
        (string Find, string Replace)[] changes =
        [
            ("\"value\":\"Goodbye\"", "\"value\":\"Goodbye\",\"value\":\"Again\""),
            ("\"value\":\"Goodbye\"", "\"value\":\"Goodbye\",\"extra\":1"),
            ("\"value\":\"Goodbye\"", "\"value\":\"Goodbye\",\"options\":[]"),
            ("{\"kind\":\"text\",\"value\":\"Goodbye\"}", "{\"kind\":\"comment\",\"value\":\"Goodbye\"}"),
            ("\"value\":\"Goodbye\"", "\"value\":7"),
            ("\"kind\":\"input\",\"value\":\"name\"", "\"kind\":\"input\",\"value\":\"name\",\"canonical\":\"1\""),
            ("\"kind\":\"input\",\"value\":\"name\"", "\"kind\":\"variable\",\"value\":\"name\""),
            ("\"valueType\":\"string\"", "\"valueType\":\"text\""),
            ("\"keys\":[]", "\"keys\":[{\"kind\":\"wildcard\",\"value\":\"*\"}]"),
            ("\"profile\":\"rmf2-execution-v2\"", "\"profile\":\"rmf2-execution-v1\""),
            ("\"declarations\":[],", ""),
            ("\"variants\":[{\"keys\":[],\"nodes\":[{\"kind\":\"text\",\"value\":\"Goodbye\"}]}]", "\"variants\":[]"),
        ];
        foreach (var (find, replace) in changes)
        {
            string data = English.Replace(find, replace, StringComparison.Ordinal);
            Assert.True(data != English, "The change did not apply: " + replace);
            Assert.Throws<TranslationPackException>(() => CompiledTranslationLocale.ReadRmf2Values(Encoding.UTF8.GetBytes(data)));
        }
        Assert.Throws<TranslationPackException>(() => CompiledTranslationLocale.ReadRmf2Values(Encoding.UTF8.GetBytes(English + " []")));
        string deep = "[[0,{\"contentLocale\":\"en\",\"ast\":{\"astVersion\":5,\"profile\":\"rmf2-execution-v2\",\"inputs\":[],\"declarations\":[],\"selectors\":[],\"variants\":[{\"keys\":[],\"nodes\":["
            + string.Concat(Enumerable.Repeat("{\"kind\":\"markup\",\"name\":\"b\",\"markupKind\":\"open\",\"options\":[],\"annotations\":[]},", 17))
            + "{\"kind\":\"text\",\"value\":\"x\"}]}]}}]]";
        Assert.Equal(TranslationPackFailureReason.LimitExceeded,
            TranslationPackFailure.GetReason(Assert.Throws<TranslationPackException>(() => CompiledTranslationLocale.ReadRmf2Values(Encoding.UTF8.GetBytes(deep)))));
    }

    [Test, DisplayName("generated locale data is read in any member order and shares equal strings")]
    public void ReadAnyMemberOrder()
    {
        // The compiler writes "kind" first; the reader must not depend on it.
        string reordered = English
            .Replace("{\"kind\":\"text\",\"value\":\"Goodbye\"}", "{\"value\":\"Goodbye\",\"kind\":\"text\"}", StringComparison.Ordinal)
            .Replace("{\"kind\":\"input\",\"value\":\"name\"}", "{\"value\":\"name\",\"kind\":\"input\"}", StringComparison.Ordinal)
            .Replace("{\"contentLocale\":\"en\",\"ast\":{\"astVersion\":5,\"profile\":\"rmf2-execution-v2\",", "{\"ast\":{\"profile\":\"rmf2-execution-v2\",\"astVersion\":5,", StringComparison.Ordinal)
            .Replace("]}]}}]", "]}]},\"contentLocale\":\"en\"}]", StringComparison.Ordinal);
        Assert.True(reordered.Contains("{\"value\":\"name\",\"kind\":\"input\"}", StringComparison.Ordinal) && reordered.Contains("\"contentLocale\":\"en\"}]", StringComparison.Ordinal), "The members were not reordered.");
        IReadOnlyList<CompiledTranslationValue> values = CompiledTranslationLocale.ReadRmf2Values(Encoding.UTF8.GetBytes(reordered));
        Assert.Equal("Goodbye", values[0].Message!.Rmf2V5!.Format([], "en"));
        Assert.Equal("Hello Ada", values[1].Message!.Rmf2V5!.Format([TextArgument.CreateRmf2("name", new TextArgument("_", "Ada"))], "en"));
        CompiledRmf2Message greeting = values[1].Message!.Rmf2V5!;
        Assert.True(ReferenceEquals(greeting.InputArray[0].Name, greeting.VariantArray[0].NodeArray[1].Expression!.Operand.Value), "Equal names were not shared.");
        Assert.True(ReferenceEquals(values[0].Message!.ContentLocale, values[1].Message!.ContentLocale), "Equal content locales were not shared.");
    }

    // Issue #97: a generated catalog's first use of a locale read the data into a JSON document and validated every
    // message again, allocating about 25 times the data for 1,300 messages. Reading must cost on the order of the data.
    [Test, NotInParallel, DisplayName("reading generated locale data allocates on the order of the data")]
    public void ReadAllocation()
    {
        byte[] data = Encoding.UTF8.GetBytes(LargeLocale(1300));
        IReadOnlyList<CompiledTranslationValue> warm = CompiledTranslationLocale.ReadRmf2Values(data);
        Assert.Equal(1300, warm.Count);
        Assert.Equal("5 items", warm[1].Message!.Rmf2V5!.Format([TextArgument.CreateRmf2("count", new TextArgument("_", 5L))], "en"));
        Assert.True(warm[2].Message!.Rmf2V5!.FormatContent([], "en") is not null, "Markup content did not format.");
        Assert.True(warm[2].Message!.Rmf2V5!.HasMarkup && !warm[1].Message!.Rmf2V5!.HasMarkup, "Markup was not detected.");
        long before = GC.GetAllocatedBytesForCurrentThread();
        IReadOnlyList<CompiledTranslationValue> values = CompiledTranslationLocale.ReadRmf2Values(data);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        GC.KeepAlive(values);
        Assert.True(allocated < 2L * data.Length, $"Reading {data.Length} bytes of locale data allocated {allocated} bytes.");
    }

    private static string LargeLocale(int count)
    {
        var json = new StringBuilder("[");
        for (int id = 0; id < count; id++)
        {
            if (id != 0) json.Append(',');
            json.Append('[').Append(id).Append(",{\"contentLocale\":\"en\",\"ast\":{\"astVersion\":5,\"profile\":\"rmf2-execution-v2\",");
            json.Append((id % 3) switch
            {
                0 => "\"inputs\":[],\"declarations\":[],\"selectors\":[],\"variants\":[{\"keys\":[],\"nodes\":[{\"kind\":\"text\",\"value\":\"Message " + id + " says something short\"}]}]",
                1 => "\"inputs\":[{\"name\":\"count\",\"type\":\"int64\"}],\"declarations\":[{\"kind\":\"input\",\"name\":\"count\",\"expression\":{\"operand\":{\"kind\":\"input\",\"value\":\"count\"},\"valueType\":\"int64\",\"function\":\"integer\",\"options\":[],\"annotations\":[]}}],"
                    + "\"selectors\":[{\"value\":{\"kind\":\"input\",\"value\":\"count\"},\"type\":\"int64\",\"function\":\"plural\"}],"
                    + "\"variants\":[{\"keys\":[{\"kind\":\"literal\",\"value\":\"one\"}],\"nodes\":[{\"kind\":\"text\",\"value\":\"one item\"}]},"
                    + "{\"keys\":[{\"kind\":\"wildcard\"}],\"nodes\":[{\"kind\":\"expression\",\"expression\":{\"operand\":{\"kind\":\"input\",\"value\":\"count\"},\"valueType\":\"int64\",\"options\":[],\"annotations\":[]}},{\"kind\":\"text\",\"value\":\" items\"}]}]",
                _ => "\"inputs\":[],\"declarations\":[],\"selectors\":[],\"variants\":[{\"keys\":[],\"nodes\":[{\"kind\":\"text\",\"value\":\"Open \"},"
                    + "{\"kind\":\"markup\",\"name\":\"b\",\"markupKind\":\"open\",\"options\":[],\"annotations\":[]},{\"kind\":\"expression\",\"expression\":{\"operand\":{\"kind\":\"number-literal\",\"value\":\"1\",\"canonical\":\"1\"},\"valueType\":\"decimal\",\"options\":[],\"annotations\":[]}},"
                    + "{\"kind\":\"markup\",\"name\":\"b\",\"markupKind\":\"close\",\"options\":[],\"annotations\":[]}]}]",
            });
            json.Append("}}]");
        }
        return json.Append(']').ToString();
    }

    [Test, DisplayName("a deferred locale validates its value order when it is built")]
    public void FactoryOrderValidated()
    {
        var reversed = CompiledTranslationLocale.CreateDeferred("en", null, () =>
            CompiledTranslationLocale.ReadRmf2Values(Encoding.UTF8.GetBytes(English)).Reverse().ToArray());
        Assert.Throws<ArgumentException>(() => _ = reversed.Values);
        Assert.Throws<ArgumentNullException>(() => CompiledTranslationLocale.CreateDeferred("en", null, null!));
    }
}
