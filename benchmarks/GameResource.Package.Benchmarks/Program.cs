using System.Buffers.Binary;
using System.Diagnostics;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using GameResource.Package;

// Workload benchmark, not a resource-packaging CLI. Each phase is run in a fresh process.
var config = Config.Parse(args);
Directory.CreateDirectory(config.Output);
var runner = new Workload(config);
try { runner.Run(); }
finally { runner.SaveResults(); }

sealed record Config(string Phase, PackageCompression Compression, string Output, int Directories, int Files, int Rounds)
{
    public string Profile => Compression == PackageCompression.None ? "none" : "lz4";
    public string PackagePath => Path.Combine(Output, Profile + ".pkg");
    public string ResultPath => Path.Combine(Output, Profile + "-" + Phase + ".json");
    public static Config Parse(string[] arguments)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int i = 0; i < arguments.Length; i += 2) map.Add(arguments[i], arguments[i + 1]);
        int directories = int.Parse(map.GetValueOrDefault("--directories", "1000"));
        int files = int.Parse(map.GetValueOrDefault("--files", "500000"));
        if (directories <= 0 || files <= 0 || files % directories != 0 || files % 40 != 0)
            throw new ArgumentException("File count must be divisible by directory count and by 40.");
        return new(map["--phase"], map["--profile"] switch
        {
            "none" => PackageCompression.None, "lz4" => PackageCompression.Auto,
            _ => throw new ArgumentException("Profile must be none or lz4.")
        }, Path.GetFullPath(map["--output"]), directories, files, int.Parse(map.GetValueOrDefault("--rounds", "3")));
    }
}

sealed partial class Workload(Config config)
{
    private readonly List<Measurement> _measurements = [];
    private readonly Dictionary<string, object?> _details = new(StringComparer.Ordinal);
    private readonly DateTimeOffset _started = DateTimeOffset.Now;
    private readonly Process _process = Process.GetCurrentProcess();
    private readonly Payloads _payloads = new();
    private string[] _paths = [];
    private int[] _shuffled = [];
    private long _sink;

