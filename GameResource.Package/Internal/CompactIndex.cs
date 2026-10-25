using System.Buffers.Binary;
using System.Security.Cryptography;

namespace GameResource.Package.Internal;

internal struct EntryRecord
{
    internal int NameOffset, NameLength;
    internal PackageEntryKind Kind;
    internal bool Blocked;
    internal PackageCompression Compression;
    internal int PropertiesLength;
    internal long DataOffset, StoredLength, RawLength, PropertiesOffset;
    internal ulong ContentHash;
    internal long AllocatedLength;

    internal readonly Blob Blob => new(DataOffset, RawLength, StoredLength, AllocatedLength, Blocked, Compression);
    internal readonly Blob MetadataBlob(bool encrypted)
    {
        long raw = PropertiesLength + 8L;
        long stored = Blob.MaximumStored(raw, encrypted, encrypted);
        return new(PropertiesOffset, raw, stored, ExtentAllocator.Align(stored), encrypted, PackageCompression.None);
    }
    internal readonly void Write(Span<byte> bytes)
    {
        bytes.Clear();
        BinaryPrimitives.WriteInt32LittleEndian(bytes, NameOffset);
        BinaryPrimitives.WriteInt32LittleEndian(bytes[4..], NameLength);
        bytes[8] = (byte)Kind;
        bytes[9] = Blocked ? (byte)1 : (byte)0;
        bytes[10] = (byte)Compression;
        BinaryPrimitives.WriteInt32LittleEndian(bytes[12..], PropertiesLength);
        BinaryPrimitives.WriteInt64LittleEndian(bytes[16..], DataOffset);
        BinaryPrimitives.WriteInt64LittleEndian(bytes[24..], StoredLength);
        BinaryPrimitives.WriteInt64LittleEndian(bytes[32..], RawLength);
        BinaryPrimitives.WriteInt64LittleEndian(bytes[40..], PropertiesOffset);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes[48..], ContentHash);
        BinaryPrimitives.WriteInt64LittleEndian(bytes[56..], AllocatedLength);
    }

    internal static EntryRecord Read(ReadOnlySpan<byte> bytes)
    {
        if (bytes[8] > 1 || bytes[9] > 1 || bytes[10] > 1 || bytes[11] != 0)
            throw new InvalidDataException("Unknown entry flags.");
        return new()
        {
            NameOffset = BinaryPrimitives.ReadInt32LittleEndian(bytes),
            NameLength = BinaryPrimitives.ReadInt32LittleEndian(bytes[4..]),
            Kind = (PackageEntryKind)bytes[8], Blocked = bytes[9] != 0,
            Compression = (PackageCompression)bytes[10],
            PropertiesLength = BinaryPrimitives.ReadInt32LittleEndian(bytes[12..]),
            DataOffset = BinaryPrimitives.ReadInt64LittleEndian(bytes[16..]),
            StoredLength = BinaryPrimitives.ReadInt64LittleEndian(bytes[24..]),
            RawLength = BinaryPrimitives.ReadInt64LittleEndian(bytes[32..]),
            PropertiesOffset = BinaryPrimitives.ReadInt64LittleEndian(bytes[40..]),
            ContentHash = BinaryPrimitives.ReadUInt64LittleEndian(bytes[48..]),
            AllocatedLength = BinaryPrimitives.ReadInt64LittleEndian(bytes[56..])
        };
    }
}

internal sealed class CompactIndex(EntryRecord[] entries, byte[] names)
{
    internal const int HeaderSize = 24, RecordSize = 64, MaxPropertiesBytes = 1024 * 1024;
    internal EntryRecord[] Entries { get; } = entries;
    internal byte[] Names { get; } = names;
    internal long MemoryBytes => (long)Entries.Length * RecordSize + Names.Length;
    internal ReadOnlySpan<byte> NameBytes(int i) => Names.AsSpan(Entries[i].NameOffset, Entries[i].NameLength);
    internal string Name(int i) => PackagePath.Utf8.GetString(NameBytes(i));

    internal int LowerBound(ReadOnlySpan<byte> path)
    {
        int lo = 0, hi = Entries.Length;
        while (lo < hi)
        {
            int mid = lo + (hi - lo) / 2;
            if (NameBytes(mid).SequenceCompareTo(path) < 0) lo = mid + 1;
            else hi = mid;
        }
        return lo;
    }

