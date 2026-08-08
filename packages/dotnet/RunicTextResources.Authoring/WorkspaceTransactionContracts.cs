using System;
using System.Collections.Generic;
using RunicTextResources.Compiler;

namespace RunicTextResources.Authoring;

public enum TextResourceWorkspaceEditKind
{
    Create,
    Replace,
    Delete,
}

public sealed class TextResourceWorkspaceEdit
{
    private readonly byte[]? _utf8Bytes;

    internal TextResourceWorkspaceEdit(
        string relativePath,
        TextResourceWorkspaceEditKind kind,
        string? expectedRevision,
        byte[]? utf8Bytes)
    {
        RelativePath = relativePath;
        Kind = kind;
        ExpectedRevision = expectedRevision;
        _utf8Bytes = utf8Bytes;
    }

    public string RelativePath { get; }
    public TextResourceWorkspaceEditKind Kind { get; }
    public string? ExpectedRevision { get; }
    public byte[]? GetUtf8Bytes() => _utf8Bytes is null ? null : (byte[])_utf8Bytes.Clone();
    internal byte[]? Bytes => _utf8Bytes;
}

public sealed class TextResourceWorkspaceTransactionPlan
{
    internal TextResourceWorkspaceTransactionPlan(
        string root,
        string catalogId,
        IReadOnlyList<TextResourceWorkspaceEdit> edits,
        TextResourceCompilation compilation)
    {
        Root = root;
        CatalogId = catalogId;
        Edits = edits;
        Compilation = compilation;
    }

    public string Root { get; }
    public string CatalogId { get; }
    public IReadOnlyList<TextResourceWorkspaceEdit> Edits { get; }
    public TextResourceCompilation Compilation { get; }
}

public enum TextResourceWorkspaceRecoveryMode
{
    Complete,
    Rollback,
}

public sealed record TextResourcePendingTransaction(
    string Root,
    string CatalogId,
    IReadOnlyList<string> Paths);
