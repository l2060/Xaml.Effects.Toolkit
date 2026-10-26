using System.Buffers;
using System.Security.Cryptography;

namespace Game.Asset.Package;

/// <summary>Owns the content buffer. Data and its slices are valid until Dispose.</summary>
public sealed class AssetData : IDisposable
{
    private byte[]? _bytes;
    private readonly int _length;
    private readonly bool _clear;
    public AssetInfo Info { get; }
    public AssetMetadata Metadata { get; }
    public ReadOnlyMemory<byte> Data => (_bytes ?? throw new ObjectDisposedException(nameof(AssetData))).AsMemory(0, _length);

    internal AssetData(AssetInfo info, AssetMetadata metadata, byte[] bytes, int length, bool clear)
    { Info = info; Metadata = metadata; _bytes = bytes; _length = length; _clear = clear; }

    public void Dispose()
    {
        var bytes = Interlocked.Exchange(ref _bytes, null);
        if (bytes is null) return;
        if (_clear) CryptographicOperations.ZeroMemory(bytes.AsSpan(0, _length));
        ArrayPool<byte>.Shared.Return(bytes);
    }
}
