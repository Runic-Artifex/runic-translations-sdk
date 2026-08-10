using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace RunicTranslations;

/// <summary>
/// Composes fully verified caller-supplied locale packs over immutable compiled fallback data.
/// </summary>
public sealed class ExternalTextResourceSnapshotFactory : ITextResourceSnapshotFactory
{
    private readonly IExternalTextResourceSource _source;
    private readonly string _catalog;
    private readonly string _contractFingerprint;
    private readonly Func<string, TextResourcePackContract> _contractFactory;
    private readonly TextResourcePackLimits? _limits;
    private readonly TextResourcePackIntegrityVerifier? _integrityVerifier;

    /// <summary>Creates a generated-contract-bound external pack composition factory.</summary>
    public ExternalTextResourceSnapshotFactory(
        IExternalTextResourceSource source,
        string catalog,
        string contractFingerprint,
        Func<string, TextResourcePackContract> contractFactory,
        TextResourcePackLimits? limits = null,
        TextResourcePackIntegrityVerifier? integrityVerifier = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(contractFactory);
        if (!TextResourceDataValidation.IsCatalog(catalog))
        {
            throw new ArgumentException("The expected catalog identifier is not canonical.", nameof(catalog));
        }

        if (!TextResourcePackValidation.IsFingerprint(contractFingerprint))
        {
            throw new ArgumentException(
                "The expected contract fingerprint must be lowercase sha256 hexadecimal text.",
                nameof(contractFingerprint));
        }

        _source = source;
        _catalog = catalog;
        _contractFingerprint = contractFingerprint;
        _contractFactory = contractFactory;
        _limits = limits;
        _integrityVerifier = integrityVerifier;
    }

    /// <inheritdoc />
    public async ValueTask<ITextResourceSnapshot> CreateSnapshotAsync(
        CompiledTextResourceCatalog catalog,
        string canonicalLocale,
        ITextValueFormatter valueFormatter,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(valueFormatter);
        cancellationToken.ThrowIfCancellationRequested();

        if (!string.Equals(catalog.Catalog, _catalog, StringComparison.Ordinal))
        {
            throw PackError(
                "The compiled catalog does not match the external pack factory.",
                TextResourcePackFailureReason.CatalogMismatch);
        }

        TextResourcePackContract contract = CreateContract(canonicalLocale, cancellationToken);
        ValidateContract(catalog, canonicalLocale, contract);

        VerifiedExternalTextResourcePack? verified = await TextResourcePackLoader.LoadAsync(
            _source,
            contract,
            _limits,
            _integrityVerifier,
            cancellationToken).ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();
        if (verified is null)
        {
            return new CompiledTextResourceSnapshot(catalog, canonicalLocale, valueFormatter);
        }

        ValidateVerifiedIdentity(canonicalLocale, verified);
        CompiledTextResourceValue[] replacements = CreateReplacements(catalog, verified);
        return new CompiledTextResourceSnapshot(
            catalog,
            canonicalLocale,
            replacements,
            valueFormatter);
    }

