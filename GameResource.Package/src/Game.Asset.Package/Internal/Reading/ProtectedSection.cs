namespace Game.Asset.Package.Internal;

/// <summary>Authenticated fixed-size pages. Returned cache arrays are immutable and never pooled.</summary>
internal sealed class ProtectedSection(FileStorage file, PackageHeader header, byte[] key,
    int domain, long offset, long length, int cacheBytes)
{
    private readonly object _sync = new();
    private readonly Dictionary<long, byte[]> _cache = new();
    private readonly Queue<long> _order = new();
    private readonly int _capacity = cacheBytes / PackageHeader.PageSize;
    private byte[]? _resident;
    internal long Length => length;
    internal ReadOnlySpan<byte> Resident => _resident;

    internal async ValueTask LoadResidentAsync(int limit, bool asynchronous, CancellationToken token)
    {
        if (length > limit) return;
        byte[] bytes = new byte[(int)length];
        if (asynchronous) await ReadAsync(bytes, 0, token).ConfigureAwait(false);
        else Read(bytes, 0);
        _resident = bytes;
        lock (_sync) { _cache.Clear(); _order.Clear(); }
    }
    internal void Read(Span<byte> target, long position)
    {
        Check(position, target.Length);
        if (_resident is not null) { _resident.AsSpan((int)position, target.Length).CopyTo(target); return; }
        while (!target.IsEmpty)
        {
            long page = position / PackageHeader.PageSize;
            int within = (int)(position % PackageHeader.PageSize);
            var bytes = GetPageAsync(page, false, default).GetAwaiter().GetResult();
            int count = Math.Min(target.Length, bytes.Length - within);
            bytes.AsSpan(within, count).CopyTo(target);
            target = target[count..]; position += count;
        }
    }
    internal async ValueTask ReadAsync(Memory<byte> target, long position, CancellationToken token)
    {
        Check(position, target.Length); token.ThrowIfCancellationRequested();
        if (_resident is not null) { _resident.AsMemory((int)position, target.Length).CopyTo(target); return; }
        while (!target.IsEmpty)
        {
            long page = position / PackageHeader.PageSize;
            int within = (int)(position % PackageHeader.PageSize);
            var bytes = await GetPageAsync(page, true, token).ConfigureAwait(false);
            int count = Math.Min(target.Length, bytes.Length - within);
            bytes.AsMemory(within, count).CopyTo(target);
            target = target[count..]; position += count;
        }
    }
    private void Check(long position, int count)
    {
        if (!EntryRecord.Range(position, count, length)) throw new InvalidDataException("Index range is outside its section.");
    }
    private async ValueTask<byte[]> GetPageAsync(long page, bool asynchronous, CancellationToken token)
    {
        lock (_sync) if (_cache.TryGetValue(page, out var found)) return found;
        int count = (int)Math.Min(PackageHeader.PageSize, length - page * PackageHeader.PageSize);
        byte[] stored = new byte[count + 16], plain = new byte[count];
        long physical = checked(offset + page * (PackageHeader.PageSize + 16L));
        if (asynchronous) await file.ReadAsync(stored, physical, token).ConfigureAwait(false);
        else file.Read(stored, physical);
        using var cipher = new BlockCipher(key, header.TagId, domain);
        cipher.Decrypt(page, stored, plain, count);
        if (_capacity > 0)
        {
            lock (_sync)
            {
                if (_cache.TryGetValue(page, out var existing)) return existing;
                while (_cache.Count >= _capacity) _cache.Remove(_order.Dequeue());
                _cache.Add(page, plain); _order.Enqueue(page);
            }
        }
        return plain;
    }
    internal void Clear() { _resident = null; lock (_sync) { _cache.Clear(); _order.Clear(); } }
}
