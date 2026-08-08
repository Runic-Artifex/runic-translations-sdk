using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace RunicTextResources;

/// <summary>Verifies and parses caller-supplied external text resource pack bytes.</summary>
public static class TextResourcePackLoader
{
    /// <summary>
    /// Requests bytes from an explicit caller source and verifies them when a pack is available.
    /// The runtime itself performs no file or network discovery.
    /// </summary>
    public static async ValueTask<VerifiedExternalTextResourcePack?> LoadAsync(
        IExternalTextResourceSource source,
        TextResourcePackContract contract,
        TextResourcePackLimits? limits = null,
        TextResourcePackIntegrityVerifier? integrityVerifier = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(contract);
        cancellationToken.ThrowIfCancellationRequested();

        ExternalTextResourcePack? pack;
        try
        {
            pack = await source.LoadAsync(contract.Catalog, contract.Locale, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            throw PackError("The external pack source failed to load a pack.", TextResourcePackFailureReason.SourceFailure);
        }

        if (pack is null) return null;
        return await VerifyAsync(pack, contract, limits, integrityVerifier, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Runs optional integrity verification, then fully validates an external pack without
    /// performing any file or network access.
    /// </summary>
    public static async ValueTask<VerifiedExternalTextResourcePack> VerifyAsync(
        ExternalTextResourcePack pack,
        TextResourcePackContract contract,
        TextResourcePackLimits? limits = null,
        TextResourcePackIntegrityVerifier? integrityVerifier = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pack);
        ArgumentNullException.ThrowIfNull(contract);
        limits ??= new TextResourcePackLimits();
        cancellationToken.ThrowIfCancellationRequested();

        ReadOnlyMemory<byte> callerContent = pack.Content;
        if (callerContent.Length == 0) throw PackError("The external pack is empty.");
        if (callerContent.Length > limits.MaximumDocumentBytes) throw LimitError("The external pack exceeds the configured document limit.");

        // ExternalTextResourcePack deliberately accepts caller-owned memory. Take one bounded
        // snapshot before the first await and use that exact snapshot for integrity and parse,
        // so mutation of the caller's backing store cannot create a verification/parsing TOCTOU.
        byte[] ownedContent = callerContent.ToArray();
        ReadOnlyMemory<byte> verifiedContent = ownedContent;

        if (integrityVerifier is not null)
        {
            bool accepted;
            try
            {
                accepted = await integrityVerifier(verifiedContent, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                throw PackError("External pack integrity verification failed.", TextResourcePackFailureReason.IntegrityRejected);
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (!accepted) throw PackError("The external pack was rejected by the integrity policy.", TextResourcePackFailureReason.IntegrityRejected);
        }

        return contract.MessageGrammarVersion == 2 && IsVersion2(ownedContent, cancellationToken)
            ? TextResourcePackV2Loader.Parse(ownedContent, contract, limits, cancellationToken)
            : Parse(ownedContent, contract, limits, cancellationToken);
    }

    private static VerifiedExternalTextResourcePack Parse(
        ReadOnlySpan<byte> content,
        TextResourcePackContract contract,
        TextResourcePackLimits limits,
        CancellationToken cancellationToken)
    {
        var options = new JsonReaderOptions
        {
            AllowTrailingCommas = false,
            CommentHandling = JsonCommentHandling.Disallow,
            // Reserve one reader level so Read can classify the configured boundary itself.
            MaxDepth = limits.MaximumDepth + 1,
        };
        var reader = new Utf8JsonReader(content, options);
        bool completedRoot = false;

        try
        {
            RequireRead(ref reader, "The external pack is not a JSON document.", cancellationToken);
            RequireToken(reader.TokenType, JsonTokenType.StartObject, "The external pack root must be an object.");

            int? artifactVersion = null;
            int? grammarVersion = null;
            string? catalog = null;
            string? locale = null;
            string? fingerprint = null;
            List<VerifiedTextResourcePackMessage>? messages = null;
            var rootMembers = new HashSet<string>(StringComparer.Ordinal);

            while (Read(ref reader, cancellationToken))
            {
                if (reader.TokenType == JsonTokenType.EndObject) break;
                RequireToken(reader.TokenType, JsonTokenType.PropertyName, "The external pack root contains an invalid member.");
                string member = GetString(ref reader, "The external pack contains an invalid property name.");
                if (!rootMembers.Add(member)) throw PackError("The external pack contains duplicate property '" + member + "'.");
                RequireRead(ref reader, "The external pack property is missing a value.", cancellationToken);

                switch (member)
                {
                    case "artifactVersion":
                        RequireToken(reader.TokenType, JsonTokenType.Number, "'artifactVersion' must be an integer.");
                        if (!reader.TryGetInt32(out int artifact)) throw PackError("'artifactVersion' must be an integer.");
                        artifactVersion = artifact;
                        break;
                    case "messageGrammarVersion":
                        RequireToken(reader.TokenType, JsonTokenType.Number, "'messageGrammarVersion' must be an integer.");
                        if (!reader.TryGetInt32(out int grammar)) throw PackError("'messageGrammarVersion' must be an integer.");
                        grammarVersion = grammar;
                        break;
                    case "catalog":
                        RequireToken(reader.TokenType, JsonTokenType.String, "'catalog' must be a string.");
                        catalog = GetString(ref reader, "'catalog' is not valid text.");
                        if (!TextResourcePackValidation.IsCatalog(catalog)) throw PackError("The external pack catalog identifier is invalid.");
                        break;
                    case "locale":
                        RequireToken(reader.TokenType, JsonTokenType.String, "'locale' must be a string.");
                        locale = GetString(ref reader, "'locale' is not valid text.");
                        if (!TextResourcePackValidation.IsCanonicalLocale(locale)) throw PackError("The external pack locale is not canonical.");
                        break;
                    case "contractFingerprint":
                        RequireToken(reader.TokenType, JsonTokenType.String, "'contractFingerprint' must be a string.");
                        fingerprint = GetString(ref reader, "'contractFingerprint' is not valid text.");
                        if (!TextResourcePackValidation.IsFingerprint(fingerprint)) throw PackError("The external pack contract fingerprint is invalid.");
                        break;
                    case "messages":
                        messages = ReadMessages(ref reader, contract, limits, cancellationToken);
                        break;
                    default:
                        throw PackError("The external pack contains unknown property '" + member + "'.", TextResourcePackFailureReason.UnknownMember);
                }
            }

            if (reader.TokenType != JsonTokenType.EndObject) throw PackError("The external pack root object is incomplete.");
            completedRoot = true;
            if (Read(ref reader, cancellationToken)) throw PackError("The external pack contains data after the root object.");
            if (artifactVersion is null || grammarVersion is null || catalog is null || locale is null || fingerprint is null || messages is null)
                throw PackError("The external pack is missing one or more required properties.");
            if (artifactVersion.Value != 1) throw PackError("The external pack artifact version is unsupported.", TextResourcePackFailureReason.ArtifactVersionMismatch);
            if (grammarVersion.Value != contract.MessageGrammarVersion || grammarVersion.Value != 1)
                throw PackError("The external pack message grammar version is unsupported.", TextResourcePackFailureReason.MessageGrammarVersionMismatch);
            if (!string.Equals(catalog, contract.Catalog, StringComparison.Ordinal))
                throw PackError("The external pack catalog does not match the generated contract.", TextResourcePackFailureReason.CatalogMismatch);
            if (!string.Equals(locale, contract.Locale, StringComparison.Ordinal))
                throw PackError("The external pack locale does not match the requested canonical locale.", TextResourcePackFailureReason.LocaleMismatch);
            if (!string.Equals(fingerprint, contract.ContractFingerprint, StringComparison.Ordinal))
                throw PackError("The external pack fingerprint does not match the generated contract.", TextResourcePackFailureReason.ContractFingerprintMismatch);

            messages.Sort(static (left, right) => StringComparer.Ordinal.Compare(left.Key.Name, right.Key.Name));
            return new VerifiedExternalTextResourcePack(catalog, locale, fingerprint, messages.ToArray());
        }
        catch (TextResourcePackException)
        {
            throw;
        }
        catch (JsonException exception)
        {
            if (completedRoot) throw PackError("The external pack contains data after the root object.");
            throw PackError("The external pack is incomplete or contains malformed JSON near byte " + exception.BytePositionInLine + ".");
        }
        catch (DecoderFallbackException)
        {
            throw PackError("The external pack contains invalid UTF-8 text.");
        }
    }

    private static bool IsVersion2(ReadOnlySpan<byte> content, CancellationToken cancellationToken)
    {
        var reader = new Utf8JsonReader(content, new JsonReaderOptions { AllowTrailingCommas = false, CommentHandling = JsonCommentHandling.Disallow });
        if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject) return false;
        while (reader.Read())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (reader.TokenType == JsonTokenType.EndObject) return false;
            if (reader.TokenType != JsonTokenType.PropertyName) return false;
            bool artifact = reader.ValueTextEquals("artifactVersion");
            if (!reader.Read()) return false;
            if (artifact) return reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out int version) && version == 2;
            reader.Skip();
        }
        return false;
    }

    private static List<VerifiedTextResourcePackMessage> ReadMessages(
        ref Utf8JsonReader reader,
        TextResourcePackContract contract,
        TextResourcePackLimits limits,
        CancellationToken cancellationToken)
    {
        RequireToken(reader.TokenType, JsonTokenType.StartObject, "'messages' must be an object.");
        var result = new List<VerifiedTextResourcePackMessage>();
        var names = new HashSet<string>(StringComparer.Ordinal);
        while (Read(ref reader, cancellationToken))
        {
            if (reader.TokenType == JsonTokenType.EndObject) return result;
            RequireToken(reader.TokenType, JsonTokenType.PropertyName, "The messages object contains an invalid member.");
            string key = GetString(ref reader, "The messages object contains an invalid key.");
            if (!names.Add(key)) throw PackError("The external pack contains duplicate message key '" + SafeKey(key) + "'.");
            if (result.Count >= limits.MaximumMessages) throw LimitError("The external pack exceeds the configured message limit.");
            if (!TextResourcePackValidation.IsResourceKey(key)) throw PackError("The external pack contains an invalid message key.");
            if (!contract.TryGetMessage(key, out TextResourcePackMessageContract known))
                throw PackError("The external pack contains unknown message key '" + SafeKey(key) + "'.", TextResourcePackFailureReason.UnknownKey);
            RequireRead(ref reader, "The external pack message is missing a value.", cancellationToken);
            result.Add(ReadMessage(ref reader, known, limits, cancellationToken));
        }
        throw PackError("The external pack messages object is incomplete.");
    }

    private static VerifiedTextResourcePackMessage ReadMessage(
        ref Utf8JsonReader reader,
        TextResourcePackMessageContract contract,
        TextResourcePackLimits limits,
        CancellationToken cancellationToken)
    {
        RequireToken(reader.TokenType, JsonTokenType.StartObject,
            "Message '" + contract.Key.Name + "' must be an object.");
        string? pattern = null;
        TextResourcePackArgumentContract[]? arguments = null;
        var members = new HashSet<string>(StringComparer.Ordinal);
        while (Read(ref reader, cancellationToken))
        {
            if (reader.TokenType == JsonTokenType.EndObject) break;
            RequireToken(reader.TokenType, JsonTokenType.PropertyName,
                "Message '" + contract.Key.Name + "' contains an invalid member.");
            string member = GetString(ref reader, "A message contains an invalid property name.");
            if (!members.Add(member))
                throw PackError("Message '" + contract.Key.Name + "' contains duplicate property '" + member + "'.");
            RequireRead(ref reader, "A message property is missing a value.", cancellationToken);
            switch (member)
            {
                case "pattern":
                    RequireToken(reader.TokenType, JsonTokenType.String,
                        "Message '" + contract.Key.Name + "' pattern must be a string.");
                    pattern = GetString(ref reader, "A message pattern is not valid text.");
                    if (Utf8Length(pattern) > limits.MaximumPatternBytes)
                        throw LimitError("Message '" + contract.Key.Name + "' exceeds the configured pattern limit.");
                    break;
                case "arguments":
                    arguments = ReadArguments(ref reader, contract.Key.Name, limits, cancellationToken);
                    break;
                default:
                    throw PackError("Message '" + contract.Key.Name + "' contains unknown property '" + member + "'.", TextResourcePackFailureReason.UnknownMember);
            }
        }
        if (reader.TokenType != JsonTokenType.EndObject || pattern is null || arguments is null)
            throw PackError("Message '" + contract.Key.Name + "' is incomplete.");
        ValidateArgumentParity(contract, arguments);
        ValidatePattern(contract, pattern);
        return new VerifiedTextResourcePackMessage(contract.Key, pattern);
    }

    private static TextResourcePackArgumentContract[] ReadArguments(
        ref Utf8JsonReader reader,
        string key,
        TextResourcePackLimits limits,
        CancellationToken cancellationToken)
    {
        RequireToken(reader.TokenType, JsonTokenType.StartArray, "Message '" + key + "' arguments must be an array.");
        var result = new List<TextResourcePackArgumentContract>();
        while (Read(ref reader, cancellationToken))
        {
            if (reader.TokenType == JsonTokenType.EndArray) return result.ToArray();
            if (result.Count >= limits.MaximumArgumentsPerMessage)
                throw LimitError("Message '" + key + "' exceeds the configured argument limit.");
            RequireToken(reader.TokenType, JsonTokenType.StartObject, "Message '" + key + "' contains an invalid argument descriptor.");
            result.Add(ReadArgument(ref reader, key, cancellationToken));
        }
        throw PackError("Message '" + key + "' arguments are incomplete.");
    }

    private static TextResourcePackArgumentContract ReadArgument(
        ref Utf8JsonReader reader,
        string key,
        CancellationToken cancellationToken)
    {
        string? name = null;
        string? typeName = null;
        string? formatName = null;
        var members = new HashSet<string>(StringComparer.Ordinal);
        while (Read(ref reader, cancellationToken))
        {
            if (reader.TokenType == JsonTokenType.EndObject) break;
            RequireToken(reader.TokenType, JsonTokenType.PropertyName, "Message '" + key + "' contains an invalid argument member.");
            string member = GetString(ref reader, "An argument contains an invalid property name.");
            if (!members.Add(member)) throw PackError("Message '" + key + "' contains a duplicate argument property '" + member + "'.");
            RequireRead(ref reader, "An argument property is missing a value.", cancellationToken);
            RequireToken(reader.TokenType, JsonTokenType.String, "Message '" + key + "' argument properties must be strings.");
            string value = GetString(ref reader, "An argument property is not valid text.");
            switch (member)
            {
                case "name": name = value; break;
                case "type": typeName = value; break;
                case "format": formatName = value; break;
                default: throw PackError("Message '" + key + "' contains unknown argument property '" + member + "'.", TextResourcePackFailureReason.UnknownMember);
            }
        }
        if (reader.TokenType != JsonTokenType.EndObject || name is null || typeName is null || formatName is null)
            throw PackError("Message '" + key + "' contains an incomplete argument descriptor.");
        if (!TextResourcePackValidation.IsIdentifier(name)) throw PackError("Message '" + key + "' contains an invalid argument name.");
        if (!TryType(typeName, out TextArgumentType type) || !TryFormat(formatName, out TextArgumentFormat format) ||
            !TextResourcePackValidation.IsFormatAllowed(type, format))
            throw PackError("Message '" + key + "' contains an invalid argument type or format and does not match its generated argument contract.", TextResourcePackFailureReason.ArgumentContractMismatch);
        return new TextResourcePackArgumentContract(name, type, format);
    }

    private static void ValidateArgumentParity(
        TextResourcePackMessageContract contract,
        TextResourcePackArgumentContract[] actual)
    {
        if (actual.Length != contract.Arguments.Count)
            throw PackError("Message '" + contract.Key.Name + "' does not match its generated argument contract.", TextResourcePackFailureReason.ArgumentContractMismatch);
        for (int i = 0; i < actual.Length; i++)
        {
            TextResourcePackArgumentContract expected = contract.Arguments[i];
            if (actual[i] != expected)
                throw PackError("Message '" + contract.Key.Name + "' does not match its generated argument contract.", TextResourcePackFailureReason.ArgumentContractMismatch);
        }
    }

    private static void ValidatePattern(TextResourcePackMessageContract contract, string pattern)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < pattern.Length; i++)
        {
            char character = pattern[i];
            if (character == '{')
            {
                if (i + 1 < pattern.Length && pattern[i + 1] == '{') { i++; continue; }
                int close = pattern.IndexOf('}', i + 1);
                if (close < 0) throw PatternError(contract.Key.Name);
                string name = pattern.Substring(i + 1, close - i - 1);
                if (!TextResourcePackValidation.IsIdentifier(name)) throw PatternError(contract.Key.Name);
                names.Add(name);
                i = close;
            }
            else if (character == '}')
            {
                if (i + 1 < pattern.Length && pattern[i + 1] == '}') { i++; continue; }
                throw PatternError(contract.Key.Name);
            }
        }

