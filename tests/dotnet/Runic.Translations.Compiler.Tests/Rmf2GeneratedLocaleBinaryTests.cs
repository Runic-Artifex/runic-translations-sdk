using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Runic.Translations.Compiler.Generation;
using Runic.Translations.Generated;
using TUnit.Core;

namespace Runic.Translations.Compiler.Tests;

// RMF2 runtime ABI 5 (D077.16): generated C# embeds each locale in the binary encoding instead of JSON. The binary reader
// must build exactly the model the JSON reader builds from the same locale, for every catalog and corpus in the repository
// (the conformance corpora compile through Rmf2EsmV5Tests.CompileProject, which checks each project here), and must fail
// closed, in bounded time and memory, on any other input.
internal sealed class Rmf2GeneratedLocaleBinaryTests
{
    // Every catalog of the repository that compiles: examples, specs, corpora, fixtures, templates and the editor.
    private static IEnumerable<string> RepositoryCatalogs()
    {
        string root = RepositoryPaths.RepositoryRoot;
        string[] skipped = ["node_modules", "bin", "obj", "artifacts", ".cache", ".git", ".direnv"];
        return Directory.EnumerateFiles(root, "runic.json", SearchOption.AllDirectories)
            .Where(path => !Path.GetRelativePath(root, path).Split(Path.DirectorySeparatorChar).Any(skipped.Contains))
            .Order(StringComparer.Ordinal);
    }

