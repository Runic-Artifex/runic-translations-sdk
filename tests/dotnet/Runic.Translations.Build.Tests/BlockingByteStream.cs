using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;

namespace Runic.Translations.Build.Tests;

// An in-process LSP transport: the server reads and writes one of these, a test the other end (Rmf2DiagnosticLspTests.Session).
internal sealed class BlockingByteStream : Stream
{
    private readonly BlockingCollection<byte[]> _chunks = new();
    private byte[]? _current;
    private int _offset;

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

    internal void CompleteWriting()
    {
        if (!_chunks.IsAddingCompleted) _chunks.CompleteAdding();
    }

    public override void Flush() { }

    public override int Read(byte[] buffer, int offset, int count)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        if (offset < 0 || count < 0 || offset + count > buffer.Length) throw new ArgumentOutOfRangeException(nameof(count), "Offset and count must identify a range within the buffer.");
        while (_current is null || _offset == _current.Length)
        {
            if (!_chunks.TryTake(out _current, Timeout.Infinite)) return 0;
            _offset = 0;
        }
        int copied = Math.Min(count, _current.Length - _offset);
        Array.Copy(_current, _offset, buffer, offset, copied);
        _offset += copied;
        return copied;
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        if (offset < 0 || count < 0 || offset + count > buffer.Length) throw new ArgumentOutOfRangeException(nameof(count), "Offset and count must identify a range within the buffer.");
        byte[] copy = new byte[count];
        Array.Copy(buffer, offset, copy, 0, count);
        _chunks.Add(copy);
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            CompleteWriting();
            _chunks.Dispose();
        }
        base.Dispose(disposing);
    }
}