    private TextResourcePackContract CreateContract(
        string canonicalLocale,
        CancellationToken cancellationToken)
    {
        try
        {
            TextResourcePackContract contract = _contractFactory(canonicalLocale);
            return contract ?? throw PackError("The generated external pack contract factory returned null.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (TextResourcePackException)
        {
            throw;
        }
        catch (Exception)
        {
            throw PackError("The generated external pack contract factory failed.");
        }
    }

    private void ValidateContract(
        CompiledTextResourceCatalog catalog,
        string canonicalLocale,
        TextResourcePackContract contract)
    {
        if (!string.Equals(contract.Catalog, _catalog, StringComparison.Ordinal))
        {
            throw PackError(
                "The generated external pack contract catalog is incompatible.",
                TextResourcePackFailureReason.CatalogMismatch);
        }

        if (!string.Equals(contract.Locale, canonicalLocale, StringComparison.Ordinal))
        {
            throw PackError(
                "The generated external pack contract locale is incompatible.",
                TextResourcePackFailureReason.LocaleMismatch);
        }

        if (!string.Equals(contract.ContractFingerprint, _contractFingerprint, StringComparison.Ordinal))
        {
            throw PackError(
                "The generated external pack contract fingerprint is incompatible.",
                TextResourcePackFailureReason.ContractFingerprintMismatch);
        }

        CompiledTextResourceDefinition[] definitions = catalog.DefinitionArray;
        string?[] resolvedPatterns = catalog.GetResolvedPatterns(canonicalLocale);
        var seen = new bool[definitions.Length];
        IReadOnlyList<TextResourcePackMessageContract> messages = contract.Messages;
        for (int i = 0; i < messages.Count; i++)
        {
            TextResourcePackMessageContract message = messages[i];
            int id = message.Key.Id;
            if (id < 0 || id >= definitions.Length || seen[id])
            {
                throw PackError("The generated external pack contract contains an invalid key identity.");
            }

            CompiledTextResourceDefinition definition = definitions[id];
            if (!string.Equals(message.Key.Catalog, _catalog, StringComparison.Ordinal) ||
                !string.Equals(message.Key.Name, definition.Name, StringComparison.Ordinal) ||
                (!definition.IsCanonical && resolvedPatterns[id] is null) ||
                !ArgumentsMatch(definition.PlaceholderArray, message.Arguments))
            {
                throw PackError("The generated external pack contract does not match compiled catalog data.");
            }

            seen[id] = true;
        }

        for (int id = 0; id < definitions.Length; id++)
        {
            bool expected = definitions[id].IsCanonical || resolvedPatterns[id] is not null;
            if (seen[id] != expected)
            {
                throw PackError("The generated external pack contract does not contain the exact resolved key set.");
            }
        }
    }

    private static bool ArgumentsMatch(
        TextResourcePlaceholderDescriptor[] expected,
        IReadOnlyList<TextResourcePackArgumentContract> actual)
    {
        if (expected.Length != actual.Count)
        {
            return false;
        }

        for (int i = 0; i < expected.Length; i++)
        {
            TextResourcePlaceholderDescriptor descriptor = expected[i];
            TextResourcePackArgumentContract argument = actual[i];
            if (!string.Equals(descriptor.Name, argument.Name, StringComparison.Ordinal) ||
                descriptor.Type != argument.Type ||
                descriptor.Format != argument.Format)
            {
                return false;
            }
        }

        return true;
    }

    private void ValidateVerifiedIdentity(
        string canonicalLocale,
        VerifiedExternalTextResourcePack verified)
    {
        if (!string.Equals(verified.Catalog, _catalog, StringComparison.Ordinal))
        {
            throw PackError(
                "The verified external pack catalog changed during composition.",
                TextResourcePackFailureReason.CatalogMismatch);
        }

        if (!string.Equals(verified.Locale, canonicalLocale, StringComparison.Ordinal))
        {
            throw PackError(
                "The verified external pack locale changed during composition.",
                TextResourcePackFailureReason.LocaleMismatch);
        }

        if (!string.Equals(verified.ContractFingerprint, _contractFingerprint, StringComparison.Ordinal))
        {
            throw PackError(
                "The verified external pack fingerprint changed during composition.",
                TextResourcePackFailureReason.ContractFingerprintMismatch);
        }
    }

    private static CompiledTextResourceValue[] CreateReplacements(
        CompiledTextResourceCatalog catalog,
        VerifiedExternalTextResourcePack verified)
    {
        IReadOnlyList<VerifiedTextResourcePackMessage> messages = verified.Messages;
        var replacements = new CompiledTextResourceValue[messages.Count];
        CompiledTextResourceDefinition[] definitions = catalog.DefinitionArray;
        for (int i = 0; i < messages.Count; i++)
        {
            VerifiedTextResourcePackMessage message = messages[i];
            int id = message.Key.Id;
            if (id < 0 || id >= definitions.Length ||
                !string.Equals(message.Key.Catalog, catalog.Catalog, StringComparison.Ordinal) ||
                !string.Equals(message.Key.Name, definitions[id].Name, StringComparison.Ordinal))
            {
                throw PackError("A verified external pack message has an invalid compiled key identity.");
            }

            replacements[i] = message.Message is null
                ? new CompiledTextResourceValue(id, message.Pattern)
                : new CompiledTextResourceValue(id, message.Pattern, message.Message);
        }

        Array.Sort(replacements, static (left, right) => left.Id.CompareTo(right.Id));
        for (int i = 1; i < replacements.Length; i++)
        {
            if (replacements[i - 1].Id == replacements[i].Id)
            {
                throw PackError("A verified external pack contains a duplicate compiled key identity.");
            }
        }

        return replacements;
    }

    private static TextResourcePackException PackError(
        string message,
        TextResourcePackFailureReason reason = TextResourcePackFailureReason.ArgumentContractMismatch) =>
        TextResourcePackFailure.Create(message, reason);
}
