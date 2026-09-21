using System;
using System.Collections.Generic;
using System.Linq;

namespace Runic.Translations.Authoring;

/// <summary>Stable, machine-readable details for information that cannot be carried across a source migration.</summary>
public sealed record TranslationMigrationLoss(
    string Code,
    string Location,
    string Message,
    bool SemanticLoss = false);

/// <summary>Structured report for a translation source migration.</summary>
public sealed class TranslationMigrationReport
{
    internal TranslationMigrationReport(IEnumerable<TranslationMigrationLoss> losses)
    {
        ArgumentNullException.ThrowIfNull(losses);
        Losses = losses.ToArray();
    }

    /// <summary>Losses are sorted by source location and stable code.</summary>
    public IReadOnlyList<TranslationMigrationLoss> Losses { get; }

    /// <summary>Whether migration dropped or could not preserve source information.</summary>
    public bool HasLosses => Losses.Count != 0;

    /// <summary>Legacy human-readable projection retained for existing callers.</summary>
    public IReadOnlyList<string> Notes => Losses.Select(static loss => loss.Message).ToArray();
}
