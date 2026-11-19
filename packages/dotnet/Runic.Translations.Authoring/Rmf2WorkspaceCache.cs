using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Runic.Translations.Compiler;

namespace Runic.Translations.Authoring;

/// <summary>Reuses unchanged resource syntax, and compile work, across independent, cancellation-scoped catalog snapshots.</summary>
/// <remarks>
/// A grouped <c>.rmf2</c> file is read once for the workspace's syntax and its project's validation session
/// (<see cref="TranslationValidationSession"/>), which <see cref="Rmf2Workspace.Validate"/> uses. Sessions are kept for the
/// most recently used projects, at most <c>projects</c>.
/// </remarks>
internal sealed class Rmf2WorkspaceCache
{
    private readonly int _capacity;
    private readonly int _projects;
    // The most recently read documents, at most capacity; a grouped file's document is its unit in the project's session
    // (Project), which loses the unit when the document leaves the cache.
    private readonly Dictionary<string, (Rmf2ResourceDocument Document, LinkedListNode<string> Node, string? Project)> _documents = new(StringComparer.Ordinal);
    private readonly LinkedList<string> _recent = new();
    private readonly Dictionary<string, (TranslationValidationSession Session, LinkedListNode<string> Node)> _sessions = new(StringComparer.Ordinal);
    private readonly LinkedList<string> _recentSessions = new();
    /// <summary>Creates a bounded cache owned by one authoring session.</summary>
    public Rmf2WorkspaceCache(int capacity = 256, int projects = 8)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(projects, 1);
        _capacity = capacity;
        _projects = projects;
    }
    /// <summary>Creates a fresh revisioned workspace while sharing immutable syntax for unchanged source bytes.</summary>
    public Rmf2Workspace Create(string root, TranslationSource project, IEnumerable<TranslationSource> sources, CancellationToken cancellationToken = default) =>
        new(root, project, sources, this, cancellationToken);

    /// <summary>The validation session of the project whose runic.json is at <paramref name="projectPath"/>.</summary>
    internal TranslationValidationSession Session(string projectPath)
    {
        lock (_sessions)
        {
            if (_sessions.TryGetValue(projectPath, out var cached))
            {
                _recentSessions.Remove(cached.Node); _recentSessions.AddLast(cached.Node);
                return cached.Session;
            }
            if (_sessions.Count == _projects)
            {
                _sessions.Remove(_recentSessions.First!.Value); _recentSessions.RemoveFirst();
            }
            var session = new TranslationValidationSession();
            _sessions.Add(projectPath, (session, _recentSessions.AddLast(projectPath)));
            return session;
        }
    }

    internal Rmf2ResourceDocument Read(TranslationSource source, CancellationToken cancellationToken) => Read(null, source, cancellationToken);

    /// <param name="projectPath">The project's runic.json: a grouped file is read as its validation session's unit, one parse for both.</param>
    internal Rmf2ResourceDocument Read(string? projectPath, TranslationSource source, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string? project = projectPath is not null && source.Path.EndsWith(".rmf2", StringComparison.OrdinalIgnoreCase) ? projectPath : null;
        lock (_documents)
        {
            if (_documents.TryGetValue(source.Path, out var cached) && cached.Document.Source.Bytes.AsSpan().SequenceEqual(source.Bytes))
            {
                _recent.Remove(cached.Node); _recent.AddLast(cached.Node);
                return cached.Document;
            }
        }
        // Read outside the lock, so a workspace's files are read in parallel.
        var document = project is not null ? Session(project).Cache.Unit(source, cancellationToken).Resource!
            : source.Path.EndsWith(".mf2", StringComparison.OrdinalIgnoreCase)
            ? Rmf2ResourceReader.ReadDirect(source, cancellationToken: cancellationToken)
            : Rmf2ResourceReader.Read(source, cancellationToken: cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_documents)
        {
            if (_documents.TryGetValue(source.Path, out var cached))
            {
                // Another reader added this path meanwhile: keep the document of these bytes.
                _documents.Remove(source.Path); _recent.Remove(cached.Node);
            }
            if (_documents.Count == _capacity)
            {
                string evicted = _recent.First!.Value;
                if (_documents[evicted].Project is { } owner) Session(owner).Cache.Forget(evicted);
                _documents.Remove(evicted); _recent.RemoveFirst();
            }
            _documents.Add(source.Path, (document, _recent.AddLast(source.Path), project));
        }
        return document;
    }
}
