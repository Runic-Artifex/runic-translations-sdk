using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using RunicTranslations.Compiler.Generation;

namespace RunicTranslations.Compiler.Analysis;

public static class TranslationAnalyzer
{
    public static TranslationAnalysisReport Analyze(
        TranslationCompilation compilation,
        IEnumerable<TranslationUsageSource> usageSources,
        TranslationAnalysisOptions? options = null)
        => AnalyzeCore(compilation, usageSources, null, options);

    public static TranslationAnalysisReport Analyze(
        TranslationCompilation compilation,
        IEnumerable<TranslationUsageSource> usageSources,
        IEnumerable<TranslationArtifactSnapshot> artifactSnapshots,
        TranslationAnalysisOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(artifactSnapshots);
        return AnalyzeCore(compilation, usageSources, artifactSnapshots, options);
    }

    private static TranslationAnalysisReport AnalyzeCore(
        TranslationCompilation compilation,
        IEnumerable<TranslationUsageSource> usageSources,
        IEnumerable<TranslationArtifactSnapshot>? artifactSnapshots,
        TranslationAnalysisOptions? options)
    {
        ArgumentNullException.ThrowIfNull(compilation);
        ArgumentNullException.ThrowIfNull(usageSources);
        options ??= new TranslationAnalysisOptions();
        if (options.DynamicUsagePolicy is not TranslationDynamicUsagePolicy.Conservative and
            not TranslationDynamicUsagePolicy.IgnoreForDeletionCandidates)
            throw new ArgumentOutOfRangeException(nameof(options), "Unknown dynamic usage policy.");

        var catalogs = new List<CompiledTextCatalog>(compilation.Catalogs.Count);
        var catalogsById = new Dictionary<string, CompiledTextCatalog>(StringComparer.Ordinal);
        for (int index = 0; index < compilation.Catalogs.Count; index++)
        {
            CompiledTextCatalog catalog = compilation.Catalogs[index];
            if (!catalogsById.TryAdd(catalog.Id, catalog))
                throw new ArgumentException("Compilation contains duplicate catalog ID '" + catalog.Id + "'.", nameof(compilation));
            catalogs.Add(catalog);
        }
        catalogs.Sort(static (left, right) => StringComparer.Ordinal.Compare(left.Id, right.Id));

        var sources = new List<TranslationUsageSource>();
        foreach (TranslationUsageSource source in usageSources)
        {
            if (source is null) throw new ArgumentException("Usage source collection contains null.", nameof(usageSources));
            if (source.CatalogId is not null && !catalogsById.ContainsKey(source.CatalogId))
                throw new ArgumentException("Usage source '" + source.Path + "' names unknown catalog '" + source.CatalogId + "'.", nameof(usageSources));
            sources.Add(source);
        }
        sources.Sort(static (left, right) =>
        {
            int path = StringComparer.Ordinal.Compare(left.Path, right.Path);
            if (path != 0) return path;
            int language = left.Language.CompareTo(right.Language);
            if (language != 0) return language;
            return StringComparer.Ordinal.Compare(left.CatalogId, right.CatalogId);
        });

        Dictionary<string, TranslationArtifactSnapshot>? snapshots = null;
        if (artifactSnapshots is not null)
        {
            snapshots = new Dictionary<string, TranslationArtifactSnapshot>(StringComparer.Ordinal);
            foreach (TranslationArtifactSnapshot snapshot in artifactSnapshots)
            {
                if (snapshot is null) throw new ArgumentException("Artifact snapshot collection contains null.", nameof(artifactSnapshots));
                if (!snapshots.TryAdd(snapshot.CatalogId, snapshot))
                    throw new ArgumentException("Artifact snapshots contain duplicate catalog ID '" + snapshot.CatalogId + "'.", nameof(artifactSnapshots));
            }
        }

        var usage = new Dictionary<string, CatalogUsage>(StringComparer.Ordinal);
        for (int catalogIndex = 0; catalogIndex < catalogs.Count; catalogIndex++)
            usage.Add(catalogs[catalogIndex].Id, new CatalogUsage(catalogs[catalogIndex]));

        for (int sourceIndex = 0; sourceIndex < sources.Count; sourceIndex++)
        {
            TranslationUsageSource source = sources[sourceIndex];
            IReadOnlyList<CompiledTextCatalog> candidates = CandidateCatalogs(source, catalogs, catalogsById);
            if (source.Language == TranslationUsageSourceLanguage.CSharp)
                ScanCSharp(source, candidates, usage);
            else
                ScanTypeScript(source, candidates, usage);
        }

        var analyzedCatalogs = new List<TranslationCatalogAnalysis>(catalogs.Count);
        for (int catalogIndex = 0; catalogIndex < catalogs.Count; catalogIndex++)
        {
            CompiledTextCatalog catalog = catalogs[catalogIndex];
            string sourceFingerprint = SourceFingerprint(catalog);
            TranslationArtifactStatus artifactStatus;
            string? artifactPath;
            if (snapshots is null)
            {
                artifactStatus = TranslationArtifactStatus.Unknown;
                artifactPath = null;
            }
            else if (!snapshots.TryGetValue(catalog.Id, out TranslationArtifactSnapshot? snapshot))
            {
                artifactStatus = TranslationArtifactStatus.Missing;
                artifactPath = null;
            }
            else
            {
                artifactStatus = string.Equals(snapshot.SourceFingerprint, sourceFingerprint, StringComparison.Ordinal)
                    ? TranslationArtifactStatus.Current
                    : TranslationArtifactStatus.Stale;
                artifactPath = snapshot.Path;
            }

            analyzedCatalogs.Add(new TranslationCatalogAnalysis(
                catalog.Id,
                catalog.Fingerprint,
                sourceFingerprint,
                artifactStatus,
                artifactPath,
                AnalyzeKeys(catalog, usage[catalog.Id], options)));
        }

        return new TranslationAnalysisReport(analyzedCatalogs.ToArray(), options.DynamicUsagePolicy);
    }