        if (names.Count != contract.Arguments.Count) throw PatternError(contract.Key.Name);
        for (int i = 0; i < contract.Arguments.Count; i++)
            if (!names.Contains(contract.Arguments[i].Name)) throw PatternError(contract.Key.Name);
    }

    private static TextResourcePackException PatternError(string key) =>
        PackError("Message '" + key + "' pattern does not match its generated argument contract.", TextResourcePackFailureReason.MalformedPattern);

    private static bool TryType(string value, out TextArgumentType type)
    {
        switch (value)
        {
            case "string": type = TextArgumentType.String; return true;
            case "int": type = TextArgumentType.Int; return true;
            case "number": type = TextArgumentType.Number; return true;
            case "bool": type = TextArgumentType.Bool; return true;
            case "date": type = TextArgumentType.Date; return true;
            case "time": type = TextArgumentType.Time; return true;
            case "datetime": type = TextArgumentType.DateTime; return true;
            case "guid": type = TextArgumentType.Guid; return true;
            default: type = default; return false;
        }
    }

    private static bool TryFormat(string value, out TextArgumentFormat format)
    {
        switch (value)
        {
            case "none": format = TextArgumentFormat.None; return true;
            case "plain": format = TextArgumentFormat.Plain; return true;
            case "grouped": format = TextArgumentFormat.Grouped; return true;
            case "fixed0": format = TextArgumentFormat.Fixed0; return true;
            case "fixed1": format = TextArgumentFormat.Fixed1; return true;
            case "fixed2": format = TextArgumentFormat.Fixed2; return true;
            case "fixed3": format = TextArgumentFormat.Fixed3; return true;
            case "fixed4": format = TextArgumentFormat.Fixed4; return true;
            case "fixed5": format = TextArgumentFormat.Fixed5; return true;
            case "fixed6": format = TextArgumentFormat.Fixed6; return true;
            case "percent0": format = TextArgumentFormat.Percent0; return true;
            case "percent1": format = TextArgumentFormat.Percent1; return true;
            case "percent2": format = TextArgumentFormat.Percent2; return true;
            case "percent3": format = TextArgumentFormat.Percent3; return true;
            case "percent4": format = TextArgumentFormat.Percent4; return true;
            case "lower": format = TextArgumentFormat.Lower; return true;
            case "iso": format = TextArgumentFormat.Iso; return true;
            case "short": format = TextArgumentFormat.Short; return true;
            case "medium": format = TextArgumentFormat.Medium; return true;
            case "long": format = TextArgumentFormat.Long; return true;
            case "d": format = TextArgumentFormat.D; return true;
            case "n": format = TextArgumentFormat.N; return true;
            default: format = default; return false;
        }
    }

    private static int Utf8Length(string value)
    {
        var utf8 = new UTF8Encoding(false, true);
        return utf8.GetByteCount(value);
    }

    private static string SafeKey(string key) => TextResourcePackValidation.IsResourceKey(key) ? key : "<invalid>";

    private static bool Read(ref Utf8JsonReader reader, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        bool result = reader.Read();
        if (result && reader.CurrentDepth >= reader.CurrentState.Options.MaxDepth - 1)
            throw LimitError("The external pack exceeds the configured depth limit.");
        return result;
    }

    private static void RequireRead(ref Utf8JsonReader reader, string message, CancellationToken cancellationToken)
    {
        if (!Read(ref reader, cancellationToken)) throw PackError(message);
    }

    private static string GetString(ref Utf8JsonReader reader, string message)
    {
        try
        {
            return reader.GetString() ?? throw PackError(message);
        }
        catch (InvalidOperationException exception) when (exception.InnerException is DecoderFallbackException)
        {
            throw PackError("The external pack contains invalid UTF-8 text.");
        }
    }

    private static void RequireToken(JsonTokenType actual, JsonTokenType expected, string message)
    {
        if (actual != expected) throw PackError(message);
    }

    private static TextResourcePackException LimitError(string message) =>
        PackError(message, TextResourcePackFailureReason.LimitExceeded);

    private static TextResourcePackException PackError(
        string message,
        TextResourcePackFailureReason reason = TextResourcePackFailureReason.Malformed) =>
        TextResourcePackFailure.Create(message, reason);
}
