namespace GameResource.Package;

/// <summary>
/// A path reference with no I/O or retained resource buffer at creation. Overlapping LoadAsync calls
/// share one read, with independent result leases and metadata objects. Completed loads are not cached.
/// </summary>
public sealed class ResourceReference<T>
{
    private readonly ResourcePackage _package;
    private readonly object _sync = new();
    private Pending? _pending;
    public string Path { get; }
    public ResourceInfo Info => _package.GetInfo(Path);
    internal ResourceReference(ResourcePackage package, string path) { _package = package; Path = path; }
    public T? GetMetadata() => _package.ReadMetadata<T>(Path);
    public ValueTask<T?> GetMetadataAsync(CancellationToken cancellationToken = default) => _package.ReadMetadataAsync<T>(Path, cancellationToken);
    public ResourceData<T> Load(CancellationToken cancellationToken = default) => _package.ReadResource<T>(Path, cancellationToken);
    public ResourceStream<T> Open() => _package.OpenResource<T>(Path);
    public ValueTask<ResourceStream<T>> OpenAsync(CancellationToken cancellationToken = default) => _package.OpenResourceAsync<T>(Path, cancellationToken);

    public async ValueTask<ResourceData<T>> LoadAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Pending pending;
        lock (_sync)
        {
            if (_pending is null || _pending.Revision != _package.Revision || _pending.Completed)
            {
                pending = new(_package.Revision);
                pending.Waiters = 1;
                _pending = pending;
                pending.Task = _package.ReadResourceAsync<T>(Path, pending.Cancellation.Token).AsTask();
                _ = ObserveCompletionAsync(pending);
            }
            else { pending = _pending; pending.Waiters++; }
        }
        try
        {
            var result = await pending.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            // This waiter keeps the initial result alive until its own lease has been acquired.
            return result.Share();
        }
        finally
        {
            bool drain = false;
            lock (_sync)
            {
                if (--pending.Waiters == 0)
                {
                    if (ReferenceEquals(_pending, pending)) _pending = null;
                    if (pending.Completed) pending.Dispose();
                    else { pending.Cancellation.Cancel(); drain = true; }
                }
            }
            if (drain)
            {
                // The last canceled waiter waits for OS I/O to finish before releasing the package lease.
                try { await pending.Task.ConfigureAwait(false); }
                catch { }
            }
        }
    }

    private async Task ObserveCompletionAsync(Pending pending)
    {
        try { await pending.Task.ConfigureAwait(false); }
        catch { /* Every waiter observes the failure. Also observe faults when all waiters cancel. */ }
        lock (_sync)
        {
            pending.Completed = true;
            if (pending.Waiters == 0) pending.Dispose();
        }
    }
    private sealed class Pending(long revision) : IDisposable
    {
        internal readonly long Revision = revision;
        internal readonly CancellationTokenSource Cancellation = new();
        internal Task<ResourceData<T>> Task = null!;
        internal int Waiters;
        internal bool Completed;
        public void Dispose()
        {
            if (Task.IsCompletedSuccessfully) Task.Result.Dispose();
            Cancellation.Dispose();
        }
    }
}
