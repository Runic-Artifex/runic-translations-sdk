using System;
using System.Collections.Generic;

namespace WebUIToolkit.TextResources;

/// <summary>
/// Contains validated, immutable generated catalog data used to create locale snapshots.
/// </summary>
public sealed class CompiledTextResourceCatalog
{
    /// <summary>The key-ID sentinel used for name-based lookup of permitted locale extras.</summary>
    public const int DynamicKeyId = -1;

    private const int MaximumKeys = 50_000;
    private const int MaximumLocales = 256;

    private readonly CompiledTextResourceDefinition[] _definitions;
    private readonly CompiledTextResourceLocale[] _locales;
    private readonly Dictionary<string, int> _idByName;
    private readonly Dictionary<string, LocaleState> _localeByTag;

    /// <summary>Creates a validated compiled catalog from generated closed data.</summary>
    public CompiledTextResourceCatalog(
        string catalog,
        string defaultLocale,
        IReadOnlyList<CompiledTextResourceDefinition> definitions,
        IReadOnlyList<CompiledTextResourceLocale> locales,
        UnsupportedLocalePolicy unsupportedLocale = UnsupportedLocalePolicy.ParentsThenDefault,
        MissingTextResourcePolicy missingKey = MissingTextResourcePolicy.Throw)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(catalog);
        ArgumentException.ThrowIfNullOrWhiteSpace(defaultLocale);
        ArgumentNullException.ThrowIfNull(definitions);
        ArgumentNullException.ThrowIfNull(locales);

        if (!TextResourceDataValidation.IsCatalog(catalog))
        {
            throw new ArgumentException("The catalog identifier is not canonical.", nameof(catalog));
        }

        if (!LocaleTag.TryCanonicalize(defaultLocale, out string canonicalDefault) ||
            !string.Equals(defaultLocale, canonicalDefault, StringComparison.Ordinal))
        {
            throw new ArgumentException("The default locale must be a canonical BCP 47 tag.", nameof(defaultLocale));
        }

        if (definitions.Count is 0 or > MaximumKeys)
        {
            throw new ArgumentException($"A catalog must contain between 1 and {MaximumKeys} canonical keys.", nameof(definitions));
        }

        if (locales.Count is 0 or > MaximumLocales)
        {
            throw new ArgumentException($"A catalog must contain between 1 and {MaximumLocales} locales.", nameof(locales));
        }

        ValidatePolicies(unsupportedLocale, missingKey, nameof(unsupportedLocale), nameof(missingKey));

        _definitions = CopyDefinitions(definitions);
        if (!_definitions[0].IsCanonical)
        {
            throw new ArgumentException("A catalog must contain at least one canonical definition.", nameof(definitions));
        }

        _idByName = BuildDefinitionIndex(_definitions);
        _locales = CopyLocales(locales);
        _localeByTag = BuildLocaleStates(_locales, _definitions);

        if (!_localeByTag.TryGetValue(defaultLocale, out LocaleState? defaultState))
        {
            throw new ArgumentException("The default locale must be declared.", nameof(defaultLocale));
        }

        if (defaultState.FallbackLocale is not null)
        {
            throw new ArgumentException("The default locale cannot declare a fallback.", nameof(locales));
        }

        ValidateFallbackGraph(defaultLocale, _localeByTag, nameof(locales));
        ResolvePatterns(defaultLocale, _localeByTag, _definitions.Length);

        for (int i = 0; i < defaultState.DirectPatterns.Length; i++)
        {
            if (_definitions[i].IsCanonical && defaultState.DirectPatterns[i] is null)
            {
                throw new ArgumentException(
                    $"The default locale does not define canonical key '{_definitions[i].Name}'.",
                    nameof(locales));
            }

            else if (!_definitions[i].IsCanonical && defaultState.DirectPatterns[i] is not null)
            {
                throw new ArgumentException(
                    $"Permitted locale extra '{_definitions[i].Name}' cannot be defined by the default locale.",
                    nameof(locales));
            }

            if (!_definitions[i].IsCanonical && !HasDirectValue(_localeByTag, i))
            {
                throw new ArgumentException(
                    $"Permitted locale extra '{_definitions[i].Name}' is not directly defined by any locale.",
                    nameof(locales));
            }
        }

