using System;
using System.Threading;
using System.Threading.Tasks;

namespace Runic.Translations;

/// <summary>Verifies and parses caller-supplied RMF2 v5 external translation pack bytes.</summary>
public static class TranslationPackLoader
{
    /// <summary>
    /// Requests bytes from an explicit caller source and verifies them when a pack is available.
    /// The runtime itself performs no file or network discovery.
    /// </summary>
    public static async ValueTask<VerifiedExternalTranslationPack?> LoadAsync(
        IExternalTranslationSource source,
        TranslationPackContract contract,
        TranslationPackLimits? limits = null,
        TranslationPackIntegrityVerifier? integrityVerifier = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(contract);
        cancellationToken.ThrowIfCancellationRequested();

        ExternalTranslationPack? pack;
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
            throw PackError("The external pack source failed to load a pack.", TranslationPackFailureReason.SourceFailure);
        }

        if (pack is null) return null;
        return await VerifyAsync(pack, contract, limits, integrityVerifier, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Runs optional integrity verification, then fully validates an RMF2 v5 external pack without
    /// performing any file or network access.
    /// </summary>
    public static async ValueTask<VerifiedExternalTranslationPack> VerifyAsync(
        ExternalTranslationPack pack,
        TranslationPackContract contract,
        TranslationPackLimits? limits = null,
        TranslationPackIntegrityVerifier? integrityVerifier = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pack);
        ArgumentNullException.ThrowIfNull(contract);
        limits ??= new TranslationPackLimits();
        cancellationToken.ThrowIfCancellationRequested();

        if (contract.MessageGrammarVersion != 5 ||
            !string.Equals(contract.Profile, "rmf2-execution-v2", StringComparison.Ordinal) ||
            contract.Rmf2MarkupContract is null)
        {
            throw PackError("The external pack contract does not select the supported RMF2 v5 execution profile.",
                TranslationPackFailureReason.MessageGrammarVersionMismatch);
        }

        ReadOnlyMemory<byte> callerContent = pack.Content;
        if (callerContent.Length == 0) throw PackError("The external pack is empty.");
        if (callerContent.Length > limits.MaximumDocumentBytes) throw LimitError("The external pack exceeds the configured document limit.");

        // ExternalTranslationPack deliberately accepts caller-owned memory. Take one bounded
        // snapshot before the first await. Parsing uses this exact image; integrity receives a
        // defensive equal-value copy, so neither caller nor verifier mutation can create a
        // verification/parsing TOCTOU.
        byte[] ownedContent = callerContent.ToArray();
        ReadOnlyMemory<byte> verifiedContent = ownedContent;

        if (integrityVerifier is not null)
        {
            bool accepted;
            try
            {
                // ReadOnlyMemory does not make its backing array immutable to unsafe or
                // interop callers. Give the verifier an independent image so parsing is
                // always bound to the exact snapshot it verified by policy.
                accepted = await integrityVerifier(verifiedContent.ToArray(), cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                throw PackError("External pack integrity verification failed.", TranslationPackFailureReason.IntegrityRejected);
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (!accepted) throw PackError("The external pack was rejected by the integrity policy.", TranslationPackFailureReason.IntegrityRejected);
        }

        return TranslationPackV5Loader.Parse(ownedContent, contract, limits, cancellationToken);
    }

    private static TranslationPackException PackError(string message, TranslationPackFailureReason reason = TranslationPackFailureReason.Malformed) =>
        TranslationPackFailure.Create(message, reason);

    private static TranslationPackException LimitError(string message) =>
        TranslationPackFailure.Create(message, TranslationPackFailureReason.LimitExceeded);
}