    private static Rmf2ProjectV5? CompileDirectory(string runicJson)
    {
        string directory = Path.GetDirectoryName(runicJson)!;
        TranslationSource[] sources = Directory.EnumerateFiles(directory, "*.*", SearchOption.AllDirectories)
            .Where(path => path.EndsWith(".rmf2", StringComparison.Ordinal) || path.EndsWith(".mf2", StringComparison.Ordinal))
            .Where(path => !Path.GetRelativePath(directory, path).StartsWith("overrides", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .Select(path => new TranslationSource("translations/" + Path.GetRelativePath(directory, path).Replace('\\', '/'), File.ReadAllBytes(path)))
            .ToArray();
        Rmf2ProjectCompilation compilation = TranslationCompiler.CompileRmf2ProjectV5(new TranslationSource("translations/runic.json", File.ReadAllBytes(runicJson)), sources);
        return compilation.Success ? compilation.Project : null;
    }

    [Test, DisplayName("binary locale data decodes to the JSON model and formats identically for every catalog of the repository")]
    public void RepositoryCatalogsMatchJson()
    {
        var checkedCatalogs = new List<string>();
        int messages = 0;
        foreach (string catalog in RepositoryCatalogs())
        {
            if (CompileDirectory(catalog) is not { } project) continue;
            messages += AssertBinaryMatchesJson(project);
            checkedCatalogs.Add(Path.GetRelativePath(RepositoryPaths.RepositoryRoot, catalog));
        }
        Assert.True(checkedCatalogs.Count >= 15, "Only " + checkedCatalogs.Count + " catalogs compiled: " + string.Join(", ", checkedCatalogs));
        Assert.True(checkedCatalogs.Contains("apps/translations-editor/EditorResources/runic.json".Replace('/', Path.DirectorySeparatorChar)), "The editor's catalog was not checked.");
        Assert.True(messages >= 2000, "Only " + messages + " locale messages were compared.");
    }

    /// <summary>
    /// Encodes every locale of <paramref name="project"/> as the JSON of ABI 4 and the binary of ABI 5, reads both, and
    /// compares the models field by field and the formatted output of every message, with the binary read built at once
    /// and on first format. Returns the number of locale messages compared.
    /// </summary>
    internal static int AssertBinaryMatchesJson(Rmf2ProjectV5 project)
    {
        Rmf2V5DefinitionTable table = Rmf2V5DefinitionTable.Create(project);
        Rmf2FormatData? formatData = Rmf2CSharpOutputRendererV5.CatalogFormatData(project) is { } formats ? Rmf2FormatData.Parse(Encoding.UTF8.GetBytes(formats)) : null;
        int compared = 0;
        foreach (Rmf2LocaleV5 locale in project.Locales)
        {
            byte[] json = Rmf2CSharpOutputRendererV5.LocaleJson(locale, table);
            byte[] binary = Rmf2CSharpOutputRendererV5.LocaleBinary(locale, table);
            IReadOnlyList<CompiledTranslationValue> fromJson = CompiledTranslationLocale.ReadRmf2Values(json, formatData);
            CompiledTranslationValue[] eager = GeneratedLocaleBinary.Read(binary, formatData, decodeOnFirstFormat: false);
            CompiledTranslationValue[] lazy = GeneratedLocaleBinary.Read(binary, formatData);
            string where = project.Id + "/" + locale.Tag;
            Assert.Equal(fromJson.Count, eager.Length, where + ": value count");
            Assert.Equal(fromJson.Count, lazy.Length, where + ": value count (lazy)");
            for (int index = 0; index < fromJson.Count; index++)
            {
                string name = where + "/" + table.Definitions[fromJson[index].Id].Contract.Key;
                Assert.Equal(fromJson[index].Id, eager[index].Id, name + ": ID");
                Assert.Equal(fromJson[index].Id, lazy[index].Id, name + ": ID (lazy)");
                Assert.Equal(fromJson[index].Pattern, lazy[index].Pattern, name + ": pattern");
                CompiledTextMessage jsonMessage = fromJson[index].Message!, lazyMessage = lazy[index].Message!;
                // What the catalog reads before a message is formatted, without building it.
                Assert.True(lazyMessage.IsRmf2V5, name + ": the deferred message is not RMF2");
                Assert.Equal(jsonMessage.ContentLocale, lazyMessage.ContentLocale, name + ": content locale before the first format");
                Assert.True(ReferenceEquals(jsonMessage.Rmf2V5!.FormatData, lazyMessage.Rmf2FormatData), name + ": format data before the first format");
                TranslationPlaceholderDescriptor[] placeholders = CompiledTranslationDefinition.FromRmf2Inputs("x", jsonMessage.Rmf2V5.InputArray).PlaceholderArray;
                Assert.True(lazyMessage.TryMatchDeferredInputs(placeholders, out bool matches) && matches, name + ": deferred input check");
                if (placeholders.Length > 0)
                    Assert.True(lazyMessage.TryMatchDeferredInputs(placeholders[1..], out bool fewer) && !fewer, name + ": deferred input check accepts fewer inputs");
                Same(jsonMessage.Rmf2V5, eager[index].Message!.Rmf2V5!, name);
                Same(jsonMessage.Rmf2V5, lazyMessage.Rmf2V5!, name + " (lazy)");
                Assert.True(ReferenceEquals(lazyMessage.Rmf2V5, lazyMessage.Rmf2V5), name + ": the deferred model is built more than once");
                string expected = Format(jsonMessage.Rmf2V5, locale.Tag);
                Assert.Equal(expected, Format(eager[index].Message!.Rmf2V5!, locale.Tag), name + ": formatted");
                Assert.Equal(expected, Format(lazyMessage.Rmf2V5!, locale.Tag), name + ": formatted (lazy)");
                compared++;
            }
        }
        return compared;
    }

    // Formats a message with an argument of each input's carrier, as text and as content when it has markup; an
    // evaluation error is part of the result.
    private static string Format(CompiledRmf2Message message, string locale)
    {
        TextArgument[] arguments = message.InputArray.Select(input => TextArgument.CreateRmf2(input.Name, input.Type switch
        {
            TextArgumentType.String => new TextArgument("_", "Ada"),
            TextArgumentType.Int => new TextArgument("_", 3L),
            TextArgumentType.Number => new TextArgument("_", 1234.5m),
            TextArgumentType.Bool => new TextArgument("_", true),
            TextArgumentType.Date => new TextArgument("_", new DateOnly(2026, 11, 19)),
            TextArgumentType.Time => new TextArgument("_", new TimeOnly(14, 30)),
            TextArgumentType.DateTime => new TextArgument("_", new DateTimeOffset(2026, 11, 19, 14, 30, 0, TimeSpan.Zero)),
            TextArgumentType.Guid => new TextArgument("_", Guid.Parse("8c1c63b8-1d5f-4a63-9a0f-58c3d8a5f1a2")),
            _ => new TextArgument("_", (IReadOnlyList<string>)["a", "b", "c"]),
        })).ToArray();
        string text, content;
        try { text = message.Format(arguments, locale); }
        catch (Exception exception) { text = "!" + exception.GetType().Name + ": " + exception.Message; }
        if (!message.HasMarkup) return text;
        try
        {
            LocalizedTextContent formatted = message.FormatContent(arguments, locale);
            content = formatted.Locale + "/" + formatted.FormattingLocale + ":" + string.Join("|", formatted.Nodes.ToArray().Select(node =>
                node.Kind + "=" + node.Value + "[" + string.Join(",", node.Attributes.ToArray().Select(attribute => attribute.ToString())) + "]{" +
                string.Join(",", node.Annotations.ToArray().Select(annotation => annotation.Name + "=" + annotation.Value?.Value)) + "}"));
        }
        catch (Exception exception) { content = "!" + exception.GetType().Name + ": " + exception.Message; }
        return text + "\n" + content;
    }

    // Field-by-field equality of two models, with the path of the first difference.
    private static void Same(CompiledRmf2Message expected, CompiledRmf2Message actual, string path)
    {
        Assert.Equal(expected.ContentLocale, actual.ContentLocale, path + ": content locale");
        Assert.True(ReferenceEquals(expected.FormatData, actual.FormatData), path + ": format data");
        Assert.Equal(expected.HasMarkup, actual.HasMarkup, path + ": markup");
        List(expected.InputArray, actual.InputArray, path + ".inputs", (left, right, at) =>
        {
            Assert.Equal(left.Name, right.Name, at + ".name");
            Assert.Equal(left.Type, right.Type, at + ".type");
        });
        List(expected.DeclarationArray, actual.DeclarationArray, path + ".declarations", (left, right, at) =>
        {
            Assert.Equal(left.Kind, right.Kind, at + ".kind");
            Assert.Equal(left.Name, right.Name, at + ".name");
            Expression(left.Expression, right.Expression, at + ".expression");
        });
        List(expected.SelectorArray, actual.SelectorArray, path + ".selectors", (left, right, at) =>
        {
            Value(left.Value, right.Value, at + ".value");
            Assert.Equal(left.Type, right.Type, at + ".type");
            Assert.Equal(left.Function, right.Function, at + ".function");
        });
        List(expected.VariantArray, actual.VariantArray, path + ".variants", (left, right, at) =>
        {
            List(left.KeyArray, right.KeyArray, at + ".keys", (l, r, k) =>
            {
                Assert.Equal(l.Value, r.Value, k + ".value");
                Assert.Equal(l.Canonical, r.Canonical, k + ".canonical");
            });
            List(left.NodeArray, right.NodeArray, at + ".nodes", (l, r, n) =>
            {
                Assert.Equal(l.Kind, r.Kind, n + ".kind");
                Assert.Equal(l.Value, r.Value, n + ".value");
                Assert.Equal(l.MarkupKind, r.MarkupKind, n + ".markupKind");
                Assert.Equal(l.Expression is null, r.Expression is null, n + ".expression");
                if (l.Expression is not null) Expression(l.Expression, r.Expression!, n + ".expression");
                Options(l.OptionArray, r.OptionArray, n);
                Annotations(l.AnnotationArray, r.AnnotationArray, n);
            });
        });
    }

    private static void Expression(CompiledRmf2Expression expected, CompiledRmf2Expression actual, string path)
    {
        Value(expected.Operand, actual.Operand, path + ".operand");
        Assert.Equal(expected.ValueType, actual.ValueType, path + ".valueType");
        Assert.Equal(expected.Function, actual.Function, path + ".function");
        Options(expected.OptionArray, actual.OptionArray, path);
        Annotations(expected.AnnotationArray, actual.AnnotationArray, path);
    }

    private static void Options(CompiledRmf2Option[] expected, CompiledRmf2Option[] actual, string path) =>
        List(expected, actual, path + ".options", (left, right, at) =>
        {
            Assert.Equal(left.Name, right.Name, at + ".name");
            Value(left.Value, right.Value, at + ".value");
        });

    private static void Annotations(CompiledRmf2Annotation[] expected, CompiledRmf2Annotation[] actual, string path) =>
        List(expected, actual, path + ".annotations", (left, right, at) =>
        {
            Assert.Equal(left.Name, right.Name, at + ".name");
            Assert.Equal(left.Value is null, right.Value is null, at + ".value");
            if (left.Value is not null) Value(left.Value, right.Value!, at + ".value");
        });

    private static void Value(CompiledRmf2Value expected, CompiledRmf2Value actual, string path)
    {
        Assert.Equal(expected.Kind, actual.Kind, path + ".kind");
        Assert.Equal(expected.Value, actual.Value, path + ".value");
        Assert.Equal(expected.Canonical, actual.Canonical, path + ".canonical");
    }

    private static void List<T>(T[] expected, T[] actual, string path, Action<T, T, string> same)
    {
        Assert.Equal(expected.Length, actual.Length, path + ".length");
        for (int index = 0; index < expected.Length; index++) same(expected[index], actual[index], path + "[" + index + "]");
    }

    // The binary locales of the repository's catalogs, the fuzz test's seeds.
    private static List<byte[]> Seeds() => RepositoryCatalogs().Select(CompileDirectory).OfType<Rmf2ProjectV5>()
        .SelectMany(project =>
        {
            Rmf2V5DefinitionTable table = Rmf2V5DefinitionTable.Create(project);
            return project.Locales.Select(locale => Rmf2CSharpOutputRendererV5.LocaleBinary(locale, table));
        })
        .Where(binary => binary.Length > 8).OrderBy(binary => binary.Length).ToList();

    [Test, DisplayName("the binary locale reader fails closed on truncated, flipped, spliced and oversized data, in bounded time and memory")]
    public void FuzzedDataFailsClosed()
    {
        List<byte[]> seeds = Seeds();
        Assert.True(seeds.Count >= 20, "Too few seeds: " + seeds.Count);
        // Small seeds mutate fastest, so most cases use them; the largest (the editor) still gets its share.
        byte[][] pool = [.. seeds.Take(12), .. seeds.TakeLast(3)];
        var random = new Random(0x0D077016);
        int accepted = 0, rejected = 0;
        long slowest = 0;
        for (int round = 0; round < 6000; round++)
        {
            byte[] seed = pool[random.Next(pool.Length)];
            byte[] data = Mutate(seed, random);
            try
            {
                long allocated = GC.GetAllocatedBytesForCurrentThread();
                long started = Stopwatch.GetTimestamp();
                CompiledTranslationValue[] values;
                try { values = GeneratedLocaleBinary.Read(data); }
                finally
                {
                    slowest = Math.Max(slowest, Stopwatch.GetTimestamp() - started);
                    // The check allocates no array larger than the data, and per message only its value and deferred message.
                    long used = GC.GetAllocatedBytesForCurrentThread() - allocated;
                    Assert.True(used <= 64L * data.Length + 65536, "Round " + round + " allocated " + used + " bytes for " + data.Length + " bytes of data.");
                }
                foreach (CompiledTranslationValue value in values)
                {
                    // Data that passes the check decodes without error, on first use and later.
                    CompiledRmf2Message message = value.Message!.Rmf2V5!;
                    Assert.True(ReferenceEquals(message, value.Message.Rmf2V5), "a deferred model was published twice");
                }
                accepted++;
            }
            catch (TranslationPackException exception)
            {
                Assert.Equal(TranslationPackFailureReason.Malformed, exception.Reason, "failure reason of round " + round);
                rejected++;
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException("Round " + round + " (" + data.Length + " bytes) failed with " + exception.GetType().Name + ", not TranslationPackException: " + exception, exception);
            }
        }
        Assert.True(rejected > 3000 && accepted > 50, "The mutations did not cover both outcomes: " + accepted + " accepted, " + rejected + " rejected.");
        Assert.True(Stopwatch.GetElapsedTime(0, slowest) < TimeSpan.FromSeconds(1), "A single read took " + Stopwatch.GetElapsedTime(0, slowest) + ".");
    }

    private static byte[] Mutate(byte[] seed, Random random)
    {
        byte[] data = (byte[])seed.Clone();
        switch (random.Next(7))
        {
            case 0: return data[..random.Next(data.Length)];                                            // truncation
            case 1:                                                                                     // bit flips
                for (int flips = 1 + random.Next(8); flips > 0; flips--) data[random.Next(data.Length)] ^= (byte)(1 << random.Next(8));
                return data;
            case 2:                                                                                     // a huge varint
            {
                int at = 1 + random.Next(data.Length - 1);
                byte[] huge = random.Next(2) == 0 ? [0xFF, 0xFF, 0xFF, 0xFF, 0x07] : [0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x7F];
                return [.. data[..at], .. huge, .. data[Math.Min(data.Length, at + 1)..]];
            }
            case 3:                                                                                     // random bytes
                for (int index = random.Next(data.Length), end = Math.Min(data.Length, index + 1 + random.Next(16)); index < end; index++) data[index] = (byte)random.Next(256);
                return data;
            case 4:                                                                                     // a removed range
            {
                int at = random.Next(data.Length), length = 1 + random.Next(Math.Min(32, data.Length - at));
                return [.. data[..at], .. data[(at + length)..]];
            }
            case 5:                                                                                     // a duplicated range
            {
                int at = random.Next(data.Length), length = 1 + random.Next(Math.Min(64, data.Length - at));
                return [.. data[..(at + length)], .. data[at..(at + length)], .. data[(at + length)..]];
            }
            default:                                                                                    // invalid UTF-8
                data[random.Next(data.Length)] = (byte)(0xC0 | random.Next(2));
                return data;
        }
    }

    [Test, DisplayName("the binary locale reader refuses each limit and malformed shape as malformed data")]
    public void RefusesMalformedShapes()
    {
        // A valid locale: version 1, a pool of "en" and "hello", default locale 0, one plain message (ID 0) with text 1.
        byte[] valid = [1, 2, 2, (byte)'e', (byte)'n', 5, (byte)'h', (byte)'e', (byte)'l', (byte)'l', (byte)'o', 0, 1, 0, 1, 1];
        Assert.Equal("hello", GeneratedLocaleBinary.Read(valid).Single().Message!.Rmf2V5!.Format([], "en"), "the valid data");
        byte[][] cases =
        [
            [],                                                                       // empty
            [2, .. valid[1..]],                                                       // unknown version
            valid[..^1],                                                              // truncated
            [.. valid, 0],                                                            // trailing data
            [1, 0xFF, 0xFF, 0xFF, 0xFF, 0x07],                                        // a pool count beyond the data
            [1, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x01],                                  // a six-byte varint
            [1, 0x80, 0x80, 0x80, 0x80, 0x10],                                        // a varint above int.MaxValue
            [1, 1, 2, 0xC3, 0x28, 0, 0],                                              // invalid UTF-8
            [1, 1, 2, (byte)'e', (byte)'n', 1, 0],                                    // default locale out of range
            [1, 2, 2, (byte)'e', (byte)'n', 5, (byte)'h', (byte)'e', (byte)'l', (byte)'l', (byte)'o', 0, 1, 0, 1, 2], // text out of range
            [1, 2, 2, (byte)'e', (byte)'n', 5, (byte)'h', (byte)'e', (byte)'l', (byte)'l', (byte)'o', 0, 1, 0, 0x84, 1], // unknown flags
            [1, 2, 2, (byte)'e', (byte)'n', 5, (byte)'h', (byte)'e', (byte)'l', (byte)'l', (byte)'o', 0, 0x7F, 0, 1, 1], // a message count beyond the data
            [1, 2, 2, (byte)'e', (byte)'n', 5, (byte)'h', (byte)'e', (byte)'l', (byte)'l', (byte)'o', 0, 2, 0, 1, 1, 0xFF, 0xFF, 0xFF, 0xFF, 0x07, 1, 1], // ID overflow
            Body(inputs: 33),                                                         // too many inputs
            Body(declarations: 257),                                                  // too many declarations
            Body(selectors: 17),                                                      // too many selectors
            Body(variants: 0),                                                        // no variant
            Body(variants: 257),                                                      // too many variants
            Body(keys: 17),                                                           // too many keys
            Body(nodes: 4097),                                                        // too many nodes
            Body(depth: 17),                                                          // markup nested too deep
            Body(nodeTag: 9),                                                         // unknown node kind
            Body(typeByte: 9),                                                        // unknown carrier
            Body(annotationValueKind: 0),                                             // annotation value not literal
        ];
        for (int index = 0; index < cases.Length; index++)
        {
            TranslationPackException exception;
            try { GeneratedLocaleBinary.Read(cases[index]); throw new InvalidOperationException("Case " + index + " was accepted."); }
            catch (TranslationPackException caught) { exception = caught; }
            Assert.Equal(TranslationPackFailureReason.Malformed, exception.Reason, "case " + index);
        }
        // The text limit of a variant: 64 KiB of UTF-8 pass, one byte more does not.
        Assert.Equal(1, GeneratedLocaleBinary.Read(Text(TranslationPackLimits.DefaultMaximumPatternBytes)).Length, "text at the pattern limit");
        Assert.Throws<TranslationPackException>(() => GeneratedLocaleBinary.Read(Text(TranslationPackLimits.DefaultMaximumPatternBytes + 1)), "text over the pattern limit");
    }

    // One non-plain message with the pool ["en", "x", "y"], whose lists have the given lengths; elements are minimal.
    private static byte[] Body(int inputs = 0, int declarations = 0, int selectors = 0, int variants = 1, int keys = 0, int nodes = 1,
        int depth = 0, int nodeTag = 0, int typeByte = 0, int annotationValueKind = -1)
    {
        using var stream = new MemoryStream();
        stream.Write([1, 3, 2, (byte)'e', (byte)'n', 1, (byte)'x', 1, (byte)'y', 0, 1, 0, 0]);
        void Varint(int value) => Rmf2GeneratedLocaleBinaryV5.WriteVarint(stream, value);
        void Expression() { stream.Write([2, 1, (byte)typeByte]); Varint(0); Varint(0); Varint(0); }
        Varint(inputs);
        for (int index = 0; index < inputs; index++) stream.Write([1, (byte)typeByte]);
        Varint(declarations);
        for (int index = 0; index < declarations; index++) { stream.Write([1, 2]); Expression(); }
        Varint(selectors);
        for (int index = 0; index < selectors; index++) stream.Write([2, 1, 0, 0]);
        Varint(variants);
        for (int variant = 0; variant < variants; variant++)
        {
            Varint(keys);
            for (int index = 0; index < keys; index++) stream.WriteByte(0);
            if (depth > 0)
            {
                Varint(2 * depth);
                for (int index = 0; index < depth; index++) stream.Write([2, 1, 0, 0]);
                for (int index = 0; index < depth; index++) stream.Write([3, 1, 0, 0]);
                continue;
            }
            Varint(nodes);
            for (int index = 0; index < nodes; index++)
            {
                if (annotationValueKind >= 0) { stream.Write([4, 1, 0, 1, 1, 1, (byte)annotationValueKind, 1]); continue; }
                if (nodeTag == 0 && typeByte != 0) { stream.WriteByte(1); Expression(); continue; }
                stream.Write([(byte)nodeTag, 1]);
            }
        }
        return stream.ToArray();
    }

    // A plain message whose text is the given number of ASCII bytes.
    private static byte[] Text(int bytes)
    {
        using var stream = new MemoryStream();
        stream.Write([1, 2, 2, (byte)'e', (byte)'n']);
        Rmf2GeneratedLocaleBinaryV5.WriteVarint(stream, bytes);
        stream.Write(Encoding.ASCII.GetBytes(new string('a', bytes)));
        stream.Write([0, 1, 0, 1, 1]);
        return stream.ToArray();
    }

    [Test, DisplayName("concurrent first formats of a deferred message publish one model and format the same text")]
    public async Task ConcurrentFirstFormats()
    {
        Rmf2ProjectV5 project = CompileDirectory(RepositoryPaths.Resolve("apps", "translations-editor", "EditorResources", "runic.json"))!;
        Rmf2V5DefinitionTable table = Rmf2V5DefinitionTable.Create(project);
        Rmf2LocaleV5 locale = project.Locales.Single(item => item.Tag == "de");
        byte[] binary = Rmf2CSharpOutputRendererV5.LocaleBinary(locale, table);
        string[] expected = GeneratedLocaleBinary.Read(binary, null, decodeOnFirstFormat: false).Select(value => Format(value.Message!.Rmf2V5!, "de")).ToArray();
        for (int attempt = 0; attempt < 5; attempt++)
        {
            CompiledTranslationValue[] values = GeneratedLocaleBinary.Read(binary);
            const int Threads = 8;
            using var start = new Barrier(Threads);
            var models = new CompiledRmf2Message[Threads][];
            var texts = new string[Threads][];
            await Task.WhenAll(Enumerable.Range(0, Threads).Select(thread => Task.Factory.StartNew(() =>
            {
                start.SignalAndWait();
                // Each thread walks the messages from another start, so first uses overlap.
                models[thread] = new CompiledRmf2Message[values.Length];
                texts[thread] = new string[values.Length];
                for (int step = 0; step < values.Length; step++)
                {
                    int index = (step + thread * values.Length / Threads) % values.Length;
                    models[thread][index] = values[index].Message!.Rmf2V5!;
                    texts[thread][index] = Format(models[thread][index], "de");
                }
            }, TaskCreationOptions.LongRunning)));
            for (int index = 0; index < values.Length; index++)
            {
                CompiledRmf2Message published = values[index].Message!.Rmf2V5!;
                for (int thread = 0; thread < Threads; thread++)
                {
                    Assert.True(ReferenceEquals(published, models[thread][index]), "message " + index + ": thread " + thread + " saw another model");
                    Assert.Equal(expected[index], texts[thread][index], "message " + index + ": thread " + thread);
                }
            }
        }
    }
}
