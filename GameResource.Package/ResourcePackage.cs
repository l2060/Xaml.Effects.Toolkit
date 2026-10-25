using System.Buffers;
using System.Security.Cryptography;
using Microsoft.Win32.SafeHandles;
using GameResource.Package.Internal;

namespace GameResource.Package;

/// <summary>
/// A standalone, case-sensitive resource container. Readers may run concurrently; writes are exclusive.
/// A returned stream or active enumeration holds a read lease until disposed.
/// Changes are committed only by Save; Dispose does not save.
/// </summary>
public sealed partial class ResourcePackage : IDisposable
{
    private readonly FileStream _file;
    private SafeFileHandle? _asyncFile;
    private readonly object _handleSync = new();
    private readonly Func<SafeFileHandle> _asyncFileFactory;
    private readonly bool _writable;
    private readonly byte[]? _key;
    private readonly AesGcm? _aes;
    private readonly PackageOpenOptions _limits;
    private PackageHeader _header;
    private CompactIndex _index;
    private ExtentAllocator _allocator;
    private readonly List<Extent> _retired = [];
    private SortedSet<EditEntry>? _edit;
    private int _busy;
    private long _revision;
    private readonly MetadataCache _metadataCache;
    internal long Revision => Volatile.Read(ref _revision);
    private bool _disposed, _faulted, _dirty;
    internal Action<CommitStage>? CommitObserver { get; set; }

    private ResourcePackage(FileStream file, SafeFileHandle? asyncFile, bool writable, byte[]? key, PackageHeader header,
        CompactIndex index, List<Extent> free, PackageOpenOptions limits)
    {
        _file = file; _asyncFile = asyncFile; _writable = writable; _key = key; _header = header; _index = index;
        _asyncFileFactory = GetAsyncFile;
        _aes = key is null ? null : new AesGcm(key, 16);
        _allocator = new(header.CommittedLength, free);
        _limits = limits with { Password = null };
        _metadataCache = new(limits.MetadataCacheBytes, limits.MetadataCacheEntries);
    }

    public bool IsEncrypted => _key is not null;
    public bool IsReadOnly => !_writable;

