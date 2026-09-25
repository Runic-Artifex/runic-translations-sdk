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
    private readonly ValidationReceipt _validation;

    internal TranslationWorkspaceTransactionPlan(
        string root,
        string catalogId,
        IReadOnlyList<TranslationWorkspaceEdit> edits,
        Rmf2ProjectCompilationV5 compilation)
    {
        Root = root;
        CatalogId = catalogId;
        Edits = SnapshotEdits(edits);
        _validation = ValidationReceipt.From(compilation);
    }

    public string Root { get; }
    public string CatalogId { get; }
    public IReadOnlyList<TranslationWorkspaceEdit> Edits { get; }
    /// <summary>Whether the selected RMF2 contract validated the proposed edits.</summary>
    public bool IsValid => _validation.Success;
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
        internal static ValidationReceipt From(Rmf2ProjectCompilationV5 compilation) =>
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
