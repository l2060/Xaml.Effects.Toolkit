namespace GameResource.Package;

public enum PackageCompression : byte { None, Auto }
public enum PackageEntryKind : byte { File, Directory }
public enum PackageAccess { ReadOnly, ReadWrite }

public sealed record PackageCreateOptions
{
    public string? Password { get; init; }
    public int PasswordIterations { get; init; } = 600_000;
    public int MaxBufferedResourceBytes { get; init; } = 32 * 1024 * 1024;
    public int MetadataCacheBytes { get; init; } = 4 * 1024 * 1024;
    public int MetadataCacheEntries { get; init; } = 4096;
}

public sealed record PackageOpenOptions
{
    public string? Password { get; init; }
    public PackageAccess Access { get; init; } = PackageAccess.ReadOnly;
    public int MaxBufferedResourceBytes { get; init; } = 32 * 1024 * 1024;
    public int MetadataCacheBytes { get; init; } = 4 * 1024 * 1024;
    public int MetadataCacheEntries { get; init; } = 4096;
    // Limits apply before allocating the resident index. Resource contents are streamed.
    public int MaxEntries { get; init; } = 2_000_000;
    public int MaxIndexMemoryBytes { get; init; } = 256 * 1024 * 1024;
}

public sealed record PackageEntry(
    string Path, PackageEntryKind Kind, long Length, long StoredLength,
    PackageCompression Compression, bool IsEncrypted, bool HasProperties)
{
    public bool HasMetadata => HasProperties;
}

public readonly record struct ResourceInfo(
    string Path, PackageEntryKind Kind, long Length, long StoredLength,
    PackageCompression Compression, bool IsEncrypted, bool HasMetadata);

public sealed record ResourceWriteOptions
{
    public PackageCompression Compression { get; init; } = PackageCompression.Auto;
    public bool Overwrite { get; init; } = true;
    public long? Length { get; init; }
}

public sealed record PackageStatistics(
    int FileCount, int EmptyDirectoryCount, long PackageLength, long FreeBytes,
    long PendingReleaseBytes, long IndexMemoryBytes, long Generation, bool HasChanges,
    long MetadataCacheBytes = 0, int MetadataCacheEntries = 0);
