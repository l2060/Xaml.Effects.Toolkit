using System.Security.Cryptography;
using Game.Asset.Cli.Configuration;
using Game.Asset.Cli.Sources;
using Game.Asset.Cli.Tags;
using Game.Asset.Package;
using Game.Asset.Package.Internal;

namespace Game.Asset.Cli.Build;

internal sealed record BuildPlan(BuildEntry[] Changes, FileState[] Files, int MissingSources);

internal static class ProjectBuilder
{
    internal static async Task<BuildPlan> PlanAsync(ProjectConfig project, Guid? baseTag, BuildOptions options, string? password, CancellationToken token)
    {
        var baseline = baseTag is { } id ? TagStore.LoadFiles(project.StateDirectory, id) : new Dictionary<(string Package, string Path), FileState>();
        var sources = await SourceScanner.ScanAsync(project, options.IoConcurrency, token).ConfigureAwait(false);
        var changes = new List<BuildEntry>(); var present = new HashSet<(string, string)>();
        // Fingerprints stay in the local .asset directory; no credentials are serialized.
        string keyFingerprint = KeyFingerprint(project.StateDirectory, password);
        foreach (var source in sources)
        {
            var key = (source.Package, source.Path); present.Add(key);
            string encoding = $"v1:{options.BlockSize}:{source.Compression}:{project.Encrypted}:{keyFingerprint}";
            var current = new FileState(source.Package, source.Path, source.Length, Convert.ToHexString(source.ContentHash),
                source.MetadataHash is { } hash ? Convert.ToHexString(hash) : null, encoding);
            if (!baseline.TryGetValue(key, out var old) || old.Length != current.Length || old.ContentHash != current.ContentHash || old.Encoding != current.Encoding)
            { changes.Add(new(source, false)); baseline[key] = current; }
            else if (current.MetadataHash is not null && current.MetadataHash != old.MetadataHash)
            { changes.Add(new(source, true)); baseline[key] = current; }
            // Absence of a source or a sidecar is intentionally not a deletion operation.
        }
        return new(changes.ToArray(), baseline.Values.ToArray(), baseline.Keys.Count(k => !present.Contains(k)));
    }

    private static string KeyFingerprint(string state, string? password)
    {
        if (password is null) return "public";
        Directory.CreateDirectory(state);
        string path = Path.Combine(state, "key.salt");
        if (!File.Exists(path))
        {
            using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            file.Write(RandomNumberGenerator.GetBytes(16)); file.Flush(true);
        }
        byte[] salt = File.ReadAllBytes(path);
        if (salt.Length != 16) throw new InvalidDataException("Invalid local key fingerprint salt.");
        byte[] derived = Rfc2898DeriveBytes.Pbkdf2(password, salt, 600000, HashAlgorithmName.SHA256, 32);
        try { return Convert.ToHexString(SHA256.HashData(derived)); }
        finally { CryptographicOperations.ZeroMemory(derived); }
    }

    internal static async Task<BuildTag> BuildAsync(ProjectConfig project, Guid? baseTag, BuildOptions options, CancellationToken token)
    {
        options.Validate(); Directory.CreateDirectory(project.StateDirectory);
        using var buildLock = new FileStream(Path.Combine(project.StateDirectory, "build.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        string? password = project.Password;
        var plan = await PlanAsync(project, baseTag, options, password, token).ConfigureAwait(false);
        Guid tagId = Guid.NewGuid();
        long previous = TagStore.List(project.StateDirectory).Select(t => t.BuildTimeUtc.UtcTicks).DefaultIfEmpty(0).Max();
        long ticks = Math.Max(DateTime.UtcNow.Ticks, checked(previous + 1));
        string name = tagId.ToString("N");
        Directory.CreateDirectory(project.Output);
        string stage = Path.Combine(project.Output, "." + name + ".tmp"), final = Path.Combine(project.Output, name);
        string scratch = Path.Combine(project.StateDirectory, "temp", name);
        string tagStage = Path.Combine(project.StateDirectory, "tags", "." + name + ".tmp"), tagFinal = Path.Combine(project.StateDirectory, "tags", name);
        Directory.CreateDirectory(stage); Directory.CreateDirectory(scratch);
        bool outputPublished = false;
        try
        {
            var outputs = new List<string>();
            foreach (var group in plan.Changes.GroupBy(e => e.Source.Package).OrderBy(g => g.Key, StringComparer.Ordinal))
            {
                token.ThrowIfCancellationRequested();
                string filename = group.Key + ".asset";
                await PackageWriter.WriteAsync(Path.Combine(stage, filename), Path.Combine(scratch, group.Key), tagId, ticks,
                    group.OrderBy(e => e.Source.Path, AssetPath.Comparer).ToArray(), password, options, token).ConfigureAwait(false);
                outputs.Add(Path.GetRelativePath(project.Root, Path.Combine(final, filename)).Replace('\\', '/'));
            }
            // Verify the actual staged artifacts, including metadata-only records and content digests.
            using (var packages = await AssetPackageSet.OpenDirectoryAsync(stage, new AssetReadOptions { Password = password }, token).ConfigureAwait(false))
                await packages.VerifyAsync(token).ConfigureAwait(false);
            string configHash;
            await using (var configFile = File.OpenRead(project.FilePath))
                configHash = Convert.ToHexString(await SHA256.HashDataAsync(configFile, token).ConfigureAwait(false));
            var tag = new BuildTag(tagId, new DateTimeOffset(ticks, TimeSpan.Zero), baseTag, configHash, outputs.ToArray(),
                plan.Changes.Count(e => !e.MetadataOnly), plan.Changes.Count(e => e.MetadataOnly));
            TagStore.Write(tagStage, tag, plan.Files);
            token.ThrowIfCancellationRequested();
            Directory.Move(stage, final); outputPublished = true;
            Directory.Move(tagStage, tagFinal);
            return tag;
        }
        catch
        {
            // Complete but unrecorded output is retained if publishing the Tag fails.
            if (outputPublished) Console.Error.WriteLine($"Completed output retained at {final}; Tag publication did not finish.");
            throw;
        }
        finally
        {
            DeleteOwnedDirectory(stage, project.Output);
            DeleteOwnedDirectory(tagStage, Path.Combine(project.StateDirectory, "tags"));
            DeleteOwnedDirectory(scratch, Path.Combine(project.StateDirectory, "temp"));
        }
    }
    private static void DeleteOwnedDirectory(string path, string parent)
    {
        string resolved = Path.GetFullPath(path), root = Path.GetFullPath(parent);
        if (!ProjectConfig.Within(resolved, root) || resolved.Equals(root, StringComparison.OrdinalIgnoreCase)) throw new IOException("Unsafe temporary directory boundary.");
        if (Directory.Exists(resolved)) Directory.Delete(resolved, true);
    }
}
