using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Runic.Translations.Compiler;

namespace Runic.Translations.Authoring;

/// <summary>Reuses unchanged resource syntax across independent, cancellation-scoped catalog snapshots.</summary>
public sealed class Rmf2WorkspaceCache
{
    private readonly int _capacity;
    private readonly Dictionary<string, (Rmf2ResourceDocument Document, LinkedListNode<string> Node)> _documents = new(StringComparer.Ordinal);
    private readonly LinkedList<string> _recent = new();
    /// <summary>Creates a bounded cache owned by one authoring session.</summary>
    public Rmf2WorkspaceCache(int capacity = 256)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        _capacity = capacity;
    }
    /// <summary>Creates a fresh revisioned workspace while sharing immutable syntax for unchanged source bytes.</summary>
    public Rmf2Workspace Create(string root, TranslationSource project, IEnumerable<TranslationSource> sources, CancellationToken cancellationToken = default) =>
        new(root, project, sources, this, cancellationToken);

    internal Rmf2ResourceDocument Read(TranslationSource source, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_documents)
        {
            if (_documents.TryGetValue(source.Path, out var cached))
            {
                if (cached.Document.Source.GetUtf8Bytes().SequenceEqual(source.GetUtf8Bytes()))
                {
                    _recent.Remove(cached.Node); _recent.AddLast(cached.Node);
                    return cached.Document;
                }
                _documents.Remove(source.Path); _recent.Remove(cached.Node);
            }
            var document = Rmf2ResourceReader.Read(source, cancellationToken: cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (_documents.Count == _capacity)
            {
                _documents.Remove(_recent.First!.Value); _recent.RemoveFirst();
            }
            _documents.Add(source.Path, (document, _recent.AddLast(source.Path)));
            return document;
        }
    }
}
