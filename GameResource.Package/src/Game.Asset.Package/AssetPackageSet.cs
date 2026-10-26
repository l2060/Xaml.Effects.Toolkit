using System.Buffers;
using System.Collections.ObjectModel;
using System.Security.Cryptography;
using Game.Asset.Package.Internal;

namespace Game.Asset.Package;

/// <summary>A fixed, read-only view of packages discovered in a directory. Later packages override paths.</summary>
public sealed class AssetPackageSet : IDisposable, IAsyncDisposable
{
    private readonly PackageReader[] _packages;
    private readonly AssetReadOptions _options;
    private int _references = 1, _closed;
    private readonly object _cacheSync = new();
    private readonly Dictionary<(PackageReader, long), AssetMetadata> _metadata = new();
    private readonly Queue<(PackageReader, long)> _metadataOrder = new();
    private long _metadataBytes;
    public IReadOnlyList<AssetPackageInfo> Packages { get; }

    private AssetPackageSet(PackageReader[] packages, AssetReadOptions options)
    { _packages = packages; _options = options; Packages = Array.AsReadOnly(packages.Select(p => p.Info).ToArray()); }

    public static AssetPackageSet OpenDirectory(string directory, AssetReadOptions? options = null) =>
        OpenCoreAsync(directory, options ?? new(), false, default).GetAwaiter().GetResult();
    public static ValueTask<AssetPackageSet> OpenDirectoryAsync(string directory, AssetReadOptions? options = null, CancellationToken cancellationToken = default) =>
        OpenCoreAsync(directory, options ?? new(), true, cancellationToken);

