namespace GameResource.Package.Internal;

// Cache only encoded metadata. Decoded DTOs and content buffers are owned by callers.
internal sealed class MetadataCache(int budget, int maxEntries)
{
    private readonly object _sync = new();
    private readonly Dictionary<long, LinkedListNode<(long Offset, byte[] Bytes)>> _entries = [];
    private readonly LinkedList<(long Offset, byte[] Bytes)> _lru = [];
    private long _bytes;
    internal (long Bytes, int Entries) Statistics { get { lock (_sync) return (_bytes, _entries.Count); } }
    internal byte[]? Get(long offset)
    {
        lock (_sync)
        {
            if (!_entries.TryGetValue(offset, out var node)) return null;
            _lru.Remove(node); _lru.AddFirst(node);
            return node.Value.Bytes;
        }
    }
    internal void Add(long offset, byte[] bytes)
    {
        if (bytes.Length > budget || budget == 0 || maxEntries == 0) return;
        lock (_sync)
        {
            if (_entries.ContainsKey(offset)) return;
            while (_bytes + bytes.Length > budget || _entries.Count >= maxEntries)
            {
                var last = _lru.Last!;
                _bytes -= last.Value.Bytes.Length;
                _entries.Remove(last.Value.Offset); _lru.RemoveLast();
                // Another reader may still be decoding this array. Do not clear an evicted array.
            }
            _entries.Add(offset, _lru.AddFirst((offset, bytes)));
            _bytes += bytes.Length;
        }
    }
    internal void Clear(bool clearBytes)
    {
        lock (_sync)
        {
            if (clearBytes)
                foreach (var entry in _lru) System.Security.Cryptography.CryptographicOperations.ZeroMemory(entry.Bytes);
            _entries.Clear(); _lru.Clear(); _bytes = 0;
        }
    }
}