    public static ResourcePackage Create(string filePath, PackageCreateOptions? options = null)
    {
        options ??= new();
        var limits = new PackageOpenOptions
        {
            MaxBufferedResourceBytes = options.MaxBufferedResourceBytes,
            MetadataCacheBytes = options.MetadataCacheBytes, MetadataCacheEntries = options.MetadataCacheEntries
        };
        ValidateOptions(limits);
        if (options.Password == "") throw new ArgumentException("Use null for an unencrypted package.", nameof(options));
        if (options.PasswordIterations is < PackageHeader.MinIterations or > PackageHeader.MaxIterations)
            throw new ArgumentOutOfRangeException(nameof(options), "Unsupported password iteration count.");
        byte[] salt = RandomNumberGenerator.GetBytes(16);
        byte[]? key = options.Password is null ? null :
            PackageHeader.DeriveKey(options.Password, salt, options.PasswordIterations);
        FileStream? file = null;
        ResourcePackage? package = null;
        try
        {
            // The primary handle rejects other writers. Other library readers deny sharing this write handle.
            // Read sharing here allows our secondary asynchronous read handle (which shares writes).
            file = new(filePath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read, 1, FileOptions.RandomAccess);
            file.SetLength(PackageHeader.DataStart);
            var header = new PackageHeader(0, 0, 0, 0, PackageHeader.DataStart, salt,
                key is null ? 0 : options.PasswordIterations, new byte[32], 0, key is not null);
            package = new(file, null, true, key, header, new([], []), [], limits);
            package._dirty = true;
            package.Save();
            return package;
        }
        catch
        {
            if (package is not null) package.Dispose();
            else { file?.Dispose(); if (key is not null) CryptographicOperations.ZeroMemory(key); }
            if (file is not null)
            {
                try { File.Delete(filePath); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            throw;
        }
    }

    public static ResourcePackage OpenRead(string filePath, PackageOpenOptions? options = null) =>
        OpenCore(filePath, false, options ?? new(), FileShare.Read);

    public static ResourcePackage OpenEdit(string filePath, PackageOpenOptions? options = null) =>
        OpenCore(filePath, true, options ?? new(), FileShare.None);

    public static ResourcePackage Open(string filePath, PackageOpenOptions? options = null)
    {
        options ??= new();
        return OpenCore(filePath, options.Access == PackageAccess.ReadWrite, options,
            options.Access == PackageAccess.ReadWrite ? FileShare.None : FileShare.Read);
    }

    public static ValueTask<ResourcePackage> OpenAsync(string filePath, PackageOpenOptions? options = null, CancellationToken cancellationToken = default)
    {
        options ??= new();
        return OpenCoreAsync(filePath, options.Access == PackageAccess.ReadWrite, options,
            options.Access == PackageAccess.ReadWrite ? FileShare.None : FileShare.Read, true, cancellationToken);
    }

    private static ResourcePackage OpenCore(string path, bool writable, PackageOpenOptions options, FileShare share) =>
        OpenCoreAsync(path, writable, options, share, false, default).GetAwaiter().GetResult();

    private static SafeFileHandle OpenAsyncHandle(string path) => File.OpenHandle(path, FileMode.Open, FileAccess.Read,
        FileShare.ReadWrite | FileShare.Delete, FileOptions.RandomAccess | FileOptions.Asynchronous);

    // Windows synchronous reads are faster on a synchronous handle. Create the second handle only
    // when native async I/O is requested, and close it before writing to avoid cross-handle cache costs.
    private SafeFileHandle GetAsyncFile()
    {
        lock (_handleSync) return _asyncFile ??= OpenAsyncHandle(_file.Name);
    }

    private static void ValidateOptions(PackageOpenOptions options)
    {
        if (options.MaxEntries <= 0 || options.MaxIndexMemoryBytes < CompactIndex.HeaderSize ||
            options.MaxBufferedResourceBytes < 0 || options.MetadataCacheBytes < 0 || options.MetadataCacheEntries < 0 ||
            options.Access is not (PackageAccess.ReadOnly or PackageAccess.ReadWrite))
            throw new ArgumentOutOfRangeException(nameof(options));
    }

    private static async ValueTask<ResourcePackage> OpenCoreAsync(string path, bool writable, PackageOpenOptions options,
        FileShare share, bool asynchronous, CancellationToken token)
    {
        ValidateOptions(options);
        token.ThrowIfCancellationRequested();
        var file = new FileStream(path, FileMode.Open, writable ? FileAccess.ReadWrite : FileAccess.Read,
            writable ? FileShare.Read : share, 1, FileOptions.RandomAccess);
        SafeFileHandle? asyncFile = null;
        byte[]? key = null;
        ResourcePackage? package = null;
        try
        {
            if (file.Length < PackageHeader.DataStart) throw new InvalidDataException("Not a resource package.");
            if (asynchronous) asyncFile = OpenAsyncHandle(path);
            var candidates = new List<(PackageHeader Header, byte[] Bytes)>();
            for (int slot = 0; slot < 2; slot++)
            {
                var bytes = new byte[192];
                if (asynchronous) await PackageHeader.ReadExactlyAsync(asyncFile!, bytes, slot * PackageHeader.SlotSize, token).ConfigureAwait(false);
                else PackageHeader.ReadExactly(file.SafeFileHandle, bytes, slot * PackageHeader.SlotSize);
                var header = PackageHeader.Decode(bytes, file.Length);
                if (header is not null) candidates.Add((header, bytes));
            }
            candidates.Sort((a, b) => b.Header.Generation.CompareTo(a.Header.Generation));
            if (candidates.Count == 0) throw new InvalidDataException("Neither package header is valid.");
            byte[]? keySalt = null;
            int keyIterations = 0;
            Exception? failure = null;
            foreach (var (header, bytes) in candidates)
            {
                if (header.Encrypted)
                {
                    if (options.Password is null) throw new CryptographicException("A password is required.");
                    if (key is null || keyIterations != header.Iterations || !header.Salt.AsSpan().SequenceEqual(keySalt))
                    {
                        if (key is not null) CryptographicOperations.ZeroMemory(key);
                        key = PackageHeader.DeriveKey(options.Password, header.Salt, header.Iterations);
                        keySalt = header.Salt; keyIterations = header.Iterations;
                    }
                    if (!PackageHeader.Authenticate(bytes, key))
                    {
                        failure = new CryptographicException("Invalid password or damaged authentication header.");
                        continue;
                    }
                }
                else if (key is not null)
                {
                    CryptographicOperations.ZeroMemory(key); key = null;
                }
                try
                {
                    using var aes = key is null ? null : new AesGcm(key, 16);
                    using var reader = new BlobReadStream(file.SafeFileHandle, header.IndexBlob, aes, BlobContext.Index, asynchronousFile: asyncFile);
                    var (index, free) = await CompactIndex.LoadAsync(reader, header, options, asynchronous, token).ConfigureAwait(false);
                    token.ThrowIfCancellationRequested();
                    package = new(file, asyncFile, writable, key, header, index, free, options);
                    if (writable)
                    {
                        package._asyncFile?.Dispose();
                        package._asyncFile = null;
                        // Consolidate both roots before any formerly committed bytes can be reused.
                        byte[] committedHeader = header.Encode(key);
                        RandomAccess.Write(file.SafeFileHandle, committedHeader, 0);
                        file.Flush(true);
                        RandomAccess.Write(file.SafeFileHandle, committedHeader, PackageHeader.SlotSize);
                        file.Flush(true);
                        file.SetLength(header.CommittedLength);
                    }
                    return package;
                }
                catch (Exception e) when (package is null &&
                    e is InvalidDataException or EndOfStreamException or CryptographicException or OverflowException)
                {
                    failure = e;
                }
            }
            throw failure ?? new InvalidDataException("No valid directory index was found.");
        }
        catch
        {
            if (package is not null) package.Dispose();
            else { asyncFile?.Dispose(); file.Dispose(); if (key is not null) CryptographicOperations.ZeroMemory(key); }
            throw;
        }
    }

    public bool Exists(string path)
    {
        using var operation = Enter();
        return FindEntry(PackagePath.Normalize(path, true)) is not null;
    }

    public PackageEntry GetEntry(string path)
    {
        using var operation = Enter();
        path = PackagePath.Normalize(path, true);
        return FindEntry(path) ?? throw new FileNotFoundException("Resource path not found.", path);
    }

    public Stream OpenRead(string path)
    {
        var operation = Enter();
        try
        {
            path = PackagePath.Normalize(path);
            var entry = RequireFile(path);
            return CreateReader(entry.Record.Blob, BlobContext.File, operation);
        }
        catch { operation.Dispose(); throw; }
    }

    public int ReadAt(string path, long offset, Span<byte> destination)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        using var stream = OpenRead(path);
        stream.Position = offset;
        return stream.Read(destination);
    }

    public void CopyTo(string path, Stream destination, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destination);
        using var source = OpenRead(path);
        Copy(source, destination, cancellationToken);
    }

