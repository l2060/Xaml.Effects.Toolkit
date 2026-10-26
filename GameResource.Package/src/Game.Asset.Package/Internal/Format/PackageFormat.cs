using System.Buffers.Binary;
using System.Security.Cryptography;

namespace Game.Asset.Package.Internal;

internal sealed record PackageHeader(Guid TagId, long BuildTicks, bool Encrypted, int BlockSize,
    int Iterations, long FileLength, long EntryCount, long DirectoryLength, long MetadataLength,
    long BlocksLength, long DataOffset, byte[] Salt)
{
    internal const int Size = 4096, PageSize = 65536, EntrySize = 128, BlockEntrySize = 32;
    internal const int MaxMetadataBytes = 1024 * 1024;
    internal long DirectoryOffset => Size;
    internal long MetadataOffset => checked(DirectoryOffset + ProtectedLength(DirectoryLength));
    internal long BlocksOffset => checked(MetadataOffset + ProtectedLength(MetadataLength));
    internal static long Pages(long length, int size) => length == 0 ? 0 : (length - 1) / size + 1;
    internal static long ProtectedLength(long length) => checked(length + Pages(length, PageSize) * 16);
    internal static long Align(long value) => checked((value + 7) & ~7L);

    internal byte[] Encode(byte[] key)
    {
        byte[] bytes = new byte[Size];
        "GASSET01"u8.CopyTo(bytes);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(8), 1);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(12), Encrypted ? 1 : 0);
        TagId.TryWriteBytes(bytes.AsSpan(16, 16));
        BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(32), BuildTicks);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(40), BlockSize);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(44), Iterations);
        long[] fields = [FileLength, EntryCount, DirectoryLength, MetadataLength, BlocksLength, DataOffset];
        for (int i = 0; i < fields.Length; i++) BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(48 + i * 8), fields[i]);
        Salt.CopyTo(bytes, 96);
        HMACSHA256.HashData(key, bytes.AsSpan(0, 128), bytes.AsSpan(128, 32));
        SHA256.HashData(bytes.AsSpan(0, 160), bytes.AsSpan(160, 32));
        return bytes;
    }

    internal static PackageHeader Decode(ReadOnlySpan<byte> bytes, long actualLength)
    {
        Span<byte> digest = stackalloc byte[32];
        SHA256.HashData(bytes[..160], digest);
        if (!bytes[..8].SequenceEqual("GASSET01"u8) || BinaryPrimitives.ReadInt32LittleEndian(bytes[8..]) != 1 ||
            !CryptographicOperations.FixedTimeEquals(digest, bytes.Slice(160, 32)))
            throw new InvalidDataException("Invalid or unsupported asset header.");
        int flags = BinaryPrimitives.ReadInt32LittleEndian(bytes[12..]);
        if (flags is not (0 or 1)) throw new InvalidDataException("Unknown package flags.");
        var h = new PackageHeader(new Guid(bytes.Slice(16, 16)), BinaryPrimitives.ReadInt64LittleEndian(bytes[32..]), flags == 1,
            BinaryPrimitives.ReadInt32LittleEndian(bytes[40..]), BinaryPrimitives.ReadInt32LittleEndian(bytes[44..]),
            BinaryPrimitives.ReadInt64LittleEndian(bytes[48..]), BinaryPrimitives.ReadInt64LittleEndian(bytes[56..]),
            BinaryPrimitives.ReadInt64LittleEndian(bytes[64..]), BinaryPrimitives.ReadInt64LittleEndian(bytes[72..]),
            BinaryPrimitives.ReadInt64LittleEndian(bytes[80..]), BinaryPrimitives.ReadInt64LittleEndian(bytes[88..]), bytes.Slice(96, 16).ToArray());
        if (h.TagId == Guid.Empty || h.BuildTicks <= 0 || h.BuildTicks > DateTime.MaxValue.Ticks ||
            h.BlockSize is < 65536 or > 1048576 || (h.BlockSize & (h.BlockSize - 1)) != 0 ||
            h.FileLength != actualLength || h.EntryCount < 0 || h.DirectoryLength < 0 || h.MetadataLength < 0 || h.BlocksLength < 0 ||
            (h.Encrypted ? h.Iterations is < 100000 or > 10000000 : h.Iterations != 0))
            throw new InvalidDataException("Invalid package dimensions or encryption parameters.");
        try
        {
            if (checked(h.EntryCount * EntrySize) > h.DirectoryLength || h.BlocksLength % BlockEntrySize != 0 ||
                h.DataOffset != Align(checked(h.BlocksOffset + ProtectedLength(h.BlocksLength))) || h.DataOffset > actualLength)
                throw new InvalidDataException("Invalid package section layout.");
        }
        catch (OverflowException e) { throw new InvalidDataException("Package dimensions overflow.", e); }
        return h;
    }
}

