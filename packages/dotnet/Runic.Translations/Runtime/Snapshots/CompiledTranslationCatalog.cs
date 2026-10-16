using System;
using System.Collections.Generic;
using System.Threading;

namespace Runic.Translations;

/// <summary>
/// Contains validated, immutable generated catalog data used to create locale snapshots.
/// </summary>
public sealed class CompiledTranslationCatalog
{
    /// <summary>The key-ID sentinel used for name-based lookup of permitted locale extras.</summary>
    public const int DynamicKeyId = -1;

    private const int MaximumKeys = 50_000;
    private const int MaximumLocales = 256;

    private readonly CompiledTranslationDefinition[] _definitions;
    private readonly CompiledTranslationLocale[] _locales;
    private readonly Dictionary<string, int> _idByName;
    private readonly Dictionary<string, LocaleState> _localeByTag;
    private readonly object _stateGate;

    /// <summary>Creates a validated compiled catalog from generated closed data.</summary>
    public CompiledTranslationCatalog(
        string catalog,
        string defaultLocale,
        IReadOnlyList<CompiledTranslationDefinition> definitions,
        IReadOnlyList<CompiledTranslationLocale> locales,
        UnsupportedLocalePolicy unsupportedLocale = UnsupportedLocalePolicy.ParentsThenDefault,
        MissingTranslationPolicy missingKey = MissingTranslationPolicy.Throw)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(catalog);
        ArgumentException.ThrowIfNullOrWhiteSpace(defaultLocale);
        ArgumentNullException.ThrowIfNull(definitions);
        ArgumentNullException.ThrowIfNull(locales);

        if (!TranslationDataValidation.IsCatalog(catalog))
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
        _localeByTag = CreateLocaleStates(_locales);
        _stateGate = new object();

        if (!_localeByTag.TryGetValue(defaultLocale, out LocaleState? defaultState))
        {
            throw new ArgumentException("The default locale must be declared.", nameof(defaultLocale));
        }

        if (defaultState.FallbackLocale is not null)
        {
            throw new ArgumentException("The default locale cannot declare a fallback.", nameof(locales));
        }

        ValidateFallbackGraph(defaultLocale, _localeByTag, nameof(locales));

        // Deferred locales (generated catalogs) are built and validated when a snapshot first needs them; the
        // locales given with their values are validated here, as before.
        bool deferred = false;
        foreach (CompiledTranslationLocale locale in _locales)
        {
            if (locale.IsDeferred)
            {
                deferred = true;
                continue;
            }

            BuildDirect(_localeByTag[locale.Locale], _definitions, nameof(locales));
        }

        if (!deferred)
        {
            for (int i = 0; i < _definitions.Length; i++)
            {
                if (!_definitions[i].IsCanonical && !HasDirectValue(_localeByTag, i))
                {
                    throw new ArgumentException(
                        $"Permitted locale extra '{_definitions[i].Name}' is not directly defined by any locale.",
                        nameof(locales));
                }
            }

            foreach (string tag in _localeByTag.Keys)
            {
                Resolve(tag);
            }
        }