    private static IReadOnlyList<CompiledTextCatalog> CandidateCatalogs(
        TranslationUsageSource source,
        IReadOnlyList<CompiledTextCatalog> catalogs,
        Dictionary<string, CompiledTextCatalog> catalogsById)
    {
        if (source.CatalogId is null) return catalogs;
        return new[] { catalogsById[source.CatalogId] };
    }

    private static TranslationKeyAnalysis[] AnalyzeKeys(
        CompiledTextCatalog catalog,
        CatalogUsage usage,
        TranslationAnalysisOptions options)
    {
        IReadOnlyList<CompiledTranslation> resources = GenerationSupport.OrderedResources(catalog.CanonicalResources);
        var result = new List<TranslationKeyAnalysis>(resources.Count);
        for (int resourceIndex = 0; resourceIndex < resources.Count; resourceIndex++)
        {
            CompiledTranslation canonical = resources[resourceIndex];
            List<TranslationUsageEvidence> evidence = usage.EvidenceByKey[canonical.Key];
            if (usage.DynamicEvidence.Count != 0)
                evidence.AddRange(usage.DynamicEvidence);
            evidence.Sort(EvidenceComparison);

            bool proven = false;
            TranslationUsageLanguage provenLanguages = TranslationUsageLanguage.None;
            TranslationUsageLanguage dynamicLanguages = TranslationUsageLanguage.None;
            for (int evidenceIndex = 0; evidenceIndex < evidence.Count; evidenceIndex++)
            {
                TranslationUsageEvidence item = evidence[evidenceIndex];
                if (item.Kind == TranslationUsageEvidenceKind.DynamicLookup) dynamicLanguages |= item.Language;
                else
                {
                    proven = true;
                    provenLanguages |= item.Language;
                }
            }

            TranslationUsageClassification classification = proven
                ? TranslationUsageClassification.Proven
                : usage.DynamicEvidence.Count != 0
                    ? TranslationUsageClassification.PossibleDynamic
                    : TranslationUsageClassification.Unknown;
            bool deletionCandidate = classification == TranslationUsageClassification.Unknown ||
                (classification == TranslationUsageClassification.PossibleDynamic &&
                 options.DynamicUsagePolicy == TranslationDynamicUsagePolicy.IgnoreForDeletionCandidates);

            result.Add(new TranslationKeyAnalysis(
                canonical.Key,
                classification,
                proven ? provenLanguages : dynamicLanguages,
                deletionCandidate,
                AnalyzeLocales(catalog, canonical),
                evidence.ToArray()));
        }

        return result.ToArray();
    }

