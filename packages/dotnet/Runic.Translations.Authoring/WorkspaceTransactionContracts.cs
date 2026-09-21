using System;
using System.Collections.Generic;
using Runic.Translations.Compiler;

namespace Runic.Translations.Authoring;

public enum TranslationWorkspaceEditKind
{
    Create,
    Replace,
    Delete,
}

public sealed class TranslationWorkspaceEdit
{
    private readonly byte[]? _utf8Bytes;

    internal TranslationWorkspaceEdit(
        string relativePath,
        TranslationWorkspaceEditKind kind,
        string? expectedRevision,
        byte[]? utf8Bytes)
    {
        RelativePath = relativePath;
        Kind = kind;
        ExpectedRevision = expectedRevision;
        _utf8Bytes = utf8Bytes;
    }

    public string RelativePath { get; }
    public TranslationWorkspaceEditKind Kind { get; }
    public string? ExpectedRevision { get; }
    public byte[]? GetUtf8Bytes() => _utf8Bytes is null ? null : (byte[])_utf8Bytes.Clone();
    internal byte[]? Bytes => _utf8Bytes;
}

public sealed class TranslationWorkspaceTransactionPlan
{
    private readonly TranslationCompilation? _compilation;
    private readonly ValidationReceipt _validation;

    internal TranslationWorkspaceTransactionPlan(
        string root,
        string catalogId,
        IReadOnlyList<TranslationWorkspaceEdit> edits,
        TranslationCompilation compilation)
    {
        Root = root;
        CatalogId = catalogId;
        Edits = SnapshotEdits(edits);
        _compilation = compilation;
        _validation = ValidationReceipt.From(compilation);
    }

    internal TranslationWorkspaceTransactionPlan(
        string root,
        string catalogId,
        IReadOnlyList<TranslationWorkspaceEdit> edits,
        TranslationProfileCompilation compilation)
    {
        Root = root;
        CatalogId = catalogId;
        Edits = SnapshotEdits(edits);
        _compilation = compilation.Current;
        _validation = ValidationReceipt.From(compilation);
    }

    public string Root { get; }
    public string CatalogId { get; }
    public IReadOnlyList<TranslationWorkspaceEdit> Edits { get; }
    /// <summary>The exact v4 compilation that validated the plan.</summary>
    /// <exception cref="InvalidOperationException">The selected profile has no v4 compilation carrier.</exception>
    public TranslationCompilation Compilation => _compilation ?? throw new InvalidOperationException(
        "Compilation is unavailable because this plan was validated as rmf2-execution-v2 rather than the v4 compiler profile.");
    internal bool IsCompilerValid => _validation.Success;

    private static System.Collections.ObjectModel.ReadOnlyCollection<TranslationWorkspaceEdit> SnapshotEdits(IReadOnlyList<TranslationWorkspaceEdit> edits)
    {
        ArgumentNullException.ThrowIfNull(edits);
        var snapshot = new TranslationWorkspaceEdit[edits.Count];
        for (int index = 0; index < edits.Count; index++)
            snapshot[index] = edits[index] ?? throw new ArgumentException("A workspace transaction edit cannot be null.", nameof(edits));
        return Array.AsReadOnly(snapshot);
    }

    private readonly record struct ValidationReceipt
    {
        private ValidationReceipt(bool success) => Success = success;
        internal bool Success { get; }
        internal static ValidationReceipt From(TranslationCompilation compilation) =>
            new((compilation ?? throw new ArgumentNullException(nameof(compilation))).Success);
        internal static ValidationReceipt From(TranslationProfileCompilation compilation) =>
            new((compilation ?? throw new ArgumentNullException(nameof(compilation))).Success);
    }
}

public enum TranslationWorkspaceRecoveryMode
{
    Complete,
    Rollback,
}

public sealed record TranslationPendingTransaction(
    string Root,
    string CatalogId,
    IReadOnlyList<string> Paths);
