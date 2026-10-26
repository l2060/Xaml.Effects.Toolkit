using System.Buffers.Binary;
using System.Security.Cryptography;

namespace Game.Asset.Package.Internal;

internal sealed class PackageReader : IDisposable
{
    internal FileStorage File { get; }
    internal PackageHeader Header { get; }
    internal AssetPackageInfo Info { get; }
    internal byte[] Key { get; }
    internal ProtectedSection Directory { get; }
    internal ProtectedSection Metadata { get; }
    internal ProtectedSection Blocks { get; }
    internal CipherPool Ciphers { get; }

    private PackageReader(FileStorage file, PackageHeader h, AssetPackageInfo info, byte[] key, AssetReadOptions options)
    {
        File = file; Header = h; Info = info; Key = key;
        Ciphers = new(key, h.TagId);
        Directory = new(file, h, key, 1, h.DirectoryOffset, h.DirectoryLength, options.PageCacheBytes / 3);
        Metadata = new(file, h, key, 2, h.MetadataOffset, h.MetadataLength, options.PageCacheBytes / 3);
        Blocks = new(file, h, key, 3, h.BlocksOffset, h.BlocksLength, options.PageCacheBytes / 3);
    }

    internal static async ValueTask<PackageReader> OpenAsync(string path, AssetReadOptions options, bool asynchronous, CancellationToken token)
    {
        FileStorage? file = null; PackageReader? reader = null; byte[]? key = null;
        try
        {
            token.ThrowIfCancellationRequested();
            file = new(path, options.ReadMode == AssetReadMode.MemoryMapped);
            byte[] bytes = new byte[PackageHeader.Size];
            if (asynchronous) await file.ReadAsync(bytes, 0, token).ConfigureAwait(false); else file.Read(bytes, 0);
            var h = PackageHeader.Decode(bytes, file.Length);
            var info = new AssetPackageInfo(path, h.TagId, new DateTimeOffset(h.BuildTicks, TimeSpan.Zero), h.EntryCount, h.Encrypted);
            key = BlockCipher.DeriveKey(h, h.Encrypted ? options.PasswordProvider?.Invoke(info) ?? options.Password : null);
            byte[] mac = HMACSHA256.HashData(key, bytes.AsSpan(0, 128));
            if (!CryptographicOperations.FixedTimeEquals(mac, bytes.AsSpan(128, 32)))
                throw new CryptographicException("Incorrect password or unauthenticated package header.");
            reader = new(file, h, info, key, options);
            await reader.Directory.LoadResidentAsync(options.MaxResidentIndexBytes, asynchronous, token).ConfigureAwait(false);
            // Validate ordering without constructing a permanent string object per entry.
            byte[] prior = [], current = new byte[4096];
            long expectedPath = checked(h.EntryCount * PackageHeader.EntrySize);
            for (long i = 0; i < h.EntryCount; i++)
            {
                token.ThrowIfCancellationRequested();
                var entry = reader.ReadEntry(i);
                if (entry.PathOffset != expectedPath) throw new InvalidDataException("Noncanonical path pool.");
                reader.Directory.Read(current.AsSpan(0, entry.PathLength), entry.PathOffset);
                string name = AssetPath.Utf8.GetString(current.AsSpan(0, entry.PathLength));
                AssetPath.Validate(name);
                if (i > 0 && prior.AsSpan().SequenceCompareTo(current.AsSpan(0, entry.PathLength)) >= 0)
                    throw new InvalidDataException("Asset paths must be unique and sorted.");
                prior = current.AsSpan(0, entry.PathLength).ToArray(); expectedPath += entry.PathLength;
            }
            if (expectedPath != h.DirectoryLength) throw new InvalidDataException("Untracked path data.");
            return reader;
        }
        catch (Exception e)
        {
            if (reader is not null) reader.Dispose();
            else { if (key is not null) CryptographicOperations.ZeroMemory(key); file?.Dispose(); }
            if (e is OperationCanceledException) throw;
            throw new InvalidDataException($"Cannot open asset package '{path}': {e.Message}", e);
        }
    }

    internal EntryRecord ReadEntry(long index)
    {
        Span<byte> b = stackalloc byte[PackageHeader.EntrySize];
        Directory.Read(b, checked(index * PackageHeader.EntrySize));
        return EntryRecord.Decode(b, Header);
    }
    private (long Offset, int Length) ReadPathLocation(long index)
    {
        Span<byte> b = stackalloc byte[12]; Directory.Read(b, checked(index * PackageHeader.EntrySize));
        return (BinaryPrimitives.ReadInt64LittleEndian(b), BinaryPrimitives.ReadInt32LittleEndian(b[8..]));
    }
    internal string ReadPath(long index)
    {
        var p = ReadPathLocation(index);
        if (!Directory.Resident.IsEmpty) return AssetPath.Utf8.GetString(Directory.Resident.Slice((int)p.Offset, p.Length));
        Span<byte> bytes = stackalloc byte[p.Length]; Directory.Read(bytes, p.Offset); return AssetPath.Utf8.GetString(bytes);
    }
    internal EntryRecord? Find(string path)
    {
        int n = AssetPath.Utf8.GetByteCount(path); Span<byte> wanted = stackalloc byte[n]; AssetPath.Utf8.GetBytes(path, wanted);
        Span<byte> scratch = stackalloc byte[4096];
        long lo = 0, hi = Header.EntryCount;
        while (lo < hi)
        {
            long mid = lo + (hi - lo) / 2; var p = ReadPathLocation(mid);
            int cmp;
            if (!Directory.Resident.IsEmpty) cmp = Directory.Resident.Slice((int)p.Offset, p.Length).SequenceCompareTo(wanted);
            else { Directory.Read(scratch[..p.Length], p.Offset); cmp = scratch[..p.Length].SequenceCompareTo(wanted); }
            if (cmp == 0) return ReadEntry(mid);
            if (cmp < 0) lo = mid + 1; else hi = mid;
        }
        return null;
    }
    internal IEnumerable<string> Paths()
    { for (long i = 0; i < Header.EntryCount; i++) yield return ReadPath(i); }
    internal async ValueTask<AssetMetadata> ReadMetadataAsync(EntryRecord e, bool asynchronous, CancellationToken token)
    {
        if (!e.HasMetadata) return AssetMetadata.Empty;
        byte[] bytes = new byte[e.MetadataLength];
        if (asynchronous) await Metadata.ReadAsync(bytes, e.MetadataOffset, token).ConfigureAwait(false);
        else Metadata.Read(bytes, e.MetadataOffset);
        return new AssetMetadata(bytes);
    }
    public void Dispose()
    {
        Directory.Clear(); Metadata.Clear(); Blocks.Clear();
        Ciphers.Dispose(); CryptographicOperations.ZeroMemory(Key); File.Dispose();
    }
}
