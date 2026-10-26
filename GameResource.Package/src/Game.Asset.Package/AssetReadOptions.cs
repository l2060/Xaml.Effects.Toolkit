namespace Game.Asset.Package;

public enum AssetReadMode { RandomAccess, MemoryMapped }

public sealed record AssetReadOptions
{
    public AssetReadMode ReadMode { get; init; } = AssetReadMode.RandomAccess;
    public string? Password { get; init; }
    public Func<AssetPackageInfo, string?>? PasswordProvider { get; init; }
    public bool Recursive { get; init; } = true;
    public int MaxBufferedAssetBytes { get; init; } = 32 * 1024 * 1024;
    public int MaxResidentIndexBytes { get; init; } = 64 * 1024 * 1024;
    public int PageCacheBytes { get; init; } = 8 * 1024 * 1024;
    public int MetadataCacheBytes { get; init; } = 4 * 1024 * 1024;

    internal void Validate()
    {
        if (!Enum.IsDefined(ReadMode) || MaxBufferedAssetBytes < 0 || MaxResidentIndexBytes < 0 || PageCacheBytes < 0 || MetadataCacheBytes < 0)
            throw new ArgumentOutOfRangeException(nameof(AssetReadOptions), "Memory limits cannot be negative.");
    }
}