    private static TranslationLocaleAnalysis[] AnalyzeLocales(
        CompiledTextCatalog catalog,
        CompiledTranslation canonical)
    {
        IReadOnlyList<CompiledTextLocale> locales = GenerationSupport.OrderedLocales(catalog.Locales);
        var result = new List<TranslationLocaleAnalysis>(locales.Count);
        for (int localeIndex = 0; localeIndex < locales.Count; localeIndex++)
        {
            CompiledTextLocale locale = locales[localeIndex];
            CompiledTranslation? direct = Find(locale.DirectResources, canonical.Key);
            CompiledTranslation? resolved = direct ?? Find(locale.ResolvedResources, canonical.Key);
            TranslationLocaleAvailability availability = direct is not null
                ? TranslationLocaleAvailability.Direct
                : resolved is not null
                    ? TranslationLocaleAvailability.FallbackOnly
                    : TranslationLocaleAvailability.Missing;
            TranslationContractStatus contract = resolved is null
                ? TranslationContractStatus.Missing
                : SameContract(canonical, resolved)
                    ? TranslationContractStatus.Matches
                    : TranslationContractStatus.Drift;
            result.Add(new TranslationLocaleAnalysis(
                locale.Tag,
                availability,
                contract,
                direct is null && resolved is not null ? FindDefiningLocale(locales, resolved) : null));
        }
        return result.ToArray();
    }

    private static CompiledTranslation? Find(IReadOnlyList<CompiledTranslation> resources, string key)
    {
        for (int index = 0; index < resources.Count; index++)
            if (string.Equals(resources[index].Key, key, StringComparison.Ordinal)) return resources[index];
        return null;
    }

    private static string? FindDefiningLocale(IReadOnlyList<CompiledTextLocale> locales, CompiledTranslation resolved)
    {
        for (int localeIndex = 0; localeIndex < locales.Count; localeIndex++)
        {
            CompiledTranslation? candidate = Find(locales[localeIndex].DirectResources, resolved.Key);
            if (candidate is not null && SameLocation(candidate.SourceLocation, resolved.SourceLocation)) return locales[localeIndex].Tag;
        }
        return null;
    }

    private static bool SameLocation(TextSourceLocation left, TextSourceLocation right) =>
        string.Equals(left.Path, right.Path, StringComparison.Ordinal) &&
        left.StartByte == right.StartByte && left.LengthBytes == right.LengthBytes;

    private static bool SameContract(CompiledTranslation canonical, CompiledTranslation candidate)
    {
        if (canonical.ProducesStructuredContent != candidate.ProducesStructuredContent ||
            canonical.Placeholders.Count != candidate.Placeholders.Count)
            return false;
        IReadOnlyList<CompiledTextPlaceholder> left = GenerationSupport.OrderedPlaceholders(canonical.Placeholders);
        IReadOnlyList<CompiledTextPlaceholder> right = GenerationSupport.OrderedPlaceholders(candidate.Placeholders);
        for (int index = 0; index < left.Count; index++)
        {
            if (!string.Equals(left[index].Name, right[index].Name, StringComparison.Ordinal) ||
                left[index].Type != right[index].Type ||
                !string.Equals(left[index].Format, right[index].Format, StringComparison.Ordinal))
                return false;
        }
        return true;
    }

