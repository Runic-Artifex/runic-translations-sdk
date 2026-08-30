using System.Text.Json;

namespace Runic.Translations.Editor;

/// <summary>
/// Session-local, bounded history for editor-owned document and workflow changes.
/// Workspace catalog transactions remain recovery-owned and confirmation-bound until the
/// authoring package exposes an exact scoped-transaction adapter.
/// </summary>
internal sealed class EditorHistory
{
    private const int MaximumEntries = 64;
    private const int MaximumBytes = 32 * 1024 * 1024;
    private readonly LinkedList<Entry> _undo = [];
    private readonly LinkedList<Entry> _redo = [];
    private int _bytes;

    public EditorHistoryState State => new(
        _undo.Count > 0,
        _redo.Count > 0,
        _undo.Last?.Value.Label,
        _redo.Last?.Value.Label);

    public void Record(Entry? entry)
    {
        InvalidateRedo();
        if (entry is null || entry.Bytes > MaximumBytes) return;
        _undo.AddLast(entry);
        _bytes += entry.Bytes;
        Trim();
    }

    public void InvalidateRedo() => Clear(_redo);

    public bool TryBeginUndo(out Entry entry) => TryPeek(_undo, out entry);
    public bool TryBeginRedo(out Entry entry) => TryPeek(_redo, out entry);

    public void CompleteUndo(Entry entry)
    {
        Move(_undo, _redo, entry);
    }

    public void CompleteRedo(Entry entry)
    {
        Move(_redo, _undo, entry);
    }

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
        _bytes = 0;
    }

    private static bool TryPeek(LinkedList<Entry> source, out Entry entry)
    {
        if (source.Last is null)
        {
            entry = null!;
            return false;
        }
        entry = source.Last.Value;
        return true;
    }

    private static void Move(LinkedList<Entry> source, LinkedList<Entry> destination, Entry entry)
    {
        if (!ReferenceEquals(source.Last?.Value, entry))
            throw new InvalidOperationException("The editor history changed while an operation was running.");
        source.RemoveLast();
        destination.AddLast(entry);
    }

    private void Trim()
    {
        while (_undo.Count > MaximumEntries || _bytes > MaximumBytes)
        {
            Entry oldest = _undo.First!.Value;
            _undo.RemoveFirst();
            _bytes -= oldest.Bytes;
        }
    }

    private void Clear(LinkedList<Entry> entries)
    {
        foreach (Entry entry in entries) _bytes -= entry.Bytes;
        entries.Clear();
    }

    internal abstract class Entry
    {
        protected Entry(string label, int bytes)
        {
            Label = label;
            Bytes = bytes;
        }

        public string Label { get; }
        public int Bytes { get; }
    }

    internal sealed class SaveEntry(
        string path,
        string before,
        string after,
        string undoRevision,
        string redoRevision)
        : Entry($"Save {path}", Estimate(path, before, after, undoRevision, redoRevision))
    {
        public string Path { get; } = path;
        public string Before { get; } = before;
        public string After { get; } = after;
        public string UndoRevision { get; private set; } = undoRevision;
        public string RedoRevision { get; private set; } = redoRevision;

        public void SetUndoRevision(string revision) => UndoRevision = revision;
        public void SetRedoRevision(string revision) => RedoRevision = revision;
    }

    internal sealed class ReviewEntry(
        EditorReviewSaveRequest undo,
        EditorReviewSaveRequest redo,
        string? undoRevision,
        string? redoRevision,
        bool deleteOnUndo)
        : Entry("Save workflow", Estimate(undo, redo, undoRevision, redoRevision))
    {
        public EditorReviewSaveRequest Undo { get; } = undo;
        public EditorReviewSaveRequest Redo { get; } = redo;
        public string? UndoRevision { get; private set; } = undoRevision;
        public string? RedoRevision { get; private set; } = redoRevision;
        public bool DeleteOnUndo { get; } = deleteOnUndo;

        public void SetUndoRevision(string revision) => UndoRevision = revision;
        public void SetRedoRevision(string? revision) => RedoRevision = revision;
    }

    private static int Estimate(params object?[] values)
    {
        try
        {
            long bytes = JsonSerializer.SerializeToUtf8Bytes(values).LongLength;
            return bytes > int.MaxValue ? int.MaxValue : (int)bytes;
        }
        catch (NotSupportedException)
        {
            return int.MaxValue;
        }
    }
}
