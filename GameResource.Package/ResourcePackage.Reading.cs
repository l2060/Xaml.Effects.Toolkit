using System.Buffers;
using System.Buffers.Binary;
using System.Security.Cryptography;
using GameResource.Package.Internal;

namespace GameResource.Package;

public sealed partial class ResourcePackage
{
    internal Func<string, CancellationToken, ValueTask>? BeforeResourceRead { get; set; }
    public ResourceInfo GetInfo(string path)
    {
        using var operation = Enter();
        path = PackagePath.Normalize(path, true);
        return ToInfo(FindEntry(path) ?? throw new FileNotFoundException("Resource path not found.", path));
    }
    public bool TryGetInfo(string path, out ResourceInfo info)
    {
        using var operation = Enter();
        var entry = FindEntry(PackagePath.Normalize(path, true));
        info = entry is null ? default : ToInfo(entry);
        return entry is not null;
    }
    private static ResourceInfo ToInfo(PackageEntry entry) => new(entry.Path, entry.Kind, entry.Length,
        entry.StoredLength, entry.Compression, entry.IsEncrypted, entry.HasMetadata);
    private ResourceInfo Info(string path, EntryRecord record) => new(path, record.Kind, record.RawLength,
        record.StoredLength, record.Compression, IsEncrypted, record.PropertiesLength != 0);

    private BlobReadStream CreateReader(Blob blob, BlobContext context, IDisposable? lease = null) =>
        new(_file.SafeFileHandle, blob, _key is null ? null : new AesGcm(_key, 16), context, lease, ownsAes: true, asyncFileFactory: _asyncFileFactory);

    public ResourceProperties ReadMetadata(string path) => ReadMetadata<ResourceProperties>(path)!;
    public T? ReadMetadata<T>(string path) => ReadMetadataCoreAsync<T>(path, false, default).GetAwaiter().GetResult();
    public async ValueTask<ResourceProperties> ReadMetadataAsync(string path, CancellationToken cancellationToken = default) =>
        (await ReadMetadataCoreAsync<ResourceProperties>(path, true, cancellationToken).ConfigureAwait(false))!;
    public ValueTask<T?> ReadMetadataAsync<T>(string path, CancellationToken cancellationToken = default) =>
        ReadMetadataCoreAsync<T>(path, true, cancellationToken);

    private async ValueTask<T?> ReadMetadataCoreAsync<T>(string path, bool asynchronous, CancellationToken token)
    {
        using var operation = Enter();
        token.ThrowIfCancellationRequested();
        var record = RequireFile(PackagePath.Normalize(path)).Record;
        var bytes = await ReadMetadataBytesAsync(record, asynchronous, token).ConfigureAwait(false);
        return MetadataCodec.Decode<T>(bytes, token);
    }

    // Call only while holding a package lease. Cached arrays never escape the library.
    private async ValueTask<byte[]?> ReadMetadataBytesAsync(EntryRecord record, bool asynchronous, CancellationToken token, bool useCache = true)
    {
        token.ThrowIfCancellationRequested();
        if (record.PropertiesLength == 0) return null;
        if (useCache && _metadataCache.Get(record.PropertiesOffset) is { } cached) return cached;
        using var reader = CreateReader(record.MetadataBlob(IsEncrypted), BlobContext.Metadata);
        byte[] frame = new byte[record.PropertiesLength + 8];
        if (asynchronous)
            await reader.ReadExactlyAsync(frame, token).ConfigureAwait(false);
        else reader.ReadExactly(frame);
        if (BinaryPrimitives.ReadUInt64LittleEndian(frame) != XxHash64.HashToUInt64(frame.AsSpan(8)))
            throw new InvalidDataException("Metadata checksum mismatch.");
        byte[] bytes = frame.AsSpan(8).ToArray();
        if (useCache) _metadataCache.Add(record.PropertiesOffset, bytes);
        return bytes;
    }

    public ResourceData<ResourceProperties> ReadResource(string path, CancellationToken cancellationToken = default) =>
        ReadResource<ResourceProperties>(path, cancellationToken);
    public ResourceData<T> ReadResource<T>(string path, CancellationToken cancellationToken = default) =>
        ReadResourceCoreAsync<T>(path, false, cancellationToken).GetAwaiter().GetResult();
    public ValueTask<ResourceData<ResourceProperties>> ReadResourceAsync(string path, CancellationToken cancellationToken = default) =>
        ReadResourceAsync<ResourceProperties>(path, cancellationToken);
    public ValueTask<ResourceData<T>> ReadResourceAsync<T>(string path, CancellationToken cancellationToken = default) =>
        ReadResourceCoreAsync<T>(path, true, cancellationToken);

