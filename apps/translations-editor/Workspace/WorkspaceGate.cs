namespace Runic.Translations.Editor;

/// <summary>
/// The one lock of an <see cref="EditorWorkspace"/> and its disposed state. Every workspace service takes this same gate
/// around each of its operations, so at most one workspace operation runs at a time, as when they were one class. A
/// service never calls another service's gated operation while it holds the gate, only ungated members.
/// </summary>
internal sealed class WorkspaceGate : IDisposable
{
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    internal bool Disposed { get; private set; }

    internal Task WaitAsync(CancellationToken cancellationToken) => _semaphore.WaitAsync(cancellationToken);

    internal void Wait() => _semaphore.Wait();

    internal void Release() => _semaphore.Release();

    /// <summary>Throws as the workspace did: an <see cref="ObjectDisposedException"/> naming <see cref="EditorWorkspace"/>.</summary>
    internal void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(Disposed, typeof(EditorWorkspace));

    /// <summary>Marks the workspace disposed; the lock itself is disposed by <see cref="Dispose"/>.</summary>
    internal void MarkDisposed() => Disposed = true;

    public void Dispose() => _semaphore.Dispose();
}
