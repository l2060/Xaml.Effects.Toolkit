namespace Game.Asset.Package.Internal;

/// <summary>Exclusive leases of GCM contexts, grouped by derived key. Never shares an active context.</summary>
internal sealed class CipherPool(byte[] key, Guid tag) : IDisposable
{
    private readonly object _sync = new();
    private readonly Dictionary<long, Stack<BlockCipher>> _groups = new();
    private int _count;
    private bool _disposed;
    internal BlockCipher Rent(long sequence)
    {
        long group = sequence / 1024;
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_groups.TryGetValue(group, out var items))
            {
                var cipher = items.Pop(); _count--;
                if (items.Count == 0) _groups.Remove(group);
                return cipher;
            }
        }
        return new(key, tag, 4);
    }
    internal void Return(BlockCipher cipher)
    {
        lock (_sync)
        {
            if (!_disposed && _count < 32 && cipher.Group >= 0)
            {
                if (!_groups.TryGetValue(cipher.Group, out var items)) _groups.Add(cipher.Group, items = new());
                items.Push(cipher); _count++; return;
            }
        }
        cipher.Dispose();
    }
    public void Dispose()
    {
        lock (_sync)
        {
            _disposed = true;
            foreach (var stack in _groups.Values) foreach (var cipher in stack) cipher.Dispose();
            _groups.Clear(); _count = 0;
        }
    }
}