    public void Run()
    {
        _details["runtime"] = RuntimeInformation.FrameworkDescription;
        _details["os"] = RuntimeInformation.OSDescription;
        _details["architecture"] = RuntimeInformation.ProcessArchitecture.ToString();
        _details["logicalProcessors"] = Environment.ProcessorCount;
        _details["serverGc"] = GCSettings.IsServerGC;
        _details["tieredCompilation"] = Environment.GetEnvironmentVariable("DOTNET_TieredCompilation");
        _details["assemblySha256"] = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(ResourcePackage).Assembly.Location)));
        _details["rawBytes"] = RawBytes();
        _details["dataset"] = "80% 1KiB, 15% 4KiB, 5% 16KiB; half repetitive 64-byte patterns, half seeded random bytes; per-file id/version header; no properties/encryption";
        _details["cachePolicy"] = "OS file cache is not purged; full-content read warmup before measured read rounds; process start is not storage cold start.";
        Console.WriteLine($"START {config.Profile}/{config.Phase} dirs={config.Directories} files={config.Files:N0}");
        if (config.Phase != "open") BuildKeys();
        switch (config.Phase)
        {
            case "prepare": Prepare(); break;
            case "open": Open(); break;
            case "read": Read(); break;
            case "enumerate": Enumerate(); break;
            case "update": Update(); break;
            case "modern": Modern(); break;
            default: throw new ArgumentException("Unknown benchmark phase.");
        }
        _details["completed"] = true;
        _details["sink"] = _sink;
        _details["packageBytesAfterPhase"] = new FileInfo(config.PackagePath).Length;
        CheckBudget();
    }

    public void SaveResults()
    {
        _process.Refresh();
        _details["phasePeakWorkingSetBytes"] = _process.PeakWorkingSet64;
        var result = new
        {
            Started = _started, Ended = DateTimeOffset.Now, Config = config,
            Details = _details, Measurements = _measurements
        };
        File.WriteAllText(config.ResultPath, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"RESULT {config.ResultPath}");
    }

    private void BuildKeys()
    {
        _paths = new string[config.Files];
        _shuffled = Enumerable.Range(0, config.Files).ToArray();
        for (int i = 0; i < _paths.Length; i++)
            _paths[i] = $"dir{i / (config.Files / config.Directories):D4}/file{i:D6}.bin";
        var random = new Random(20261025);
        random.Shuffle(_shuffled);
        _details["harnessPathUtf16Bytes"] = _paths.Sum(x => (long)x.Length * sizeof(char));
        _details["harnessManagedBytesBeforePackage"] = GC.GetTotalMemory(true);
    }

    private void Prepare()
    {
        using var package = ResourcePackage.Create(config.PackagePath);
        Measure("create.put_all", 0, config.Files, RawBytes(), () =>
        {
            for (int i = 0; i < config.Files; i++)
            {
                Put(package, i, 0);
                if ((i + 1) % 50_000 == 0) Console.WriteLine($"  prepared {i + 1:N0}/{config.Files:N0}");
            }
        });
        Measure("create.save", 0, 1, 0, () => package.Save());
        _details["statistics"] = package.GetStatistics();
        _details["packageBytes"] = new FileInfo(config.PackagePath).Length;
        _details["compressionRatioToRaw"] = (double)new FileInfo(config.PackagePath).Length / RawBytes();
        CheckBudget();
        // Correctness check is deliberately outside the measured creation phases.
        var verify = Stopwatch.StartNew();
        package.Verify();
        Require(package.GetStatistics().FileCount == config.Files, "File count mismatch.");
        Require(package.Enumerate().Count() == config.Directories, "Directory count mismatch.");
        Require(package.EnumerateFiles().Sum(x => x.Length) == RawBytes(), "Raw dataset length mismatch.");
        _details["verificationMilliseconds"] = verify.Elapsed.TotalMilliseconds;
        _details["verified"] = true;
    }

    private void Open()
    {
        FullGc();
        _details["memoryBeforeFirstOpen"] = Memory();
        ResourcePackage? first = null;
        Measure("open.first_in_process", 0, 1, 0, () => first = ResourcePackage.OpenRead(config.PackagePath));
        _details["memoryAfterOpenBeforeGc"] = Memory();
        FullGc();
        _details["memoryAfterOpenRetained"] = Memory();
        _details["statistics"] = first!.GetStatistics();
        first.Dispose();
        // First open includes first-use JIT/runtime initialization; repetitions below do not.
        for (int r = 1; r <= config.Rounds + 2; r++)
        {
            ResourcePackage? package = null;
            Measure("open.repeated", r, 1, 0, () => package = ResourcePackage.OpenRead(config.PackagePath));
            package!.Dispose();
        }
    }

    private void Read()
    {
        using var package = ResourcePackage.OpenRead(config.PackagePath);
        byte[] buffer = new byte[65536];
        int[] sequential = Enumerable.Range(0, config.Files).ToArray();
        Console.WriteLine("  warming all resource data (not timed)");
        ReadFiles(package, sequential, buffer);
        for (int r = 1; r <= config.Rounds; r++)
        {
            Measure("lookup.all_random", r, config.Files, 0, () =>
            {
                long sum = 0;
                foreach (int id in _shuffled)
                {
                    var entry = package.GetEntry(_paths[id]);
                    Require(entry.Length == Payloads.Length(id), "Entry length mismatch.");
                    sum += entry.Length;
                }
                _sink ^= sum;
            });
            Measure("read.all_sequential", r, config.Files, RawBytes(), () => ReadFiles(package, sequential, buffer));
            Measure("read.all_random", r, config.Files, RawBytes(), () => ReadFiles(package, _shuffled, buffer));
            int count = Math.Min(100_000, config.Files);
            long bytes = _shuffled.Take(count).Sum(id => (long)Math.Min(4096, Payloads.Length(id)));
            long[] latencies = new long[count];
            Measure("read.random_range_up_to_4KiB", r, count, bytes, () =>
            {
                long sum = 0;
                for (int i = 0; i < count; i++)
                {
                    int id = _shuffled[i], length = Math.Min(4096, Payloads.Length(id));
                    int offset = (id * 17) % (Payloads.Length(id) - length + 1);
                    long start = Stopwatch.GetTimestamp();
                    int read = package.ReadAt(_paths[id], offset, buffer.AsSpan(0, length));
                    latencies[i] = Stopwatch.GetTimestamp() - start;
                    Require(read == length, "Range length mismatch.");
                    sum += buffer[0] + buffer[read - 1];
                }
                _sink ^= sum;
            }, latencies);
        }
    }

    private void ReadFiles(ResourcePackage package, int[] ids, byte[] buffer)
    {
        long sum = 0;
        foreach (int id in ids)
        {
            using var stream = package.OpenRead(_paths[id]);
            int length = Payloads.Length(id);
            Require(stream.Length == length, "Content length mismatch.");
            stream.ReadExactly(buffer.AsSpan(0, length));
            Require(BinaryPrimitives.ReadInt32LittleEndian(buffer) == id, "Content id mismatch.");
            sum += buffer[length - 1] + stream.Length;
        }
        _sink ^= sum;
    }

    private void Enumerate()
    {
        using var package = ResourcePackage.OpenRead(config.PackagePath);
        _sink ^= package.EnumerateFiles().LongCount();
        for (int r = 1; r <= config.Rounds + 2; r++)
        {
            Measure("enumerate.root_directories", r, config.Directories, 0, () =>
            {
                int count = 0;
                foreach (var item in package.Enumerate())
                {
                    Require(item.Kind == PackageEntryKind.Directory, "Unexpected root file.");
                    count++;
                }
                Require(count == config.Directories, "Root directory count mismatch.");
                _sink ^= count;
            });
            Measure("enumerate.all_files", r, config.Files, 0, () =>
            {
                long count = 0, bytes = 0;
                foreach (var item in package.EnumerateFiles()) { count++; bytes += item.Length; }
                Require(count == config.Files && bytes == RawBytes(), "Enumeration mismatch.");
                _sink ^= count + bytes;
            });
            long[] directoryLatencies = new long[config.Directories];
            Measure("enumerate.each_directory", r, config.Files, 0, () =>
            {
                int count = 0;
                for (int directory = 0; directory < config.Directories; directory++)
                {
                    string path = $"dir{directory:D4}";
                    long start = Stopwatch.GetTimestamp();
                    foreach (var item in package.Enumerate(path)) { count++; _sink ^= item.Length; }
                    directoryLatencies[directory] = Stopwatch.GetTimestamp() - start;
                }
                Require(count == config.Files, "Per-directory count mismatch.");
            }, directoryLatencies);
        }
    }

    private void Update()
    {
        int count = Math.Min(10_000, config.Files);
        int[] ids = _shuffled[..count];
        _details["updatedRawBytesPerBatch"] = ids.Sum(id => (long)Payloads.Length(id));
        _details["updateProtocol"] = "3 sequential rounds on the same package, reopening before each; same ids, changing version; same-length replacement; no reset between rounds";
        for (int r = 1; r <= config.Rounds; r++)
        {
            ResourcePackage? package = null;
            Measure("update.single.open_edit", r, 1, 0, () => package = ResourcePackage.OpenEdit(config.PackagePath));
            using (package!)
            {
                Measure("update.single.put_first", r, 1, Payloads.Length(ids[0]), () => Put(package!, ids[0], 100 + r));
                Measure("update.single.save", r, 1, 0, () => package!.Save());
            }
            CheckBudget();
        }
        for (int r = 1; r <= config.Rounds; r++)
        {
            ResourcePackage? package = null;
            Measure("update.batch.open_edit", r, 1, 0, () => package = ResourcePackage.OpenEdit(config.PackagePath));
            using (package!)
            {
                FullGc();
                _details[$"batch{r}.memoryBeforeEditing"] = Memory();
                Measure("update.batch.put_first", r, 1, Payloads.Length(ids[0]), () => Put(package!, ids[0], 200 + r));
                FullGc();
                _details[$"batch{r}.memoryEditingRetained"] = Memory();
                long[] latency = new long[count - 1];
                Measure("update.batch.put_remaining", r, count - 1, ids.Skip(1).Sum(id => (long)Payloads.Length(id)), () =>
                {
                    for (int i = 1; i < count; i++)
                    {
                        long start = Stopwatch.GetTimestamp();
                        Put(package!, ids[i], 200 + r);
                        latency[i - 1] = Stopwatch.GetTimestamp() - start;
                    }
                }, latency);
                Measure("update.batch.save", r, 1, 0, () => package!.Save());
                _details[$"batch{r}.statistics"] = package!.GetStatistics();
            }
            CheckBudget();
            using var verify = ResourcePackage.OpenRead(config.PackagePath);
            byte[] bytes = new byte[16384];
            foreach (int id in ids)
            {
                byte[] expected = _payloads.Get(id, 200 + r);
                using var stream = verify.OpenRead(_paths[id]);
                stream.ReadExactly(bytes.AsSpan(0, expected.Length));
                Require(bytes.AsSpan(0, expected.Length).SequenceEqual(expected), "Updated payload mismatch.");
            }
        }
        using var final = ResourcePackage.OpenRead(config.PackagePath);
        final.Verify();
        Require(final.GetStatistics().FileCount == config.Files, "Update changed file count.");
        _details["updatedFilesVerified"] = count;
        _details["fullPackageVerifiedAfterUpdate"] = true;
    }

    private void Put(ResourcePackage package, int id, int version)
    {
        byte[] data = _payloads.Get(id, version);
        using var input = new MemoryStream(data, false);
        package.PutFile(_paths[id], input, config.Compression, length: data.Length);
    }

    private void Measure(string name, int round, long operations, long bytes, Action action, long[]? latencies = null)
    {
        FullGc();
        _process.Refresh();
        TimeSpan cpuBefore = _process.TotalProcessorTime;
        long allocated = GC.GetTotalAllocatedBytes(true);
        int g0 = GC.CollectionCount(0), g1 = GC.CollectionCount(1), g2 = GC.CollectionCount(2);
        long started = Stopwatch.GetTimestamp();
        action();
        double ms = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        long allocatedAfter = GC.GetTotalAllocatedBytes(true);
        _process.Refresh();
        double cpuMs = (_process.TotalProcessorTime - cpuBefore).TotalMilliseconds;
        int collections0 = GC.CollectionCount(0) - g0, collections1 = GC.CollectionCount(1) - g1,
            collections2 = GC.CollectionCount(2) - g2;
        double? p50 = null, p95 = null, p99 = null;
        if (latencies is { Length: > 0 })
        {
            Array.Sort(latencies);
            p50 = Quantile(latencies, .50); p95 = Quantile(latencies, .95); p99 = Quantile(latencies, .99);
        }
        var result = new Measurement(name, round, ms, operations, bytes, allocatedAfter - allocated,
            cpuMs, collections0, collections1, collections2, _process.WorkingSet64, _process.PrivateMemorySize64,
            p50, p95, p99);
        _measurements.Add(result);
        Console.WriteLine($"  {name} #{round}: {ms:N3} ms; alloc={result.AllocatedBytes / 1048576d:N2} MiB");
        SaveResults();
    }

    private object Memory()
    {
        _process.Refresh();
        return new
        {
            ManagedLiveBytes = GC.GetTotalMemory(false), WorkingSetBytes = _process.WorkingSet64,
            PrivateBytes = _process.PrivateMemorySize64, PeakWorkingSetBytes = _process.PeakWorkingSet64
        };
    }
    private long RawBytes() => (long)config.Files / 20 * (16 * 1024 + 3 * 4096 + 16384);
    private void CheckBudget()
    {
        long total = Directory.EnumerateFiles(config.Output, "*.pkg").Sum(path => new FileInfo(path).Length);
        _details["totalDatasetBytes"] = total;
        _details["maxObservedDatasetBytes"] = Math.Max(total, Convert.ToInt64(_details.GetValueOrDefault("maxObservedDatasetBytes", 0L)));
        Require(total < 3_000_000_000L, $"Dataset exceeded the user's 3 GB limit: {total:N0} bytes.");
    }
    private static void FullGc() { GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); }
    private static double Quantile(long[] sorted, double q) =>
        sorted[Math.Clamp((int)Math.Ceiling(sorted.Length * q) - 1, 0, sorted.Length - 1)] * 1_000_000d / Stopwatch.Frequency;
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }
}

