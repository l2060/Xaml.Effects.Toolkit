namespace Game.Asset.Package;

public readonly record struct AssetInfo(string Path, long Length, bool HasMetadata);

public sealed record AssetPackageInfo(string FilePath, Guid TagId, DateTimeOffset BuildTimeUtc,
    long EntryCount, bool IsEncrypted);