    private static void ScanTypeScript(
        TranslationUsageSource source,
        IReadOnlyList<CompiledTextCatalog> catalogs,
        IReadOnlyDictionary<string, CatalogUsage> usage)
    {
        List<Token> tokens = Tokenize(source.Text);
        var aliases = new HashSet<string>(StringComparer.Ordinal) { "m" };
        for (int index = 0; index + 4 < tokens.Count; index++)
        {
            if (tokens[index].Text == "m" && tokens[index + 1].Text == "as" && tokens[index + 2].Kind == TokenKind.Identifier)
                aliases.Add(tokens[index + 2].Text);
        }

        for (int index = 0; index < tokens.Count; index++)
        {
            Token token = tokens[index];
            if (token.Kind == TokenKind.Identifier && token.Text.StartsWith("m$", StringComparison.Ordinal))
            {
                string key = token.Text.Substring(2).Replace('$', '.');
                AddStaticReference(source, catalogs, usage, key, token,
                    TranslationUsageLanguage.TypeScript, TranslationUsageEvidenceKind.TypeScriptGeneratedIdentifier);
                continue;
            }

            if (token.Kind == TokenKind.Identifier && aliases.Contains(token.Text) &&
                index + 2 < tokens.Count && tokens[index + 1].Text == "[")
            {
                if (tokens[index + 2].Kind == TokenKind.String && index + 3 < tokens.Count && tokens[index + 3].Text == "]")
                {
                    AddStaticReference(source, catalogs, usage, tokens[index + 2].Text, token,
                        TranslationUsageLanguage.TypeScript, TranslationUsageEvidenceKind.TypeScriptMessageNamespace);
                }
                else
                {
                    AddDynamicReference(source, catalogs, usage, token, TranslationUsageLanguage.TypeScript);
                }
                continue;
            }

            if (token.Kind == TokenKind.Identifier && aliases.Contains(token.Text) &&
                index + 2 < tokens.Count && tokens[index + 1].Text == "." && tokens[index + 2].Kind == TokenKind.Identifier)
            {
                AddStaticReference(source, catalogs, usage, tokens[index + 2].Text, token,
                    TranslationUsageLanguage.TypeScript, TranslationUsageEvidenceKind.TypeScriptMessageNamespace);
                continue;
            }

            if (token.Text == "formatDynamicMessage" && index + 1 < tokens.Count && tokens[index + 1].Text == "(")
            {
                List<(int Start, int End)> arguments = Arguments(tokens, index + 1);
                if (arguments.Count >= 2 && arguments[1].Start == arguments[1].End &&
                    tokens[arguments[1].Start].Kind == TokenKind.String)
                {
                    AddStaticReference(source, catalogs, usage, tokens[arguments[1].Start].Text, token,
                        TranslationUsageLanguage.TypeScript, TranslationUsageEvidenceKind.TypeScriptMessageNamespace);
                }
                else
                {
                    AddDynamicReference(source, catalogs, usage, token, TranslationUsageLanguage.TypeScript);
                }
            }
        }
    }

    private static void ScanCSharp(
        TranslationUsageSource source,
        IReadOnlyList<CompiledTextCatalog> catalogs,
        IReadOnlyDictionary<string, CatalogUsage> usage)
    {
        List<Token> tokens = Tokenize(source.Text);
        var accessorVariables = new Dictionary<string, List<CompiledTextCatalog>>(StringComparer.Ordinal);
        for (int index = 0; index + 1 < tokens.Count; index++)
        {
            if (tokens[index].Kind != TokenKind.Identifier || tokens[index + 1].Kind != TokenKind.Identifier) continue;
            var matches = new List<CompiledTextCatalog>();
            for (int catalogIndex = 0; catalogIndex < catalogs.Count; catalogIndex++)
                if (string.Equals(tokens[index].Text, GenerationSupport.CSharpIdentifier(catalogs[catalogIndex].ClassName), StringComparison.Ordinal))
                    matches.Add(catalogs[catalogIndex]);
            if (matches.Count != 0) accessorVariables[tokens[index + 1].Text] = matches;
        }

        for (int catalogIndex = 0; catalogIndex < catalogs.Count; catalogIndex++)
        {
            CompiledTextCatalog catalog = catalogs[catalogIndex];
            foreach (CompiledTranslation resource in catalog.CanonicalResources)
            {
                string[] segments = resource.Key.Split('.');
                var staticPath = new string[segments.Length + 1];
                staticPath[0] = GenerationSupport.CSharpIdentifier(catalog.ClassName) + "Keys";
                for (int segmentIndex = 0; segmentIndex < segments.Length; segmentIndex++)
                    staticPath[segmentIndex + 1] = GenerationSupport.CSharpIdentifier(segments[segmentIndex]);
                FindCSharpPath(source, tokens, staticPath, catalog, usage, resource.Key,
                    TranslationUsageEvidenceKind.CSharpGeneratedKey);

                foreach (KeyValuePair<string, List<CompiledTextCatalog>> variable in accessorVariables)
                {
                    if (variable.Value.Count != 1 || !ReferenceEquals(variable.Value[0], catalog)) continue;
                    var accessorPath = new string[segments.Length + 1];
                    accessorPath[0] = variable.Key;
                    for (int segmentIndex = 0; segmentIndex < segments.Length; segmentIndex++)
                        accessorPath[segmentIndex + 1] = GenerationSupport.CSharpIdentifier(segments[segmentIndex]);
                    FindCSharpPath(source, tokens, accessorPath, catalog, usage, resource.Key,
                        TranslationUsageEvidenceKind.CSharpGeneratedAccessor);
                }
            }
        }

        for (int index = 0; index + 2 < tokens.Count; index++)
        {
            if (tokens[index].Text != "TranslationKey" || tokens[index + 1].Text != "(") continue;
            List<(int Start, int End)> arguments = Arguments(tokens, index + 1);
            if (arguments.Count < 3) continue;
            string? catalogId = SingleString(tokens, arguments[0]);
            string? key = SingleString(tokens, arguments[2]);
            if (catalogId is not null && key is not null)
            {
                var matching = new List<CompiledTextCatalog>();
                for (int catalogIndex = 0; catalogIndex < catalogs.Count; catalogIndex++)
                    if (string.Equals(catalogs[catalogIndex].Id, catalogId, StringComparison.Ordinal)) matching.Add(catalogs[catalogIndex]);
                AddStaticReference(source, matching, usage, key, tokens[index],
                    TranslationUsageLanguage.CSharp, TranslationUsageEvidenceKind.CSharpTranslationKey);
            }
            else
            {
                AddDynamicReference(source, catalogs, usage, tokens[index], TranslationUsageLanguage.CSharp);
            }
        }
    }