    /// <summary>Enumerates direct children, including inferred directories and preserved empty directories.</summary>
    public IEnumerable<PackageEntry> Enumerate(string directory = "")
    {
        using var operation = Enter();
        directory = PackagePath.Normalize(directory, true);
        RequireDirectory(directory);
        string prefix = directory.Length == 0 ? "" : directory + "/";
        string? previous = null;
        foreach (var entry in StoredEntries(prefix))
        {
            int slash = entry.Path.IndexOf('/', prefix.Length);
            string child = slash < 0 ? entry.Path : entry.Path[..slash];
            if (child == previous) continue;
            previous = child;
            yield return slash < 0 ? Describe(child, entry.Record) : DirectoryEntry(child);
        }
    }

    public IEnumerable<PackageEntry> EnumerateFiles(string directory = "", bool recursive = true)
    {
        using var operation = Enter();
        directory = PackagePath.Normalize(directory, true);
        RequireDirectory(directory);
        string prefix = directory.Length == 0 ? "" : directory + "/";
        foreach (var entry in StoredEntries(prefix))
            if (entry.Record.Kind == PackageEntryKind.File &&
                (recursive || entry.Path.IndexOf('/', prefix.Length) < 0))
                yield return Describe(entry.Path, entry.Record);
    }

    public PackageStatistics GetStatistics()
    {
        using var operation = Enter();
        int files = 0, directories = 0;
        foreach (var item in StoredEntries(""))
            if (item.Record.Kind == PackageEntryKind.File) files++; else directories++;
        var cache = _metadataCache.Statistics;
        return new(files, directories, _allocator.Length, _allocator.Free.Sum(x => x.Length),
            _retired.Sum(x => x.Length), _index.MemoryBytes, _header.Generation, _dirty, cache.Bytes, cache.Entries);
    }