    private static async ValueTask<AssetPackageSet> OpenCoreAsync(string directory, AssetReadOptions options, bool asynchronous, CancellationToken token)
    {
        options.Validate();
        if (!BitConverter.IsLittleEndian) throw new PlatformNotSupportedException("Asset packages currently support little-endian .NET platforms.");
        string root = Path.GetFullPath(directory);
        if (!System.IO.Directory.Exists(root)) throw new DirectoryNotFoundException(root);
        var readers = new List<PackageReader>();
        try
        {
            foreach (string path in Discover(root, options.Recursive).OrderBy(p => Path.GetRelativePath(root, p).Replace('\\', '/'), StringComparer.Ordinal))
                readers.Add(await PackageReader.OpenAsync(path, options, asynchronous, token).ConfigureAwait(false));
            return new(readers.OrderBy(p => p.Header.BuildTicks).ToArray(), options);
        }
        catch { foreach (var p in readers) p.Dispose(); throw; }
    }
    private static IEnumerable<string> Discover(string directory, bool recursive)
    {
        foreach (var item in new DirectoryInfo(directory).EnumerateFileSystemInfos())
        {
            if ((item.Attributes & FileAttributes.ReparsePoint) != 0) continue;
            if (item is DirectoryInfo)
            {
                if (recursive && item.Name != ".asset" && !item.Name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
                    foreach (var path in Discover(item.FullName, true)) yield return path;
            }
            else if (item.Extension.Equals(".asset", StringComparison.OrdinalIgnoreCase)) yield return item.FullName;
        }
    }

    private sealed record Resolved(PackageReader ContentPackage, EntryRecord Content, PackageReader MetadataPackage, EntryRecord Metadata);
    private Resolved? Resolve(string path)
    {
        (PackageReader Package, EntryRecord Entry)? meta = null;
        for (int i = _packages.Length - 1; i >= 0; i--)
        {
            var found = _packages[i].Find(path);
            if (found is not { } e) continue;
            if (e.MetadataOnly) { meta ??= (_packages[i], e); continue; }
            return new(_packages[i], e, meta?.Package ?? _packages[i], meta?.Entry ?? e);
        }
        return null;
    }
    private Resolved Require(string path) => Resolve(path) ?? throw new FileNotFoundException("Asset content was not found in the loaded packages.", path);
    private static AssetInfo Describe(string path, Resolved r) => new(path, r.Content.Length, r.Metadata.HasMetadata);
    public bool Contains(string path) => TryGetInfo(path, out _);
    public bool TryGetInfo(string path, out AssetInfo info)
    {
        using var lease = Acquire(); AssetPath.Validate(path);
        var r = Resolve(path); info = r is null ? default : Describe(path, r); return r is not null;
    }
    public AssetInfo GetInfo(string path)
    { using var lease = Acquire(); AssetPath.Validate(path); return Describe(path, Require(path)); }

    public IEnumerable<AssetInfo> Enumerate(string directory = "", bool recursive = true)
    {
        using var lease = Acquire(); AssetPath.Validate(directory, true);
        string prefix = directory.Length == 0 ? "" : directory + "/";
        var queue = new PriorityQueue<IEnumerator<string>, string>(AssetPath.Comparer);
        var iterators = new List<IEnumerator<string>>();
        try
        {
            foreach (var package in _packages)
            {
                var iterator = package.Paths().GetEnumerator(); iterators.Add(iterator);
                if (iterator.MoveNext()) queue.Enqueue(iterator, iterator.Current);
            }
            string? previous = null;
            while (queue.TryDequeue(out var iterator, out string? path))
            {
                if (iterator.MoveNext()) queue.Enqueue(iterator, iterator.Current);
                if (path == previous) continue;
                previous = path;
                if (!path.StartsWith(prefix, StringComparison.Ordinal) || (!recursive && path.AsSpan(prefix.Length).Contains('/'))) continue;
                if (Resolve(path) is { } r) yield return Describe(path, r);
            }
        }
        finally { foreach (var i in iterators) i.Dispose(); }
    }
    public AssetMetadata ReadMetadata(string path) => ReadMetadataCoreAsync(path, false, default).GetAwaiter().GetResult();
    public ValueTask<AssetMetadata> ReadMetadataAsync(string path, CancellationToken cancellationToken = default) => ReadMetadataCoreAsync(path, true, cancellationToken);
    private async ValueTask<AssetMetadata> ReadMetadataCoreAsync(string path, bool asynchronous, CancellationToken token)
    {
        using var lease = Acquire(); AssetPath.Validate(path); token.ThrowIfCancellationRequested();
        return await MetadataAsync(Require(path), asynchronous, token).ConfigureAwait(false);
    }
    private async ValueTask<AssetMetadata> MetadataAsync(Resolved r, bool asynchronous, CancellationToken token)
    {
        if (!r.Metadata.HasMetadata) return AssetMetadata.Empty;
        var key = (r.MetadataPackage, r.Metadata.MetadataOffset);
        lock (_cacheSync) if (_metadata.TryGetValue(key, out var existing)) return existing;
        var result = await r.MetadataPackage.ReadMetadataAsync(r.Metadata, asynchronous, token).ConfigureAwait(false);
        if (result.ByteLength <= _options.MetadataCacheBytes)
        {
            lock (_cacheSync)
            {
                if (_metadata.TryGetValue(key, out var existing)) return existing;
                while (_metadata.Count > 0 && _metadataBytes + result.ByteLength > _options.MetadataCacheBytes)
                { var old = _metadataOrder.Dequeue(); _metadataBytes -= _metadata[old].ByteLength; _metadata.Remove(old); }
                _metadata.Add(key, result); _metadataOrder.Enqueue(key); _metadataBytes += result.ByteLength;
            }
        }
        return result;
    }

    public Stream OpenRead(string path)
    {
        var lease = Acquire();
        try { AssetPath.Validate(path); var r = Require(path); return new AssetReadStream(r.ContentPackage, r.Content, lease); }
        catch { lease.Dispose(); throw; }
    }
    public int ReadAt(string path, long offset, Span<byte> destination)
    { using var stream = OpenRead(path); stream.Seek(offset, SeekOrigin.Begin); return stream.Read(destination); }
    public async ValueTask<int> ReadAtAsync(string path, long offset, Memory<byte> destination, CancellationToken cancellationToken = default)
    { using var stream = OpenRead(path); stream.Seek(offset, SeekOrigin.Begin); return await stream.ReadAsync(destination, cancellationToken).ConfigureAwait(false); }
    public void CopyTo(string path, Stream destination, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var stream = OpenRead(path); byte[] buffer = ArrayPool<byte>.Shared.Rent(65536);
        try { int n; while ((n = stream.Read(buffer, 0, buffer.Length)) > 0) { cancellationToken.ThrowIfCancellationRequested(); destination.Write(buffer, 0, n); } }
        finally { ArrayPool<byte>.Shared.Return(buffer, true); }
    }
    public async Task CopyToAsync(string path, Stream destination, CancellationToken cancellationToken = default)
    { using var stream = OpenRead(path); await stream.CopyToAsync(destination, cancellationToken).ConfigureAwait(false); }
    public AssetData Read(string path, CancellationToken cancellationToken = default) => ReadCoreAsync(path, false, cancellationToken).GetAwaiter().GetResult();
    public ValueTask<AssetData> ReadAsync(string path, CancellationToken cancellationToken = default) => ReadCoreAsync(path, true, cancellationToken);
    private async ValueTask<AssetData> ReadCoreAsync(string path, bool asynchronous, CancellationToken token)
    {
        var lease = Acquire(); byte[]? bytes = null; bool clear = false;
        try
        {
            AssetPath.Validate(path); token.ThrowIfCancellationRequested(); var r = Require(path);
            if (r.Content.Length > _options.MaxBufferedAssetBytes) throw new InvalidOperationException("Asset exceeds MaxBufferedAssetBytes; use OpenRead or ReadAt.");
            int length = checked((int)r.Content.Length); clear = r.ContentPackage.Header.Encrypted;
            var metadata = await MetadataAsync(r, asynchronous, token).ConfigureAwait(false);
            bytes = ArrayPool<byte>.Shared.Rent(length);
            using var stream = new AssetReadStream(r.ContentPackage, r.Content, lease); lease = null!;
            if (asynchronous) await stream.ReadExactlyAsync(bytes.AsMemory(0, length), token).ConfigureAwait(false);
            else
            {
                int pos = 0;
                while (pos < length) { token.ThrowIfCancellationRequested(); int n = Math.Min(length - pos, 65536); stream.ReadExactly(bytes.AsSpan(pos, n)); pos += n; }
            }
            byte[] hash = SHA256.HashData(bytes.AsSpan(0, length));
            if (!r.Content.ContentHash.Matches(hash)) throw new InvalidDataException($"Asset checksum mismatch: {path}");
            var result = new AssetData(Describe(path, r), metadata, bytes, length, clear); bytes = null; return result;
        }
        finally { lease?.Dispose(); if (bytes is not null) ArrayPool<byte>.Shared.Return(bytes, clear); }
    }

    public void Verify(CancellationToken cancellationToken = default) => VerifyCoreAsync(false, cancellationToken).GetAwaiter().GetResult();
    public Task VerifyAsync(CancellationToken cancellationToken = default) => VerifyCoreAsync(true, cancellationToken);
    private async Task VerifyCoreAsync(bool asynchronous, CancellationToken token)
    {
        using var lease = Acquire(); byte[] buffer = ArrayPool<byte>.Shared.Rent(65536);
        try
        {
            foreach (var package in _packages)
            for (long i = 0; i < package.Header.EntryCount; i++)
            {
                token.ThrowIfCancellationRequested(); var entry = package.ReadEntry(i);
                _ = await package.ReadMetadataAsync(entry, asynchronous, token).ConfigureAwait(false);
                if (entry.MetadataOnly) continue;
                using var stream = new AssetReadStream(package, entry, new NoopLease());
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                while (true)
                {
                    token.ThrowIfCancellationRequested();
                    int n = asynchronous ? await stream.ReadAsync(buffer, token).ConfigureAwait(false) : stream.Read(buffer);
                    if (n == 0) break; hash.AppendData(buffer.AsSpan(0, n));
                }
                if (!entry.ContentHash.Matches(hash.GetHashAndReset()))
                    throw new InvalidDataException($"Asset checksum mismatch: {package.ReadPath(i)}");
            }
        }
        finally { ArrayPool<byte>.Shared.Return(buffer, true); }
    }
    private Lease Acquire()
    {
        while (true)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _closed) != 0, this);
            int count = Volatile.Read(ref _references);
            if (count == 0) throw new ObjectDisposedException(nameof(AssetPackageSet));
            if (Interlocked.CompareExchange(ref _references, checked(count + 1), count) != count) continue;
            if (Volatile.Read(ref _closed) == 0) return new(this);
            Release(); throw new ObjectDisposedException(nameof(AssetPackageSet));
        }
    }
    private void Release()
    {
        if (Interlocked.Decrement(ref _references) != 0) return;
        lock (_cacheSync) { _metadata.Clear(); _metadataOrder.Clear(); }
        foreach (var p in _packages) p.Dispose();
    }
    public void Dispose() { if (Interlocked.Exchange(ref _closed, 1) == 0) Release(); }
    public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
    private sealed class Lease(AssetPackageSet owner) : IDisposable
    { private AssetPackageSet? _owner = owner; public void Dispose() => Interlocked.Exchange(ref _owner, null)?.Release(); }
    private sealed class NoopLease : IDisposable { public void Dispose() { } }
}
