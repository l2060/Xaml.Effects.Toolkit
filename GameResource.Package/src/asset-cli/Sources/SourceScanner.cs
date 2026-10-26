using System.Collections.Concurrent;
using System.Security.Cryptography;
using Game.Asset.Cli.Configuration;
using Game.Asset.Package.Internal;

namespace Game.Asset.Cli.Sources;

internal sealed record SourceAsset(string Package, string Path, string SourcePath, long Length,
    byte[] ContentHash, byte[]? Metadata, byte[]? MetadataHash, bool Compression);

internal static class SourceScanner
{
    private sealed record Candidate(PackageConfig Package, string Path, string FullPath, string? MetadataPath);
    private sealed record ScanDirectory(PackageConfig Package, SourceConfig Source, string Directory, IReadOnlyList<IgnoreRule> Rules, Glob[] Includes, Glob[] Excludes);

    internal static async Task<SourceAsset[]> ScanAsync(ProjectConfig project, int concurrency, CancellationToken token)
    {
        var candidates = new ConcurrentBag<Candidate>();
        var roots = project.Packages.SelectMany(p => p.Sources.Select(s => new ScanDirectory(p, s, s.Directory,
            IgnoreRule.Load(Path.Combine(project.Root, ".assetignore"), s.Directory), s.Include.Select(x => new Glob(x)).ToArray(), s.Exclude.Select(x => new Glob(x)).ToArray()))).ToArray();
        var pending = new ConcurrentQueue<ScanDirectory>(roots);
        int unfinished = roots.Length;
        var scans = Enumerable.Range(0, concurrency).Select(_ => Task.Run(async () =>
        {
            while (Volatile.Read(ref unfinished) > 0)
            {
                token.ThrowIfCancellationRequested();
                if (!pending.TryDequeue(out var dir)) { await Task.Delay(1, token).ConfigureAwait(false); continue; }
                try
                {
                    var rules = dir.Rules;
                    string ignoreFile = Path.Combine(dir.Directory, ".assetignore");
                    if (!Path.GetFullPath(ignoreFile).Equals(Path.Combine(project.Root, ".assetignore"), StringComparison.OrdinalIgnoreCase))
                        rules = rules.Concat(IgnoreRule.Load(ignoreFile, dir.Directory)).ToArray();
                    foreach (var item in new DirectoryInfo(dir.Directory).EnumerateFileSystemInfos())
                    {
                        token.ThrowIfCancellationRequested();
                        if ((item.Attributes & FileAttributes.ReparsePoint) != 0 || ProjectConfig.Within(item.FullName, project.StateDirectory) ||
                            ProjectConfig.Within(item.FullName, project.Output)) continue;
                        bool isDirectory = item is DirectoryInfo;
                        if (IgnoreRule.IsIgnored(rules, item.FullName, isDirectory)) continue;
                        if (isDirectory)
                        {
                            Interlocked.Increment(ref unfinished);
                            pending.Enqueue(dir with { Directory = item.FullName, Rules = rules }); continue;
                        }
                        if (item.Name == ".assetignore" || Path.GetFullPath(item.FullName).Equals(project.FilePath, StringComparison.OrdinalIgnoreCase)) continue;
                        string relative = Path.GetRelativePath(dir.Source.Directory, item.FullName).Replace('\\', '/');
                        if (item.Name.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
                        {
                            string assetPath = item.FullName[..^5];
                            string assetRelative = relative[..^5];
                            if (!File.Exists(assetPath) && Included(assetRelative, dir) && !IgnoreRule.IsIgnored(rules, assetPath, false))
                                throw new InvalidDataException($"Orphan metadata file: {item.FullName}");
                            continue;
                        }
                        if (!Included(relative, dir)) continue;
                        string path = dir.Source.Prefix.Length == 0 ? relative : dir.Source.Prefix + "/" + relative;
                        AssetPath.Validate(path);
                        string metaPath = item.FullName + ".meta";
                        if (!File.Exists(metaPath) || (File.GetAttributes(metaPath) & FileAttributes.ReparsePoint) != 0 ||
                            IgnoreRule.IsIgnored(rules, metaPath, false) || dir.Excludes.Any(g => g.Matches(relative + ".meta"))) metaPath = null!;
                        candidates.Add(new(dir.Package, path, item.FullName, metaPath));
                    }
                }
                finally { Interlocked.Decrement(ref unfinished); }
            }
        }, token)).ToArray();
        await Task.WhenAll(scans).ConfigureAwait(false);
        var inputs = candidates.OrderBy(x => x.Package.Name, StringComparer.Ordinal).ThenBy(x => x.Path, AssetPath.Comparer).ToArray();
        for (int i = 1; i < inputs.Length; i++)
            if (inputs[i - 1].Package.Name == inputs[i].Package.Name && inputs[i - 1].Path == inputs[i].Path)
                throw new InvalidDataException($"Duplicate path '{inputs[i].Path}' in package '{inputs[i].Package.Name}'.");
        var output = new SourceAsset[inputs.Length];
        await Parallel.ForEachAsync(Enumerable.Range(0, inputs.Length), new ParallelOptions { MaxDegreeOfParallelism = concurrency, CancellationToken = token }, async (i, ct) =>
        {
            var input = inputs[i]; var before = new FileInfo(input.FullPath); long length = before.Length; DateTime stamp = before.LastWriteTimeUtc;
            await using var file = new FileStream(input.FullPath, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.Asynchronous | FileOptions.SequentialScan);
            byte[] hash = await SHA256.HashDataAsync(file, ct).ConfigureAwait(false);
            before.Refresh();
            if (before.Length != length || before.LastWriteTimeUtc != stamp || file.Length != length) throw new IOException($"Source changed while scanning: {input.FullPath}");
            byte[]? meta = null;
            if (input.MetadataPath is { } metadataPath)
            {
                if (new FileInfo(metadataPath).Length > PackageHeader.MaxMetadataBytes) throw new InvalidDataException($"Metadata exceeds 1 MiB: {metadataPath}");
                meta = PropertyMetadata.Parse(await File.ReadAllTextAsync(metadataPath, AssetPath.Utf8, ct).ConfigureAwait(false), metadataPath);
            }
            output[i] = new(input.Package.Name, input.Path, input.FullPath, length, hash, meta, meta is null ? null : SHA256.HashData(meta), input.Package.Compression);
        }).ConfigureAwait(false);
        return output;
    }
    private static bool Included(string path, ScanDirectory dir) => (dir.Includes.Length == 0 || dir.Includes.Any(g => g.Matches(path))) && !dir.Excludes.Any(g => g.Matches(path));
}