    private static void FindCSharpPath(
        TranslationUsageSource source,
        IReadOnlyList<Token> tokens,
        string[] path,
        CompiledTextCatalog catalog,
        IReadOnlyDictionary<string, CatalogUsage> usage,
        string key,
        TranslationUsageEvidenceKind kind)
    {
        int tokenLength = path.Length * 2 - 1;
        for (int index = 0; index + tokenLength <= tokens.Count; index++)
        {
            bool match = true;
            for (int pathIndex = 0; pathIndex < path.Length; pathIndex++)
            {
                if (!string.Equals(tokens[index + pathIndex * 2].Text, path[pathIndex], StringComparison.Ordinal) ||
                    (pathIndex != path.Length - 1 && tokens[index + pathIndex * 2 + 1].Text != "."))
                {
                    match = false;
                    break;
                }
            }
            if (!match) continue;
            AddEvidence(usage[catalog.Id].EvidenceByKey[key], source, tokens[index],
                TranslationUsageLanguage.CSharp, kind);
        }
    }

    private static void AddStaticReference(
        TranslationUsageSource source,
        IReadOnlyList<CompiledTextCatalog> catalogs,
        IReadOnlyDictionary<string, CatalogUsage> usage,
        string key,
        Token token,
        TranslationUsageLanguage language,
        TranslationUsageEvidenceKind kind)
    {
        var matches = new List<CompiledTextCatalog>();
        for (int catalogIndex = 0; catalogIndex < catalogs.Count; catalogIndex++)
            if (usage[catalogs[catalogIndex].Id].EvidenceByKey.ContainsKey(key)) matches.Add(catalogs[catalogIndex]);

        if (matches.Count == 1)
        {
            AddEvidence(usage[matches[0].Id].EvidenceByKey[key], source, token, language, kind);
            return;
        }

        if (matches.Count > 1)
            AddDynamicReference(source, matches, usage, token, language);
    }

    private static void AddDynamicReference(
        TranslationUsageSource source,
        IReadOnlyList<CompiledTextCatalog> catalogs,
        IReadOnlyDictionary<string, CatalogUsage> usage,
        Token token,
        TranslationUsageLanguage language)
    {
        for (int catalogIndex = 0; catalogIndex < catalogs.Count; catalogIndex++)
            AddEvidence(usage[catalogs[catalogIndex].Id].DynamicEvidence, source, token, language,
                TranslationUsageEvidenceKind.DynamicLookup);
    }

