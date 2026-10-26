using Game.Asset.Cli.Build;
using Game.Asset.Cli.Configuration;
using Game.Asset.Cli.Tags;
using Game.Asset.Package;

namespace Game.Asset.Cli;

internal static class CommandLine
{
    internal static async Task<int> RunAsync(string[] args, CancellationToken token)
    {
        if (args.Length == 0 || args[0] is "help" or "--help" or "-h")
        {
            Console.WriteLine("""
                asset-cli build [--project asset-project.yml] [--base-tag TAG]
                                [--jobs N] [--io-concurrency N] [--write-concurrency N]
                                [--memory-mib N] [--block-size BYTES]
                asset-cli diff  [--project asset-project.yml] --base-tag TAG
                asset-cli tags  [--project asset-project.yml]
                asset-cli inspect DIRECTORY [--password-env NAME]
                asset-cli verify  DIRECTORY [--password-env NAME]

                Paths in asset-project.yml are relative to that file. State is stored in .asset/.
                Meta is property=value only. Deleted source files never produce deletion records.
                """);
            return 0;
        }
        var flags = new Dictionary<string, string>(StringComparer.Ordinal); var positional = new List<string>();
        for (int i = 1; i < args.Length; i++)
        {
            if (!args[i].StartsWith("--", StringComparison.Ordinal)) { positional.Add(args[i]); continue; }
            string key = args[i]; if (i + 1 >= args.Length || args[i + 1].StartsWith("--", StringComparison.Ordinal)) throw new ArgumentException($"Missing value for {key}.");
            if (!flags.TryAdd(key, args[++i])) throw new ArgumentException($"Duplicate option: {key}");
        }
        string Get(string key, string fallback) => flags.GetValueOrDefault(key, fallback);
        int Number(string key, int fallback) => flags.TryGetValue(key, out string? value) ? int.Parse(value, System.Globalization.CultureInfo.InvariantCulture) : fallback;
        string[] allowed = args[0] switch
        {
            "build" or "diff" => ["--project", "--base-tag", "--jobs", "--io-concurrency", "--write-concurrency", "--memory-mib", "--block-size"],
            "tags" => ["--project"], "inspect" or "verify" => ["--password-env"], _ => throw new ArgumentException($"Unknown command: {args[0]}")
        };
        foreach (var key in flags.Keys) if (!allowed.Contains(key)) throw new ArgumentException($"Unknown option: {key}");
        if (args[0] is "inspect" or "verify")
        {
            if (positional.Count != 1) throw new ArgumentException("Specify exactly one package directory.");
            string? password = flags.TryGetValue("--password-env", out var variable) ? Environment.GetEnvironmentVariable(variable) : null;
            using var set = await AssetPackageSet.OpenDirectoryAsync(positional[0], new AssetReadOptions { Password = password }, token).ConfigureAwait(false);
            foreach (var p in set.Packages) Console.WriteLine($"{p.BuildTimeUtc:O}  {p.TagId:N}  {p.EntryCount} entries  encrypted={p.IsEncrypted}  {p.FilePath}");
            if (args[0] == "verify") { await set.VerifyAsync(token).ConfigureAwait(false); Console.WriteLine("Verification passed."); }
            else foreach (var asset in set.Enumerate()) Console.WriteLine($"{asset.Length,12}  meta={asset.HasMetadata}  {asset.Path}");
            return 0;
        }
        if (positional.Count != 0) throw new ArgumentException("Unexpected positional argument.");
        var project = ProjectConfig.Load(Get("--project", "asset-project.yml"));
        if (args[0] == "tags")
        {
            foreach (var tag in TagStore.List(project.StateDirectory).OrderBy(t => t.BuildTimeUtc))
                Console.WriteLine($"{tag.TagId:N}  {tag.BuildTimeUtc:O}  content={tag.ContentChanges} meta={tag.MetadataChanges}  base={tag.BaseTag?.ToString("N") ?? "full"}");
            return 0;
        }
        Guid? baseline = flags.TryGetValue("--base-tag", out var tagText) ? Guid.Parse(tagText) : null;
        var defaults = new BuildOptions();
        var options = defaults with { Jobs = Number("--jobs", defaults.Jobs), IoConcurrency = Number("--io-concurrency", defaults.IoConcurrency),
            WriteConcurrency = Number("--write-concurrency", defaults.WriteConcurrency), MemoryMiB = Number("--memory-mib", defaults.MemoryMiB), BlockSize = Number("--block-size", defaults.BlockSize) };
        options.Validate();
        if (args[0] == "diff")
        {
            if (baseline is null) throw new ArgumentException("diff requires --base-tag.");
            var plan = await ProjectBuilder.PlanAsync(project, baseline, options, project.Password, token).ConfigureAwait(false);
            foreach (var e in plan.Changes) Console.WriteLine($"{(e.MetadataOnly ? "META" : "CONTENT")}  {e.Source.Package}/{e.Source.Path}");
            Console.WriteLine($"{plan.Changes.Length} changes; {plan.MissingSources} inherited paths absent from source.");
            return 0;
        }
        var built = await ProjectBuilder.BuildAsync(project, baseline, options, token).ConfigureAwait(false);
        Console.WriteLine($"Tag: {built.TagId:N}\nTime: {built.BuildTimeUtc:O}\nContent: {built.ContentChanges}; Meta: {built.MetadataChanges}");
        foreach (var output in built.Outputs) Console.WriteLine(output);
        return 0;
    }
}
