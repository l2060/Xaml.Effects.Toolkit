using System.Buffers;
using System.Buffers.Binary;
using GameResource.Package.Internal;

namespace GameResource.Package;

public sealed partial class ResourcePackage
{
    public void CreateDirectory(string path)
    {
        using var operation = Enter(true);
        path = PackagePath.Normalize(path, true);
        var existing = FindEntry(path);
        if (existing?.Kind == PackageEntryKind.Directory) return;
        if (existing is not null) throw new IOException("A file already occupies this path.");
        EnsureEdit();
        CheckParents(path);
        RemoveEmptyAncestors(path);
        _edit!.Add(new(path, new() { Kind = PackageEntryKind.Directory }));
        Changed();
    }

    /// <summary>
    /// Writes content from the source's current position, preserving existing metadata.
    /// Unknown-length streams are spooled to a temporary file, not to memory.
    /// </summary>
    public void PutFile(string path, Stream source, PackageCompression compression = PackageCompression.Auto,
        bool overwrite = true, long? length = null, CancellationToken cancellationToken = default) =>
        WriteCore(path, source, null, true, new() { Compression = compression, Overwrite = overwrite, Length = length }, cancellationToken);

    public void WriteResource<T>(string path, Stream source, T metadata, ResourceWriteOptions? options = null,
        CancellationToken cancellationToken = default) =>
        WriteCore(path, source, MetadataCodec.Encode(metadata), false, options ?? new(), cancellationToken);

    public void WriteResource(string path, Stream source, ResourceWriteOptions? options = null,
        CancellationToken cancellationToken = default) => WriteCore(path, source, null, false, options ?? new(), cancellationToken);

    public void UpdateContent(string path, Stream source, ResourceWriteOptions? options = null,
        CancellationToken cancellationToken = default) => WriteCore(path, source, null, true, options ?? new(), cancellationToken, requireExisting: true);