    private static void AddEvidence(
        List<TranslationUsageEvidence> evidence,
        TranslationUsageSource source,
        Token token,
        TranslationUsageLanguage language,
        TranslationUsageEvidenceKind kind)
    {
        for (int index = 0; index < evidence.Count; index++)
            if (evidence[index].Path == source.Path && evidence[index].Line == token.Line &&
                evidence[index].Column == token.Column && evidence[index].Language == language && evidence[index].Kind == kind)
                return;
        evidence.Add(new TranslationUsageEvidence(source.Path, token.Line, token.Column, language, kind));
    }

    private static int EvidenceComparison(TranslationUsageEvidence left, TranslationUsageEvidence right)
    {
        int path = StringComparer.Ordinal.Compare(left.Path, right.Path);
        if (path != 0) return path;
        int line = left.Line.CompareTo(right.Line);
        if (line != 0) return line;
        int column = left.Column.CompareTo(right.Column);
        if (column != 0) return column;
        return left.Kind.CompareTo(right.Kind);
    }

    private static string SourceFingerprint(CompiledTextCatalog catalog)
    {
        var value = new StringBuilder();
        Append(value, catalog.Id);
        Append(value, catalog.DefaultLocale);
        Append(value, catalog.Fingerprint);
        IReadOnlyList<CompiledTextLocale> locales = GenerationSupport.OrderedLocales(catalog.Locales);
        for (int localeIndex = 0; localeIndex < locales.Count; localeIndex++)
        {
            Append(value, locales[localeIndex].Tag);
            Append(value, locales[localeIndex].FallbackTag ?? string.Empty);
            IReadOnlyList<CompiledTranslation> resources = GenerationSupport.OrderedResources(locales[localeIndex].DirectResources);
            for (int resourceIndex = 0; resourceIndex < resources.Count; resourceIndex++)
            {
                CompiledTranslation resource = resources[resourceIndex];
                Append(value, resource.Key);
                Append(value, resource.Pattern);
                Append(value, resource.Description ?? string.Empty);
                Append(value, resource.Since ?? string.Empty);
                Append(value, resource.DeprecatedReason ?? string.Empty);
                for (int tagIndex = 0; tagIndex < resource.Tags.Count; tagIndex++) Append(value, resource.Tags[tagIndex]);
                IReadOnlyList<CompiledTextPlaceholder> placeholders = GenerationSupport.OrderedPlaceholders(resource.Placeholders);
                for (int placeholderIndex = 0; placeholderIndex < placeholders.Count; placeholderIndex++)
                {
                    Append(value, placeholders[placeholderIndex].Name);
                    Append(value, placeholders[placeholderIndex].Type.ToString());
                    Append(value, placeholders[placeholderIndex].Format);
                }
            }
        }
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(value.ToString()));
        return "sha256:" + Convert.ToHexStringLower(hash);