sealed record Measurement(string Name, int Round, double Milliseconds, long Operations, long Bytes,
    long AllocatedBytes, double CpuMilliseconds, int Gen0, int Gen1, int Gen2, long WorkingSetBytes,
    long PrivateBytes, double? P50Microseconds, double? P95Microseconds, double? P99Microseconds);

sealed class Payloads
{
    private readonly byte[][] _buffers = new byte[6][];
    public Payloads()
    {
        int[] sizes = [1024, 4096, 16384];
        for (int size = 0; size < 3; size++)
        {
            _buffers[size * 2] = new byte[sizes[size]];
            _buffers[size * 2 + 1] = new byte[sizes[size]];
            var random = new Random(8701 + size);
            random.NextBytes(_buffers[size * 2]);
            byte[] pattern = new byte[64];
            random.NextBytes(pattern);
            for (int i = 0; i < sizes[size]; i++) _buffers[size * 2 + 1][i] = pattern[i % pattern.Length];
        }
    }
    public static int Length(int id) => (id % 20) switch { < 16 => 1024, < 19 => 4096, _ => 16384 };
    public byte[] Get(int id, int version)
    {
        int kind = (id % 20) switch { < 16 => 0, < 19 => 1, _ => 2 };
        bool compressible = (id / 20) % 2 == 0;
        byte[] bytes = _buffers[kind * 2 + (compressible ? 1 : 0)];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, id);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4), version);
        return bytes;
    }
}