    internal int Find(string path)
    {
        int size = PackagePath.Utf8.GetByteCount(path);
        Span<byte> bytes = size <= 512 ? stackalloc byte[size] : new byte[size];
        PackagePath.Utf8.GetBytes(path, bytes);
        int i = LowerBound(bytes);
        return i < Entries.Length && NameBytes(i).SequenceEqual(bytes) ? i : -1;
    }

    internal bool HasChildren(string path)
    {
        byte[] prefix = PackagePath.Utf8.GetBytes(path.Length == 0 ? "" : path + "/");
        int i = LowerBound(prefix);
        return i < Entries.Length && NameBytes(i).StartsWith(prefix);
    }

    internal static (CompactIndex Index, List<Extent> Free) Load(
        BlobReadStream reader, PackageHeader header, PackageOpenOptions options) =>
        LoadAsync(reader, header, options, false, default).GetAwaiter().GetResult();

    internal static async ValueTask<(CompactIndex Index, List<Extent> Free)> LoadAsync(
        BlobReadStream reader, PackageHeader header, PackageOpenOptions options, bool asynchronous, CancellationToken token)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] buffer = new byte[PackageHeader.BlockSize];
        await ReadAndHash(reader, buffer.AsMemory(0, HeaderSize), hash, asynchronous, token).ConfigureAwait(false);
        var bytes = buffer.AsSpan();
        if (!bytes[..8].SequenceEqual("GRINDEX2"u8)) throw new InvalidDataException("Invalid directory index.");
        int count = BinaryPrimitives.ReadInt32LittleEndian(bytes[8..]);
        int nameLength = BinaryPrimitives.ReadInt32LittleEndian(bytes[12..]);
        int freeCount = BinaryPrimitives.ReadInt32LittleEndian(bytes[16..]);
        long memory = (long)count * RecordSize + nameLength;
        long fixedSize = HeaderSize + memory + (long)freeCount * 16;
        if (count < 0 || nameLength < 0 || freeCount < 0 ||
            fixedSize != reader.Length || BinaryPrimitives.ReadInt32LittleEndian(bytes[20..]) != 0)
            throw new InvalidDataException("Invalid index dimensions.");
        // A caller's lower memory limit must never cause recovery to an older commit.
        if (count > options.MaxEntries || memory > options.MaxIndexMemoryBytes ||
            (long)freeCount * 16 > options.MaxIndexMemoryBytes)
            throw new InvalidOperationException("The package exceeds the configured index memory or entry limit.");
        var entries = new EntryRecord[count];
        int nameEnd = 0;
        for (int i = 0; i < count; i++)
        {
            int batchIndex = i % (PackageHeader.BlockSize / RecordSize);
            if (batchIndex == 0)
                await ReadAndHash(reader, buffer.AsMemory(0, Math.Min(count - i, PackageHeader.BlockSize / RecordSize) * RecordSize),
                    hash, asynchronous, token).ConfigureAwait(false);
            var item = EntryRecord.Read(buffer.AsSpan(batchIndex * RecordSize, RecordSize));
            if (item.NameOffset != nameEnd || item.NameLength is <= 0 or > PackagePath.MaxBytes ||
                item.NameLength > nameLength - nameEnd || item.PropertiesLength is < 0 or > MaxPropertiesBytes ||
                (item.PropertiesLength == 0 ? item.PropertiesOffset != 0 :
                    !PackageHeader.ValidExtent(item.PropertiesOffset, item.MetadataBlob(header.Encrypted).AllocatedLength, header.CommittedLength)))
                throw new InvalidDataException("Invalid path or properties range.");
            nameEnd += item.NameLength;
            ValidateBlob(item, header);
            entries[i] = item;
        }
        if (nameEnd != nameLength)
            throw new InvalidDataException("Inconsistent index size.");
        var names = new byte[nameLength];
        await ReadAndHash(reader, names, hash, asynchronous, token).ConfigureAwait(false);
        var index = new CompactIndex(entries, names);
        for (int i = 0; i < count; i++)
        {
            if ((i & 1023) == 0) token.ThrowIfCancellationRequested();
            string path;
            try
            {
                path = index.Name(i);
                if (PackagePath.Normalize(path) != path) throw new ArgumentException("Noncanonical path.");
            }
            catch (ArgumentException e) { throw new InvalidDataException("Invalid stored path.", e); }
            if (i > 0 && index.NameBytes(i - 1).SequenceCompareTo(index.NameBytes(i)) >= 0)
                throw new InvalidDataException("Paths must be unique and sorted.");
        }
        // Neither a file nor an empty-directory marker can have descendants.
        for (int i = 0; i < count; i++)
        {
            if ((i & 1023) == 0) token.ThrowIfCancellationRequested();
            if (index.HasChildren(index.Name(i))) throw new InvalidDataException("Conflicting file/directory paths.");
        }
        var free = new List<Extent>(freeCount);
        for (int i = 0; i < freeCount; i++)
        {
            int batchIndex = i % (PackageHeader.BlockSize / 16);
            if (batchIndex == 0)
                await ReadAndHash(reader, buffer.AsMemory(0, Math.Min(freeCount - i, PackageHeader.BlockSize / 16) * 16),
                    hash, asynchronous, token).ConfigureAwait(false);
            bytes = buffer.AsSpan(batchIndex * 16, 16);
            var extent = new Extent(BinaryPrimitives.ReadInt64LittleEndian(bytes),
                BinaryPrimitives.ReadInt64LittleEndian(bytes[8..]));
            if (!PackageHeader.ValidExtent(extent.Offset, extent.Length, header.CommittedLength) ||
                (i > 0 && free[i - 1].End >= extent.Offset))
                throw new InvalidDataException("Invalid free-space list.");
            free.Add(extent);
        }
        if (!CryptographicOperations.FixedTimeEquals(hash.GetHashAndReset(), header.IndexHash))
            throw new InvalidDataException("Directory index checksum mismatch.");
        ValidateExtents(index, free, header);
        return (index, free);
    }

    private static async ValueTask ReadAndHash(Stream reader, Memory<byte> bytes, IncrementalHash hash, bool asynchronous, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (asynchronous) await reader.ReadExactlyAsync(bytes, token).ConfigureAwait(false);
        else reader.ReadExactly(bytes.Span);
        hash.AppendData(bytes.Span);
    }

    private static void ValidateBlob(EntryRecord item, PackageHeader header)
    {
        if (item.Kind == PackageEntryKind.Directory)
        {
            if (item.DataOffset != 0 || item.RawLength != 0 || item.StoredLength != 0 ||
                item.AllocatedLength != 0 || item.PropertiesLength != 0 || item.Blocked ||
                item.ContentHash != 0 || item.Compression != PackageCompression.None)
                throw new InvalidDataException("An empty-directory marker cannot contain data.");
            return;
        }
        if (item.RawLength < 0 || item.StoredLength < 0 || item.AllocatedLength < 0 ||
            item.StoredLength > item.AllocatedLength ||
            (item.RawLength == 0
                ? item.DataOffset != 0 || item.StoredLength != 0 || item.AllocatedLength != 0
                : !PackageHeader.ValidExtent(item.DataOffset, item.AllocatedLength, header.CommittedLength)) ||
            (!item.Blocked && (header.Encrypted || item.Compression != PackageCompression.None ||
                item.RawLength != item.StoredLength)))
            throw new InvalidDataException("Invalid file extent.");
        if (item.Blocked && item.RawLength > 0)
        {
            long count = item.Blob.BlockCount;
            if (count > item.StoredLength / (16 + (header.Encrypted ? 29 : 1)))
                throw new InvalidDataException("Truncated block table.");
        }
    }

    private static void ValidateExtents(CompactIndex index, List<Extent> free, PackageHeader header)
    {
        var all = new List<Extent>(index.Entries.Length + free.Count + 1) { new(header.IndexOffset, header.IndexAllocated) };
        all.AddRange(free);
        foreach (var entry in index.Entries)
        {
            if (entry.AllocatedLength != 0) all.Add(entry.Blob.Extent);
            if (entry.PropertiesLength != 0) all.Add(entry.MetadataBlob(header.Encrypted).Extent);
        }
        all.Sort((a, b) => a.Offset.CompareTo(b.Offset));
        long end = PackageHeader.DataStart;
        foreach (var extent in all)
        {
            if (extent.Offset != end) throw new InvalidDataException("Overlapping or untracked package space.");
            end = extent.End;
        }
        if (end != header.CommittedLength) throw new InvalidDataException("Untracked package tail.");
    }
}