        static void Append(StringBuilder builder, string item) => builder
            .Append(item.Length.ToString(CultureInfo.InvariantCulture)).Append(':').Append(item).Append(';');
    }

    private static string? SingleString(IReadOnlyList<Token> tokens, (int Start, int End) argument) =>
        argument.Start == argument.End && argument.Start >= 0 && tokens[argument.Start].Kind == TokenKind.String
            ? tokens[argument.Start].Text
            : null;

    private static List<(int Start, int End)> Arguments(IReadOnlyList<Token> tokens, int openParenthesis)
    {
        var result = new List<(int Start, int End)>();
        int depth = 0;
        int start = openParenthesis + 1;
        for (int index = openParenthesis + 1; index < tokens.Count; index++)
        {
            string text = tokens[index].Text;
            if (text is "(" or "[" or "{") depth++;
            else if (text is ")" or "]" or "}")
            {
                if (text == ")" && depth == 0)
                {
                    if (index > start) result.Add((start, index - 1));
                    return result;
                }
                depth--;
            }
            else if (text == "," && depth == 0)
            {
                result.Add((start, index - 1));
                start = index + 1;
            }
        }
        return result;
    }

    private static List<Token> Tokenize(string text)
    {
        var tokens = new List<Token>();
        int index = 0;
        int line = 1;
        int column = 1;
        while (index < text.Length)
        {
            char current = text[index];
            if (char.IsWhiteSpace(current))
            {
                Advance(current, ref index, ref line, ref column);
                continue;
            }
            if (current == '/' && index + 1 < text.Length && text[index + 1] == '/')
            {
                while (index < text.Length && text[index] != '\n') Advance(text[index], ref index, ref line, ref column);
                continue;
            }
            if (current == '/' && index + 1 < text.Length && text[index + 1] == '*')
            {
                Advance(text[index], ref index, ref line, ref column);
                Advance(text[index], ref index, ref line, ref column);
                while (index + 1 < text.Length && !(text[index] == '*' && text[index + 1] == '/'))
                    Advance(text[index], ref index, ref line, ref column);
                if (index < text.Length) Advance(text[index], ref index, ref line, ref column);
                if (index < text.Length) Advance(text[index], ref index, ref line, ref column);
                continue;
            }

            int tokenLine = line;
            int tokenColumn = column;
            bool verbatim = current == '@' && index + 1 < text.Length && text[index + 1] == '"';
            if (current is '"' or '\'' or '`' || verbatim)
            {
                char quote = verbatim ? '"' : current;
                if (verbatim) Advance(text[index], ref index, ref line, ref column);
                Advance(text[index], ref index, ref line, ref column);
                var value = new StringBuilder();
                bool dynamicTemplate = false;
                while (index < text.Length)
                {
                    char character = text[index];
                    if (character == quote)
                    {
                        if (verbatim && index + 1 < text.Length && text[index + 1] == quote)
                        {
                            value.Append(quote);
                            Advance(text[index], ref index, ref line, ref column);
                            Advance(text[index], ref index, ref line, ref column);
                            continue;
                        }
                        Advance(text[index], ref index, ref line, ref column);
                        break;
                    }
                    if (!verbatim && character == '\\' && index + 1 < text.Length)
                    {
                        Advance(text[index], ref index, ref line, ref column);
                        char escaped = text[index];
                        value.Append(escaped switch { 'n' => '\n', 'r' => '\r', 't' => '\t', _ => escaped });
                        Advance(text[index], ref index, ref line, ref column);
                        continue;
                    }
                    if (quote == '`' && character == '$' && index + 1 < text.Length && text[index + 1] == '{') dynamicTemplate = true;
                    value.Append(character);
                    Advance(text[index], ref index, ref line, ref column);
                }
                tokens.Add(new Token(dynamicTemplate ? TokenKind.DynamicString : TokenKind.String, value.ToString(), tokenLine, tokenColumn));
                continue;
            }

            if (IsIdentifierStart(current))
            {
                int start = index;
                Advance(text[index], ref index, ref line, ref column);
                while (index < text.Length && IsIdentifierPart(text[index])) Advance(text[index], ref index, ref line, ref column);
                tokens.Add(new Token(TokenKind.Identifier, text.Substring(start, index - start), tokenLine, tokenColumn));
                continue;
            }

            tokens.Add(new Token(TokenKind.Punctuation, current.ToString(), tokenLine, tokenColumn));
            Advance(text[index], ref index, ref line, ref column);
        }
        return tokens;
    }

    private static bool IsIdentifierStart(char value) => char.IsLetter(value) || value is '_' or '$' or '@';
    private static bool IsIdentifierPart(char value) => char.IsLetterOrDigit(value) || value is '_' or '$';

    private static void Advance(char value, ref int index, ref int line, ref int column)
    {
        index++;
        if (value == '\n') { line++; column = 1; }
        else column++;
    }

    private sealed class CatalogUsage
    {
        internal CatalogUsage(CompiledTextCatalog catalog)
        {
            foreach (CompiledTranslation resource in catalog.CanonicalResources)
                EvidenceByKey.Add(resource.Key, new List<TranslationUsageEvidence>());
        }

        internal Dictionary<string, List<TranslationUsageEvidence>> EvidenceByKey { get; } =
            new Dictionary<string, List<TranslationUsageEvidence>>(StringComparer.Ordinal);
        internal List<TranslationUsageEvidence> DynamicEvidence { get; } = new List<TranslationUsageEvidence>();
    }

    private enum TokenKind
    {
        Identifier,
        String,
        DynamicString,
        Punctuation,
    }

    private readonly record struct Token(TokenKind Kind, string Text, int Line, int Column);
}