    private void WriteCore(string path, Stream source, byte[]? properties, bool preserveMetadata,
        ResourceWriteOptions options, CancellationToken cancellationToken, bool requireExisting = false)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!source.CanRead) throw new ArgumentException("The input must be readable.", nameof(source));
        using var operation = Enter(true);
        path = PackagePath.Normalize(path);
        var compression = options.Compression;
        long? length = options.Length;
        ValidateCompression(compression);
        if (length < 0) throw new ArgumentOutOfRangeException(nameof(length));
        var existing = FindEntry(path);
        if (requireExisting && existing is null) throw new FileNotFoundException("Resource path not found.", path);
        if (existing?.Kind == PackageEntryKind.Directory) throw new IOException("A directory already occupies this path.");
        if (existing is not null && !options.Overwrite) throw new IOException("The resource already exists.");
        EnsureEdit();
        CheckParents(path);
        var old = FindStored(path);
        cancellationToken.ThrowIfCancellationRequested();
        FileStream? temporary = null;
        try
        {
            if (length is null)
            {
                if (source.CanSeek) length = checked(source.Length - source.Position);
                else
                {
                    string temp = System.IO.Path.Combine(System.IO.Path.GetTempPath(), System.IO.Path.GetRandomFileName());
                    temporary = new(temp, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None,
                        PackageHeader.BlockSize, FileOptions.DeleteOnClose | FileOptions.SequentialScan);
                    Copy(source, temporary, cancellationToken);
                    length = temporary.Length;
                    temporary.Position = 0;
                    source = temporary;
                }
            }
            EntryRecord metadataRecord = default;
            EntryRecord record;
            try
            {
                if (properties is not null) metadataRecord = WriteMetadataBytes(properties);
                record = WriteContent(source, length.Value, compression, cancellationToken);
            }
            catch
            {
                if (metadataRecord.PropertiesLength != 0) _allocator.Release(metadataRecord.MetadataBlob(IsEncrypted).Extent);
                _allocator.TrimTail();
                throw;
            }
            if (preserveMetadata && old is not null)
            {
                record.PropertiesLength = old.Record.PropertiesLength;
                record.PropertiesOffset = old.Record.PropertiesOffset;
            }
            else
            {
                record.PropertiesLength = metadataRecord.PropertiesLength;
                record.PropertiesOffset = metadataRecord.PropertiesOffset;
            }
            if (old is not null)
            {
                _edit!.Remove(old);
                Retire(old.Record, metadata: !preserveMetadata);
            }
            RemoveEmptyAncestors(path);
            _edit!.Add(new(path, record));
            Changed();
        }
        finally { temporary?.Dispose(); }
    }

    public bool DeleteFile(string path)
    {
        using var operation = Enter(true);
        path = PackagePath.Normalize(path);
        var item = FindStored(path);
        if (item is null)
        {
            if (HasChildren(path)) throw new IOException("The path is a directory.");
            return false;
        }
        if (item.Record.Kind != PackageEntryKind.File) throw new IOException("The path is a directory.");
        EnsureEdit();
        _edit!.Remove(item);
        Retire(item.Record);
        PreserveParent(path);
        Changed();
        return true;
    }

    public bool DeleteDirectory(string path, bool recursive = false)
    {
        using var operation = Enter(true);
        path = PackagePath.Normalize(path);
        var entry = FindEntry(path);
        if (entry is null) return false;
        if (entry.Kind != PackageEntryKind.Directory) throw new IOException("The path is a file.");
        if (!recursive && HasChildren(path)) throw new IOException("The directory is not empty.");
        EnsureEdit();
        var affected = StoredEntries(path + "/").ToArray();
        foreach (var item in affected)
        {
            _edit!.Remove(item);
            Retire(item.Record);
        }
        if (FindStored(path) is { } marker) _edit!.Remove(marker);
        PreserveParent(path);
        Changed();
        return true;
    }

    public void Move(string sourcePath, string destinationPath)
    {
        using var operation = Enter(true);
        sourcePath = PackagePath.Normalize(sourcePath);
        destinationPath = PackagePath.Normalize(destinationPath);
        var source = FindEntry(sourcePath) ?? throw new FileNotFoundException("Resource path not found.", sourcePath);
        if (sourcePath == destinationPath) return;
        if (FindEntry(destinationPath) is not null) throw new IOException("The destination already exists.");
        if (source.Kind == PackageEntryKind.Directory && destinationPath.StartsWith(sourcePath + "/", StringComparison.Ordinal))
            throw new IOException("A directory cannot be moved into itself.");
        EnsureEdit();
        CheckParents(destinationPath);
        EditEntry[] affected = FindStored(sourcePath) is { } exact
            ? [exact] : StoredEntries(sourcePath + "/").ToArray();
        var renamed = new EditEntry[affected.Length];
        for (int i = 0; i < affected.Length; i++)
        {
            var item = affected[i];
            string path = PackagePath.Normalize(destinationPath + item.Path[sourcePath.Length..]);
            renamed[i] = new(path, item.Record);
        }
        foreach (var item in affected) _edit!.Remove(item);
        RemoveEmptyAncestors(destinationPath);
        foreach (var item in renamed) _edit!.Add(item);
        PreserveParent(sourcePath);
        Changed();
    }

    public void UpdateMetadata<T>(string path, T metadata) => SetMetadataBytes(path, MetadataCodec.Encode(metadata));
    public void RemoveMetadata(string path) => SetMetadataBytes(path, null);

    private void SetMetadataBytes(string path, byte[]? properties)
    {
        using var operation = Enter(true);
        path = PackagePath.Normalize(path);
        var item = RequireFile(path);
        EnsureEdit();
        var record = item.Record;
        var metadataRecord = properties is null ? default : WriteMetadataBytes(properties);
        record.PropertiesOffset = metadataRecord.PropertiesOffset;
        record.PropertiesLength = metadataRecord.PropertiesLength;
        if (item.Record.PropertiesLength != 0) _retired.Add(item.Record.MetadataBlob(IsEncrypted).Extent);
        _edit!.Remove(item);
        _edit.Add(new(path, record));
        Changed();
    }

    public void Recompress(string path, PackageCompression compression, CancellationToken cancellationToken = default)
    {
        using var operation = Enter(true);
        path = PackagePath.Normalize(path);
        ValidateCompression(compression);
        var item = RequireFile(path);
        EnsureEdit();
        using var input = new BlobReadStream(_file.SafeFileHandle, item.Record.Blob, _aes, BlobContext.File);
        var record = WriteContent(input, item.Record.RawLength, compression, cancellationToken);
        if (record.ContentHash != item.Record.ContentHash)
        {
            _allocator.Release(record.Blob.Extent);
            throw new InvalidDataException("The original resource is damaged.");
        }
        record.PropertiesLength = item.Record.PropertiesLength;
        record.PropertiesOffset = item.Record.PropertiesOffset;
        _edit!.Remove(item);
        _edit.Add(new(path, record));
        Retire(item.Record, metadata: false);
        Changed();
    }

    private EntryRecord WriteContent(Stream source, long length, PackageCompression compression, CancellationToken token)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        bool blocked = IsEncrypted || compression == PackageCompression.Auto;
        _allocator.TrimTail();
        var allocation = _allocator.Allocate(Blob.MaximumStored(length, blocked, IsEncrypted));
        byte[] buffer = ArrayPool<byte>.Shared.Rent(PackageHeader.BlockSize);
        try
        {
            var blob = new Blob(allocation.Offset, length, 0, allocation.Length, blocked, compression);
            using var output = new BlobWriteStream(_file.SafeFileHandle, blob, _aes, BlobContext.File);
            long remaining = length;
            while (remaining > 0)
            {
                token.ThrowIfCancellationRequested();
                int requested = (int)Math.Min(remaining, PackageHeader.BlockSize);
                int count = source.Read(buffer.AsSpan(0, requested));
                if (count == 0) throw new EndOfStreamException("The input ended before its declared length.");
                output.Write(buffer.AsSpan(0, count));
                remaining -= count;
            }
            token.ThrowIfCancellationRequested();
            blob = output.Complete();
            long used = ExtentAllocator.Align(blob.StoredLength);
            if (used < allocation.Length) _allocator.Release(new(allocation.Offset + used, allocation.Length - used));
            _allocator.TrimTail();
            return new()
            {
                Kind = PackageEntryKind.File, DataOffset = blob.Offset,
                RawLength = length, StoredLength = blob.StoredLength, AllocatedLength = used,
                Blocked = blocked, Compression = compression, ContentHash = output.ContentHash
            };
        }
        catch { _allocator.Release(allocation); _allocator.TrimTail(); throw; }
        finally { ArrayPool<byte>.Shared.Return(buffer, IsEncrypted); }
    }

    private void EnsureEdit()
    {
        if (_edit is not null) return;
        _edit = new(Comparer<EditEntry>.Create((a, b) => PackagePath.Compare(a.Path, b.Path)));
        for (int i = 0; i < _index.Entries.Length; i++) _edit.Add(new(_index.Name(i), _index.Entries[i]));
    }
    private void CheckParents(string path)
    {
        for (string parent = PackagePath.Parent(path); parent.Length != 0; parent = PackagePath.Parent(parent))
            if (FindStored(parent)?.Record.Kind == PackageEntryKind.File)
                throw new IOException($"A file occupies a parent directory: {parent}");
    }
    private void RemoveEmptyAncestors(string path)
    {
        for (string parent = PackagePath.Parent(path); parent.Length != 0; parent = PackagePath.Parent(parent))
            if (FindStored(parent) is { Record.Kind: PackageEntryKind.Directory } marker) _edit!.Remove(marker);
    }
    private void PreserveParent(string path)
    {
        string parent = PackagePath.Parent(path);
        if (parent.Length != 0 && FindStored(parent) is null && !HasChildren(parent))
            _edit!.Add(new(parent, new() { Kind = PackageEntryKind.Directory }));
    }
    private void Retire(EntryRecord record, bool metadata = true)
    {
        if (record.AllocatedLength != 0) _retired.Add(record.Blob.Extent);
        if (metadata && record.PropertiesLength != 0) _retired.Add(record.MetadataBlob(IsEncrypted).Extent);
    }
    private static void ValidateCompression(PackageCompression compression)
    {
        if (compression is not (PackageCompression.None or PackageCompression.Auto))
            throw new ArgumentOutOfRangeException(nameof(compression));
    }
    private EntryRecord WriteMetadataBytes(byte[] bytes)
    {
        var allocation = _allocator.Allocate(Blob.MaximumStored(bytes.Length + 8L, IsEncrypted, IsEncrypted));
        try
        {
            var blob = new Blob(allocation.Offset, bytes.Length + 8L, 0, allocation.Length, IsEncrypted, PackageCompression.None);
            using var writer = new BlobWriteStream(_file.SafeFileHandle, blob, _aes, BlobContext.Metadata);
            Span<byte> hash = stackalloc byte[8];
            BinaryPrimitives.WriteUInt64LittleEndian(hash, XxHash64.HashToUInt64(bytes));
            writer.Write(hash);
            writer.Write(bytes);
            writer.Complete();
            return new() { PropertiesOffset = allocation.Offset, PropertiesLength = bytes.Length };
        }
        catch { _allocator.Release(allocation); throw; }
    }
}