        Catalog = catalog;
        DefaultLocale = defaultLocale;
        UnsupportedLocale = unsupportedLocale;
        MissingKey = missingKey;
    }

    private CompiledTextResourceCatalog(
        CompiledTextResourceCatalog source,
        UnsupportedLocalePolicy unsupportedLocale,
        MissingTextResourcePolicy missingKey)
    {
        _definitions = source._definitions;
        _locales = source._locales;
        _idByName = source._idByName;
        _localeByTag = source._localeByTag;
        Catalog = source.Catalog;
        DefaultLocale = source.DefaultLocale;
        UnsupportedLocale = unsupportedLocale;
        MissingKey = missingKey;
    }

    /// <summary>The stable catalog identifier.</summary>
    public string Catalog { get; }

    /// <summary>The canonical default locale.</summary>
    public string DefaultLocale { get; }

    /// <summary>The unsupported requested-locale policy.</summary>
    public UnsupportedLocalePolicy UnsupportedLocale { get; }

    /// <summary>The missing-resource policy.</summary>
    public MissingTextResourcePolicy MissingKey { get; }

    /// <summary>The definitions in canonical ordinal key-ID order.</summary>
    public ReadOnlyMemory<CompiledTextResourceDefinition> Definitions =>
        (CompiledTextResourceDefinition[])_definitions.Clone();

    /// <summary>The declared locales in ordinal canonical-tag order.</summary>
    public ReadOnlyMemory<CompiledTextResourceLocale> Locales =>
        (CompiledTextResourceLocale[])_locales.Clone();

    /// <summary>
    /// Captures caller options into an immutable catalog view without mutating generated data.
    /// </summary>
    public CompiledTextResourceCatalog WithOptions(TextResourceOptions? options)
    {
        if (options is null)
        {
            return this;
        }

        UnsupportedLocalePolicy unsupportedLocale = options.UnsupportedLocale;
        MissingTextResourcePolicy missingKey = options.MissingKey;
        ValidatePolicies(unsupportedLocale, missingKey, nameof(options), nameof(options));
        if (unsupportedLocale == UnsupportedLocale && missingKey == MissingKey)
        {
            return this;
        }

        return new CompiledTextResourceCatalog(this, unsupportedLocale, missingKey);
    }

    internal CompiledTextResourceDefinition[] DefinitionArray => _definitions;

    internal string ResolveRequestedLocale(string requestedLocale)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestedLocale);
        if (!LocaleTag.TryCanonicalize(requestedLocale, out string canonical))
        {
            throw new ArgumentException("The requested locale is not a structurally valid BCP 47 tag.", nameof(requestedLocale));
        }

        if (_localeByTag.ContainsKey(canonical))
        {
            return canonical;
        }

        switch (UnsupportedLocale)
        {
            case UnsupportedLocalePolicy.Default:
                return DefaultLocale;
            case UnsupportedLocalePolicy.ParentsThenDefault:
                int separator = canonical.LastIndexOf('-');
                while (separator > 0)
                {
                    canonical = canonical[..separator];
                    if (_localeByTag.ContainsKey(canonical))
                    {
                        return canonical;
                    }

                    separator = canonical.LastIndexOf('-');
                }

                return DefaultLocale;
            case UnsupportedLocalePolicy.Exact:
            default:
                throw new TextResourceNotFoundException(
                    $"Locale '{canonical}' is not declared by catalog '{Catalog}'.");
        }
    }

    internal string?[] GetResolvedPatterns(string canonicalLocale) =>
        _localeByTag[canonicalLocale].ResolvedPatterns;

    internal bool TryResolveKey(TextResourceKey key, out int id)
    {
        id = key.Id;
        if (!string.Equals(key.Catalog, Catalog, StringComparison.Ordinal))
        {
            return false;
        }

        if (id == DynamicKeyId)
        {
            return key.Name is not null &&
                _idByName.TryGetValue(key.Name, out id) &&
                !_definitions[id].IsCanonical;
        }

        return id >= 0 &&
            id < _definitions.Length &&
            string.Equals(key.Name, _definitions[id].Name, StringComparison.Ordinal);
    }

    private static CompiledTextResourceDefinition[] CopyDefinitions(
        IReadOnlyList<CompiledTextResourceDefinition> definitions)
    {
        var result = new CompiledTextResourceDefinition[definitions.Count];
        string? previousCanonical = null;
        string? previousExtra = null;
        bool sawExtra = false;
        var names = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < definitions.Count; i++)
        {
            CompiledTextResourceDefinition definition = definitions[i] ??
                throw new ArgumentException("Definitions cannot contain null values.", nameof(definitions));
            if (!names.Add(definition.Name))
            {
                throw new ArgumentException(
                    "Definitions must have unique resource names.",
                    nameof(definitions));
            }

            if (definition.IsCanonical)
            {
                if (sawExtra ||
                    (previousCanonical is not null && string.CompareOrdinal(previousCanonical, definition.Name) >= 0))
                {
                    throw new ArgumentException(
                        "Canonical definitions must precede extras and be ordered by ordinal resource name.",
                        nameof(definitions));
                }

                previousCanonical = definition.Name;
            }
            else
            {
                sawExtra = true;
                if (previousExtra is not null && string.CompareOrdinal(previousExtra, definition.Name) >= 0)
                {
                    throw new ArgumentException(
                        "Permitted locale extras must be ordered by ordinal resource name.",
                        nameof(definitions));
                }

                previousExtra = definition.Name;
            }

            result[i] = definition;
        }

        return result;
    }

    private static Dictionary<string, int> BuildDefinitionIndex(
        CompiledTextResourceDefinition[] definitions)
    {
        var result = new Dictionary<string, int>(definitions.Length, StringComparer.Ordinal);
        for (int i = 0; i < definitions.Length; i++)
        {
            result.Add(definitions[i].Name, i);
        }

        return result;
    }

    private static bool HasDirectValue(Dictionary<string, LocaleState> localeByTag, int id)
    {
        foreach (KeyValuePair<string, LocaleState> pair in localeByTag)
        {
            if (pair.Value.DirectPatterns[id] is not null)
            {
                return true;
            }
        }

        return false;
    }

    private static void ValidatePolicies(
        UnsupportedLocalePolicy unsupportedLocale,
        MissingTextResourcePolicy missingKey,
        string unsupportedParameterName,
        string missingParameterName)
    {
        if (unsupportedLocale is not UnsupportedLocalePolicy.Exact and
            not UnsupportedLocalePolicy.ParentsThenDefault and
            not UnsupportedLocalePolicy.Default)
        {
            throw new ArgumentOutOfRangeException(unsupportedParameterName, unsupportedLocale, "Unknown unsupported-locale policy.");
        }

        if (missingKey is not MissingTextResourcePolicy.Throw and
            not MissingTextResourcePolicy.ReturnKey and
            not MissingTextResourcePolicy.ReturnMarker)
        {
            throw new ArgumentOutOfRangeException(missingParameterName, missingKey, "Unknown missing-key policy.");
        }
    }

    private static CompiledTextResourceLocale[] CopyLocales(IReadOnlyList<CompiledTextResourceLocale> locales)
    {
        var result = new CompiledTextResourceLocale[locales.Count];
        string? previous = null;
        for (int i = 0; i < locales.Count; i++)
        {
            CompiledTextResourceLocale locale = locales[i] ??
                throw new ArgumentException("Locales cannot contain null values.", nameof(locales));
            if (previous is not null && string.CompareOrdinal(previous, locale.Locale) >= 0)
            {
                throw new ArgumentException(
                    "Locales must be unique and ordered by ordinal canonical tag.",
                    nameof(locales));
            }

            result[i] = locale;
            previous = locale.Locale;
        }

        return result;
    }

    private static Dictionary<string, LocaleState> BuildLocaleStates(
        CompiledTextResourceLocale[] locales,
        CompiledTextResourceDefinition[] definitions)
    {
        var result = new Dictionary<string, LocaleState>(locales.Length, StringComparer.Ordinal);
        for (int i = 0; i < locales.Length; i++)
        {
            CompiledTextResourceLocale locale = locales[i];
            var directPatterns = new string?[definitions.Length];
            CompiledTextResourceValue[] values = locale.ValueArray;
            for (int valueIndex = 0; valueIndex < values.Length; valueIndex++)
            {
                CompiledTextResourceValue value = values[valueIndex];
                if (value.Id >= definitions.Length)
                {
                    throw new ArgumentException(
                        $"Locale '{locale.Locale}' contains unknown key ID {value.Id}.",
                        nameof(locales));
                }

                if (!TextResourceDataValidation.PatternMatches(
                    value.Pattern,
                    definitions[value.Id].PlaceholderArray))
                {
                    throw new ArgumentException(
                        $"Locale '{locale.Locale}' pattern for key '{definitions[value.Id].Name}' does not match its placeholder contract.",
                        nameof(locales));
                }

                directPatterns[value.Id] = value.Pattern;
            }

            result.Add(locale.Locale, new LocaleState(locale.FallbackLocale, directPatterns));
        }

        return result;
    }

    private static void ValidateFallbackGraph(
        string defaultLocale,
        Dictionary<string, LocaleState> localeByTag,
        string parameterName)
    {
        foreach (KeyValuePair<string, LocaleState> pair in localeByTag)
        {
            if (string.Equals(pair.Key, defaultLocale, StringComparison.Ordinal))
            {
                continue;
            }

            if (pair.Value.FallbackLocale is null)
            {
                throw new ArgumentException(
                    $"Non-default locale '{pair.Key}' must declare a fallback.",
                    parameterName);
            }

            var visited = new HashSet<string>(StringComparer.Ordinal) { pair.Key };
            string current = pair.Key;
            while (!string.Equals(current, defaultLocale, StringComparison.Ordinal))
            {
                string? fallback = localeByTag[current].FallbackLocale;
                if (fallback is null || !localeByTag.ContainsKey(fallback))
                {
                    throw new ArgumentException(
                        $"Fallback from locale '{current}' does not reference a declared locale.",
                        parameterName);
                }

                if (!visited.Add(fallback))
                {
                    throw new ArgumentException("The locale fallback graph contains a cycle.", parameterName);
                }

                current = fallback;
            }
        }
    }

    private static void ResolvePatterns(
        string defaultLocale,
        Dictionary<string, LocaleState> localeByTag,
        int keyCount)
    {
        foreach (KeyValuePair<string, LocaleState> pair in localeByTag)
        {
            var resolved = new string?[keyCount];
            Array.Copy(pair.Value.DirectPatterns, resolved, keyCount);

            string current = pair.Key;
            while (!string.Equals(current, defaultLocale, StringComparison.Ordinal))
            {
                string fallback = localeByTag[current].FallbackLocale!;
                string?[] fallbackPatterns = localeByTag[fallback].DirectPatterns;
                for (int i = 0; i < resolved.Length; i++)
                {
                    resolved[i] ??= fallbackPatterns[i];
                }

                current = fallback;
            }

            pair.Value.ResolvedPatterns = resolved;
        }
    }

    private sealed class LocaleState
    {
        internal LocaleState(string? fallbackLocale, string?[] directPatterns)
        {
            FallbackLocale = fallbackLocale;
            DirectPatterns = directPatterns;
            ResolvedPatterns = Array.Empty<string?>();
        }

        internal string? FallbackLocale { get; }

        internal string?[] DirectPatterns { get; }

        internal string?[] ResolvedPatterns { get; set; }
    }
}