    public void Verify(CancellationToken cancellationToken = default)
    {
        using var operation = Enter();
        byte[] buffer = ArrayPool<byte>.Shared.Rent(PackageHeader.BlockSize);
        try
        {
            foreach (var item in StoredEntries(""))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (item.Record.Kind == PackageEntryKind.Directory) continue;
                var hash = new XxHash64();
                using var stream = CreateReader(item.Record.Blob, BlobContext.File);
                int n;
                while ((n = stream.Read(buffer.AsSpan(0, PackageHeader.BlockSize))) != 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    hash.Append(buffer.AsSpan(0, n));
                }
                if (hash.GetCurrentHashAsUInt64() != item.Record.ContentHash)
                    throw new InvalidDataException($"Resource checksum mismatch: {item.Path}");
                if (ReadMetadataBytesAsync(item.Record, false, cancellationToken, useCache: false).GetAwaiter().GetResult() is { } properties)
                    MetadataCodec.Validate(properties);
            }
        }
        finally { ArrayPool<byte>.Shared.Return(buffer, IsEncrypted); }
    }

    private PackageEntry? FindEntry(string path)
    {
        if (path.Length == 0) return DirectoryEntry("");
        var item = FindStored(path);
        if (item is not null) return Describe(path, item.Record);
        return HasChildren(path) ? DirectoryEntry(path) : null;
    }
    private PackageEntry Describe(string path, EntryRecord item) =>
        new(path, item.Kind, item.RawLength, item.StoredLength, item.Compression,
            IsEncrypted && item.Kind == PackageEntryKind.File, item.PropertiesLength != 0);
    private static PackageEntry DirectoryEntry(string path) =>
        new(path, PackageEntryKind.Directory, 0, 0, PackageCompression.None, false, false);
    private EditEntry RequireFile(string path)
    {
        var item = FindStored(path);
        if (item is null || item.Record.Kind != PackageEntryKind.File)
            throw new FileNotFoundException("File resource not found.", path);
        return item;
    }
    private void RequireDirectory(string path)
    {
        var entry = FindEntry(path);
        if (entry is null || entry.Kind != PackageEntryKind.Directory)
            throw new DirectoryNotFoundException($"Package directory not found: {path}");
    }
    private EditEntry? FindStored(string path)
    {
        if (_edit is not null) return _edit.TryGetValue(new(path, default), out var found) ? found : null;
        int i = _index.Find(path);
        return i < 0 ? null : new(path, _index.Entries[i]);
    }
    private bool HasChildren(string path)
    {
        if (_edit is null) return _index.HasChildren(path);
        string prefix = path.Length == 0 ? "" : path + "/";
        using var iterator = StoredEntries(prefix).GetEnumerator();
        return iterator.MoveNext();
    }
    private IEnumerable<EditEntry> StoredEntries(string prefix)
    {
        if (_edit is not null)
        {
            var range = prefix.Length == 0 ? _edit :
                _edit.GetViewBetween(new(prefix, default), new(prefix[..^1] + "0", default));
            foreach (var item in range)
            {
                if (!item.Path.StartsWith(prefix, StringComparison.Ordinal)) yield break;
                yield return item;
            }
        }
        else
        {
            byte[] bytes = PackagePath.Utf8.GetBytes(prefix);
            for (int i = _index.LowerBound(bytes); i < _index.Entries.Length; i++)
            {
                if (!_index.NameBytes(i).StartsWith(bytes)) yield break;
                yield return new(_index.Name(i), _index.Entries[i]);
            }
        }
    }
    private static void Copy(Stream source, Stream destination, CancellationToken cancellationToken)
    {
        byte[] buffer = ArrayPool<byte>.Shared.Rent(PackageHeader.BlockSize);
        try
        {
            int count;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                count = source.Read(buffer.AsSpan(0, PackageHeader.BlockSize));
                if (count == 0) break;
                destination.Write(buffer.AsSpan(0, count));
            }
        }
        finally { ArrayPool<byte>.Shared.Return(buffer, clearArray: true); }
    }

    private Operation Enter(bool write = false)
    {
        while (true)
        {
            int state = Volatile.Read(ref _busy);
            if (state < 0 || (write && state != 0) || state == int.MaxValue)
                throw new InvalidOperationException("The package is busy. Writes require all readers and enumerators to finish.");
            if (Interlocked.CompareExchange(ref _busy, write ? -1 : state + 1, state) == state) break;
        }
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_faulted) throw new IOException("A commit failed. Close and reopen the package before continuing.");
            if (write && !_writable) throw new InvalidOperationException("The package was opened read-only.");
            if (write) { _asyncFile?.Dispose(); _asyncFile = null; }
            return new(this, write);
        }
        catch { Exit(write); throw; }
    }
    private void Exit(bool write)
    {
        if (write) Volatile.Write(ref _busy, 0);
        else Interlocked.Decrement(ref _busy);
    }
    private void Changed()
    {
        _dirty = true;
        Interlocked.Increment(ref _revision);
        _metadataCache.Clear(IsEncrypted);
    }
    public void Dispose()
    {
        if (_disposed) return;
        if (Interlocked.CompareExchange(ref _busy, -1, 0) != 0)
            throw new InvalidOperationException("Dispose active resource streams/enumerators before the package.");
        try
        {
            if (_disposed) return;
            _disposed = true;
            _metadataCache.Clear(IsEncrypted);
            _aes?.Dispose();
            if (_key is not null) CryptographicOperations.ZeroMemory(_key);
            _asyncFile?.Dispose();
            _file.Dispose();
        }
        finally { Volatile.Write(ref _busy, 0); }
    }
    private sealed class Operation(ResourcePackage package, bool write) : IDisposable
    {
        private ResourcePackage? _owner = package;
        public void Dispose()
        {
            var owner = Interlocked.Exchange(ref _owner, null);
            owner?.Exit(write);
        }
    }
    private sealed class EditEntry(string path, EntryRecord record)
    {
        internal string Path { get; } = path;
        internal EntryRecord Record { get; set; } = record;
    }
}
