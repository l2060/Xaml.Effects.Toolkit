using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Game.Asset.Cli.Build;
using Game.Asset.Cli.Configuration;
using Game.Asset.Package;

int files = 3000, operations = 12000;
string resultPath = Path.GetFullPath(args.Length > 0 ? args[0] : "benchmarks/results/read-backends.json");
string root = Path.Combine(Path.GetTempPath(), "asset-benchmark-" + Guid.NewGuid().ToString("N"));
string input = Path.Combine(root, "input"); Directory.CreateDirectory(input);
var results = new List<Measurement>();
var metadataResults = new List<object>();
Environment.SetEnvironmentVariable("ASSET_BENCHMARK_PASSWORD", "benchmark-only-password");
try
{
    var random = new Random(1729);
    for (int i = 0; i < files; i++)
    {
        int length = i % 20 < 16 ? 1024 : i % 20 < 19 ? 4096 : 65536;
        byte[] bytes = new byte[length]; if (i % 2 == 0) random.NextBytes(bytes);
        BitConverter.TryWriteBytes(bytes, i);
        File.WriteAllBytes(Path.Combine(input, $"f{i:D5}.bin"), bytes);
        File.WriteAllText(Path.Combine(input, $"f{i:D5}.bin.meta"), $"id={i}\nname=asset-{i}\nvisible=true\nweights=[0.25,0.75]\n");
    }
    byte[] large = new byte[16 * 1024 * 1024]; random.NextBytes(large);
    File.WriteAllBytes(Path.Combine(input, "large.bin"), large); large = [];
    foreach (bool encrypted in new[] { false, true })
    foreach (bool compressed in new[] { false, true })
    {
        string profile = (compressed ? "lz4" : "raw") + (encrypted ? "-aes" : "");
        string projectRoot = Path.Combine(root, profile); Directory.CreateDirectory(projectRoot);
        string yaml = Path.Combine(projectRoot, "asset-project.yml");
        File.WriteAllText(yaml, $"version: 1\noutput: ./dist\ncompression: {compressed.ToString().ToLowerInvariant()}\nencryption:\n  enabled: {encrypted.ToString().ToLowerInvariant()}\n  key-environment: ASSET_BENCHMARK_PASSWORD\npackages:\n  - name: data\n    sources:\n      - directory: ../input\n        prefix: data\n");
        Console.WriteLine($"Building {profile}...");
        await ProjectBuilder.BuildAsync(ProjectConfig.Load(yaml), null, new BuildOptions { Jobs = 4, IoConcurrency = 4 }, default);
        string output = Path.Combine(projectRoot, "dist");
        string[] paths = Enumerable.Range(0, files).Select(i => $"data/f{i:D5}.bin").ToArray();
        int[] order = Enumerable.Range(0, operations).Select(_ => random.Next(files)).ToArray();
        // Alternate backend order between profiles. Both are explicitly warmed before measurement.
        foreach (bool mapped in compressed ? new[] { true, false } : new[] { false, true })
        {
            string backend = mapped ? "MemoryMapped" : "RandomAccess";
            var options = new AssetReadOptions { Password = encrypted ? "benchmark-only-password" : null, ReadMode = mapped ? AssetReadMode.MemoryMapped : AssetReadMode.RandomAccess };
            var opening = Stopwatch.StartNew(); using var assets = AssetPackageSet.OpenDirectory(output, options); opening.Stop();
            byte[] warm = new byte[4096]; foreach (string path in paths) { _ = assets.ReadAt(path, 0, warm); _ = assets.ReadMetadata(path); }
            using (var stream = assets.OpenRead("data/large.bin")) stream.CopyTo(Stream.Null);
            foreach (string workload in new[] { "random-range-4KiB", "random-full-with-meta", "large-random-range-4KiB" })
            foreach (var mode in new[] { (1, false), (1, true), (4, false), (4, true), (8, true), (16, true) })
            {
                int concurrency = mode.Item1; bool asynchronous = mode.Item2;
                for (int round = 0; round < 3; round++)
                {
                    int count = workload.StartsWith("large", StringComparison.Ordinal) ? 1500 : operations;
                    byte[][] buffers = Enumerable.Range(0, concurrency).Select(_ => new byte[4096]).ToArray();
                    long[] times = new long[count]; long bytesRead = 0;
                    GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
                    long allocated = GC.GetTotalAllocatedBytes(true), cpu = Process.GetCurrentProcess().TotalProcessorTime.Ticks;
                    var watch = Stopwatch.StartNew();
                    async Task Worker(int worker)
                    {
                        long localBytes = 0;
                        for (int j = worker; j < count; j += concurrency)
                        {
                            long start = Stopwatch.GetTimestamp(); int index = order[j];
                            if (workload == "random-full-with-meta")
                            {
                                using var data = asynchronous ? await assets.ReadAsync(paths[index]) : assets.Read(paths[index]);
                                if (data.Metadata.GetInt64("id") != index || BitConverter.ToInt32(data.Data.Span) != index) throw new InvalidDataException("Benchmark identity mismatch.");
                                localBytes += data.Data.Length;
                            }
                            else
                            {
                                string path = workload.StartsWith("large", StringComparison.Ordinal) ? "data/large.bin" : paths[index];
                                long offset = workload.StartsWith("large", StringComparison.Ordinal) ? (j * 104729L) % (16 * 1024 * 1024 - 4096) : 0;
                                int n = asynchronous ? await assets.ReadAtAsync(path, offset, buffers[worker]) : assets.ReadAt(path, offset, buffers[worker]);
                                if (offset == 0 && path != "data/large.bin" && BitConverter.ToInt32(buffers[worker]) != index) throw new InvalidDataException("Benchmark content mismatch.");
                                localBytes += n;
                            }
                            times[j] = Stopwatch.GetTimestamp() - start;
                        }
                        Interlocked.Add(ref bytesRead, localBytes);
                    }
                    if (asynchronous) await Task.WhenAll(Enumerable.Range(0, concurrency).Select(Worker));
                    else if (concurrency == 1) await Worker(0);
                    else Parallel.For(0, concurrency, worker => Worker(worker).GetAwaiter().GetResult());
                    watch.Stop(); cpu = Process.GetCurrentProcess().TotalProcessorTime.Ticks - cpu; allocated = GC.GetTotalAllocatedBytes(true) - allocated;
                    Array.Sort(times);
                    results.Add(new(profile, backend, workload, concurrency, asynchronous, round, count, watch.Elapsed.TotalMilliseconds,
                        count / watch.Elapsed.TotalSeconds, bytesRead / 1048576.0 / watch.Elapsed.TotalSeconds,
                        times[(int)(count * 0.95)] * 1e6 / Stopwatch.Frequency, times[(int)(count * 0.99)] * 1e6 / Stopwatch.Frequency,
                        allocated, cpu / (double)TimeSpan.TicksPerMillisecond, Process.GetCurrentProcess().WorkingSet64, opening.Elapsed.TotalMilliseconds));
                }
            }
            for (int round = 0; round < 3; round++)
            {
                var watch = Stopwatch.StartNew(); using var stream = assets.OpenRead("data/large.bin"); await stream.CopyToAsync(Stream.Null); watch.Stop();
                results.Add(new(profile, backend, "sequential-16MiB", 1, true, round, 1, watch.Elapsed.TotalMilliseconds,
                    1 / watch.Elapsed.TotalSeconds, 16 / watch.Elapsed.TotalSeconds, 0, 0, 0, 0, Process.GetCurrentProcess().WorkingSet64, opening.Elapsed.TotalMilliseconds));
            }
            var meta = assets.ReadMetadata(paths[0]); var metaWatch = new Stopwatch(); long before = GC.GetAllocatedBytesForCurrentThread();
            metaWatch.Start(); long sum = 0; for (int i = 0; i < 1_000_000; i++) sum += meta.GetInt64("id") + (meta.GetBoolean("visible") ? 1 : 0);
            metaWatch.Stop(); long metaAllocated = GC.GetAllocatedBytesForCurrentThread() - before;
            if (sum != 1_000_000) throw new InvalidDataException();
            metadataResults.Add(new { Profile = profile, Backend = backend, GetterCalls = 2000000, Milliseconds = metaWatch.Elapsed.TotalMilliseconds, AllocatedBytes = metaAllocated });
            Console.WriteLine($"{profile}/{backend}: metadata 2M scalar getters {metaWatch.Elapsed.TotalMilliseconds:F1} ms, {metaAllocated} allocated bytes");
        }
    }
    Directory.CreateDirectory(Path.GetDirectoryName(resultPath)!);
    var report = new
    {
        TimestampUtc = DateTimeOffset.UtcNow, Runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
        OS = System.Runtime.InteropServices.RuntimeInformation.OSDescription, ProcessArchitecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(),
        ProcessorCount = Environment.ProcessorCount, Files = files, Operations = operations,
        Cache = "Warm OS cache; no cold-disk claims. Same-process repeated rounds; order alternates across profiles.",
        LibrarySha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(AssetPackageSet).Assembly.Location))),
        Measurements = results, Metadata = metadataResults
    };
    await File.WriteAllTextAsync(resultPath, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine(resultPath);
}
finally
{
    Environment.SetEnvironmentVariable("ASSET_BENCHMARK_PASSWORD", null);
    string resolved = Path.GetFullPath(root);
    if (!ProjectConfig.Within(resolved, Path.GetFullPath(Path.GetTempPath())) || !Path.GetFileName(resolved).StartsWith("asset-benchmark-", StringComparison.Ordinal)) throw new IOException("Unsafe benchmark cleanup.");
    Directory.Delete(resolved, true);
}

internal sealed record Measurement(string Profile, string Backend, string Workload, int Concurrency, bool Async, int Round,
    int Operations, double Milliseconds, double OperationsPerSecond, double MiBPerSecond, double P95Microseconds,
    double P99Microseconds, long AllocatedBytes, double CpuMilliseconds, long WorkingSetBytes, double OpenMilliseconds);