internal readonly record struct EntryRecord(long PathOffset, int PathLength, bool MetadataOnly, bool HasMetadata,
    bool Blocked, long Length, long DataOffset, long StoredLength, long MetadataOffset, int MetadataLength,
    long FirstBlock, long BlockCount, ContentDigest ContentHash)
{
    internal byte[] Encode()
    {
        byte[] b = new byte[PackageHeader.EntrySize];
        BinaryPrimitives.WriteInt64LittleEndian(b, PathOffset);
        BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(8), PathLength);
        b[12] = MetadataOnly ? (byte)1 : (byte)0;
        b[13] = (byte)((HasMetadata ? 1 : 0) | (Blocked ? 2 : 0));
        BinaryPrimitives.WriteInt64LittleEndian(b.AsSpan(16), Length);
        BinaryPrimitives.WriteInt64LittleEndian(b.AsSpan(24), DataOffset);
        BinaryPrimitives.WriteInt64LittleEndian(b.AsSpan(32), StoredLength);
        BinaryPrimitives.WriteInt64LittleEndian(b.AsSpan(40), MetadataOffset);
        BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(48), MetadataLength);
        BinaryPrimitives.WriteInt64LittleEndian(b.AsSpan(56), FirstBlock);
        BinaryPrimitives.WriteInt64LittleEndian(b.AsSpan(64), BlockCount);
        ContentHash.WriteTo(b.AsSpan(72));
        return b;
    }

    internal static EntryRecord Decode(ReadOnlySpan<byte> b, PackageHeader h)
    {
        if (b[12] > 1 || (b[13] & ~3) != 0 || b.Slice(14, 2).IndexOfAnyExcept((byte)0) >= 0 ||
            b.Slice(52, 4).IndexOfAnyExcept((byte)0) >= 0 || b[104..].IndexOfAnyExcept((byte)0) >= 0)
            throw new InvalidDataException("Invalid asset entry flags.");
        var e = new EntryRecord(BinaryPrimitives.ReadInt64LittleEndian(b), BinaryPrimitives.ReadInt32LittleEndian(b[8..]),
            b[12] == 1, (b[13] & 1) != 0, (b[13] & 2) != 0,
            BinaryPrimitives.ReadInt64LittleEndian(b[16..]), BinaryPrimitives.ReadInt64LittleEndian(b[24..]),
            BinaryPrimitives.ReadInt64LittleEndian(b[32..]), BinaryPrimitives.ReadInt64LittleEndian(b[40..]),
            BinaryPrimitives.ReadInt32LittleEndian(b[48..]), BinaryPrimitives.ReadInt64LittleEndian(b[56..]),
            BinaryPrimitives.ReadInt64LittleEndian(b[64..]), new ContentDigest(b.Slice(72, 32)));
        if (e.PathLength is <= 0 or > 4096 || e.PathOffset < h.EntryCount * PackageHeader.EntrySize ||
            !Range(e.PathOffset, e.PathLength, h.DirectoryLength) || e.Length < 0 || e.StoredLength < 0 ||
            e.MetadataLength is < 0 or > PackageHeader.MaxMetadataBytes ||
            (e.HasMetadata ? e.MetadataLength < 8 || !Range(e.MetadataOffset, e.MetadataLength, h.MetadataLength) : e.MetadataLength != 0 || e.MetadataOffset != 0))
            throw new InvalidDataException("Invalid asset entry range.");
        if (e.MetadataOnly)
        {
            if (!e.HasMetadata || e.Length != 0 || e.DataOffset != 0 || e.StoredLength != 0 || e.Blocked || e.BlockCount != 0 || e.FirstBlock != 0 || !e.ContentHash.IsZero)
                throw new InvalidDataException("Invalid metadata-only entry.");
        }
        else if (e.Blocked)
        {
            if (e.DataOffset != 0 || e.BlockCount != PackageHeader.Pages(e.Length, h.BlockSize) ||
                !Range(e.FirstBlock, e.BlockCount, h.BlocksLength / PackageHeader.BlockEntrySize))
                throw new InvalidDataException("Invalid block table reference.");
        }
        else if (h.Encrypted || e.BlockCount != 0 || e.FirstBlock != 0 || e.StoredLength != e.Length ||
            (e.Length == 0 ? e.DataOffset != 0 : e.DataOffset < h.DataOffset || !Range(e.DataOffset, e.Length, h.FileLength)))
            throw new InvalidDataException("Invalid contiguous content range.");
        return e;
    }

    internal static bool Range(long offset, long length, long limit) => offset >= 0 && length >= 0 && offset <= limit && length <= limit - offset;
}

internal readonly record struct BlockRecord(long Offset, int StoredLength, int Length, long Sequence, byte Codec)
{
    internal byte[] Encode()
    {
        byte[] b = new byte[32];
        BinaryPrimitives.WriteInt64LittleEndian(b, Offset);
        BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(8), StoredLength);
        BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(12), Length);
        BinaryPrimitives.WriteInt64LittleEndian(b.AsSpan(16), Sequence); b[24] = Codec;
        return b;
    }
    internal static BlockRecord Decode(ReadOnlySpan<byte> b, long sequence, int expectedLength, PackageHeader h)
    {
        var r = new BlockRecord(BinaryPrimitives.ReadInt64LittleEndian(b), BinaryPrimitives.ReadInt32LittleEndian(b[8..]),
            BinaryPrimitives.ReadInt32LittleEndian(b[12..]), BinaryPrimitives.ReadInt64LittleEndian(b[16..]), b[24]);
        int overhead = h.Encrypted ? 16 : 0;
        if (r.Codec > 1 || b[25..].IndexOfAnyExcept((byte)0) >= 0 || r.Sequence != sequence || r.Length != expectedLength ||
            r.StoredLength <= overhead || r.StoredLength > expectedLength + overhead ||
            (r.Codec == 0 && r.StoredLength != expectedLength + overhead) ||
            r.Offset < h.DataOffset || !EntryRecord.Range(r.Offset, r.StoredLength, h.FileLength))
            throw new InvalidDataException("Invalid content block.");
        return r;
    }
}
