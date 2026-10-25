using System.Buffers;
using GameResource.Package.Internal;

namespace GameResource.Package;

/// <summary>Owns a buffered resource. Data is valid until this lease is disposed; do not dispose while using its memory.</summary>
public sealed class ResourceData<T> : IDisposable
{
    private SharedResourceBuffer? _buffer;
    private readonly byte[]? _metadataBytes;
    public ResourceInfo Info { get; }
    public bool HasMetadata => Info.HasMetadata;
    public T? Metadata { get; }
    public ReadOnlyMemory<byte> Data => (_buffer ?? throw new ObjectDisposedException(nameof(ResourceData<T>))).Memory;

    internal ResourceData(ResourceInfo info, SharedResourceBuffer buffer, byte[]? metadataBytes)
    {
        Info = info;
        Metadata = MetadataCodec.Decode<T>(metadataBytes);
        _metadataBytes = metadataBytes;
        _buffer = buffer;
    }
    internal ResourceData<T> Share()
    {
        var buffer = _buffer ?? throw new ObjectDisposedException(nameof(ResourceData<T>));
        buffer.AddReference();
        try { return new(Info, buffer, _metadataBytes); }
        catch { buffer.Release(); throw; }
    }
    public void Dispose() => Interlocked.Exchange(ref _buffer, null)?.Release();
}

internal sealed class SharedResourceBuffer(byte[] bytes, int length, bool clear)
{
    private int _references = 1;
    internal ReadOnlyMemory<byte> Memory => bytes.AsMemory(0, length);
    internal void AddReference()
    {
        while (true)
        {
            int count = Volatile.Read(ref _references);
            if (count == 0) throw new ObjectDisposedException(nameof(SharedResourceBuffer));
            if (Interlocked.CompareExchange(ref _references, checked(count + 1), count) == count) return;
        }
    }
    internal void Release()
    {
        if (Interlocked.Decrement(ref _references) == 0) ArrayPool<byte>.Shared.Return(bytes, clear);
    }
}

/// <summary>Owns metadata and an independently positioned content stream. Dispose to release the package read lease.</summary>
public sealed class ResourceStream<T> : IDisposable, IAsyncDisposable
{
    private bool _disposed;
    private readonly Stream _data;
    public ResourceInfo Info { get; }
    public bool HasMetadata => Info.HasMetadata;
    public T? Metadata { get; }
    public Stream Data { get { ObjectDisposedException.ThrowIf(_disposed, this); return _data; } }
    internal ResourceStream(ResourceInfo info, T? metadata, Stream data) { Info = info; Metadata = metadata; _data = data; }
    public void Dispose()
    {
        if (_disposed) return;
        _data.Dispose();
        _disposed = true;
    }
    public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
}
