using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Runic.Translations.Generated;

namespace Runic.Translations.Conformance;

// The conformance adapter of the .NET Translations runtime (runic-sdk's eng/conformance/PROTOCOL.md): one JSON request
// per line on stdin, one response per line on stdout; everything else goes to stderr. The driver
// (eng/conformance/translations) reads the corpus, applies the pack recipes and matches the results. This program maps
// the tr.* operations onto the public API that generated code and applications use: TranslationPackLoader for exact
// packs, a CompiledTranslationCatalog over the pack's messages for formatting and references, TranslationReference and
// TranslationReferenceResolver for transport, and LocaleNegotiation.
internal static class AdapterProgram
{
    private const string Protocol = "rax.conformance-adapter/1";

    private sealed record LoadedPack(CompiledTranslationProvider Provider, string Locale, string ContractFingerprint, Dictionary<string, TranslationKey> Keys);

    private static readonly Dictionary<string, LoadedPack> s_packs = new(StringComparer.Ordinal);

    public static async Task<int> Main()
    {
        var protocolOut = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true };
        // Nothing but responses may reach stdout.
        Console.SetOut(Console.Error);
        using var input = new StreamReader(Console.OpenStandardInput(), Encoding.UTF8);
        while (await input.ReadLineAsync().ConfigureAwait(false) is { } line)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            JsonObject request;
            try { request = JsonNode.Parse(line)!.AsObject(); }
            catch (JsonException error) { await Console.Error.WriteLineAsync($"unreadable request ({error.Message}): {line}").ConfigureAwait(false); continue; }
            JsonNode? seq = request["seq"]?.DeepClone();
            string op = request["op"]?.GetValue<string>() ?? "";
            JsonObject args = request["args"] as JsonObject ?? [];
            JsonObject response;
            try { response = new JsonObject { ["seq"] = seq?.DeepClone(), ["ok"] = await HandleAsync(op, args).ConfigureAwait(false) }; }
            catch (AdapterFailure failure) { response = Error(seq, failure.Code, failure.Message); }
            catch (Exception error) { response = Error(seq, "failed", $"{error.GetType().Name}: {error.Message}"); }
            await protocolOut.WriteLineAsync(response.ToJsonString()).ConfigureAwait(false);
            if (op == "shutdown") break;
        }
        return 0;
    }

    private static JsonObject Error(JsonNode? seq, string code, string message) =>
        new() { ["seq"] = seq?.DeepClone(), ["error"] = new JsonObject { ["code"] = code, ["message"] = message } };

    private static async Task<JsonNode> HandleAsync(string op, JsonObject args)
    {
        switch (op)
        {
            case "describe":
                return new JsonObject
                {
                    ["protocol"] = Protocol,
                    ["implementation"] = new JsonObject
                    {
                        ["id"] = "dotnet-translations", ["name"] = "Runic.Translations",
                        ["version"] = typeof(TranslationPackLoader).Assembly.GetName().Version?.ToString(),
                    },
                    ["corpora"] = new JsonArray("translations"),
                    ["roles"] = new JsonArray("runtime"),
                    ["features"] = new JsonArray(),
                    // The public API writes version 2 references only (TranslationReference's constructor).
                    ["capabilities"] = new JsonArray(),
                };
            case "shutdown":
                return new JsonObject();
            case "tr.loadPack":
                return await LoadAsync(Str(args, "artifact"), args["contract"]!.AsObject()).ConfigureAwait(false);
            case "tr.format":
                return Format(Pack(args), Str(args, "key"), args["arguments"]!.AsObject(), Str(args, "locale"));
            case "tr.resolveReference":
                return Resolve(Pack(args), Str(args, "locale"), Str(args, "json"));
            case "tr.readReference":
                return Read(Str(args, "json"));
            case "tr.writeReference":
                return new JsonObject { ["text"] = JsonSerializer.Serialize(Build(args["reference"]!.AsObject()), TranslationReferenceJsonContext.Default.TranslationReference) };
            case "tr.negotiate":
                return new JsonObject
                {
                    ["locale"] = LocaleNegotiation.Negotiate(Strings(args["preferences"]!), Strings(args["locales"]!), Str(args, "base")),
                };
            default:
                throw new AdapterFailure("unsupported", $"Unknown operation {op}.");
        }
    }

    // An exact pack verified against the contract its generated code holds (GeneratedCatalogSupport.CreatePackContract),
    // then a catalog with the pack's messages and the contract's bidiIsolation, as a generated catalog has them.
    private static async Task<JsonNode> LoadAsync(string artifact, JsonObject contract)
    {
        string catalog = contract["catalog"]!.GetValue<string>(), locale = contract["locale"]!.GetValue<string>();
        string fingerprint = contract["contractFingerprint"]!.GetValue<string>();
        var messages = contract["messages"]!.AsArray().Select(item => item!.AsObject()).ToArray();
        CompiledRmf2Input[] Inputs(JsonObject message) => message["inputs"]!.AsArray()
            .Select(input => new CompiledRmf2Input(input!["name"]!.GetValue<string>(), Type(input["type"]!.GetValue<string>()))).ToArray();
        TranslationPackContract packContract = GeneratedCatalogSupport.CreatePackContract(catalog, locale, fingerprint,
            messages.Select(message => GeneratedCatalogSupport.CreatePackMessageContract(
                new TranslationKey(catalog, message["id"]!.GetValue<int>(), message["key"]!.GetValue<string>()), Inputs(message))).ToArray(),
            contract["markupContract"]!.ToJsonString());
        VerifiedExternalTranslationPack pack;
        try
        {
            pack = await TranslationPackLoader.VerifyAsync(new ExternalTranslationPack(Convert.FromBase64String(artifact)), packContract).ConfigureAwait(false);
        }
        catch (TranslationPackException exception)
        {
            return new JsonObject { ["rejected"] = exception.RejectionId };
        }
        CompiledTranslationDefinition[] definitions = messages.OrderBy(message => message["id"]!.GetValue<int>())
            .Select(message => GeneratedCatalogSupport.CreateDefinition(message["key"]!.GetValue<string>(), Inputs(message), message["canonical"]!.GetValue<bool>()))
            .ToArray();
        var compiled = new CompiledTranslationCatalog(catalog, locale, definitions,
            [new CompiledTranslationLocale(locale, null, pack.Messages.Select(message => new CompiledTranslationValue(message.Key.Id, string.Empty, message.Message))
                .OrderBy(value => value.Id).ToArray())]);
        compiled = compiled.WithBidiIsolation(contract["bidiIsolation"]?.GetValue<string>() switch { "on" => true, "off" => false, _ => null });
        string handle = "p" + (s_packs.Count + 1).ToString(CultureInfo.InvariantCulture);
        s_packs[handle] = new LoadedPack(new CompiledTranslationProvider(compiled), locale, fingerprint, pack.Messages.ToDictionary(message => message.Key.Name, message => message.Key, StringComparer.Ordinal));
        return new JsonObject { ["pack"] = handle };
    }

    private static LoadedPack Pack(JsonObject args) =>
        s_packs.TryGetValue(Str(args, "pack"), out LoadedPack? pack) ? pack : throw new AdapterFailure("failed", "Unknown pack handle.");

    // The snapshot of a requested locale, as an application's provider gives it: the requested tag (de-CH) formats numbers
    // and dates, the catalog's locale (de) supplies the text.
    private static ITranslationSnapshot Snapshot(LoadedPack pack, string locale) => pack.Provider.GetSnapshotAsync(locale).AsTask().GetAwaiter().GetResult();

    private static JsonObject Format(LoadedPack pack, string key, JsonObject arguments, string locale)
    {
        if (!pack.Keys.TryGetValue(key, out TranslationKey translationKey)) throw new AdapterFailure("failed", $"The pack has no message {key}.");
        TextArgument[] values = arguments.OrderBy(item => item.Key, StringComparer.Ordinal)
            .Select(item => GeneratedCatalogSupport.CreateArgument(item.Key, Carrier(item.Value!.AsObject()))).ToArray();
        try { return new JsonObject { ["text"] = Snapshot(pack, locale).Format(translationKey, values) }; }
        catch (TranslationFormatException exception) { return new JsonObject { ["error"] = exception.Message }; }
    }

    // A corpus argument ({type, value}) as the TextArgument a generated method passes for an input of that type.
    private static TextArgument Carrier(JsonObject argument)
    {
        string type = argument["type"]!.GetValue<string>();
        JsonNode value = argument["value"]!;
        if (type == "list") return new TextArgument("_", value.AsArray().Select(item => item!.GetValue<string>()).ToArray());
        string text = value.GetValueKind() == JsonValueKind.String ? value.GetValue<string>() : value.ToJsonString();
        return type switch
        {
            "string" => new TextArgument("_", text),
            "int64" => new TextArgument("_", long.Parse(text, CultureInfo.InvariantCulture)),
            "decimal" => new TextArgument("_", decimal.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture)),
            "boolean" => new TextArgument("_", text == "true"),
            "guid" => new TextArgument("_", Guid.Parse(text, CultureInfo.InvariantCulture)),
            "date" => new TextArgument("_", DateOnly.ParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture)),
            "time" => new TextArgument("_", TimeOnly.ParseExact(text, "HH:mm:ss", CultureInfo.InvariantCulture)),
            "datetime" => new TextArgument("_", DateTimeOffset.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal)),
            _ => throw new AdapterFailure("failed", $"Unsupported argument type {type}."),
        };
    }

    private static TextArgumentType Type(string type) => type switch
    {
        "string" => TextArgumentType.String, "int64" => TextArgumentType.Int, "decimal" => TextArgumentType.Number, "list" => TextArgumentType.List,
        "boolean" => TextArgumentType.Bool, "date" => TextArgumentType.Date, "time" => TextArgumentType.Time, "datetime" => TextArgumentType.DateTime,
        "guid" => TextArgumentType.Guid, _ => throw new AdapterFailure("failed", $"Unknown input type {type}."),
    };

    private static JsonObject Resolve(LoadedPack pack, string locale, string json)
    {
        TranslationReference reference;
        try { reference = TranslationReference.Parse(json); }
        catch (JsonException exception)
        {
            return new JsonObject { ["outcome"] = "failed", ["text"] = null, ["diagnostics"] = new JsonArray(new JsonObject { ["path"] = "$", ["reason"] = "unreadable: " + exception.Message }) };
        }
        TranslationReferenceResolution resolution = TranslationReferenceResolver.Resolve(Snapshot(pack, locale), pack.ContractFingerprint, reference);
        return new JsonObject
        {
            ["outcome"] = resolution.Outcome switch { TranslationReferenceOutcome.Translated => "translated", TranslationReferenceOutcome.Fallback => "fallback", _ => "failed" },
            ["text"] = resolution.Text,
            ["diagnostics"] = new JsonArray(resolution.Diagnostics.Select(diagnostic => (JsonNode)new JsonObject
            {
                ["path"] = diagnostic.Path, ["reason"] = Reason(diagnostic.Reason),
            }).ToArray()),
        };
    }

    // The corpus's reason names (transport-v2.md) for the public enumeration.
    private static string Reason(TranslationReferenceDiagnosticReason reason) => reason switch
    {
        TranslationReferenceDiagnosticReason.ContractChanged => "contract-changed",
        TranslationReferenceDiagnosticReason.CatalogMismatch => "catalog-mismatch",
        TranslationReferenceDiagnosticReason.FingerprintMismatch => "fingerprint-mismatch",
        TranslationReferenceDiagnosticReason.UnknownKey => "unknown-key",
        TranslationReferenceDiagnosticReason.IncompatibleArguments => "incompatible-arguments",
        TranslationReferenceDiagnosticReason.StructuredContent => "structured-content",
        TranslationReferenceDiagnosticReason.FormatFailed => "format-failed",
        _ => "unmapped:" + reason,
    };

    // A rejected wire text fails both readers: TranslationReference.Parse and the source-generated converter, which
    // checks the size itself. The .NET readers name no reason.
    private static JsonObject Read(string json)
    {
        var accepted = new List<string>();
        try { _ = TranslationReference.Parse(json); accepted.Add("TranslationReference.Parse"); }
        catch (JsonException) { }
        try { _ = JsonSerializer.Deserialize(json, TranslationReferenceJsonContext.Default.TranslationReference); accepted.Add("the converter"); }
        catch (JsonException) { }
        return accepted.Count == 0 ? new JsonObject { ["ok"] = false } : new JsonObject { ["ok"] = true, ["readers"] = string.Join(", ", accepted) };
    }

    // A version 2 reference built with the public constructor from the corpus's description.
    private static TranslationReference Build(JsonObject description)
    {
        if (description["version"]?.GetValue<int>() == 1) throw new AdapterFailure("unsupported", "The public API writes version 2 references only.");
        var arguments = (description["arguments"] as JsonObject ?? []).ToDictionary(item => item.Key, item => Argument(item.Value!.AsObject(), description), StringComparer.Ordinal);
        return new TranslationReference(description["catalog"]!.GetValue<string>(), description["contractFingerprint"]!.GetValue<string>(),
            description["key"]!.GetValue<string>(), arguments, description["fallbackText"]?.GetValue<string>());
    }

    private static TranslationReferenceArgument Argument(JsonObject argument, JsonObject parent)
    {
        JsonNode value = argument["value"]!;
        return argument["type"]!.GetValue<string>() switch
        {
            "message" => new TranslationReferenceArgument(Build(Nested(value.AsObject(), parent))),
            "list" => new TranslationReferenceArgument(value.AsArray().Select(item => item!.GetValue<string>()).ToArray()),
            "boolean" => new TranslationReferenceArgument(TextArgumentType.Bool, value.GetValue<bool>() ? "true" : "false"),
            "string" => new(TextArgumentType.String, value.GetValue<string>()),
            "int64" => new(TextArgumentType.Int, value.GetValue<string>()),
            "decimal" => new(TextArgumentType.Number, value.GetValue<string>()),
            "date" => new(TextArgumentType.Date, value.GetValue<string>()),
            "time" => new(TextArgumentType.Time, value.GetValue<string>()),
            "datetime" => new(TextArgumentType.DateTime, value.GetValue<string>()),
            "guid" => new(TextArgumentType.Guid, value.GetValue<string>()),
            string type => throw new AdapterFailure("failed", $"Unknown reference argument type {type}."),
        };
    }

    // A nested message argument carries no catalog or fingerprint on the wire: it is the outer reference's.
    private static JsonObject Nested(JsonObject value, JsonObject parent)
    {
        var copy = value.DeepClone().AsObject();
        copy["catalog"] = parent["catalog"]!.DeepClone();
        copy["contractFingerprint"] = parent["contractFingerprint"]!.DeepClone();
        return copy;
    }

    private static string[] Strings(JsonNode node) => node.AsArray().Select(item => item!.GetValue<string>()).ToArray();

    private static string Str(JsonObject args, string name) =>
        args[name]?.GetValue<string>() ?? throw new AdapterFailure("failed", $"Missing argument {name}.");
}

internal sealed class AdapterFailure(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