    private async ValueTask<ResourceData<T>> ReadResourceCoreAsync<T>(string path, bool asynchronous, CancellationToken token)
    {
        using var operation = Enter();
        token.ThrowIfCancellationRequested();
        path = PackagePath.Normalize(path);
        var record = RequireFile(path).Record;
        if (record.RawLength > _limits.MaxBufferedResourceBytes)
            throw new InvalidOperationException("Resource exceeds MaxBufferedResourceBytes. Use OpenResource to stream it.");
        if (BeforeResourceRead is { } observer) await observer(path, token).ConfigureAwait(false);
        byte[]? metadata = await ReadMetadataBytesAsync(record, asynchronous, token).ConfigureAwait(false);
        // The result outlives this lease and cache invalidation. Keep its encoded metadata independently owned.
        metadata = metadata?.ToArray();
        byte[] buffer = ArrayPool<byte>.Shared.Rent((int)record.RawLength);
        try
        {
            using var reader = CreateReader(record.Blob, BlobContext.File);
            if (asynchronous) await reader.ReadExactlyAsync(buffer.AsMemory(0, (int)record.RawLength), token).ConfigureAwait(false);
            else
            {
                for (int offset = 0; offset < record.RawLength;)
                {
                    token.ThrowIfCancellationRequested();
                    int count = (int)Math.Min(PackageHeader.BlockSize, record.RawLength - offset);
                    reader.ReadExactly(buffer.AsSpan(offset, count));
                    offset += count;
                }
            }
            token.ThrowIfCancellationRequested();
            if (XxHash64.HashToUInt64(buffer.AsSpan(0, (int)record.RawLength)) != record.ContentHash)
                throw new InvalidDataException($"Resource checksum mismatch: {path}");
            return new(Info(path, record), new(buffer, (int)record.RawLength, IsEncrypted), metadata);
        }
        catch { ArrayPool<byte>.Shared.Return(buffer, IsEncrypted); throw; }
    }

    public ResourceStream<ResourceProperties> OpenResource(string path) => OpenResource<ResourceProperties>(path);
    public ResourceStream<T> OpenResource<T>(string path) => OpenResourceCoreAsync<T>(path, false, default).GetAwaiter().GetResult();
    public ValueTask<ResourceStream<ResourceProperties>> OpenResourceAsync(string path, CancellationToken cancellationToken = default) =>
        OpenResourceAsync<ResourceProperties>(path, cancellationToken);
    public ValueTask<ResourceStream<T>> OpenResourceAsync<T>(string path, CancellationToken cancellationToken = default) =>
        OpenResourceCoreAsync<T>(path, true, cancellationToken);

    private async ValueTask<ResourceStream<T>> OpenResourceCoreAsync<T>(string path, bool asynchronous, CancellationToken token)
    {
        var operation = Enter();
        try
        {
            token.ThrowIfCancellationRequested();
            path = PackagePath.Normalize(path);
            var record = RequireFile(path).Record;
            var metadata = MetadataCodec.Decode<T>(await ReadMetadataBytesAsync(record, asynchronous, token).ConfigureAwait(false), token);
            return new(Info(path, record), metadata, CreateReader(record.Blob, BlobContext.File, operation));
        }
        catch { operation.Dispose(); throw; }
    }

    public async ValueTask<int> ReadAtAsync(string path, long offset, Memory<byte> destination, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        using var reader = OpenRead(path);
        reader.Position = offset;
        return await reader.ReadAsync(destination, cancellationToken).ConfigureAwait(false);
    }
    public async Task CopyToAsync(string path, Stream destination, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destination);
        using var source = OpenRead(path);
        await source.CopyToAsync(destination, PackageHeader.BlockSize, cancellationToken).ConfigureAwait(false);
    }

    public ResourceReference<ResourceProperties> GetResource(string path) => GetResource<ResourceProperties>(path);
    public ResourceReference<T> GetResource<T>(string path)
    {
        using var operation = Enter();
        path = PackagePath.Normalize(path);
        RequireFile(path);
        return new(this, path);
    }
}
