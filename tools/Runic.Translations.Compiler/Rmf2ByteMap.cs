using System;
using System.Collections;
using System.Collections.Generic;

namespace Runic.Translations.Compiler;

/// <summary>
/// The map from every UTF-8 boundary of an extracted message to its byte offset in the resource file
/// (<see cref="Rmf2ResourceNode.MessageByteMap"/>), stored as runs of consecutive offsets: an extracted line maps to one
/// run, so a message takes a few integers per line instead of one per byte.
/// </summary>
internal sealed class Rmf2ByteMap : IReadOnlyList<int>
{
    // The runs, or null for a map of one run (a one-line message), whose first offset is _first.
    private readonly int[]? _starts;
    private readonly int[]? _values;
    private readonly int _first;

    private Rmf2ByteMap(int[] starts, int[] values, int count)
    {
        Count = count;
        if (starts.Length == 1 && starts[0] == 0) _first = values[0];
        else { _starts = starts; _values = values; }
    }

    /// <summary>The map of a source that is its own message: entry <c>i</c> is <c>i</c>.</summary>
    internal static Rmf2ByteMap Identity(int count) => count == 0 ? new([], [], 0) : new([0], [0], count);

    public int Count { get; }

    public int this[int index]
    {
        get
        {
            if ((uint)index >= (uint)Count) throw new ArgumentOutOfRangeException(nameof(index));
            if (_starts is null) return _first + index;
            int run = Array.BinarySearch(_starts, index);
            if (run < 0) run = ~run - 1;
            return _values![run] + (index - _starts[run]);
        }
    }

    public IEnumerator<int> GetEnumerator()
    {
        for (int index = 0; index < Count; index++) yield return this[index];
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>Appends offsets, merging each into the previous run when it continues it. Reusable after <see cref="Build"/>.</summary>
    internal sealed class Builder
    {
        private readonly List<int> _starts = new();
        private readonly List<int> _values = new();

        internal int Count { get; private set; }

        internal void Clear() { _starts.Clear(); _values.Clear(); Count = 0; }

        internal void Add(int value) => AddRange(value, value + 1);

        /// <summary>Appends the offsets <paramref name="from"/> up to, not including, <paramref name="to"/>.</summary>
        internal void AddRange(int from, int to)
        {
            if (to <= from) return;
            int last = _starts.Count - 1;
            if (last < 0 || _values[last] + (Count - _starts[last]) != from) { _starts.Add(Count); _values.Add(from); }
            Count += to - from;
        }

        internal Rmf2ByteMap Build() => new(_starts.ToArray(), _values.ToArray(), Count);
    }
}
