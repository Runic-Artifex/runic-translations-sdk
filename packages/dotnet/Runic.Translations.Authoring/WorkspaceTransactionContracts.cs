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
    internal TranslationWorkspaceTransactionPlan(
        string root,
        string catalogId,
        IReadOnlyList<TranslationWorkspaceEdit> edits,
        TranslationCompilation compilation)
    {
        Root = root;
        CatalogId = catalogId;
        Edits = edits;
        Compilation = compilation;
    }

    public string Root { get; }
    public string CatalogId { get; }
    public IReadOnlyList<TranslationWorkspaceEdit> Edits { get; }
    public TranslationCompilation Compilation { get; }
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
