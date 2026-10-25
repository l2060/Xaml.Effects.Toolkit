using System.Buffers.Binary;
using System.Security.Cryptography;
using Microsoft.Win32.SafeHandles;

namespace GameResource.Package.Internal;

internal sealed record PackageHeader(
    long Generation, long IndexOffset, long IndexStored, long IndexRaw, long CommittedLength,
    byte[] Salt, int Iterations, byte[] IndexHash, long IndexAllocated, bool Encrypted)
{
    internal const int SlotSize = 4096;
    internal const int DataStart = SlotSize * 2;
    internal const int BlockSize = 65536;
    internal const int MinIterations = 100_000;
    internal const int MaxIterations = 10_000_000;
    internal Blob IndexBlob => new(IndexOffset, IndexRaw, IndexStored, IndexAllocated, Encrypted, PackageCompression.None);

    internal byte[] Encode(byte[]? key)
    {
        byte[] bytes = new byte[SlotSize];
        "GRPKG002"u8.CopyTo(bytes);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(8), 2);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(12), Encrypted ? 1 : 0);
        BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(16), Generation);
        BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(24), IndexOffset);
        BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(32), IndexStored);
        BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(40), IndexRaw);
        BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(48), CommittedLength);
        Salt.CopyTo(bytes, 56);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(72), Iterations);
        IndexHash.CopyTo(bytes, 80);
        BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(112), IndexAllocated);
        if (Encrypted) HMACSHA256.HashData(key!, bytes.AsSpan(0, 128), bytes.AsSpan(128, 32));
        SHA256.HashData(bytes.AsSpan(0, 160), bytes.AsSpan(160, 32));
        return bytes;
    }

    internal static PackageHeader? Decode(ReadOnlySpan<byte> bytes, long fileLength)
    {
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(bytes[..160], hash);
        if (!CryptographicOperations.FixedTimeEquals(hash, bytes.Slice(160, 32))) return null;
        if (!bytes[..8].SequenceEqual("GRPKG002"u8) || BinaryPrimitives.ReadInt32LittleEndian(bytes[8..]) != 2)
            return null;
        int flags = BinaryPrimitives.ReadInt32LittleEndian(bytes[12..]);
        if (flags is not (0 or 1)) return null;
        var result = new PackageHeader(
            BinaryPrimitives.ReadInt64LittleEndian(bytes[16..]),
            BinaryPrimitives.ReadInt64LittleEndian(bytes[24..]),
            BinaryPrimitives.ReadInt64LittleEndian(bytes[32..]),
            BinaryPrimitives.ReadInt64LittleEndian(bytes[40..]),
            BinaryPrimitives.ReadInt64LittleEndian(bytes[48..]), bytes.Slice(56, 16).ToArray(),
            BinaryPrimitives.ReadInt32LittleEndian(bytes[72..]), bytes.Slice(80, 32).ToArray(),
            BinaryPrimitives.ReadInt64LittleEndian(bytes[112..]), flags == 1);
        if (result.Generation <= 0 || result.CommittedLength < DataStart || result.CommittedLength > fileLength ||
            result.CommittedLength % 8 != 0 || result.IndexRaw < CompactIndex.HeaderSize ||
            result.IndexStored <= 0 || result.IndexStored > result.IndexAllocated ||
            !ValidExtent(result.IndexOffset, result.IndexAllocated, result.CommittedLength) ||
            (result.Encrypted && result.Iterations is < MinIterations or > MaxIterations) ||
            (!result.Encrypted && result.Iterations != 0)) return null;
        try
        {
            if (result.IndexStored != Blob.MaximumStored(result.IndexRaw, result.Encrypted, result.Encrypted)) return null;
        }
        catch (OverflowException) { return null; }
        return result;
    }

    internal static bool Authenticate(ReadOnlySpan<byte> bytes, byte[] key)
    {
        Span<byte> hash = stackalloc byte[32];
        HMACSHA256.HashData(key, bytes[..128], hash);
        return CryptographicOperations.FixedTimeEquals(hash, bytes.Slice(128, 32));
    }

    internal static byte[] DeriveKey(string password, byte[] salt, int iterations) =>
        Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, 32);

    internal static bool ValidExtent(long offset, long length, long limit) =>
        offset >= DataStart && offset % 8 == 0 && length > 0 && length % 8 == 0 &&
        offset <= limit && length <= limit - offset;

    internal static async ValueTask ReadExactlyAsync(SafeFileHandle file, Memory<byte> buffer, long offset, CancellationToken token)
    {
        while (!buffer.IsEmpty)
        {
            int count = await RandomAccess.ReadAsync(file, buffer, offset, token).ConfigureAwait(false);
            if (count == 0) throw new EndOfStreamException("The package is truncated.");
            offset = checked(offset + count);
            buffer = buffer[count..];
        }
    }

    internal static void ReadExactly(SafeFileHandle file, Span<byte> buffer, long offset)
    {
        while (!buffer.IsEmpty)
        {
            int count = RandomAccess.Read(file, buffer, offset);
            if (count == 0) throw new EndOfStreamException("The package is truncated.");
            offset = checked(offset + count);
            buffer = buffer[count..];
        }
    }
}

internal readonly record struct Blob(
    long Offset, long RawLength, long StoredLength, long AllocatedLength,
    bool Blocked, PackageCompression Compression)
{
    internal Extent Extent => new(Offset, AllocatedLength);
    internal long BlockCount => RawLength == 0 ? 0 : (RawLength - 1) / PackageHeader.BlockSize + 1;
    internal static long MaximumStored(long rawLength, bool blocked, bool encrypted)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(rawLength);
        long count = rawLength == 0 ? 0 : (rawLength - 1) / PackageHeader.BlockSize + 1;
        return blocked ? checked(rawLength + count * (16 + (encrypted ? 28 : 0))) : rawLength;
    }
}

internal enum BlobContext : byte { File = 1, Index = 2, Metadata = 3 }
internal enum CommitStage { DataFlushed, FirstHeaderFlushed, BothHeadersFlushed }
