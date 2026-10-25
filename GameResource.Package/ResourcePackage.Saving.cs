using System.Buffers;
using System.Buffers.Binary;
using GameResource.Package.Internal;

namespace GameResource.Package;

public sealed partial class ResourcePackage
{
    /// <summary>Commits all pending changes. A failed header publication requires close/reopen.</summary>
    public void Save(CancellationToken cancellationToken = default)
    {
        using var operation = Enter(true);
        if (!_dirty) return;
        cancellationToken.ThrowIfCancellationRequested();
        EnsureEdit();
        var items = _edit!.ToArray();
        long namesLength = 0;
        foreach (var item in items)
        {
            namesLength = checked(namesLength + PackagePath.Utf8.GetByteCount(item.Path));
        }
        long residentBytes = checked(namesLength + (long)items.Length * CompactIndex.RecordSize);
        if (items.Length > _limits.MaxEntries || residentBytes > _limits.MaxIndexMemoryBytes)
            throw new InvalidOperationException("The directory exceeds the configured index memory limit.");
        var records = new EntryRecord[items.Length];
        byte[] names = new byte[(int)namesLength];
        int namePosition = 0;
        for (int i = 0; i < items.Length; i++)
        {
            records[i] = items[i].Record;
            records[i].NameOffset = namePosition;
            records[i].NameLength = PackagePath.Utf8.GetBytes(items[i].Path, names.AsSpan(namePosition));
            namePosition += records[i].NameLength;
        }
        _allocator.TrimTail();
        long freeCountBound = checked((long)_allocator.Free.Count + _retired.Count + 3);
        long rawBound = checked(CompactIndex.HeaderSize + residentBytes + freeCountBound * 16);
        var allocation = _allocator.Allocate(Blob.MaximumStored(rawBound, IsEncrypted, IsEncrypted));
        bool publishing = false;
        byte[] buffer = ArrayPool<byte>.Shared.Rent(PackageHeader.BlockSize);
        try
        {
            var nextAllocator = new ExtentAllocator(_allocator.Length, _allocator.Free);
            foreach (var extent in _retired) nextAllocator.Release(extent);
            if (_header.IndexAllocated != 0) nextAllocator.Release(new(_header.IndexOffset, _header.IndexAllocated));
            if ((long)nextAllocator.Free.Count * 16 > _limits.MaxIndexMemoryBytes)
                throw new InvalidOperationException("The free-space list exceeds the configured memory limit.");
            long rawLength = checked(CompactIndex.HeaderSize + residentBytes + (long)nextAllocator.Free.Count * 16);
            var blob = new Blob(allocation.Offset, rawLength, 0, allocation.Length, IsEncrypted, PackageCompression.None);
            using var writer = new BlobWriteStream(_file.SafeFileHandle, blob, _aes, BlobContext.Index);
            Span<byte> bytes = stackalloc byte[CompactIndex.RecordSize];
            bytes.Clear();
            "GRINDEX2"u8.CopyTo(bytes);
            BinaryPrimitives.WriteInt32LittleEndian(bytes[8..], records.Length);
            BinaryPrimitives.WriteInt32LittleEndian(bytes[12..], names.Length);
            BinaryPrimitives.WriteInt32LittleEndian(bytes[16..], nextAllocator.Free.Count);
            writer.Write(bytes[..CompactIndex.HeaderSize]);
            for (int i = 0; i < records.Length;)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int count = Math.Min(PackageHeader.BlockSize / CompactIndex.RecordSize, records.Length - i);
                for (int j = 0; j < count; j++) records[i + j].Write(buffer.AsSpan(j * CompactIndex.RecordSize, CompactIndex.RecordSize));
                writer.Write(buffer.AsSpan(0, count * CompactIndex.RecordSize));
                i += count;
            }
            for (int i = 0; i < names.Length;)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int length = Math.Min(PackageHeader.BlockSize, names.Length - i);
                writer.Write(names.AsSpan(i, length));
                i += length;
            }
            foreach (var extent in nextAllocator.Free)
            {
                BinaryPrimitives.WriteInt64LittleEndian(bytes, extent.Offset);
                BinaryPrimitives.WriteInt64LittleEndian(bytes[8..], extent.Length);
                writer.Write(bytes[..16]);
            }
            blob = writer.Complete();
            if (blob.StoredLength > allocation.Length) throw new InvalidDataException("Index allocation overflow.");
            var header = _header with
            {
                Generation = checked(_header.Generation + 1), IndexOffset = blob.Offset,
                IndexRaw = blob.RawLength, IndexStored = blob.StoredLength, IndexAllocated = allocation.Length,
                CommittedLength = _allocator.Length, IndexHash = writer.Sha256!
            };
            var index = new CompactIndex(records, names);
            byte[] headerBytes = header.Encode(_key);
            // The previous header must remain readable until both new copies are durable.
            _file.SetLength(Math.Max(_header.CommittedLength, _allocator.Length));
            _file.Flush(true);
            CommitObserver?.Invoke(CommitStage.DataFlushed);
            cancellationToken.ThrowIfCancellationRequested();
            // Cancellation is intentionally not observed once publication starts.
            publishing = true;
            long first = (header.Generation & 1) * PackageHeader.SlotSize;
            RandomAccess.Write(_file.SafeFileHandle, headerBytes, first);
            _file.Flush(true);
            CommitObserver?.Invoke(CommitStage.FirstHeaderFlushed);
            RandomAccess.Write(_file.SafeFileHandle, headerBytes, PackageHeader.SlotSize - first);
            _file.Flush(true);
            CommitObserver?.Invoke(CommitStage.BothHeadersFlushed);
            _file.SetLength(_allocator.Length);
            _file.Flush(true);
            _header = header; _index = index; _allocator = nextAllocator;
            _edit = null; _retired.Clear(); _dirty = false;
        }
        catch
        {
            if (publishing) _faulted = true;
            else _allocator.Release(allocation);
            throw;
        }
        finally { ArrayPool<byte>.Shared.Return(buffer, IsEncrypted); }
    }

    /// <summary>
    /// Rewrites a closed package into a verified temporary package in the same directory, then replaces it.
    /// All resource streams and package instances for this file must be closed first.
    /// </summary>
    public static void Compact(string filePath, PackageOpenOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new();
        string path = System.IO.Path.GetFullPath(filePath);
        string temporary = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(path)!,
            "." + System.IO.Path.GetFileName(path) + "." + Guid.NewGuid().ToString("N") + ".tmp");
        bool created = false;
        try
        {
            // Windows ReplaceFile also opens the old file for reading. Writers are still denied;
            // ordinary package readers do not share delete access and prevent replacement.
            using var source = OpenCore(path, false, options, FileShare.Read | FileShare.Delete);
            using (var target = Create(temporary, new()
            {
                Password = source.IsEncrypted ? options.Password : null,
                PasswordIterations = source.IsEncrypted ? source._header.Iterations : 600_000
            }))
            {
                created = true;
                foreach (var item in source.StoredEntries(""))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (item.Record.Kind == PackageEntryKind.Directory) target.CreateDirectory(item.Path);
                    else
                    {
                        byte[]? properties = source.ReadMetadataBytesAsync(item.Record, false, cancellationToken).GetAwaiter().GetResult();
                        using var reader = source.OpenRead(item.Path);
                        target.WriteCore(item.Path, reader, properties, false,
                            new() { Compression = item.Record.Compression, Length = item.Record.RawLength }, cancellationToken);
                        if (target.RequireFile(item.Path).Record.ContentHash != item.Record.ContentHash)
                            throw new InvalidDataException($"Cannot compact a damaged resource: {item.Path}");
                    }
                }
                target.Save(cancellationToken);
                target.Verify(cancellationToken);
            }
            cancellationToken.ThrowIfCancellationRequested();
            File.Replace(temporary, path, destinationBackupFileName: null);
            created = false;
        }
        finally
        {
            if (created) File.Delete(temporary);
        }
    }
}