        Catalog = catalog;
        DefaultLocale = defaultLocale;
        UnsupportedLocale = unsupportedLocale;
        MissingKey = missingKey;
    }

    private CompiledTranslationCatalog(
        CompiledTranslationCatalog source,
        UnsupportedLocalePolicy unsupportedLocale,
        MissingTranslationPolicy missingKey)
    {
        _definitions = source._definitions;
        _locales = source._locales;
        _idByName = source._idByName;
        _localeByTag = source._localeByTag;
        _stateGate = source._stateGate;
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
    public MissingTranslationPolicy MissingKey { get; }

    /// <summary>The definitions in canonical ordinal key-ID order.</summary>
    public ReadOnlyMemory<CompiledTranslationDefinition> Definitions =>
        (CompiledTranslationDefinition[])_definitions.Clone();

    /// <summary>The declared locales in ordinal canonical-tag order.</summary>
    public ReadOnlyMemory<CompiledTranslationLocale> Locales =>
        (CompiledTranslationLocale[])_locales.Clone();

    /// <summary>
    /// Captures caller options into an immutable catalog view without mutating generated data.
    /// </summary>
    public CompiledTranslationCatalog WithOptions(TranslationOptions? options)
    {
        if (options is null)
        {
            return this;
        }

        UnsupportedLocalePolicy unsupportedLocale = options.UnsupportedLocale;
        MissingTranslationPolicy missingKey = options.MissingKey;
        ValidatePolicies(unsupportedLocale, missingKey, nameof(options), nameof(options));
        if (unsupportedLocale == UnsupportedLocale && missingKey == MissingKey)
        {
            return this;
        }

        return new CompiledTranslationCatalog(this, unsupportedLocale, missingKey);
    }

    internal CompiledTranslationDefinition[] DefinitionArray => _definitions;

    internal string ResolveRequestedLocale(string requestedLocale)
    {
        ArgumentNullException.ThrowIfNull(requestedLocale);
        // An empty tag, the name of the invariant culture (for example under LANG=C), names no locale.
        // It is an unsupported locale without subtags, so the policy chooses the default locale or rejects it.
        string canonical = string.Empty;
        if (requestedLocale.Length != 0 && !LocaleTag.TryCanonicalize(requestedLocale, out canonical))
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
                throw new TranslationNotFoundException(
                    $"Locale '{canonical}' is not declared by catalog '{Catalog}'.");
        }
    }

    internal string?[] GetResolvedPatterns(string canonicalLocale) =>
        Resolve(canonicalLocale).ResolvedPatterns!;

    internal CompiledTextMessage?[] GetResolvedMessages(string canonicalLocale) =>
        Resolve(canonicalLocale).ResolvedMessages!;

    private LocaleState Resolve(string canonicalLocale)
    {
        LocaleState state = _localeByTag[canonicalLocale];
        if (Volatile.Read(ref state.ResolvedMessages) is not null)
        {
            return state;
        }

        lock (_stateGate)
        {
            if (state.ResolvedMessages is not null)
            {
                return state;
            }

            try
            {
                // The locale and its fallback chain, ending at the default locale.
                var chain = new List<LocaleState>();
                for (LocaleState? current = state; current is not null;
                     current = current.FallbackLocale is null ? null : _localeByTag[current.FallbackLocale])
                {
                    if (current.DirectMessages is null)
                    {
                        BuildDirect(current, _definitions, "locales");
                    }

                    chain.Add(current);
                }

                int keyCount = _definitions.Length;
                var resolved = new string?[keyCount];
                var resolvedMessages = new CompiledTextMessage?[keyCount];
                Array.Copy(state.DirectPatterns!, resolved, keyCount);
                Array.Copy(state.DirectMessages!, resolvedMessages, keyCount);
                for (int link = 1; link < chain.Count; link++)
                {
                    string?[] fallbackPatterns = chain[link].DirectPatterns!;
                    CompiledTextMessage?[] fallbackMessages = chain[link].DirectMessages!;
                    for (int i = 0; i < keyCount; i++)
                    {
                        resolved[i] ??= fallbackPatterns[i];
                        resolvedMessages[i] ??= fallbackMessages[i];
                    }
                }

                state.ResolvedPatterns = resolved;
                Volatile.Write(ref state.ResolvedMessages, resolvedMessages);
                return state;
            }
            catch (Exception exception) when (exception is ArgumentException or TranslationPackException && HasDeferredData(state))
            {
                throw new InvalidOperationException(
                    $"The compiled data of locale '{canonicalLocale}' in catalog '{Catalog}' is invalid: {exception.Message}",
                    exception);
            }
        }
    }

    // A locale whose chain includes deferred data reports invalid data when it is first used, not as a constructor
    // argument.
    private bool HasDeferredData(LocaleState state)
    {
        for (LocaleState? current = state; current is not null;
             current = current.FallbackLocale is null ? null : _localeByTag[current.FallbackLocale])
        {
            if (current.Deferred)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Attempts to find a message by name, such as <c>help.usage</c>.</summary>
    /// <remarks>
    /// The name is either the key name (<see cref="TranslationKey.Name"/>) or, for RMF2 catalogs, the message path with
    /// its segments joined by dots, as written in the source (<c>help.usage</c> for the key name <c>help_usage</c>).
    /// A permitted locale extra returns a key with <see cref="DynamicKeyId"/>.
    /// </remarks>
    public bool TryGetKey(string name, out TranslationKey key)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (_idByName.TryGetValue(name, out int id) ||
            (name.Contains('.', StringComparison.Ordinal) && _idByName.TryGetValue(name.Replace('.', '_'), out id)))
        {
            key = new TranslationKey(Catalog, _definitions[id].IsCanonical ? id : DynamicKeyId, _definitions[id].Name);
            return true;
        }

        key = default;
        return false;
    }

    internal bool TryResolveKey(TranslationKey key, out int id)
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

    private static CompiledTranslationDefinition[] CopyDefinitions(
        IReadOnlyList<CompiledTranslationDefinition> definitions)
    {
        var result = new CompiledTranslationDefinition[definitions.Count];
        string? previousCanonical = null;
        string? previousExtra = null;
        bool sawExtra = false;
        var names = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < definitions.Count; i++)
        {
            CompiledTranslationDefinition definition = definitions[i] ??
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
        CompiledTranslationDefinition[] definitions)
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
            if (pair.Value.DirectPatterns![id] is not null)
            {
                return true;
            }
        }

        return false;
    }

    private static void ValidatePolicies(
        UnsupportedLocalePolicy unsupportedLocale,
        MissingTranslationPolicy missingKey,
        string unsupportedParameterName,
        string missingParameterName)
    {
        if (unsupportedLocale is not UnsupportedLocalePolicy.Exact and
            not UnsupportedLocalePolicy.ParentsThenDefault and
            not UnsupportedLocalePolicy.Default)
        {
            throw new ArgumentOutOfRangeException(unsupportedParameterName, unsupportedLocale, "Unknown unsupported-locale policy.");
        }

        if (missingKey is not MissingTranslationPolicy.Throw and
            not MissingTranslationPolicy.ReturnKey and
            not MissingTranslationPolicy.ReturnMarker)
        {
            throw new ArgumentOutOfRangeException(missingParameterName, missingKey, "Unknown missing-key policy.");
        }
    }

    private static CompiledTranslationLocale[] CopyLocales(IReadOnlyList<CompiledTranslationLocale> locales)
    {
        var result = new CompiledTranslationLocale[locales.Count];
        string? previous = null;
        for (int i = 0; i < locales.Count; i++)
        {
            CompiledTranslationLocale locale = locales[i] ??
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

    private static Dictionary<string, LocaleState> CreateLocaleStates(CompiledTranslationLocale[] locales)
    {
        var result = new Dictionary<string, LocaleState>(locales.Length, StringComparer.Ordinal);
        foreach (CompiledTranslationLocale locale in locales)
        {
            result.Add(locale.Locale, new LocaleState(locale));
        }

        return result;
    }

    private static void BuildDirect(LocaleState state, CompiledTranslationDefinition[] definitions, string parameterName)
    {
        CompiledTranslationLocale locale = state.Locale;
        var directPatterns = new string?[definitions.Length];
        var directMessages = new CompiledTextMessage?[definitions.Length];
        CompiledTranslationValue[] values = locale.ValueArray;
        for (int valueIndex = 0; valueIndex < values.Length; valueIndex++)
        {
            CompiledTranslationValue value = values[valueIndex];
            if (value.Id >= definitions.Length)
            {
                throw new ArgumentException(
                    $"Locale '{locale.Locale}' contains unknown key ID {value.Id}.",
                    parameterName);
            }

            CompiledTextMessage message;
            try
            {
                message = value.Message ?? CompiledTextMessageRuntime.ParseVersion1(value.Pattern);
            }
            catch (TranslationFormatException)
            {
                throw new ArgumentException(
                    $"Locale '{locale.Locale}' pattern for key '{definitions[value.Id].Name}' is malformed.",
                    parameterName);
            }
            if (!CompiledTextMessageRuntime.MatchesContract(message, definitions[value.Id].PlaceholderArray))
            {
                throw new ArgumentException(
                    $"Locale '{locale.Locale}' pattern for key '{definitions[value.Id].Name}' does not match its placeholder contract.",
                    parameterName);
            }

            directPatterns[value.Id] = value.Pattern;
            directMessages[value.Id] = message;
        }

        // The default locale must define exactly the canonical keys. Its values are kept only once that holds, so a
        // failed deferred build is not mistaken for a valid one on the next attempt.
        if (state.FallbackLocale is null)
        {
            ValidateDefaultLocale(directPatterns, definitions, parameterName);
        }

        state.DirectPatterns = directPatterns;
        state.DirectMessages = directMessages;
    }

    private static void ValidateDefaultLocale(string?[] directPatterns, CompiledTranslationDefinition[] definitions, string parameterName)
    {
        for (int i = 0; i < directPatterns.Length; i++)
        {
            if (definitions[i].IsCanonical && directPatterns[i] is null)
            {
                throw new ArgumentException(
                    $"The default locale does not define canonical key '{definitions[i].Name}'.",
                    parameterName);
            }

            else if (!definitions[i].IsCanonical && directPatterns[i] is not null)
            {
                throw new ArgumentException(
                    $"Permitted locale extra '{definitions[i].Name}' cannot be defined by the default locale.",
                    parameterName);
            }
        }
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

    private sealed class LocaleState
    {
        internal LocaleState(CompiledTranslationLocale locale)
        {
            Locale = locale;
            Deferred = locale.IsDeferred;
        }

        internal CompiledTranslationLocale Locale { get; }

        /// <summary>Whether the locale's values were created on first use.</summary>
        internal bool Deferred { get; }

        internal string? FallbackLocale => Locale.FallbackLocale;

        internal string?[]? DirectPatterns;

        internal CompiledTextMessage?[]? DirectMessages;

        internal string?[]? ResolvedPatterns;

        internal CompiledTextMessage?[]? ResolvedMessages;
    }
}
